using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Project Settings → Sasquatch Inspector. Project-wide preferences live on the <see cref="SasquatchSettings"/>
    /// asset (created on first visit, git-trackable); the remaining per-user (EditorPrefs) preferences are
    /// "Enable Spring Animations" (see <see cref="EditorSpringFX.MotionEnabled"/>) and "Collection Conversion
    /// Tips" (see <see cref="LayoutTreeBuilder.ShowCollectionConversionTips"/>).
    ///
    /// The page is styled to match the Sasquatch Inspector tool: settings are grouped into titled, rounded cards
    /// (<see cref="MakeCard"/>) painted from <see cref="DesignerPalette"/>, and every explanatory note is
    /// hidden behind a round "?" chip that reveals an accent-tinted info card beneath its field on click
    /// (<see cref="AddField"/>), so the page reads clean by default.
    ///
    /// The global inspector override (<see cref="SasquatchEditorMenu"/>) is surfaced as the "Enable Sasquatch
    /// Inspector" master toggle at the top of the Inspectors card. It defaults on (installing the asset implies
    /// you want its inspectors); turning it off makes every inspector fall back to Unity's exact default with no
    /// recompile — the documented global opt-out for anyone who wants the tool silent.
    /// </summary>
    public static class SasquatchSettingsPage
    {
        // Shared label-column width so every field's control lines up (see AlignFieldLabels in BuildUI).
        // Sized to clear the longest label ("Apply Script Attributes On Sync") without truncation.
        private const float LabelColumnWidth = 200f;

        /// <summary>Registers the Project Settings ▸ Sasquatch Inspector page.</summary>
        [SettingsProvider]
        public static SettingsProvider CreateSasquatchSettingsProvider()
        {
            return new SettingsProvider("Project/Sasquatch Inspector", SettingsScope.Project)
            {
                activateHandler = (searchContext, root) => BuildUI(root),
                keywords = new HashSet<string>
                {
                    "Sasquatch", "Inspector", "Layout", "Sync", "Unbound", "Theme", "Customize", "Hover", "Animation", "Spring", "Motion",
                    "Header", "Routing", "Section", "Audio", "Native", "Sound", "Preview", "Skin", "Dark", "Light",
                    "Backup", "Backups", "Snapshot", "Recovery", "Restore", "Frequency", "Kept", "Takeover", "Preset", "Particle",
                    "Dictionary", "HashSet", "Collection", "Conversion", "Tips",
                    "Hold", "Clear", "Trash", "Empty", "List", "Delete"
                }
            };
        }

        private static void BuildUI(VisualElement root)
        {
            var settings = SasquatchSettingsManager.GetOrCreate();
            var so = new SerializedObject(settings);

            // Unlike IMGUI settings pages, a UI Toolkit activateHandler root is NOT wrapped in a scroll
            // view by the Project Settings window — and UI Toolkit's default flex-shrink of 1 means a
            // window shorter than the content flex-compresses every row toward zero height (overlapping
            // labels, vanishing fields) instead of scrolling. Provide the scroll view ourselves.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var container = new VisualElement();
            container.style.marginLeft = 9;
            container.style.marginRight = 9;
            container.style.marginTop = 2;
            scroll.Add(container);

            // ---- Page header ----
            var title = new Label("Sasquatch Inspector");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = DesignerPalette.TextBright;
            title.style.marginBottom = 6;
            container.Add(title);

            var headerRule = new VisualElement();
            headerRule.style.height = 2;
            headerRule.style.marginBottom = 12;
            headerRule.style.backgroundColor = DesignerPalette.Accent;
            headerRule.style.width = 44; // short accent underline, not a full divider
            Round(headerRule, 1);
            container.Add(headerRule);

            // ===================== Inspectors =====================
            var inspectorsCard = MakeCard(container, "Inspectors");

            // Master on/off (per-user, EditorPrefs via SasquatchEditorMenu). Default on. When off, our
            // compiled-in fallback editor draws Unity's exact default for every type (deferring to any real
            // [CustomEditor]) — no layouts, no header pencil, no takeover — observably identical to the tool
            // being absent, with no recompile. This is the documented global opt-out.
            var enableToggle = new Toggle("Enable Sasquatch Inspector")
            {
                tooltip = "Master switch. Off ⇒ every inspector draws Unity's exact default and Sasquatch Inspector " +
                          "goes silent (no layouts, no header pencil, no takeover) — effective immediately, no " +
                          "recompile, no files changed. Global per user, stored in EditorPrefs."
            };
            enableToggle.AddToClassList("unity-base-field__aligned");
            enableToggle.SetValueWithoutNotify(SasquatchEditorMenu.IsOverrideEnabled());
            enableToggle.RegisterValueChangedCallback(evt => SasquatchEditorMenu.SetOverrideEnabled(evt.newValue));
            // Cheap poll in case code flips it behind our back (mirrors the motion toggle below).
            enableToggle.schedule.Execute(() =>
                enableToggle.SetValueWithoutNotify(SasquatchEditorMenu.IsOverrideEnabled())).Every(500);
            AddField(inspectorsCard, enableToggle,
                "The master on/off for Sasquatch Inspector. Off, every inspector falls back to Unity's exact default " +
                "(any hand-written [CustomEditor] is still honored) and the header pencil, layouts and takeover all " +
                "go quiet — no files change and no recompile happens, so you can flip it back on any time. Stored per user.");

            var takeoverField = new PropertyField(so.FindProperty("takeoverFromOtherInspectors"), "Take Over Other Inspectors")
            {
                tooltip = "When on (default), a type you've styled draws its Sasquatch Inspector layout in place of another " +
                          "inspector framework's inspector body (one that otherwise draws every " +
                          "MonoBehaviour/ScriptableObject). Off ⇒ Sasquatch Inspector defers to that framework for styled " +
                          "types. Either way, a type with its own hand-written [CustomEditor] keeps that editor " +
                          "unless you enable Force Takeover for it in the Layout Designer."
            };
            AddField(inspectorsCard, takeoverField,
                "Only affects projects that also have another inspector framework installed. Sasquatch Inspector " +
                "draws your saved layouts in place of that framework's inspector body, with no generated files and " +
                "no recompiles. A type's own hand-written [CustomEditor] is kept unless " +
                "you enable Force Takeover for it in the Layout Designer.");

            // Per-user (EditorPrefs). The guidance card for a plain Dictionary/HashSet field carries a
            // "Don't show this again" button that turns this off — this toggle is the way back on.
            var collectionTipsToggle = new Toggle("Collection Conversion Tips")
            {
                tooltip = "Show a conversion tip for a Dictionary/HashSet field the inspector can't draw — in " +
                          "the inspector and the Layout Designer alike. The tip's \"Don't show this again\" " +
                          "button turns this off. Per user."
            };
            collectionTipsToggle.AddToClassList("unity-base-field__aligned");
            collectionTipsToggle.SetValueWithoutNotify(LayoutTreeBuilder.ShowCollectionConversionTips);
            collectionTipsToggle.RegisterValueChangedCallback(evt => LayoutTreeBuilder.ShowCollectionConversionTips = evt.newValue);
            // Cheap poll so a card's "Don't show this again" click reflects here while the page is open.
            collectionTipsToggle.schedule.Execute(() =>
                collectionTipsToggle.SetValueWithoutNotify(LayoutTreeBuilder.ShowCollectionConversionTips)).Every(500);
            AddField(inspectorsCard, collectionTipsToggle,
                "A SortedDictionary, SortedList, HashSet or SortedSet field can't be serialized by Unity on any " +
                "version, and a plain Dictionary needs an explicit [SerializeField] (Unity 6.6+) or a " +
                "serializable stand-in (6.0-6.5) — either way the inspector has nothing to draw for it. When " +
                "this is on, such fields show an info card with the fix for the editor you're on, and a " +
                "click-to-copy line, instead of silently showing nothing — in the inspector and on the Layout " +
                "Designer canvas alike. Devs who keep plain Dictionaries around on purpose can dismiss the card " +
                "right from either (\"Don't show this again\") — this toggle re-enables it. Stored per user.");

            var clearHoldField = new PropertyField(so.FindProperty("clearHoldSeconds"), "List Clear Hold Time")
            {
                tooltip = "The trash button on a list header empties it, and you have to hold it rather than " +
                          "click it. This is how long (0.2–2 seconds, default 0.65)."
            };
            AddField(inspectorsCard, clearHoldField,
                "Emptying a list is a hold, not a click, so you can't wipe one by mis-clicking. Turn it up if " +
                "you want more of a safety net, down if you find it slow.");

            // ===================== Field Sync =====================
            var fieldSyncCard = MakeCard(container, "Field Sync");

            var syncModeField = new PropertyField(so.FindProperty("unboundFieldHandling"), "Unbound Field Handling");
            var syncInfo = AddField(fieldSyncCard, syncModeField, " "); // text filled by RefreshSyncModeUI

            var attributesToggle = new PropertyField(so.FindProperty("applyScriptAttributesOnSync"),
                "Apply Script Attributes On Sync")
            {
                tooltip = "When adding new fields, also map their code attributes onto the created widgets — " +
                          "[Tooltip], [Range], [Min], [TextArea]/[Multiline], [Space], [ColorUsage], " +
                          "[GradientUsage] and [NonReorderable], each where it applies."
            };
            AddField(fieldSyncCard, attributesToggle);

            var syncPresetToggle = new PropertyField(so.FindProperty("applyPresetsOnSync"),
                "Sync New Fields As Presets")
            {
                tooltip = "When new serialized fields are auto-added to an existing layout (per Unbound Field " +
                          "Handling), give them your Layout Preset's widget + configuration for their type. " +
                          "Off ⇒ they arrive as plain default Unity fields. (Separate from the Layout Designer " +
                          "import tools' own 'Use Layout Presets' toggle.)"
            };
            AddField(fieldSyncCard, syncPresetToggle);

            var headerRoutingToggle = new PropertyField(so.FindProperty("routeSyncedFieldsByHeader"),
                "Smart Header Routing")
            {
                tooltip = "When new serialized fields are auto-added to an existing layout, place them inside " +
                          "the section that matches their [Header] in code (a brand-new header creates a new " +
                          "section, like Smart Attribute Import). Off ⇒ new fields land next to the previous " +
                          "field in declaration order, whatever section that happens to be in."
            };
            AddField(fieldSyncCard, headerRoutingToggle);

            // ===================== Appearance =====================
            var appearanceCard = MakeCard(container, "Appearance");

            // Per-user (EditorPrefs), not on the settings asset.
            var motionToggle = new Toggle("Enable Spring Animations")
            {
                tooltip = "Play spring animations across Sasquatch Inspector's editor chrome — the Layout Designer " +
                          "canvas (drag, reorder, gaps, tab transitions), popups and banners, and rendered " +
                          "inspectors (card pop-ins, collection add/remove). When off, everything snaps " +
                          "instantly. Global per user, stored in EditorPrefs."
            };
            motionToggle.AddToClassList("unity-base-field__aligned");
            // UserMotionPreference, not MotionEnabled: this toggle IS the user's durable choice, and it is the
            // only place allowed to write it. Reading it also means a test or capture run that has temporarily
            // suppressed motion can't make the toggle lie about what the user picked.
            motionToggle.SetValueWithoutNotify(EditorSpringFX.UserMotionPreference);
            motionToggle.RegisterValueChangedCallback(evt => EditorSpringFX.UserMotionPreference = evt.newValue);
            // Cheap poll: another editor (the pref is machine-wide) may change it behind our back.
            motionToggle.schedule.Execute(() =>
                motionToggle.SetValueWithoutNotify(EditorSpringFX.UserMotionPreference)).Every(500);
            AddField(appearanceCard, motionToggle);

            // Theme is opt-in: off ⇒ theme stays null (inspectors follow the editor skin automatically), which
            // reads far clearer than a blank field that looks like it wants a value. The toggle is a pure
            // disclosure control derived from whether a theme is assigned — no model/resolver change.
            bool hasTheme = settings.DefaultTheme != null;
            var customizeThemeToggle = new Toggle("Customize Theme Asset")
            {
                tooltip = "Off ⇒ inspectors follow the Unity editor skin automatically (dark skin → Default Dark " +
                          "Theme, light skin → Default Light Theme). On ⇒ pin a specific Theme asset below."
            };
            customizeThemeToggle.AddToClassList("unity-base-field__aligned");
            customizeThemeToggle.SetValueWithoutNotify(hasTheme);
            AddField(appearanceCard, customizeThemeToggle,
                "Off, inspectors follow the Unity editor skin automatically — the dark palette in a dark editor, " +
                "the light palette in a light editor. Turn this on to pin a specific Theme asset (Default Dark " +
                "Theme, Default Light Theme, or your own) regardless of the editor skin.");

            // Theme field + "+" create button (revealed only while "Customize Theme Asset" is on).
            var themeRow = new VisualElement();
            themeRow.style.flexDirection = FlexDirection.Row;
            themeRow.style.alignItems = Align.FlexEnd;
            themeRow.style.marginBottom = 4;
            themeRow.style.marginTop = 2;

            var themeField = new PropertyField(so.FindProperty("defaultTheme"), "Theme")
            {
                tooltip = "Theme asset used by rendered Sasquatch inspectors: its Base Skin picks the dark or " +
                          "light widget palette, and its token styles (Panel, AccentHeader, …) apply to nodes " +
                          "with a Style Token."
            };
            themeField.style.flexGrow = 1;
            themeRow.Add(themeField);

            // "+" creates a brand-new theme asset (seeded from the bundled theme matching the current
            // editor skin) and assigns it here — same affordance as the Layout Preset row.
            var themeAddButton = new Button(() =>
            {
                var theme = SasquatchThemeResolver.CreateNewAsset();
                var prop = so.FindProperty("defaultTheme");
                prop.objectReferenceValue = theme;
                so.ApplyModifiedProperties();
                Selection.activeObject = theme;
                EditorGUIUtility.PingObject(theme);
            })
            { text = "+" };
            themeAddButton.tooltip = "Create a new Theme asset (copied from the current default) and assign it here.";
            themeAddButton.style.width = 24;
            themeAddButton.style.marginLeft = 4;
            themeRow.Add(themeAddButton);
            EditorSpringFX.ButtonSpring(themeAddButton);

            appearanceCard.Add(themeRow);
            themeRow.style.display = hasTheme ? DisplayStyle.Flex : DisplayStyle.None;

            customizeThemeToggle.RegisterValueChangedCallback(evt =>
            {
                themeRow.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (evt.newValue)
                {
                    EditorSpringFX.PopIn(themeRow, 0.98f, 0.3f);
                }
                else
                {
                    // Off means "auto follow skin" — that is exactly a null theme, so clear any assignment.
                    var prop = so.FindProperty("defaultTheme");
                    if (prop.objectReferenceValue != null)
                    {
                        prop.objectReferenceValue = null;
                        so.ApplyModifiedProperties();
                    }
                }
            });

            var hoverAnimField = new PropertyField(so.FindProperty("enableHoverAnimation"), "Enable Hover Animation")
            {
                tooltip = "Play a subtle spring 'lift' on field/widget cards when you hover them, in both the " +
                          "inspector and the Layout Designer. Purely cosmetic and off by default."
            };
            AddField(appearanceCard, hoverAnimField);

            var labelColumnField = new PropertyField(so.FindProperty("labelColumnMode"), "Label Column")
            {
                tooltip = "How wide the left label column of styled field rows may grow. Capped: follows Unity's " +
                          "proportional column until it reaches Max Label Width, then gives any further inspector " +
                          "width to the field itself. Unity Standard: keeps growing proportionally at any width, " +
                          "exactly like stock inspector rows."
            };
            AddField(appearanceCard, labelColumnField,
                "Capped (recommended) matches stock Unity at everyday inspector widths; on a wide inspector the " +
                "extra room goes to your fields and graphics instead of empty space after the label.");

            // The cap slider only matters while the column is Capped (mirrors the Backups sub-row gating).
            var labelColumnOptions = new VisualElement();
            var maxLabelWidthField = new PropertyField(so.FindProperty("maxLabelColumnWidth"), "Max Label Width")
            {
                tooltip = "The label column's width cap in pixels (120–300, default 180)."
            };
            labelColumnOptions.Add(maxLabelWidthField);
            appearanceCard.Add(labelColumnOptions);

            var expandableRowField = new PropertyField(so.FindProperty("expandableRowStyle"), "Expandable Rows")
            {
                tooltip = "How expandable widgets (Animation Curve, Gradient, Asset Preview, Particle System, " +
                          "Inline Editor) frame themselves. Card: each row is its own bordered box with a gap " +
                          "around it. List: no box and no gap — rows sit flush and are parted by a hairline."
            };
            AddField(appearanceCard, expandableRowField,
                "List keeps a run of expandable widgets reading as one list instead of a stack of separate " +
                "cards, which is easier to scan when a type has several of them in a row.");

            // ===================== Widget Previews =====================
            var previewsCard = MakeCard(container, "Widget Previews");

            var maxParticlesField = new PropertyField(so.FindProperty("maxParticlePreviews"), "Max Particle Previews Playing")
            {
                tooltip = "How many Particle System Preview widgets may play (animate + render) at once. " +
                          "Pressing play on a preview stops the oldest playing one(s) past this cap. Default 1."
            };
            var particlesInfo = AddField(previewsCard, maxParticlesField,
                "Each playing preview renders every frame. Values above 3 can stutter the editor.", warning: true);

            var disableAudioField = new PropertyField(so.FindProperty("disableNativeAudioPreview"),
                "Disable Native Audio Preview")
            {
                tooltip = "When on, AudioClip preview widgets never open a native audio device — the waveform " +
                          "still renders, but scrubbing and playback are silent. Turn this on if you don't want " +
                          "native audio P/Invoke in your editor. (On macOS, native audio is also auto-disabled on " +
                          "a future non-Mono editor runtime; the SASQ_NO_NATIVE_AUDIO define forces silence globally.)"
            };
            AddField(previewsCard, disableAudioField);
            // Closing the live device on toggle means the next preview re-creates it under the new setting (the
            // backend re-reads DisableNativeAudioPreview in Create()). Fires on initial bind too, but Shutdown is a
            // no-op when nothing is open.
            disableAudioField.RegisterValueChangeCallback(_ => Audio.NativeAudioBackend.Shutdown());

            // ===================== Layout Presets =====================
            var presetsCard = MakeCard(container, "Layout Presets");

            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.alignItems = Align.FlexEnd;

            var presetField = new PropertyField(so.FindProperty("layoutPreset"), "Layout Preset")
            {
                tooltip = "Default widget + configuration per field type, applied by the Layout Designer's import " +
                          "tools (Smart Attribute Import, Flat Import, …). Empty ⇒ the first preset found in the " +
                          "project, otherwise the built-in recommended defaults."
            };
            presetField.style.flexGrow = 1;
            presetRow.Add(presetField);

            // "+" creates a brand-new preset asset (recommended defaults) and assigns it here.
            var presetAddButton = new Button(() =>
            {
                var preset = LayoutPresetManager.CreateNewAsset();
                var prop = so.FindProperty("layoutPreset");
                prop.objectReferenceValue = preset;
                so.ApplyModifiedProperties();
                Selection.activeObject = preset;
                EditorGUIUtility.PingObject(preset);
            })
            { text = "+" };
            presetAddButton.tooltip = "Create a new Layout Preset asset (with recommended defaults) and assign it here.";
            presetAddButton.style.width = 24;
            presetAddButton.style.marginLeft = 4;
            presetRow.Add(presetAddButton);
            EditorSpringFX.ButtonSpring(presetAddButton);

            AddField(presetsCard, presetRow,
                "Edit your presets in the Preset Editor: select the Layout Preset asset and click \"Open Preset " +
                "Editor\", or use Tools ▸ Sasquatch Inspector ▸ Layout Presets.");

            // ===================== Layout Storage =====================
            var storageCard = MakeCard(container, "Layout Storage");

            var compressToggle = new PropertyField(so.FindProperty("compressLayoutAssets"), "Compress Layout Assets")
            {
                tooltip = "Store each layout's node graph as a compressed blob inside its per-type asset — " +
                          "typically 20–40× smaller on disk and faster to save and import. The file stays a text " +
                          "asset (type, GUID and sync info remain readable); only the graph itself is packed. " +
                          "Turn this off if you want fully human-readable layout files for version-control diffs."
            };
            AddField(storageCard, compressToggle,
                "On (recommended): layout assets shrink dramatically (a large layout drops from ~1 MB to tens of " +
                "KB) and save/import faster. Off: layouts are stored as plain, fully diffable YAML — the " +
                "historical format. Flipping this rewrites your existing layout files in the chosen format " +
                "(a safety snapshot of the layouts folder is taken first), and files from teammates on the other " +
                "setting are converted automatically the next time they load.");

            // ===================== Layout Backups =====================
            var backupsCard = MakeCard(container, "Layout Backups");

            var autoBackupToggle = new PropertyField(so.FindProperty("enableAutomaticLayoutBackups"),
                "Automatic Layout Backups")
            {
                tooltip = "When on, Sasquatch Inspector snapshots the whole layouts folder before a save overwrites it — at " +
                          "the frequency and number-kept you choose below — under Library/SasquatchInspector/Backups, which " +
                          "is never committed to version control. Off by default: layout assets and their snapshots " +
                          "can get large. \"Restore Layouts Backup…\" still snapshots the current layouts first " +
                          "regardless of this setting, so a restore stays reversible."
            };
            AddField(backupsCard, autoBackupToggle,
                "Turn on Automatic Layout Backups to snapshot the whole layouts folder under Library/SasquatchInspector/Backups " +
                "(never committed to version control) before each save — at the frequency you set, keeping the newest " +
                "few. Restoring replaces the current layouts, which are themselves snapshotted first (so a restore is " +
                "reversible even when automatic backups are off).");

            // The two sub-options are only relevant when automatic backups are on; shown/hidden as a unit below.
            var backupOptions = new VisualElement();
            backupOptions.style.marginLeft = 14;

            var maxBackupsField = new PropertyField(so.FindProperty("maxLayoutBackups"), "Backups Kept")
            {
                tooltip = "How many rolling snapshots to keep (1–5). Each snapshot holds a copy of the whole layouts " +
                          "folder, so this is effectively how many past versions of every layout you can restore. " +
                          "When a new snapshot would exceed this, the oldest is deleted."
            };
            backupOptions.Add(maxBackupsField);

            var backupFrequencyField = new PropertyField(so.FindProperty("backupFrequency"), "Backup Frequency")
            {
                tooltip = "The most often a snapshot is taken. A backup is taken before a save, but no more than once " +
                          "per this interval of edits, so an active editing session never spams the disk."
            };
            backupOptions.Add(backupFrequencyField);
            backupsCard.Add(backupOptions);

            var restoreButton = new Button(ShowRestoreBackupMenu) { text = "Restore Layouts Backup…" };
            restoreButton.tooltip = "Roll the inspector layouts back to a snapshot (kept rolling, up to your " +
                                    "Backups Kept cap, throttled to your Backup Frequency while Automatic Layout " +
                                    "Backups is on). Use this to recover from a corrupted layout or a bad merge.";
            restoreButton.style.marginTop = 8;
            restoreButton.style.alignSelf = Align.FlexStart;
            backupsCard.Add(restoreButton);
            EditorSpringFX.ButtonSpring(restoreButton);

            container.Bind(so);

            // A SettingsProvider gives no InspectorElement context, so UI Toolkit's automatic label alignment
            // never engages and controls would sit ragged after each auto-sized label. Pin every field label to
            // one column width so the controls line up. Re-applied on layout changes so async-built PropertyField
            // labels and revealed sub-fields (Theme, Backups Kept/Frequency) pick it up too — idempotent (the
            // width only changes on first touch), so it can't loop.
            void AlignFieldLabels()
            {
                container.Query<Label>(className: "unity-base-field__label").ForEach(l =>
                {
                    l.style.width = LabelColumnWidth;
                    l.style.minWidth = LabelColumnWidth;
                    l.style.maxWidth = LabelColumnWidth;
                });
            }
            container.RegisterCallback<GeometryChangedEvent>(_ => AlignFieldLabels());
            AlignFieldLabels();

            void RefreshSyncModeUI()
            {
                var mode = settings.UnboundFieldHandling;
                if (syncInfo != null)
                {
                    var syncLabel = syncInfo.Q<Label>();
                    syncLabel.text = mode switch
                    {
                        UnboundFieldSyncMode.AutoAppend =>
                            "Layouts follow the script automatically: new serialized fields are added in code " +
                            "order, deleted fields are removed, and renamed fields keep their styling and " +
                            "position ([FormerlySerializedAs] makes renames unambiguous).",
                        UnboundFieldSyncMode.NotificationBanner =>
                            "Layouts are never changed automatically. The inspector shows a dismissible banner " +
                            "listing added, removed and renamed fields, with a one-click Sync. Until synced, " +
                            "deleted fields are hidden in the inspector.",
                        _ =>
                            "Script changes are ignored. Deleted fields render hidden in the inspector; manage " +
                            "the layout manually in the designer (Bulk Add → \"Sync Missing\").",
                    };
                    // New text ⇒ let the label's GeometryChanged handler re-pin the wrapped height for it.
                    syncLabel.style.height = StyleKeyword.Auto;
                }
                attributesToggle.SetEnabled(mode != UnboundFieldSyncMode.DoNothing);
                headerRoutingToggle.SetEnabled(mode != UnboundFieldSyncMode.DoNothing);
            }
            RefreshSyncModeUI();

            // The particle warning auto-reveals when the cap climbs past the safe range (it never force-closes,
            // so the user can always dismiss it).
            void RefreshParticlesUI()
            {
                if (settings.MaxParticlePreviews > 3) OpenInfo(particlesInfo);
            }
            RefreshParticlesUI();

            // The Backups-Kept / Frequency controls only matter when automatic backups are enabled.
            void RefreshBackupOptionsUI() =>
                backupOptions.style.display = settings.EnableAutomaticLayoutBackups ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshBackupOptionsUI();

            // The Max Label Width slider only matters while the label column is Capped.
            void RefreshLabelColumnUI() =>
                labelColumnOptions.style.display = settings.LabelColumnMode == LabelColumnMode.Capped ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshLabelColumnUI();

            // Flipping Compress Layout Assets rewrites every layout file in the newly chosen representation
            // (ResaveAllLayoutAssets snapshots the layouts folder first). Guarded by a last-value compare because
            // the tracker below fires for EVERY settings change, not just this one.
            bool lastCompress = settings.CompressLayoutAssets;

            // Persist edits with the project and push them to open inspectors immediately.
            container.TrackSerializedObjectValueWithRefresh(so, _ =>
            {
                EditorUtility.SetDirty(settings);
                RefreshSyncModeUI();
                RefreshParticlesUI();
                RefreshBackupOptionsUI();
                RefreshLabelColumnUI();
                if (settings.CompressLayoutAssets != lastCompress)
                {
                    lastCompress = settings.CompressLayoutAssets;
                    int rewritten = LayoutGraphStorage.ResaveAllLayoutAssets();
                    Debug.Log($"[Sasquatch Inspector] Rewrote {rewritten} layout asset{(rewritten == 1 ? "" : "s")} as " +
                              (lastCompress ? "compressed" : "plain-text") + " storage.");
                }
                LayoutPresetManager.InvalidateCache(); // a changed active preset must re-resolve for the import tools
                // Toggling "Take Over Other Inspectors" takes effect live: the header scan re-evaluates the
                // displacement gate on the next tick (on → styled blanket-claimed types show their layout; off →
                // the other framework's inspector is released). No codegen, no recompile — just a refresh.
                SasquatchEditorRegistrar.RefreshInspectors();
            });
        }

        // ---- Card + field builders (styled to the Sasquatch Inspector palette) ----------------------------------

        /// <summary>Adds a titled, rounded card to <paramref name="parent"/> and returns its content body
        /// (add fields to the returned element, not the card root).</summary>
        private static VisualElement MakeCard(VisualElement parent, string title)
        {
            var card = new VisualElement();
            card.style.backgroundColor = DesignerPalette.CardBg;
            card.style.flexShrink = 0; // never let a height-constrained ancestor compress the card
            card.style.marginBottom = 10;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 9;
            card.style.paddingBottom = 10;
            Round(card, 8);
            SetBorder(card, 1, DesignerPalette.BorderMid);

            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11;
            header.style.color = DesignerPalette.TextHeader;
            header.style.letterSpacing = 0.5f;
            header.style.marginBottom = 8;
            header.style.paddingBottom = 6;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = DesignerPalette.DividerLine;
            card.Add(header);

            parent.Add(card);
            return card;
        }

        /// <summary>
        /// Adds <paramref name="field"/> to <paramref name="card"/>. When <paramref name="infoText"/> is
        /// supplied, a round "?" chip is placed at the right of the field's row; clicking it reveals a hidden,
        /// accent-tinted (or amber, when <paramref name="warning"/>) info card beneath the field with a spring
        /// pop-in. Returns that info card (or null when there is no info), so callers can update its text
        /// (dynamic help) or auto-open it.
        /// </summary>
        private static VisualElement AddField(VisualElement card, VisualElement field, string infoText = null, bool warning = false)
        {
            var block = new VisualElement();
            block.style.marginBottom = 4;

            if (string.IsNullOrEmpty(infoText))
            {
                block.Add(field);
                card.Add(block);
                return null;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            row.Add(field);

            var infoCard = MakeInfoCard(infoText, warning);
            var chip = MakeInfoChip();
            infoCard.userData = chip; // lets OpenInfo() sync the chip's active state
            chip.clicked += () => ToggleInfo(infoCard, chip);
            row.Add(chip);

            block.Add(row);
            block.Add(infoCard);
            card.Add(block);
            return infoCard;
        }

        /// <summary>The round "?" reveal chip. Outlined accent at rest, solid accent when its info is open.</summary>
        private static Button MakeInfoChip()
        {
            var chip = new Button { text = "?" };
            chip.style.width = 18;
            chip.style.height = 18;
            chip.style.minWidth = 18;
            chip.style.minHeight = 18;
            chip.style.marginLeft = 6;
            chip.style.marginRight = 0;
            chip.style.marginTop = 0;
            chip.style.marginBottom = 0;
            chip.style.paddingLeft = 0;
            chip.style.paddingRight = 0;
            chip.style.paddingTop = 0;
            chip.style.paddingBottom = 0;
            chip.style.fontSize = 11;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.unityTextAlign = TextAnchor.MiddleCenter;
            Round(chip, 9);
            chip.tooltip = "Show help";
            StyleChip(chip, false);
            EditorSpringFX.ButtonSpring(chip);
            return chip;
        }

        /// <summary>The hidden info panel: an accent-soft (or amber warning) wash with a left accent stripe.</summary>
        private static VisualElement MakeInfoCard(string text, bool warning)
        {
            var card = new VisualElement();
            card.style.display = DisplayStyle.None;
            card.style.marginTop = 5;
            card.style.marginBottom = 3;
            card.style.paddingLeft = 9;
            card.style.paddingRight = 9;
            card.style.paddingTop = 7;
            card.style.paddingBottom = 7;
            Round(card, 6);

            var stripe = warning ? DesignerPalette.ToastWarning : DesignerPalette.Accent;
            card.style.backgroundColor = warning ? DesignerPalette.WarningBg : DesignerPalette.AccentSoft;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = stripe;

            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = warning ? DesignerPalette.WarningText : DesignerPalette.TextBody;

            // UI Toolkit under-measures a wrapping Label's height when the element is first laid out from a
            // display:none → flex reveal (it measures the text against an unbounded width → a single line), so
            // the info card ends up far too short and the lower lines spill out below the blue background — and
            // the card's auto height, cached from that bad pass, doesn't re-grow when the label is fixed. So in
            // the label's GeometryChanged handler (where its width IS valid) compute the TRUE wrapped height via
            // MeasureTextSize — UITK's own text-measurement routine — then pin the label height AND force the
            // card tall enough via minHeight (the reliable lever the cached auto height ignores). Recomputed on
            // every layout change (reveal, window resize, scrollbar reflow, or a dynamic text swap that first
            // resets height to Auto). Loop-safe: each value is only re-assigned when it actually differs.
            float lastCardMinH = -1f;
            label.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float avail = label.contentRect.width;
                if (avail <= 1f) return;
                float textH = label.MeasureTextSize(label.text, avail, VisualElement.MeasureMode.Exactly,
                                                    0f, VisualElement.MeasureMode.Undefined).y;
                if (Mathf.Abs(textH - label.resolvedStyle.height) > 0.5f) label.style.height = textH;
                float needed = textH + card.resolvedStyle.paddingTop + card.resolvedStyle.paddingBottom;
                if (Mathf.Abs(needed - lastCardMinH) > 0.5f) { lastCardMinH = needed; card.style.minHeight = needed; }
            });

            card.Add(label);
            return card;
        }

        /// <summary>Paints a "?" chip for its open/closed state (solid accent vs. outlined accent).</summary>
        private static void StyleChip(Button chip, bool open)
        {
            var accent = DesignerPalette.Accent;
            chip.style.backgroundColor = open ? accent : DesignerPalette.AccentSoft;
            chip.style.color = open ? Color.white : accent;
            SetBorder(chip, 1, accent);
        }

        /// <summary>Toggles an info card's visibility, springing it in and syncing the chip's active state.</summary>
        private static void ToggleInfo(VisualElement infoCard, Button chip)
        {
            bool opening = infoCard.style.display.value == DisplayStyle.None;
            infoCard.style.display = opening ? DisplayStyle.Flex : DisplayStyle.None;
            if (opening) EditorSpringFX.PopIn(infoCard, 0.97f, 0.35f);
            StyleChip(chip, opening);
        }

        /// <summary>Force-opens an info card (used to surface warnings automatically); no-op if already open.</summary>
        private static void OpenInfo(VisualElement infoCard)
        {
            if (infoCard == null || infoCard.style.display.value == DisplayStyle.Flex) return;
            infoCard.style.display = DisplayStyle.Flex;
            EditorSpringFX.PopIn(infoCard, 0.97f, 0.35f);
            if (infoCard.userData is Button chip) StyleChip(chip, true);
        }

        private static void Round(VisualElement ve, float radius)
        {
            ve.style.borderTopLeftRadius = radius;
            ve.style.borderTopRightRadius = radius;
            ve.style.borderBottomLeftRadius = radius;
            ve.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement ve, float width, Color color)
        {
            ve.style.borderTopWidth = width;
            ve.style.borderRightWidth = width;
            ve.style.borderBottomWidth = width;
            ve.style.borderLeftWidth = width;
            ve.style.borderTopColor = color;
            ve.style.borderRightColor = color;
            ve.style.borderBottomColor = color;
            ve.style.borderLeftColor = color;
        }

        // Lists the rolling layout snapshots (newest first) and, on pick, restores after a confirm.
        private static void ShowRestoreBackupMenu()
        {
            var backups = LayoutRegistry.ListLayoutBackups();
            var menu = new GenericMenu();
            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No backups yet — edit a layout to create one"));
            }
            else
            {
                foreach (var dir in backups)
                {
                    string when;
                    try { when = System.IO.Directory.GetLastWriteTime(dir).ToString("g"); }
                    catch { when = System.IO.Path.GetFileName(dir); }
                    string captured = dir;
                    menu.AddItem(new GUIContent(when + "  (" + System.IO.Path.GetFileName(dir) + ")"), false, () =>
                    {
                        if (!EditorUtility.DisplayDialog("Restore Layouts Backup",
                                "Replace the current inspector layouts with this snapshot?\n\n" + captured +
                                "\n\nThe current layouts are replaced (they are backed up first, so this is reversible).",
                                "Restore", "Cancel"))
                            return;

                        bool ok = LayoutRegistry.RestoreLayoutBackup(captured);
                        EditorUtility.DisplayDialog("Restore Layouts Backup",
                            ok ? "Inspector layouts restored from backup." : "Restore failed — see the Console for details.", "OK");
                    });
                }
            }
            menu.ShowAsContext();
        }
    }
}
