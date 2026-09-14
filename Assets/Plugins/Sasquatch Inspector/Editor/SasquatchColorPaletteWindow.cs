using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    using Palette = SasquatchColorPaletteLibrary.Palette;
    using Swatch = SasquatchColorPaletteLibrary.Swatch;

    /// <summary>
    /// A standalone browser/editor for the project's single <see cref="SasquatchColorPaletteLibrary"/>. Swap
    /// between the named palettes in the library, click any swatch to copy its hex to the clipboard (no scene
    /// "dummy object" required — the pain point this feature targets), add/remove/rename palettes, and
    /// add/remove/rename/reorder the colors within a palette. Built programmatically (no UXML), the same way as
    /// the Layout Designer, and shares <c>SasquatchWidgets.uss</c> for swatch styling.
    /// </summary>
    public class SasquatchColorPaletteWindow : EditorWindow
    {
        // Palettes are list entries (not assets), so we track the selection by stable id across domain reloads.
        [SerializeField] private string _currentId;
        private VisualElement _grid;

        /// <summary>Opens the Color Palette window.</summary>
        [MenuItem("Tools/Sasquatch Inspector/Color Palette", false, 11)]
        public static void Open() => Open(null);

        /// <summary>Opens the window, optionally focused on a specific palette.</summary>
        public static void Open(Palette focus)
        {
            var w = GetWindow<SasquatchColorPaletteWindow>("Color Palette");
            w.minSize = new Vector2(300f, 420f);
            if (focus != null) w._currentId = focus.Id;
            w.Rebuild();
            w.Show();
        }

        /// <summary>
        /// Adds a new palette (fresh GUID id, seeded with a starter swatch set) to the project library, persists
        /// it, and returns it. Shared by the window's New button, the inspector widget's gear / "Create Palette",
        /// and the Layout Designer.
        /// </summary>
        public static Palette CreatePalette(string suggestedName)
        {
            var library = SasquatchPaletteLibraryManager.GetOrCreate();
            Undo.RecordObject(library, "Create Color Palette");
            var palette = new Palette(
                Guid.NewGuid().ToString("N"),
                MakeUniqueName(library, string.IsNullOrEmpty(suggestedName) ? "New Palette" : suggestedName),
                StarterSwatches());
            library.Palettes.Add(palette);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            return palette;
        }

        private void CreateGUI() => Rebuild();

        private void Rebuild()
        {
            var root = rootVisualElement;
            if (root == null) return;
            root.Clear();

            // Widget sheet + skin token sheet (this window's root sits outside any build root, so the
            // --sasq-* variables must be applied here to reach its widget classes).
            SasquatchSkin.ApplyWithWidgetSheet(root);

            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 6;
            root.style.paddingBottom = 8;

            var title = new Label("Color Palette");
            title.AddToClassList("sasq-palettewin-title");
            root.Add(title);

            // Opening the window to manage palettes implies persistence — materialize the (otherwise transient)
            // library so edits land on a git-trackable asset, mirroring the Project Settings page.
            var library = SasquatchPaletteLibraryManager.GetOrCreate();
            if (library.Palettes.Count == 0)
            {
                BuildEmptyState(root, library);
                return;
            }

            var current = ResolveCurrent(library);
            _currentId = current.Id;

            // ---- Toolbar: palette switcher + New + Set Default + Delete ----
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginBottom = 6;

            var picker = new PopupField<Palette>(library.Palettes, current, FormatPalette, FormatPalette);
            picker.style.flexGrow = 1;
            picker.RegisterValueChangedCallback(evt => { if (evt.newValue != null) { _currentId = evt.newValue.Id; Rebuild(); } });
            toolbar.Add(picker);

            var newBtn = new Button(() =>
            {
                var p = CreatePalette("New Palette");
                if (p != null) { _currentId = p.Id; Rebuild(); }
            })
            { text = "New" };
            newBtn.tooltip = "Add a new palette to the library.";
            newBtn.style.marginLeft = 4;
            toolbar.Add(newBtn);

            bool isDefault = library.DefaultPaletteId == current.Id;
            var defaultBtn = new Button(() =>
            {
                Undo.RecordObject(library, "Set Default Palette");
                library.DefaultPaletteId = current.Id;
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                SasquatchEditorRegistrar.RefreshInspectors();
                Rebuild();
            })
            { text = isDefault ? "★ Default" : "Set Default" };
            defaultBtn.tooltip = "Use this palette for Color Palette fields that don't pick one explicitly.";
            defaultBtn.SetEnabled(!isDefault);
            defaultBtn.style.marginLeft = 4;
            toolbar.Add(defaultBtn);

            var deleteBtn = new Button(() =>
            {
                if (library.Palettes.Count <= 1) return; // always keep at least one palette
                if (!EditorUtility.DisplayDialog("Delete Palette",
                        $"Delete the palette “{current.DisplayName}”? Fields using it fall back to the default palette.",
                        "Delete", "Cancel"))
                    return;
                Undo.RecordObject(library, "Delete Palette");
                library.Palettes.Remove(current);
                if (library.DefaultPaletteId == current.Id)
                    library.DefaultPaletteId = library.Palettes.Count > 0 ? library.Palettes[0].Id : "";
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                _currentId = library.Palettes.Count > 0 ? library.Palettes[0].Id : null;
                SasquatchEditorRegistrar.RefreshInspectors();
                Rebuild();
            })
            { text = "Delete" };
            deleteBtn.tooltip = "Remove this palette from the library.";
            deleteBtn.SetEnabled(library.Palettes.Count > 1);
            deleteBtn.style.marginLeft = 4;
            toolbar.Add(deleteBtn);

            root.Add(toolbar);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            int idx = library.Palettes.IndexOf(current);
            var so = new SerializedObject(library);
            var paletteProp = so.FindProperty("palettes").GetArrayElementAtIndex(idx);
            var nameProp = paletteProp.FindPropertyRelative("name");
            var swatchesProp = paletteProp.FindPropertyRelative("swatches");

            // ---- Browse grid (click = copy) ----
            _grid = new VisualElement();
            _grid.style.flexDirection = FlexDirection.Row;
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.style.marginTop = 4;
            scroll.Add(_grid);
            RefreshGrid(current);

            // ---- Edit "Colors" expandable card (rename palette, add/remove/rename/reorder colors) ----
            // A .sasq-group card (header bar + inset body) rather than a native Foldout, so it matches
            // the designer's collapsible groups — with the same spring fold + spinning caret.
            var editCard = new VisualElement();
            editCard.AddToClassList("sasq-group");
            editCard.style.marginTop = 8;

            var editHeader = new VisualElement();
            editHeader.AddToClassList("sasq-group-header");
            editHeader.style.flexDirection = FlexDirection.Row;
            editHeader.style.alignItems = Align.Center;

            var editChevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, new Color(0.365f, 0.365f, 0.365f, 1f));
            editChevron.AddToClassList("sasq-group-chevron");
            editHeader.Add(editChevron);

            var editTitle = new Label("Edit Colors");
            editTitle.AddToClassList("sasq-group-title");
            editTitle.pickingMode = PickingMode.Ignore;
            editTitle.style.flexGrow = 1;
            editHeader.Add(editTitle);

            var editContent = new VisualElement();
            editContent.AddToClassList("sasq-group-content");

            editCard.Add(editHeader);
            editCard.Add(editContent);

            bool editExpanded = current.Swatches.Count == 0; // empty palette opens ready to add
            void ApplyEditExpanded(bool animate)
            {
                editHeader.style.borderBottomLeftRadius = editExpanded ? 0 : 4;
                editHeader.style.borderBottomRightRadius = editExpanded ? 0 : 4;
                EditorSpringFX.SpringCaret(editChevron, editExpanded, animate);
                if (animate) EditorSpringFX.Fold(editContent, editExpanded);
                else editContent.style.display = editExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ApplyEditExpanded(false);
            editHeader.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                editExpanded = !editExpanded;
                ApplyEditExpanded(true);
                evt.StopPropagation();
            });

            BuildEditList(editContent, so, nameProp, swatchesProp);
            scroll.Add(editCard);

            // Keep the browse grid in sync on ANY change (color, name, reorder, add, remove) WITHOUT a full
            // window rebuild — so the card stays open and the scroll position holds. Bound edits already
            // dirty the library via ApplyModifiedProperties; the heavier save + inspector refresh is triggered
            // explicitly by add/remove/reorder via Persist().
            scroll.TrackSerializedObjectValueWithRefresh(so, _ => RefreshGrid(ResolveCurrent(library)));
        }

        // Repopulates the persistent browse grid in place (no window rebuild) so the swatches always reflect the
        // current palette after an edit. Empty palettes show a gentle note instead.
        private void RefreshGrid(Palette current)
        {
            if (_grid == null) return;
            _grid.Clear();
            if (current == null || current.Swatches.Count == 0)
            {
                var emptyNote = new Label("This palette is empty — add colors below.");
                emptyNote.AddToClassList("sasq-palettewin-hint");
                _grid.Add(emptyNote);
                return;
            }
            foreach (var s in current.Swatches)
                _grid.Add(MakeBrowseSwatch(s));
        }

        private VisualElement MakeBrowseSwatch(Swatch s)
        {
            var card = new VisualElement();
            card.AddToClassList("sasq-palettewin-card");
            card.tooltip = "Click to copy " + ColorHexUtility.ToHex(s.color);

            var block = new VisualElement();
            block.AddToClassList("sasq-palettewin-block");
            block.style.backgroundColor = s.color;
            block.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
            card.Add(block);

            var hex = new Label(ColorHexUtility.ToHex(s.color));
            hex.AddToClassList("sasq-palettewin-hex");
            card.Add(hex);

            if (!string.IsNullOrEmpty(s.name))
            {
                var nameLbl = new Label(s.name);
                nameLbl.AddToClassList("sasq-palettewin-name");
                card.Add(nameLbl);
            }

            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (EditorSpringFX.MotionEnabled) block.SpringScale(1.08f, EditorSpringFX.Tune(SpringConfig.Snappy));
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (EditorSpringFX.MotionEnabled) block.SpringScale(1f, EditorSpringFX.Tune(SpringConfig.Snappy));
            });
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                CopyColor(s.color, block);
                evt.StopPropagation();
            });
            return card;
        }

        private void CopyColor(Color c, VisualElement popTarget)
        {
            EditorGUIUtility.systemCopyBuffer = ColorHexUtility.ToHex(c);
            ShowCopiedToast(c, popTarget);
            if (EditorSpringFX.MotionEnabled) popTarget.PunchScale(0.45f, EditorSpringFX.Tune(SpringConfig.Bouncy));
        }

        // Springy "Copied #hex" toaster that slides in over the clicked swatch and fades itself out — the same
        // flourish the inspector's grid/bar widget uses (shares the .sasq-swatch-toast style).
        private void ShowCopiedToast(Color c, VisualElement nearBlock)
        {
            var root = rootVisualElement;
            if (root == null) return;

            var toast = new Label("Copied  " + ColorHexUtility.ToHex(c));
            toast.AddToClassList("sasq-swatch-toast");
            toast.pickingMode = PickingMode.Ignore;
            toast.style.position = Position.Absolute;
            toast.style.right = StyleKeyword.Auto; // override the USS top-right anchor; we place it ourselves
            root.Add(toast);

            // Centre it just above the clicked swatch, clamped inside the window. Re-runs on GeometryChanged
            // because the toast's width isn't known until it's measured (so the first pass centres on a guess).
            void Place()
            {
                if (nearBlock?.panel == null) return;
                Rect b = nearBlock.worldBound;
                Vector2 local = root.WorldToLocal(new Vector2(b.center.x, b.yMin));
                float w = toast.resolvedStyle.width;
                if (float.IsNaN(w) || w < 1f) w = 90f;
                float maxLeft = Mathf.Max(4f, root.layout.width - w - 4f);
                toast.style.left = Mathf.Clamp(local.x - w * 0.5f, 4f, maxLeft);
                toast.style.top = Mathf.Max(2f, local.y - 20f);
            }
            toast.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Place();

            if (EditorSpringFX.MotionEnabled) EditorSpringFX.SlideInY(toast, -6f);
            toast.schedule.Execute(() =>
            {
                if (EditorSpringFX.MotionEnabled)
                {
                    toast.SpringOpacity(0f, EditorSpringFX.Tune(SpringConfig.Snappy));
                    toast.schedule.Execute(() => toast.RemoveFromHierarchy()).ExecuteLater(240);
                }
                else
                {
                    toast.RemoveFromHierarchy();
                }
            }).ExecuteLater(950);
        }

        private void BuildEditList(VisualElement content, SerializedObject so, SerializedProperty nameProp, SerializedProperty swatchesProp)
        {
            content.Clear();

            var nameField = new TextField("Palette Name");
            nameField.tooltip = "Display name for this palette (falls back to the id when blank).";
            nameField.BindProperty(nameProp);
            content.Add(nameField);

            var rows = new VisualElement();
            rows.style.marginTop = 4;
            content.Add(rows);

            // Rebuilds ONLY the rows (structural change: add/remove) in place — the foldout and the window's
            // ScrollView are untouched, so the open-state and scroll position survive. Reorders don't rebuild
            // here at all: CollectionRowReorder reflows the bound fields by array index in place.
            void RebuildRows()
            {
                rows.Clear();
                for (int i = 0; i < swatchesProp.arraySize; i++)
                {
                    int idx = i;
                    var elem = swatchesProp.GetArrayElementAtIndex(idx);

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 2;
                    row.userData = idx; // CollectionRowReorder keys the reorder off this

                    var grip = LayoutTreeBuilder.MakeReorderGrip();
                    row.Add(grip);

                    var nm = new TextField();
                    nm.style.flexGrow = 1;
                    nm.BindProperty(elem.FindPropertyRelative("name"));
                    row.Add(nm);

                    var colorField = new ColorField();
                    colorField.style.width = 96;
                    colorField.style.marginLeft = 4;
                    colorField.BindProperty(elem.FindPropertyRelative("color"));
                    row.Add(colorField);

                    var del = new Button(() =>
                    {
                        swatchesProp.DeleteArrayElementAtIndex(idx);
                        so.ApplyModifiedProperties();
                        RebuildRows();
                        Persist();
                    })
                    { text = "✕" };
                    del.tooltip = "Remove this color";
                    del.style.width = 22;
                    del.style.marginLeft = 2;
                    row.Add(del);

                    rows.Add(row);

                    CollectionRowReorder.Attach(grip, row, rows, swatchesProp, so, () => Persist());
                }
            }

            RebuildRows();

            var addBtn = new Button(() =>
            {
                int n = swatchesProp.arraySize;
                swatchesProp.arraySize = n + 1;
                var elem = swatchesProp.GetArrayElementAtIndex(n);
                elem.FindPropertyRelative("name").stringValue = "New Color";
                elem.FindPropertyRelative("color").colorValue = Color.white;
                so.ApplyModifiedProperties();
                RebuildRows();
                Persist();
            })
            { text = "+  Add Color" };
            addBtn.style.marginTop = 4;
            content.Add(addBtn);
        }

        private void Persist()
        {
            var library = SasquatchPaletteLibraryManager.Current;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            SasquatchEditorRegistrar.RefreshInspectors();
        }

        private void BuildEmptyState(VisualElement root, SasquatchColorPaletteLibrary library)
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-swatch-empty");

            var msg = new Label("No color palettes in the library yet.");
            msg.AddToClassList("sasq-swatch-empty-msg");
            box.Add(msg);

            var createBtn = new Button(() =>
            {
                var p = CreatePalette("New Palette");
                if (p != null) { _currentId = p.Id; Rebuild(); }
            })
            { text = "Create Your First Palette" };
            createBtn.AddToClassList("sasq-swatch-empty-btn");
            box.Add(createBtn);

            root.Add(box);
        }

        // Resolves the selected palette by id, falling back to the library default, then the first palette.
        private Palette ResolveCurrent(SasquatchColorPaletteLibrary library)
        {
            var byId = library.GetPalette(_currentId);
            if (byId != null) return byId;
            var def = library.DefaultPalette;
            if (def != null) return def;
            return library.Palettes.Count > 0 ? library.Palettes[0] : null;
        }

        private static string FormatPalette(Palette p) => p != null ? p.DisplayName : "(none)";

        // A fresh starter swatch set for a newly created palette (a copy of the Nord defaults, ready to edit).
        private static List<Swatch> StarterSwatches()
        {
            var defaults = SasquatchColorPaletteLibrary.DefaultPalettes();
            return defaults.Count > 0 ? new List<Swatch>(defaults[0].Swatches) : new List<Swatch>();
        }

        // Disambiguates a palette name against the library so the picker doesn't show duplicates ("New Palette",
        // "New Palette 2", …). Ids are always unique regardless; this is purely cosmetic.
        private static string MakeUniqueName(SasquatchColorPaletteLibrary library, string baseName)
        {
            bool Taken(string n)
            {
                foreach (var p in library.Palettes)
                    if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            if (!Taken(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!Taken(candidate)) return candidate;
            }
            return baseName;
        }
    }
}
