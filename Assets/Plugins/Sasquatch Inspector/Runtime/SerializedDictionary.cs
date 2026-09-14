using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Non-generic marker base for <see cref="SerializedDictionary{TKey,TValue}"/>. Its only job is to give the
    /// custom drawer a single, non-generic target: <c>[CustomPropertyDrawer(typeof(SerializedDictionaryBase), true)]</c>
    /// matches every closed generic (Unity can't target an open generic directly).
    /// <para>
    /// Carries <see cref="SerializableAttribute"/> purely to keep the hierarchy complete: a serializable type may not
    /// derive from a non-serializable base, and Unity 6.2+ raises UAC1002 if it does. It declares no fields, so the
    /// attribute adds nothing to the serialized data.
    /// </para>
    /// </summary>
    [Serializable]
    public abstract class SerializedDictionaryBase { }

    /// <summary>
    /// A Unity-serializable dictionary. Native Unity can't serialize <see cref="Dictionary{TKey,TValue}"/> at all;
    /// this wraps a serialized list of key/value pairs and exposes a full <see cref="IDictionary{TKey,TValue}"/> at
    /// runtime, so you can write <c>myDict[key]</c> / <c>TryGetValue</c> as usual.
    /// <para>
    /// The serialized <c>entries</c> list is the source of truth. It may temporarily contain duplicate keys while
    /// you edit it in the inspector (the drawer flags them); the runtime dictionary view resolves duplicates
    /// <b>first-occurrence-wins</b>, which is what the "only the first is used" warning refers to.
    /// </para>
    /// <para>
    /// Unity's render-pipeline core package has an unrelated <c>UnityEngine.Rendering.SerializedDictionary</c>; in
    /// the rare file that imports both namespaces, qualify this one or add a closed-type alias.
    /// </para>
    /// </summary>
    [Serializable]
    public class SerializedDictionary<TKey, TValue>
        : SerializedDictionaryBase, IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        /// <summary>One serialized key/value pair. One <see cref="Entry"/> == one array element == one table row.</summary>
        [Serializable]
        public struct Entry
        {
            public TKey Key;
            public TValue Value;

            public Entry(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        // The single backing array. A LIST OF PAIRS (not two parallel key/value lists) so each pair is exactly one
        // SerializedProperty array element with .Key/.Value sub-properties — that's what lets the drawer reuse the
        // collection table's per-array-element reorder / row-menu machinery and keeps pairs from ever desyncing.
        [SerializeField] private List<Entry> entries = new List<Entry>();

        // Lazy runtime lookup view, rebuilt first-wins from `entries`. Never serialized (Unity ignores it anyway;
        // [NonSerialized] documents intent and guards the odd reflection path).
        [NonSerialized] private Dictionary<TKey, TValue> _cache;
        [NonSerialized] private bool _cacheBuilt;

        public SerializedDictionary() { }

        /// <summary>Seeds the dictionary from an existing collection of pairs.</summary>
        public SerializedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            if (source == null) return;
            foreach (var kvp in source) entries.Add(new Entry(kvp.Key, kvp.Value));
        }

        // ---------------------------------------------------------------------------------------------------------
        // Runtime lookup view (first-wins over the raw entries).
        //
        // INVARIANT — whenever `_cacheBuilt`: for any non-null key k, k is in `_cache` ⇔ k occurs in `entries`,
        // and `_cache[k]` equals the FIRST occurrence's value. The write methods below maintain the cache in place
        // (instead of tearing it down) so interleaved write/read loops stay O(1) per read; every incremental cache
        // touch is guarded with `key != null` because null keys never enter the cache (the rebuild skips them).
        // ---------------------------------------------------------------------------------------------------------

        private Dictionary<TKey, TValue> Cache
        {
            get
            {
                if (_cacheBuilt && _cache != null) return _cache;

                var comparer = EqualityComparer<TKey>.Default;
                _cache = new Dictionary<TKey, TValue>(entries.Count, comparer);
                for (int i = 0; i < entries.Count; i++)
                {
                    var key = entries[i].Key;
                    // Null keys can't live in a Dictionary and a serialized reference/string may be null mid-edit.
                    if (key == null) continue;
                    // First occurrence wins; later duplicates are ignored at runtime (the drawer flags them).
                    if (!_cache.ContainsKey(key)) _cache.Add(key, entries[i].Value);
                }
                _cacheBuilt = true;
                return _cache;
            }
        }

        private void InvalidateCache() => _cacheBuilt = false;

        private int IndexOfKey(TKey key)
        {
            var comparer = EqualityComparer<TKey>.Default;
            for (int i = 0; i < entries.Count; i++)
                if (comparer.Equals(entries[i].Key, key)) return i;
            return -1;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Convenience
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>The raw serialized pairs, in order (may contain duplicate keys). Read-only view.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>A plain <see cref="Dictionary{TKey,TValue}"/> copy of the effective (first-wins) contents.</summary>
        public Dictionary<TKey, TValue> ToDictionary() => new Dictionary<TKey, TValue>(Cache);

        // ---------------------------------------------------------------------------------------------------------
        // IDictionary<TKey, TValue> — reads go through the first-wins Cache; writes mutate `entries` and keep the
        // built cache in sync per the invariant above.
        // ---------------------------------------------------------------------------------------------------------

        public TValue this[TKey key]
        {
            get => Cache[key];
            set
            {
                // A null key is REJECTED, exactly as Add does and exactly as BCL Dictionary's indexer does.
                // Without this the null key fell through to IndexOfKey (which finds nothing) and was APPENDED
                // as a real entry — but the runtime view skips null keys when it builds its cache, so the
                // write appeared to succeed while `Count` never moved, the value was unreadable through every
                // API on the type, and a permanently-ignored row was left in the user's serialized data.
                // Silently storing something nothing can read back is worse than throwing.
                if (key == null) throw new ArgumentNullException(nameof(key));

                // Fast path: cache built + key absent ⇒ (by the invariant) no occurrence exists in `entries`
                // either — append without the O(n) IndexOfKey scan.
                if (_cacheBuilt && _cache != null && !_cache.ContainsKey(key))
                {
                    entries.Add(new Entry(key, value));
                    _cache.Add(key, value);
                    return;
                }

                int i = IndexOfKey(key);
                if (i >= 0) entries[i] = new Entry(key, value); // update the effective (first) occurrence
                else entries.Add(new Entry(key, value));
                // Upsert: first-wins makes this correct even with duplicates present (we updated the first).
                // The key is non-null past the guard above, so no null check is needed here any more.
                if (_cacheBuilt && _cache != null) _cache[key] = value;
            }
        }

        public ICollection<TKey> Keys => Cache.Keys;
        public ICollection<TValue> Values => Cache.Values;
        public int Count => Cache.Count;
        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            // ContainsKey builds the cache, so past this guard the cache is built AND provably lacks the key —
            // the incremental _cache.Add below cannot throw. Don't reorder these lines.
            if (ContainsKey(key)) throw new ArgumentException($"An item with the key '{key}' already exists.");
            entries.Add(new Entry(key, value));
            if (_cacheBuilt && _cache != null) _cache.Add(key, value);
        }

        public void Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

        public bool ContainsKey(TKey key) => key != null && Cache.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key != null) return Cache.TryGetValue(key, out value);
            value = default;
            return false;
        }

        public bool Remove(TKey key)
        {
            // Single pass: find the FIRST occurrence, and capture the value of the next later duplicate (if any) —
            // once the first occurrence is removed, that duplicate becomes the effective (first-wins) value.
            var comparer = EqualityComparer<TKey>.Default;
            int first = -1;
            bool hasLaterDuplicate = false;
            TValue promoted = default;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!comparer.Equals(entries[i].Key, key)) continue;
                if (first < 0) { first = i; continue; }
                hasLaterDuplicate = true;
                promoted = entries[i].Value;
                break;
            }
            if (first < 0) return false;

            entries.RemoveAt(first);

            if (key != null && _cacheBuilt && _cache != null)
            {
                if (hasLaterDuplicate) _cache[key] = promoted;
                else _cache.Remove(key);
            }
            return true;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!TryGetValue(item.Key, out var existing)) return false;
            if (!EqualityComparer<TValue>.Default.Equals(existing, item.Value)) return false;
            return Remove(item.Key);
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
            => TryGetValue(item.Key, out var v) && EqualityComparer<TValue>.Default.Equals(v, item.Value);

        public void Clear()
        {
            entries.Clear();
            if (_cacheBuilt && _cache != null) _cache.Clear(); // stays built — both views are (consistently) empty
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
            => ((ICollection<KeyValuePair<TKey, TValue>>)Cache).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => Cache.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // ---------------------------------------------------------------------------------------------------------
        // ISerializationCallbackReceiver
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>No-op — <c>entries</c> is authoritative, so there's nothing to flatten before serialization.</summary>
        public void OnBeforeSerialize() { }

        /// <summary>
        /// Invalidate the lazy runtime view so the next read rebuilds it from the freshly deserialized entries.
        /// Deliberately does no heavy work here (mirrors <c>SceneReference.OnAfterDeserialize</c>): this runs inside
        /// Unity's serialization pipeline, so we never touch AssetDatabase / main-thread-only APIs.
        /// </summary>
        public void OnAfterDeserialize() => InvalidateCache();
    }
}
