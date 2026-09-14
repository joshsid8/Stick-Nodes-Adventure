using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>One type's saved layout inside the central database: its rename-proof <see cref="ScriptGuid"/> key, the layout <see cref="Root"/>, and the auto-sync field ledger.</summary>
    [Serializable]
    public class SasquatchDatabaseEntry
    {
        // Stable, rename-proof primary key: the GUID of the target type's MonoScript (.cs.meta) asset.
        [SerializeField] private string scriptGuid;
        // Human-readable label + legacy fallback key (AssemblyQualifiedName). Breaks on rename, hence scriptGuid.
        [SerializeField] private string targetTypeName;
        [SerializeReference] private LayoutNode root;
        // Auto-sync ledger: every top-level bindable path this layout has accounted for. A code field
        // counts as "new" (and eligible for auto-add) only when it is neither bound nor listed here —
        // so a field the user deliberately removed from the layout is never force-re-added.
        [SerializeField] private List<string> syncedFieldPaths = new List<string>();
        // Parallel to syncedFieldPaths: entry i holds Type.FullName of the field at paths[i] ("" = unknown).
        // Captured while a field is still alive so a later rename (delete+add in one compile) can pair the
        // removed path with the added field by type — a deleted field's type is unrecoverable by reflection.
        [SerializeField] private List<string> syncedFieldTypes = new List<string>();
        // Opt-in: subclasses with no layout of their own inherit this one (appended; default off).
        [SerializeField] private bool applyToSubclasses;
        // Opt-in: when this type ALSO has its own hand-written [CustomEditor], draw this layout in its place by
        // hiding that editor's inspector body and rendering the layout on the header host. Default off ⇒ Sasquatch
        // Inspector defers to the custom editor.
        // Only meaningful together with a non-null Root. See SasquatchForceTakeover.
        [SerializeField] private bool forceTakeover;

        /// <summary>Parameterless constructor for serialization.</summary>
        public SasquatchDatabaseEntry() {}

        /// <summary>Creates an entry from a type name and layout root (legacy; prefer the script-GUID overload).</summary>
        public SasquatchDatabaseEntry(string targetTypeName, LayoutNode root)
        {
            this.targetTypeName = targetTypeName;
            this.root = root;
        }

        /// <summary>Creates an entry keyed by the rename-proof <paramref name="scriptGuid"/>.</summary>
        public SasquatchDatabaseEntry(string scriptGuid, string targetTypeName, LayoutNode root)
        {
            this.scriptGuid = scriptGuid;
            this.targetTypeName = targetTypeName;
            this.root = root;
        }

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

        /// <summary>Opt-in: draw this layout in place of the type's own hand-written <c>[CustomEditor]</c> (by
        /// hiding that editor's body and rendering the layout on the header host). Default off ⇒ defer to the
        /// custom editor. Only takes effect with a non-null <see cref="Root"/>. See <c>SasquatchForceTakeover</c>.</summary>
        public bool ForceTakeover
        {
            get => forceTakeover;
            set => forceTakeover = value;
        }
    }

    /// <summary>The project's central store of per-type inspector layouts (<see cref="SasquatchDatabaseEntry"/>).</summary>
    public class SasquatchInspectorDatabase : ScriptableObject
    {
        // Serialized-layout schema version this file was last written at (see SasquatchSchema). 0 on legacy files
        // saved before the stamp existed — treated as "older than v1" and migrated on load. Stamped to
        // SasquatchSchema.CurrentVersion at every save by LayoutRegistry.
        [SerializeField] private int schemaVersion;

        [SerializeField] private List<SasquatchDatabaseEntry> entries = new List<SasquatchDatabaseEntry>();

        // Legacy: Force-Takeover was once a database-level list of script GUIDs; the opt-in now lives per entry
        // on SasquatchDatabaseEntry.ForceTakeover. Retained only so older databases deserialize without dropping
        // the field — nothing reads this list.
        [SerializeField] private List<string> forceTakeoverScriptGuids = new List<string>();

        public List<SasquatchDatabaseEntry> Entries => entries;

        public List<string> ForceTakeoverScriptGuids => forceTakeoverScriptGuids;

        /// <summary>Serialized-layout schema version this database was last written at (see <see cref="SasquatchSchema"/>).
        /// 0 ⇒ a legacy file predating the stamp (migrated on load).</summary>
        public int SchemaVersion
        {
            get => schemaVersion;
            set => schemaVersion = value;
        }
    }
}
