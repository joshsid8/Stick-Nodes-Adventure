using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Project-wide Sasquatch Inspector preferences, edited via Project Settings → Sasquatch Inspector and stored
    /// as a single git-trackable asset (created on first visit to the settings page). Per-user
    /// preferences (e.g. the global inspector override toggle) stay in EditorPrefs, not here.
    /// </summary>
    public class SasquatchSettings : ScriptableObject
    {
        [SerializeField] private UnboundFieldSyncMode unboundFieldHandling = UnboundFieldSyncMode.AutoAppend;
        [SerializeField] private bool applyScriptAttributesOnSync = true;
        // When true (the default), fields auto-synced into an existing layout (Unbound Field Handling) adopt the
        // active Layout Preset's widget + config for their type; false keeps the historical behavior — new fields
        // arrive as plain default Unity fields. Independent of the designer import tools' per-user "Use Layout
        // Presets" toggle, which gates a different path.
        [SerializeField] private bool applyPresetsOnSync = true;
        // When true (default), fields auto-synced into an existing layout are routed into the section container
        // matching their [Header] context in code (a brand-new header creates a new section), instead of landing
        // next to their declaration-order predecessor regardless of sections. Off ⇒ the historical
        // predecessor-adjacent insertion.
        [SerializeField] private bool routeSyncedFieldsByHeader = true;
        // Null ⇒ fall back to project discovery (LayoutTreeBuilder.LoadDefaultTheme).
        [SerializeField] private SasquatchThemeAsset defaultTheme;
        // When true, hovering a field/widget card plays a subtle spring "lift" (inspector + Layout Designer).
        // Off by default — purely cosmetic, and it can make small fields a touch fiddlier to hover precisely.
        [SerializeField] private bool enableHoverAnimation = false;
        // The maximum number of Particle System Preview widgets allowed to PLAY (animate + render) at once.
        // Pressing play on a preview stops the oldest playing one(s) when this cap would be exceeded. Default 1
        // (only one plays at a time) — values above 3 can stutter the editor (each playing preview renders every
        // frame off EditorApplication.update).
        [SerializeField] [Range(1, 10)] private int maxParticlePreviews = 1;
        // The project's active Layout Preset (default widget + config per field type), applied by the designer's
        // import tools. Null ⇒ fall back to the first SasquatchLayoutPreset found in the project, else the built-in
        // recommended defaults (see LayoutPresetManager).
        [SerializeField] private SasquatchLayoutPreset layoutPreset;
        // When true, the AudioClip preview widget never opens a native audio device (winmm on Windows,
        // AudioToolbox on macOS, ALSA on Linux) — the waveform still renders, but scrubbing/playback is silent. A safety valve for
        // users on a future non-Mono editor runtime (CoreCLR) or anyone who prefers no native audio P/Invoke. The
        // macOS backend is ALSO auto-disabled on a non-Mono runtime regardless of this flag (see NativeAudioBackend);
        // the `SASQ_NO_NATIVE_AUDIO` scripting define forces silence globally at compile time.
        [SerializeField] private bool disableNativeAudioPreview = false;
        // When true (default), Sasquatch Inspector displaces another inspector framework's blanket editor body (which
        // otherwise claims every MonoBehaviour/ScriptableObject) with a STYLED type's layout via
        // SasquatchForceTakeover — no generated files, no recompiles. Off ⇒ Sasquatch Inspector defers to that framework
        // for styled types. Either way a type's own hand-written [CustomEditor] is kept unless per-type Force
        // Takeover is enabled. Project-wide (committed) so a team shares one takeover policy.
        [SerializeField] private bool takeoverFromOtherInspectors = true;
        // When true, Sasquatch Inspector keeps rolling automatic backups of the whole per-type layouts folder: a snapshot
        // taken before a save overwrites anything, throttled to about one per hour of edits, newest N kept, under the
        // always-gitignored Library/SasquatchInspector/Backups. OFF by default (opt-in) — layout assets and their snapshots
        // can grow large and not every team wants the disk churn. The manual "Restore Layouts Backup…" action still
        // snapshots the current layouts first regardless of this flag, so a restore stays reversible.
        [SerializeField] private bool enableAutomaticLayoutBackups = false;
        // When true (the default), each saved layout stores its node graph as a compressed blob inside its
        // per-type asset (typically 20-40x smaller on disk) instead of plain serialized YAML. The file stays a
        // text asset whose header fields (type, GUID, sync ledger, flags) remain readable; only the node graph
        // is packed. Off ⇒ fully human-readable/diffable YAML graphs (the historical format). Flipping the
        // setting rewrites existing layout files in the chosen format, and a file whose format doesn't match
        // the setting self-heals on load (see LayoutGraphStorage).
        [SerializeField] private bool compressLayoutAssets = true;
        // The maximum number of rolling automatic layout snapshots kept on disk (newest N; the oldest are pruned).
        // Effectively "how many historical versions of each layout are recoverable", since every snapshot holds a
        // copy of the whole layouts folder. Only consulted when automatic backups are enabled. Default 1.
        [SerializeField] [Range(1, 5)] private int maxLayoutBackups = 1;
        // How often an automatic backup may be taken (a snapshot is taken before a save, but no more than once per
        // this interval of edits). Only consulted when automatic backups are enabled. Default Hourly (historical).
        [SerializeField] private LayoutBackupFrequency backupFrequency = LayoutBackupFrequency.Hourly;
        // How the styled rows' label column grows with the inspector. Capped (default): follows Unity's
        // proportional column until it hits maxLabelColumnWidth, then gives further width to the field itself.
        // UnityStandard: proportional at any width, exactly like stock rows.
        [SerializeField] private LabelColumnMode labelColumnMode = LabelColumnMode.Capped;
        [SerializeField] [Range(120, 300)] private int maxLabelColumnWidth = 180;
        // How expandable widget rows (curve, gradient, asset preview, particle, inline editor) frame themselves.
        // Card (default, historical): each row is its own bordered rounded box separated by a margin. List: no box
        // and no gap, adjacent rows parted by a hairline, so a run of them reads as one list rather than N cards.
        [SerializeField] private ExpandableRowStyle expandableRowStyle = ExpandableRowStyle.Card;
        // How long a collection header's trash button must be HELD before the collection is emptied. The hold IS
        // the confirmation (there is deliberately no modal dialog), so this trades "impossible to trigger by
        // accident" against "quick to do on purpose". Default 0.65s. An exported standalone editor BAKES whatever
        // this is at the moment of export — it is package-free code and cannot read this asset (WidgetKitEmitter).
        [SerializeField] [Range(0.2f, 2f)] private float clearHoldSeconds = 0.65f;

        public UnboundFieldSyncMode UnboundFieldHandling
        {
            get => unboundFieldHandling;
            set => unboundFieldHandling = value;
        }

        public bool ApplyScriptAttributesOnSync
        {
            get => applyScriptAttributesOnSync;
            set => applyScriptAttributesOnSync = value;
        }

        /// <summary>When new code fields are auto-synced into an existing layout, whether they adopt the active
        /// Layout Preset's widget + config for their type (true, the default) or arrive as plain default Unity
        /// fields (false — historical behavior). Separate from the designer import tools' "Use Layout Presets".</summary>
        public bool ApplyPresetsOnSync
        {
            get => applyPresetsOnSync;
            set => applyPresetsOnSync = value;
        }

        /// <summary>When new code fields are auto-synced into an existing layout, whether they are routed into
        /// the section container matching their [Header] context in code (true, the default — a brand-new header
        /// creates a new section) or inserted next to their declaration-order predecessor regardless of sections
        /// (false — historical behavior).</summary>
        public bool RouteSyncedFieldsByHeader
        {
            get => routeSyncedFieldsByHeader;
            set => routeSyncedFieldsByHeader = value;
        }

        public SasquatchThemeAsset DefaultTheme
        {
            get => defaultTheme;
            set => defaultTheme = value;
        }

        /// <summary>Whether field/widget cards play a subtle spring "lift" on hover (inspector + designer).</summary>
        public bool EnableHoverAnimation
        {
            get => enableHoverAnimation;
            set => enableHoverAnimation = value;
        }

        /// <summary>Maximum number of Particle System Preview widgets allowed to play/render simultaneously
        /// (default 1). Pressing play stops the oldest playing preview(s) when this cap would be exceeded.</summary>
        public int MaxParticlePreviews
        {
            get => Mathf.Max(1, maxParticlePreviews);
            set => maxParticlePreviews = Mathf.Max(1, value);
        }

        /// <summary>The project's active Layout Preset asset (default widget + config per field type) used by the
        /// designer's import tools. Null falls back to project discovery / built-in recommended defaults.</summary>
        public SasquatchLayoutPreset LayoutPreset
        {
            get => layoutPreset;
            set => layoutPreset = value;
        }

        /// <summary>When true, the AudioClip preview widget never opens a native audio device — the waveform still
        /// renders but scrubbing/playback is silent. Auto-honored on non-Mono editor runtimes (CoreCLR) and by the
        /// <c>SASQ_NO_NATIVE_AUDIO</c> define; see <c>NativeAudioBackend</c>.</summary>
        public bool DisableNativeAudioPreview
        {
            get => disableNativeAudioPreview;
            set => disableNativeAudioPreview = value;
        }

        /// <summary>When true (default), Sasquatch Inspector displaces the blanket editor body drawn by another inspector
        /// framework with a styled type's layout via <c>SasquatchForceTakeover</c> (no generated files,
        /// no recompiles). Off ⇒ Sasquatch Inspector defers to that framework for styled types. A type's own hand-written
        /// <c>[CustomEditor]</c> is kept unless per-type Force Takeover is enabled.</summary>
        public bool TakeoverFromOtherInspectors
        {
            get => takeoverFromOtherInspectors;
            set => takeoverFromOtherInspectors = value;
        }

        /// <summary>When true, Sasquatch Inspector keeps rolling automatic backups of the layouts folder (a snapshot before
        /// each overwrite, throttled ~1/hour of edits, newest N kept, under the gitignored Library/). OFF by default —
        /// opt in via Project Settings ▸ Sasquatch Inspector. The manual "Restore Layouts Backup…" pre-restore snapshot
        /// ignores this flag so a restore stays reversible.</summary>
        public bool EnableAutomaticLayoutBackups
        {
            get => enableAutomaticLayoutBackups;
            set => enableAutomaticLayoutBackups = value;
        }

        /// <summary>When true (default), layout assets store their node graph as a compressed blob (typically
        /// 20-40x smaller on disk); off keeps the fully human-readable YAML graph. Header fields stay readable
        /// either way; existing files are rewritten on toggle and mismatched files self-heal on load.</summary>
        public bool CompressLayoutAssets
        {
            get => compressLayoutAssets;
            set => compressLayoutAssets = value;
        }

        /// <summary>Maximum number of rolling automatic layout snapshots kept on disk (newest N, oldest pruned;
        /// clamped to 1..5). Since each snapshot copies the whole layouts folder, this is effectively how many
        /// historical versions of every layout stay recoverable. Only consulted when automatic backups are on.</summary>
        public int MaxLayoutBackups
        {
            get => Mathf.Clamp(maxLayoutBackups, 1, 5);
            set => maxLayoutBackups = Mathf.Clamp(value, 1, 5);
        }

        /// <summary>How often an automatic backup may be taken (no more than one snapshot per this interval of edits).
        /// The enum's numeric value is the interval in minutes. Only consulted when automatic backups are on.</summary>
        public LayoutBackupFrequency BackupFrequency
        {
            get => backupFrequency;
            set => backupFrequency = value;
        }

        /// <summary>The automatic-backup throttle interval as minutes (≥ 1), derived from <see cref="BackupFrequency"/>.</summary>
        public int BackupFrequencyMinutes => Mathf.Max(1, (int)backupFrequency);

        /// <summary>How the styled rows' label column grows with the inspector: Capped (default) follows Unity's
        /// proportional column until <see cref="MaxLabelColumnWidth"/>, then gives further width to the field;
        /// UnityStandard keeps the fully proportional stock behavior at any width.</summary>
        public LabelColumnMode LabelColumnMode
        {
            get => labelColumnMode;
            set => labelColumnMode = value;
        }

        /// <summary>The label column's width cap in pixels (clamped 120–300; default 180). Only consulted while
        /// <see cref="LabelColumnMode"/> is Capped.</summary>
        public int MaxLabelColumnWidth
        {
            get => Mathf.Clamp(maxLabelColumnWidth, 120, 300);
            set => maxLabelColumnWidth = Mathf.Clamp(value, 120, 300);
        }

        /// <summary>How expandable widget rows frame themselves: Card (default) gives each row its own bordered
        /// box and a surrounding gap; List drops the box and the gap and parts adjacent rows with a hairline.</summary>
        public ExpandableRowStyle ExpandableRowStyle
        {
            get => expandableRowStyle;
            set => expandableRowStyle = value;
        }

        /// <summary>How long (in seconds, 0.2–2, default 0.65) a collection header's trash button must be held
        /// before the collection is emptied. Clamped on the way in and out, so a hand-edited asset can never
        /// produce a zero-length "hold" (an instant destructive click) or one nobody can finish.</summary>
        public float ClearHoldSeconds
        {
            get => Mathf.Clamp(clearHoldSeconds, 0.2f, 2f);
            set => clearHoldSeconds = Mathf.Clamp(value, 0.2f, 2f);
        }
    }
}
