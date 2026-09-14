using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Owns HOW a layout's node graph is represented inside its per-type asset file (see
    /// <see cref="InspectorLayoutAsset"/>): plainly serialized via <c>[SerializeReference]</c> (the historical,
    /// human-diffable format), or — when Project Settings → Sasquatch Inspector → Compress Layout Assets is on
    /// (the default) — as a GZip'd <see cref="EditorJsonUtility"/> JSON blob, base64-encoded so it serializes as
    /// a single YAML line. The blob form is typically 20-40x smaller: every node serializes its full override
    /// tables even when nothing is overridden, which is massively redundant text.
    ///
    /// INVARIANTS:
    ///   • A non-null <see cref="InspectorLayoutAsset.Root"/> always wins on load; the blob is only consulted
    ///     when Root is null. Plain/legacy files therefore load unchanged, and a stale blob (e.g. a global
    ///     Ctrl+S that wrote a hydrated graph next to an old blob) can never shadow real data — the fat file
    ///     simply re-packs on its next save.
    ///   • Every persist site for a standalone layout asset routes through <see cref="SaveLayoutAsset"/>
    ///     (LayoutRegistry.Save / SetForceTakeover / FlushPendingLayoutEdits, and the designer's standalone
    ///     branch), so the on-disk representation always matches the setting after a save.
    ///   • <see cref="SaveLayoutAsset"/> hydrates BEFORE packing, so "Root == null" always means "genuinely
    ///     empty layout", never "not yet hydrated" — a flag-only save (SetForceTakeover) can't wipe the graph.
    ///   • A loaded file whose representation doesn't match the setting is queued for a debounced re-save
    ///     (<see cref="ReconcileOnLoad"/>) — the automatic self-heal that also performs the one-time bulk
    ///     conversion when the setting first turns on, guarded by a one-per-session safety snapshot.
    /// </summary>
    internal static class LayoutGraphStorage
    {
        /// <summary>Format stamp written to <see cref="InspectorLayoutAsset.CompressedGraphFormat"/>.
        /// v1 = GZip(UTF8(EditorJsonUtility JSON of a single-[SerializeReference]-root carrier)), base64.</summary>
        internal const int CurrentFormat = 1;

        // EditorJsonUtility needs a UnityEngine.Object host to serialize a [SerializeReference] graph (the
        // managed-references table hangs off the host object). Transient, never persisted.
        private sealed class GraphCarrier : ScriptableObject
        {
            [SerializeReference] public LayoutNode Root;
        }

        private static bool _warnedNewerBlobFormat;
        private static bool _conversionSnapshotTaken;
        // Dedups the corrupt-blob error so a re-scan / cache rebuild of the same unreadable blob doesn't
        // re-spam the console. Keyed by blob content, so genuinely-distinct corrupt blobs each surface once.
        private static readonly HashSet<string> _warnedCorruptBlobs = new HashSet<string>();

        internal static bool CompressionEnabled => SasquatchSettingsManager.Current.CompressLayoutAssets;

        // --- Node type-rename migration seam (compressed blob format) ---------------------------------------
        // [SerializeReference] embeds each node's concrete type (class + namespace) inside the blob JSON, so
        // renaming or moving a LayoutNode subclass would strand EVERY existing blob (FromJsonOverwrite can't
        // resolve the retired type → the node drops silently). If you ever rename/move a node type, add an
        // old→new entry below and the blob's on-disk type strings are rewritten before deserialization.
        // Keep plain (uncompressed) files in step by also tagging the renamed type with Unity's
        // [MovedFrom(...)] attribute — that covers the SerializeReference path AssetDatabase loads directly.
        // EMPTY today = node type names/namespaces are frozen; this is the seam that unfreezes them safely.
        private static readonly (string oldType, string newType)[] _nodeTypeRenames =
            Array.Empty<(string, string)>();

        // Rewrites retired node type identifiers in the raw blob JSON. Inert (returns the input unchanged) while
        // _nodeTypeRenames is empty. When adding an entry, verify the exact on-disk token against a real blob —
        // EditorJsonUtility stores managed-reference types as {"class":"X","ns":"Y","asm":"Z"}; match on the
        // class token so a user string that merely contains the old name can't be corrupted.
        private static string ApplyNodeTypeRenames(string json)
        {
            if (_nodeTypeRenames.Length == 0 || string.IsNullOrEmpty(json)) return json;
            foreach (var (oldType, newType) in _nodeTypeRenames)
                json = json.Replace($"\"class\":\"{oldType}\"", $"\"class\":\"{newType}\"");
            return json;
        }

        /// <summary>Packs <paramref name="root"/> into the v1 blob format (null in ⇒ null out).</summary>
        internal static string Compress(LayoutNode root)
        {
            if (root == null) return null;
            var carrier = ScriptableObject.CreateInstance<GraphCarrier>();
            carrier.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                carrier.Root = root;
                string json = EditorJsonUtility.ToJson(carrier, prettyPrint: false);
                byte[] raw = Encoding.UTF8.GetBytes(json);
                using var packed = new MemoryStream();
                using (var gz = new GZipStream(packed, System.IO.Compression.CompressionLevel.Optimal))
                    gz.Write(raw, 0, raw.Length);
                return Convert.ToBase64String(packed.ToArray());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(carrier);
            }
        }

        /// <summary>Unpacks a v1 blob back into a graph. Returns null (with an error log) on a corrupt blob —
        /// callers keep the blob intact so the data stays recoverable.</summary>
        internal static LayoutNode Decompress(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] packed = Convert.FromBase64String(base64);
                using var input = new MemoryStream(packed);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gz.CopyTo(output);
                string json = Encoding.UTF8.GetString(output.ToArray());
                json = ApplyNodeTypeRenames(json);

                var carrier = ScriptableObject.CreateInstance<GraphCarrier>();
                carrier.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(json, carrier);
                    return carrier.Root;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(carrier);
                }
            }
            catch (Exception e)
            {
                if (_warnedCorruptBlobs.Add(base64))
                {
                    Debug.LogError("[Sasquatch Inspector] Failed to unpack a compressed layout graph — the layout will " +
                                   "render as unstyled until restored (Project Settings ▸ Sasquatch Inspector ▸ " +
                                   "\"Restore Layouts Backup…\", or your version control). " + e.Message);
                }
                return null;
            }
        }

        /// <summary>
        /// Loads the graph out of the blob into <see cref="InspectorLayoutAsset.Root"/> when (and only when)
        /// Root is null. In-memory only — never dirties the asset, so the on-disk representation is untouched.
        /// Safe to call repeatedly and on plain/legacy assets (no-op when Root is already set or no blob exists).
        /// </summary>
        // Assets this domain has already hydrated or saved. Used only to tell a freshly deserialized asset
        // from one already in hand — see the both-forms repair in ReconcileOnLoad. The id is stable for an
        // asset within a session, and the set dies with the domain, which is exactly the intended lifetime.
        //
        // Keyed through UnityObjectId.Of, NOT Object.GetInstanceID(): that method is ERROR-level obsolete
        // (CS0619) on Unity 6000.5, so calling it directly does not warn — it fails the build of the whole
        // package on a supported editor, and does so invisibly on any machine running an older one.
        // TestNoDirectInstanceIdCalls is the tripwire, and it caught exactly this.
        private static readonly HashSet<int> _touchedThisDomain = new HashSet<int>();

        /// <summary>True the first time this domain sees <paramref name="asset"/> — i.e. its fields still
        /// hold what the FILE says, because nothing has hydrated or re-saved it yet.</summary>
        private static bool FirstSightThisDomain(InspectorLayoutAsset asset)
            => asset != null && !_touchedThisDomain.Contains(UnityObjectId.Of(asset));

        /// <summary>
        /// Does the asset's FILE carry a non-null <c>root</c> AND a non-empty <c>compressedGraph</c> — the
        /// damaged both-forms shape? This is the authority the in-memory object cannot be.
        ///
        /// <para>Fails SAFE in every direction it cannot answer (binary serialization, a missing or
        /// unreadable file, an unexpected layout): it returns false, and the file is left exactly as it is.
        /// A false negative costs nothing but the status quo; a false positive would rewrite a healthy file,
        /// so every uncertainty resolves to "not damaged".</para>
        /// </summary>
        private static bool FileHasBothGraphForms(InspectorLayoutAsset asset)
        {
            // Binary/mixed serialization gives no text to read. Nothing to do but leave it alone.
            if (EditorSettings.serializationMode != SerializationMode.ForceText) return false;

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return false;

            string text;
            try
            {
                string full = Path.GetFullPath(assetPath);
                if (!File.Exists(full)) return false;
                text = File.ReadAllText(full);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            // A serialized null [SerializeReference] root is "rid: -2"; anything else is a real node tree.
            int rootAt = text.IndexOf("\n  root:", StringComparison.Ordinal);
            if (rootAt < 0) return false;
            int ridAt = text.IndexOf("rid:", rootAt, StringComparison.Ordinal);
            if (ridAt < 0) return false;
            int ridEnd = text.IndexOf('\n', ridAt);
            string rid = (ridEnd < 0 ? text.Substring(ridAt) : text.Substring(ridAt, ridEnd - ridAt)).Trim();
            if (rid == "rid: -2") return false;   // healthy compressed file: the blob IS the graph

            // …and a blob alongside it. A healthy PLAIN file has the key with nothing after it.
            const string blobKey = "\n  compressedGraph:";
            int blobAt = text.IndexOf(blobKey, StringComparison.Ordinal);
            if (blobAt < 0) return false;
            int blobValueStart = blobAt + blobKey.Length;
            int blobEnd = text.IndexOf('\n', blobValueStart);
            string blobValue = blobEnd < 0
                ? text.Substring(blobValueStart)
                : text.Substring(blobValueStart, blobEnd - blobValueStart);
            return blobValue.Trim().Length > 0;
        }

        internal static void Hydrate(InspectorLayoutAsset asset)
        {
            if (asset == null) return;
            // Marked BEFORE the early-outs, and unconditionally: what this records is "this session has
            // already looked inside that asset", which is true whether or not there was anything to
            // decompress. SaveLayoutAsset calls Hydrate first, so a save marks it too. See
            // FirstSightThisDomain, which uses it to tell a freshly deserialized asset from one already in
            // hand — the distinction the both-forms repair below depends on.
            _touchedThisDomain.Add(UnityObjectId.Of(asset));
            if (asset.Root != null) return;
            if (string.IsNullOrEmpty(asset.CompressedGraph)) return;
            if (asset.CompressedGraphFormat > CurrentFormat)
            {
                if (!_warnedNewerBlobFormat)
                {
                    _warnedNewerBlobFormat = true;
                    Debug.LogWarning($"[Sasquatch Inspector] A layout was compressed by a NEWER version of Sasquatch " +
                                     $"Inspector (blob format v{asset.CompressedGraphFormat}; this version understands " +
                                     $"v{CurrentFormat}) and cannot be read. Updating Sasquatch Inspector is recommended.");
                }
                return;
            }
            asset.Root = Decompress(asset.CompressedGraph);
        }

        /// <summary>
        /// THE persist path for a standalone per-type layout asset: hydrates (so a flag-only save can never
        /// mistake "not yet hydrated" for "empty" and wipe the graph), stamps the schema version, writes the
        /// graph in the representation the Compress Layout Assets setting asks for, and flushes to disk. The
        /// in-memory <see cref="InspectorLayoutAsset.Root"/> stays populated afterwards either way — only the
        /// serialized form changes.
        /// </summary>
        internal static void SaveLayoutAsset(InspectorLayoutAsset asset)
        {
            if (asset == null) return;
            Hydrate(asset);

            // DATA-LOSS GUARD: if hydration failed (corrupt/truncated blob, or a blob written by a NEWER
            // version this build can't read), Root stays null while CompressedGraph still holds the ONLY copy
            // of the layout. Writing now would fall into the else-branch below and null the blob — permanently
            // wiping the user's customized layout. Bail instead; the unreadable blob stays on disk, recoverable
            // via a backup restore or version control. Never null CompressedGraph unless a live Root replaces it.
            if (asset.Root == null && !string.IsNullOrEmpty(asset.CompressedGraph)) return;

            asset.SchemaVersion = SasquatchSchema.CurrentVersion;
            // Drop the migrated-away legacy per-condition list so the persisted graph carries only ConditionRules.
            ConditionModel.StripLegacyConditionsTree(asset.Root);

            var live = asset.Root;
            if (CompressionEnabled && live != null)
            {
                try
                {
                    asset.CompressedGraph = Compress(live);
                    asset.CompressedGraphFormat = CurrentFormat;
                    asset.Root = null; // serialized null; the blob is the on-disk graph
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
                finally
                {
                    // The asset is clean after the save, so restoring the live graph never rewrites the file.
                    asset.Root = live;
                }
            }
            else
            {
                asset.CompressedGraph = null;
                asset.CompressedGraphFormat = 0;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        /// <summary>
        /// Load-time hook (registry cache scan + asset postprocessor): hydrates a compressed graph, and when the
        /// file's on-disk representation doesn't match the Compress Layout Assets setting, queues a debounced
        /// re-save through <see cref="SaveLayoutAsset"/> so the file self-heals — no manual "fix it" action.
        /// This is also what performs the one-time bulk conversion of pre-existing plain files when the setting
        /// is on: before the first such rewrite of a session, an unconditional safety snapshot of the layouts
        /// folder is taken (mirroring the manual restore's pre-restore snapshot), regardless of the automatic-
        /// backup opt-in.
        /// </summary>
        internal static void ReconcileOnLoad(InspectorLayoutAsset asset)
        {
            if (asset == null) return;

            // Judge the DISK representation before hydration mutates Root. A hydrated-in-memory asset shows
            // Root != null AND a non-empty blob, which correctly matches neither of these two tests.
            bool compressedOnDisk = asset.Root == null && !string.IsNullOrEmpty(asset.CompressedGraph);
            bool plainOnDisk = asset.Root != null && string.IsNullOrEmpty(asset.CompressedGraph);

            // ---- BOTH forms in one file: repairing the damage the old GUID backfill did ------------------
            //
            // A file carrying the full node tree AND a blob of the same graph is not a representation this
            // package ever means to write. It is the footprint of a save that bypassed SaveLayoutAsset while
            // Root was hydrated (LayoutRegistry's ScriptGuid backfill, fixed 2026-08-20) and it makes the file
            // ~240x its proper size while behaving perfectly, so nothing else will ever notice it.
            //
            // ⚠ THE IN-MEMORY SHAPE CANNOT DECIDE THIS, and assuming it can is measured, not theoretical:
            // a first attempt tested `Root != null && blob non-empty` and queued a re-save for 57 HEALTHY
            // layouts, rewriting every compressed blob in the project. That shape is also what every asset
            // this session has already hydrated looks like, and what SaveLayoutAsset restores in its finally.
            //
            // So the question is answered from the FILE, behind a cheap gate that makes the read almost never
            // happen:
            //   1. FirstSightThisDomain — on a repeat visit the object has already been hydrated, so its
            //      shape says nothing. Only a freshly deserialized asset is worth asking about.
            //   2. the ambiguous shape itself — a healthy COMPRESSED asset deserializes with Root null, and a
            //      healthy PLAIN one with an empty blob, so neither reaches the read at all.
            //   3. FileHasBothGraphForms — the authority. It fails safe: anything it cannot parse reads as
            //      "not damaged", which leaves the file exactly as it is today.
            // In a healthy project that is zero file reads; in a damaged one, one read per damaged asset, once.
            bool bothFormsOnDisk = FirstSightThisDomain(asset)
                                   && asset.Root != null
                                   && !string.IsNullOrEmpty(asset.CompressedGraph)
                                   && FileHasBothGraphForms(asset);

            Hydrate(asset);

            bool wantCompressed = CompressionEnabled;
            if (bothFormsOnDisk || (wantCompressed && plainOnDisk) || (!wantCompressed && compressedOnDisk))
            {
                TakeConversionSnapshotOnce();
                LayoutRegistry.QueueLayoutResave(asset);
            }
        }

        /// <summary>Rewrites every per-type layout asset in the representation the setting currently asks for
        /// (settings-page toggle handler). Takes a safety snapshot first. Returns the number rewritten.</summary>
        internal static int ResaveAllLayoutAssets()
        {
            _conversionSnapshotTaken = false;
            TakeConversionSnapshotOnce();

            int count = 0;
            string dbPath = SasquatchPaths.DatabasePath;
            foreach (string guid in AssetDatabase.FindAssets("t:InspectorLayoutAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == dbPath) continue;
                var asset = AssetDatabase.LoadAssetAtPath<InspectorLayoutAsset>(path);
                if (asset == null) continue;
                SaveLayoutAsset(asset);
                count++;
            }
            return count;
        }

        // One unconditional layouts-folder snapshot per session before the first representation rewrite —
        // format conversion is a one-time, whole-store operation like the manual restore, so it earns the same
        // pre-snapshot regardless of the automatic-backup opt-in. Best-effort: never blocks the save.
        private static void TakeConversionSnapshotOnce()
        {
            if (_conversionSnapshotTaken) return;
            _conversionSnapshotTaken = true;
            try
            {
                LayoutRegistry.BackupService.WriteSnapshot(SasquatchSettingsManager.Current.MaxLayoutBackups);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sasquatch Inspector] Could not snapshot layouts before format conversion: " + e.Message);
            }
        }
    }
}
