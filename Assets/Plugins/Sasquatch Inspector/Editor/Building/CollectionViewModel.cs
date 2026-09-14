using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The view model shared by the List and Table collection renderers: turns a backing array property +
    /// the live search/sort UI state into the ordered list of RAW array indices that should be displayed,
    /// then a page window into that list. Pure, public, and static so the verification suite can unit-test
    /// the filter/sort/paginate math directly (mirrors <c>LayoutTreeBuilder.ComputeResizedWeights</c>).
    ///
    /// Sorting is VIEW-ONLY — it never mutates the array; it only changes the order of the returned indices.
    /// The renderer keeps each row's <c>userData = rawArrayIndex</c>, so reorder/delete/row-menu (which map
    /// a row back to a MoveArrayElement/DeleteArrayElementAtIndex) keep working unchanged.
    /// </summary>
    public static class CollectionViewModel
    {
        // ---- Filtering -------------------------------------------------------------------------------------

        /// <summary>True if any field of <paramref name="element"/> (recursively) contains <paramref name="query"/>.</summary>
        public static bool RowMatchesQuery(SerializedProperty element, string query)
        {
            if (element == null) return false;
            if (PropertyContainsText(element, query)) return true;

            if (element.hasChildren)
            {
                var it = element.Copy();
                var end = element.GetEndProperty();
                bool enterChildren = true;
                while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
                {
                    enterChildren = false; // siblings only on subsequent loops
                    if (PropertyContainsText(it, query)) return true;
                }
            }
            return false;
        }

        /// <summary>True if ONE element field (<paramref name="relativePath"/>) contains <paramref name="query"/>.
        /// An empty/null path matches against the element itself (a primitive value column).</summary>
        public static bool RowMatchesProperty(SerializedProperty element, string relativePath, string query)
        {
            if (element == null) return false;
            var prop = string.IsNullOrEmpty(relativePath) ? element : element.FindPropertyRelative(relativePath);
            return prop != null && PropertyContainsText(prop, query);
        }

        /// <summary>Case-insensitive substring match of a single property's value (int/bool/float/string/enum/objref/char).</summary>
        public static bool PropertyContainsText(SerializedProperty prop, string query)
        {
            if (prop == null || string.IsNullOrEmpty(query)) return false;
            string valStr = null;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    valStr = IntegerText(prop);
                    break;
                case SerializedPropertyType.Boolean:
                    valStr = prop.boolValue.ToString();
                    break;
                case SerializedPropertyType.Float:
                    valStr = prop.floatValue.ToString();
                    break;
                case SerializedPropertyType.String:
                    valStr = prop.stringValue;
                    break;
                case SerializedPropertyType.Enum:
                    if (prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length)
                        valStr = prop.enumDisplayNames[prop.enumValueIndex];
                    else
                        valStr = prop.enumValueIndex.ToString();
                    break;
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                        valStr = prop.objectReferenceValue.name;
                    break;
                case SerializedPropertyType.Character:
                    valStr = ((char)prop.intValue).ToString();
                    break;
            }

            return valStr != null && valStr.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Search text for an Integer property, which from Unity 6000.6 may be a <c>char</c> IN DISGUISE.
        ///
        /// <para>WHY. A plain <c>char</c> field still reports <c>SerializedPropertyType.Character</c> on every
        /// supported editor. A <c>char</c> LIST ELEMENT does not: measured on 6000.0 vs 6000.6 from the same
        /// fixture, <c>letters.Array.data[0]</c> reports Character/<c>char</c> on 6000.0 and
        /// <b>Integer/<c>ushort</c></b> on 6000.6 — identical to a real <c>List&lt;ushort&gt;</c> element. A
        /// list element is exactly what a TABLE COLUMN is, so without this a buyer on 6.6 types the letter
        /// they can see in the cell and the row does not match; only "122" finds it. Sorting is unaffected
        /// (code-point order equals letter order), which is why this is the one arm that needs the rescue.</para>
        ///
        /// <para>COST. The <c>prop.type</c> check is a string compare that is false for every ordinary int, so
        /// only a ushort-shaped column ever reaches the reflection — and that result is memoised per
        /// (target type, element path), so a 500-row table resolves it once, not 500 times. This runs on every
        /// keystroke over every row (see TestTableSearchKeystrokeStaysWithinBudget), which is why the cheap
        /// test comes first and the expensive one is cached behind it.</para>
        /// </summary>
        private static string IntegerText(SerializedProperty prop)
        {
            // Ordinary ints report "int"; only a ushort-shaped property can be a disguised char.
            if (!string.Equals(prop.type, "ushort", StringComparison.Ordinal))
                return prop.intValue.ToString();

            return IsCharBackedField(prop) ? ((char)prop.intValue).ToString() : prop.intValue.ToString();
        }

        // Keyed by declaring type + the element path with its INDEX STRIPPED, so every row of one column
        // shares a single entry rather than adding one per row.
        //
        // CAPPED, per the standing rule that no static cache grows for a whole session unbounded (see
        // TestStaticCachesAreCappedAndClearedOnReload's note). Entries are two short strings and a bool, and
        // the natural ceiling is "distinct ushort/char columns browsed this session" — a couple of dozen at
        // most. The cap only matters for a pathological case such as procedurally generated target types, and
        // a straight clear is the right response there: the answer is cheap to recompute and always correct.
        private const int MaxCharBackedEntries = 512;
        private static readonly Dictionary<string, bool> s_charBackedCache = new Dictionary<string, bool>();

        private static bool IsCharBackedField(SerializedProperty prop)
        {
            var target = prop.serializedObject?.targetObject;
            if (target == null) return false;

            string path = prop.propertyPath;
            // "letters.Array.data[7]" -> "letters.Array.data[]": the field type cannot vary by index.
            int bracket = path.IndexOf('[');
            string keyPath = bracket >= 0 ? path.Substring(0, bracket + 1) + "]" : path;
            var declaring = target.GetType();
            string key = declaring.FullName + "|" + keyPath;

            if (s_charBackedCache.TryGetValue(key, out bool cached)) return cached;

            bool isChar = false;
            try
            {
                BindingFactory.GetFieldInfoFromPropertyPath(declaring, path, out Type fieldType);
                isChar = fieldType == typeof(char);
            }
            catch (Exception)
            {
                // A path reflection cannot walk (a custom drawer's synthetic property, a renamed field mid-edit)
                // is simply not a char as far as this is concerned — never let search THROW over a cell.
                isChar = false;
            }

            if (s_charBackedCache.Count >= MaxCharBackedEntries) s_charBackedCache.Clear();
            s_charBackedCache[key] = isChar;
            return isChar;
        }

        // ---- Sorting comparator (mirrors PropertyContainsText's type switch) -------------------------------

        /// <summary>Compares the <paramref name="relativePath"/> field of two array elements (empty path =
        /// the elements themselves). Stable tiebreaking is left to the caller (LINQ OrderBy).</summary>
        public static int CompareElement(SerializedProperty arrayProp, int ia, int ib, string relativePath)
        {
            if (arrayProp == null) return 0;
            var ea = arrayProp.GetArrayElementAtIndex(ia);
            var eb = arrayProp.GetArrayElementAtIndex(ib);
            var pa = string.IsNullOrEmpty(relativePath) ? ea : ea.FindPropertyRelative(relativePath);
            var pb = string.IsNullOrEmpty(relativePath) ? eb : eb.FindPropertyRelative(relativePath);
            return CompareProperty(pa, pb);
        }

        /// <summary>Ordering of two like-typed property values. Null sorts first; an unsortable type returns
        /// 0 (left in place). Enums sort by declaration order (enumValueIndex), which is what users expect
        /// for ordered enums like Rarity (Common &lt; … &lt; Legendary).</summary>
        public static int CompareProperty(SerializedProperty a, SerializedProperty b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a.propertyType != b.propertyType)
                return string.Compare(a.propertyType.ToString(), b.propertyType.ToString(), StringComparison.Ordinal);

            switch (a.propertyType)
            {
                case SerializedPropertyType.Integer:   return a.longValue.CompareTo(b.longValue);
                case SerializedPropertyType.Boolean:   return a.boolValue.CompareTo(b.boolValue);
                case SerializedPropertyType.Float:     return a.doubleValue.CompareTo(b.doubleValue);
                case SerializedPropertyType.String:    return string.Compare(a.stringValue ?? "", b.stringValue ?? "", StringComparison.OrdinalIgnoreCase);
                case SerializedPropertyType.Enum:      return a.enumValueIndex.CompareTo(b.enumValueIndex);
                case SerializedPropertyType.Character: return a.intValue.CompareTo(b.intValue);
                case SerializedPropertyType.ObjectReference:
                    string na = a.objectReferenceValue != null ? a.objectReferenceValue.name : "";
                    string nb = b.objectReferenceValue != null ? b.objectReferenceValue.name : "";
                    return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);

                // Compound value types sort LEXICOGRAPHICALLY by component — x, then y, then z, then w — the
                // same rule that orders tuples, and the only one a user can predict without being told which
                // of several equally-plausible scalars (magnitude? area? luminance?) we picked. Returning 0
                // for these is what made "sort a List<Vector2>" silently do nothing at all.
                case SerializedPropertyType.Vector2:   return CompareComponents(a.vector2Value.x, b.vector2Value.x, a.vector2Value.y, b.vector2Value.y);
                case SerializedPropertyType.Vector3:   return CompareComponents(a.vector3Value.x, b.vector3Value.x, a.vector3Value.y, b.vector3Value.y, a.vector3Value.z, b.vector3Value.z);
                case SerializedPropertyType.Vector4:   return CompareComponents(a.vector4Value.x, b.vector4Value.x, a.vector4Value.y, b.vector4Value.y, a.vector4Value.z, b.vector4Value.z, a.vector4Value.w, b.vector4Value.w);
                case SerializedPropertyType.Quaternion:return CompareComponents(a.quaternionValue.x, b.quaternionValue.x, a.quaternionValue.y, b.quaternionValue.y, a.quaternionValue.z, b.quaternionValue.z, a.quaternionValue.w, b.quaternionValue.w);
                case SerializedPropertyType.Vector2Int:return CompareComponents(a.vector2IntValue.x, b.vector2IntValue.x, a.vector2IntValue.y, b.vector2IntValue.y);
                case SerializedPropertyType.Vector3Int:return CompareComponents(a.vector3IntValue.x, b.vector3IntValue.x, a.vector3IntValue.y, b.vector3IntValue.y, a.vector3IntValue.z, b.vector3IntValue.z);
                case SerializedPropertyType.Rect:      return CompareComponents(a.rectValue.x, b.rectValue.x, a.rectValue.y, b.rectValue.y, a.rectValue.width, b.rectValue.width, a.rectValue.height, b.rectValue.height);
                case SerializedPropertyType.RectInt:   return CompareComponents(a.rectIntValue.x, b.rectIntValue.x, a.rectIntValue.y, b.rectIntValue.y, a.rectIntValue.width, b.rectIntValue.width, a.rectIntValue.height, b.rectIntValue.height);
                // A colour's components are r,g,b,a — sorting them lexicographically groups by red first,
                // which is at least stable and reproducible; there is no natural total order on colour.
                case SerializedPropertyType.Color:     return CompareComponents(a.colorValue.r, b.colorValue.r, a.colorValue.g, b.colorValue.g, a.colorValue.b, b.colorValue.b, a.colorValue.a, b.colorValue.a);

                default:
                    return 0; // genuinely unsortable (AnimationCurve, Gradient, …) → stable, left in place
            }
        }

        /// <summary>
        /// True when <see cref="CompareProperty"/> can actually ORDER this property type. The sort UI asks
        /// before it offers a key, because a key that compares 0 for every pair is not "no sort" — it is a
        /// control that answers every click by doing nothing, which is exactly how a user experiences
        /// "sorting is broken". Kept beside the switch it mirrors so the two cannot drift.
        /// </summary>
        public static bool IsSortableType(SerializedPropertyType type)
        {
            switch (type)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Color:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Lexicographic compare over a flattened (a0,b0, a1,b1, …) component pair list: the first
        /// component that differs decides, so equal-x vectors fall through to y and so on. Takes double so
        /// the Int variants (Vector2Int/Vector3Int/RectInt) widen without losing their large values.</summary>
        private static int CompareComponents(params double[] pairs)
        {
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                int cmp = pairs[i].CompareTo(pairs[i + 1]);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        // ---- The one method the renderer calls: filter -> sort -> raw index list ---------------------------

        /// <summary>
        /// The ordered list of RAW array indices to display. Filters by <paramref name="query"/> (scoped to
        /// <paramref name="searchProperty"/> when set, else all fields), then — if <paramref name="sortPropertyPath"/>
        /// is non-null ("" sorts by the element value) — stable-sorts by that field. Natural order (no query,
        /// null sort) returns <c>[0..N-1]</c>.
        /// </summary>
        public static List<int> BuildVisibleIndices(
            SerializedProperty arrayProp, string query, string searchProperty,
            string sortPropertyPath, bool sortDescending)
        {
            var result = new List<int>();
            if (arrayProp == null || !arrayProp.isArray) return result;

            int n = arrayProp.arraySize;
            query = query?.Trim();
            bool hasQuery = !string.IsNullOrEmpty(query);
            bool scoped = !string.IsNullOrEmpty(searchProperty);

            for (int i = 0; i < n; i++)
            {
                if (hasQuery)
                {
                    var el = arrayProp.GetArrayElementAtIndex(i);
                    bool match = scoped ? RowMatchesProperty(el, searchProperty, query) : RowMatchesQuery(el, query);
                    if (!match) continue;
                }
                result.Add(i);
            }

            if (sortPropertyPath != null)
            {
                // LINQ OrderBy is a STABLE sort: equal keys keep their input (ascending-index) order, so
                // ties stay in array order in BOTH directions (descending negates a non-zero compare only).
                var comparer = Comparer<int>.Create((a, b) =>
                {
                    int cmp = CompareElement(arrayProp, a, b, sortPropertyPath);
                    return sortDescending ? -cmp : cmp;
                });
                result = result.OrderBy(i => i, comparer).ToList();
            }

            return result;
        }

        // ---- Pagination math (pure; unit-tested) -----------------------------------------------------------

        /// <summary>Number of pages for a visible count (always at least 1). pageSize &lt;= 0 = one page.</summary>
        public static int PageCount(int visibleCount, int pageSize)
        {
            if (pageSize <= 0) return 1;
            if (visibleCount <= 0) return 1;
            return (visibleCount + pageSize - 1) / pageSize;
        }

        /// <summary>Clamps a page index into [0, PageCount-1].</summary>
        public static int ClampPage(int page, int visibleCount, int pageSize)
        {
            int pages = PageCount(visibleCount, pageSize);
            if (page < 0) return 0;
            if (page > pages - 1) return pages - 1;
            return page;
        }

        /// <summary>The (start, count) window of visible indices for a page. pageSize &lt;= 0 returns the whole
        /// set (pagination off); an empty set returns (0, 0); the last page may be partial.</summary>
        public static (int start, int count) PageSlice(int visibleCount, int pageSize, int page)
        {
            if (pageSize <= 0) return (0, Mathf.Max(0, visibleCount));
            if (visibleCount <= 0) return (0, 0);
            page = ClampPage(page, visibleCount, pageSize);
            int start = page * pageSize;
            int count = Mathf.Clamp(visibleCount - start, 0, pageSize);
            return (start, count);
        }
    }
}
