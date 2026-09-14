using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A standalone inspector layout stored as its own asset (vs. an entry in the central database). Holds the
    /// target type's rename-proof <see cref="ScriptGuid"/>, the layout <see cref="Root"/>, and the auto-sync ledger.
    /// </summary>
    public class InspectorLayoutAsset : ScriptableObject
    {
        // Serialized-layout schema version this asset was last written at (see SasquatchSchema). 0 on legacy files.
        [SerializeField] private int schemaVersion;
        [SerializeField] private string scriptGuid;
        [SerializeField] private string targetTypeName;
        [SerializeReference] private LayoutNode root;
        // Compressed representation of the layout graph: GZip'd EditorJsonUtility JSON, base64-encoded (one YAML
        // line). Written INSTEAD of root when Project Settings → Sasquatch Inspector → Compress Layout Assets is on
        // (root then serializes null). PRECEDENCE: a non-null root always wins on load — the blob is only consulted
        // when root is null — so plain/legacy files load unchanged and a stale blob can never shadow real data.
        // Pack/hydrate live in the editor assembly (LayoutGraphStorage); this class only carries the fields.
        [SerializeField, HideInInspector] private string compressedGraph;
        // Format stamp for compressedGraph (see LayoutGraphStorage.CurrentFormat). 0 ⇒ no blob.
        [SerializeField, HideInInspector] private int compressedGraphFormat;
        // Auto-sync ledger; see SasquatchDatabaseEntry.SyncedFieldPaths. On database-backed layouts the
        // registry wrapper ALIASES the entry's list by reference — mutate in place, never reassign.
        [SerializeField] private List<string> syncedFieldPaths = new List<string>();
        // Parallel type names for the ledger (see SasquatchDatabaseEntry.SyncedFieldTypes). Same aliasing rule.
        [SerializeField] private List<string> syncedFieldTypes = new List<string>();
        // Opt-in: subclasses with no layout of their own inherit this one (appended; default off).
        [SerializeField] private bool applyToSubclasses;
        // Opt-in: draw this layout in place of the type's own [CustomEditor] (mirrors SasquatchDatabaseEntry.ForceTakeover
        // on the database-backed wrapper; set by value in LayoutRegistry.InitializeRegistryCache). See SasquatchForceTakeover.
        [SerializeField] private bool forceTakeover;
        // Opt-in: temporarily switch this layout OFF in the real Inspector without deleting it. Both live render
        // paths honour it — VisualInspectorEditor draws Unity's default inspector instead, and SasquatchForceTakeover
        // releases the row so the type's own [CustomEditor] draws. Design-time surfaces (the designer canvas,
        // Showcase, tutorial preview, export) deliberately IGNORE it: you must still be able to see and edit a
        // hidden layout. Toggled by the eye button above the designer canvas; see LayoutRegistry.SetLayoutHidden.
        [SerializeField] private bool layoutHidden;

        // Runtime-only markers stamped by LayoutRegistry on the transient deep copy it serves a subclass
        // (never on a real entry/asset). NonSerialized on purpose: an Instantiate() of an inherited copy
        // (the designer's working clone) comes out as a plain editable layout, which is exactly the
        // materialize-an-override-on-save flow.
        [System.NonSerialized] private bool isInheritedCopy;
        [System.NonSerialized] private string inheritedFromTypeName;

        /// <summary>Serialized-layout schema version this asset was last written at (see <see cref="SasquatchSchema"/>).
        /// 0 ⇒ a legacy file predating the stamp (migrated on load).</summary>
        public int SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }

        // Stable, rename-proof key: the GUID of the target type's MonoScript (.cs.meta) asset.
        public string ScriptGuid
        {
            get => scriptGuid;
            set => scriptGuid = value;
        }

        public string TargetTypeName
        {
            get => targetTypeName;
            set => targetTypeName = value;
        }

        public LayoutNode Root
        {
            get => root;
            set => root = value;
        }

        /// <summary>Compressed layout graph (GZip'd EditorJsonUtility JSON, base64), or null/empty when the graph
        /// is stored plainly in <see cref="Root"/>. A non-null <see cref="Root"/> always wins over this blob.</summary>
        public string CompressedGraph
        {
            get => compressedGraph;
            set => compressedGraph = value;
        }

        /// <summary>Format version of <see cref="CompressedGraph"/> (0 ⇒ no blob; see LayoutGraphStorage).</summary>
        public int CompressedGraphFormat
        {
            get => compressedGraphFormat;
            set => compressedGraphFormat = value;
        }

        public List<string> SyncedFieldPaths
        {
            get => syncedFieldPaths;
            set => syncedFieldPaths = value;
        }

        public List<string> SyncedFieldTypes
        {
            get => syncedFieldTypes;
            set => syncedFieldTypes = value;
        }

        /// <summary>Opt-in: a subclass with no layout of its own inherits this layout (nearest ancestor wins).</summary>
        public bool ApplyToSubclasses
        {
            get => applyToSubclasses;
            set => applyToSubclasses = value;
        }

        /// <summary>Opt-in: draw this layout in place of the type's own <c>[CustomEditor]</c> (mirrors
        /// <c>SasquatchDatabaseEntry.ForceTakeover</c> on the database-backed wrapper). See <c>SasquatchForceTakeover</c>.</summary>
        public bool ForceTakeover
        {
            get => forceTakeover;
            set => forceTakeover = value;
        }

        /// <summary>Opt-in: this layout is switched OFF in the real Inspector (both live render paths), which then
        /// shows whatever the user would see without Sasquatch Inspector — the type's own <c>[CustomEditor]</c> when
        /// it has one, otherwise Unity's default inspector. The layout itself is untouched and still fully editable
        /// in the designer. Toggled by the eye button above the designer canvas; see <c>LayoutRegistry.SetLayoutHidden</c>.</summary>
        public bool LayoutHidden
        {
            get => layoutHidden;
            set => layoutHidden = value;
        }

        /// <summary>
        /// True on the transient deep copy LayoutRegistry serves a subclass via <see cref="ApplyToSubclasses"/>.
        /// Such a copy is session-only: persistence (PersistLayoutEdit / undo registration) must no-op on it so
        /// auto-sync can never write a subclass's fields back into the ancestor's saved layout.
        /// </summary>
        public bool IsInheritedCopy
        {
            get => isInheritedCopy;
            set => isInheritedCopy = value;
        }

        /// <summary>On an inherited copy: the AssemblyQualifiedName of the ancestor type whose layout was copied.</summary>
        public string InheritedFromTypeName
        {
            get => inheritedFromTypeName;
            set => inheritedFromTypeName = value;
        }
    }
}
