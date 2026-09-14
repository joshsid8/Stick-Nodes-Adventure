using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // ===== Color Palette widget =====
        // Renders a grid (or continuous bar) of clickable swatches drawn from a palette in the project library
        // instead of opening the OS color picker. Clicking a swatch sets the field and (optionally) copies its
        // hex to the clipboard, with a spring "Copied" flourish. Falls back to a default field for non-color
        // properties, and shows a one-click "Create Palette" prompt when no palette is assigned anywhere.
        //
        // Each swatch is a STABLE hit "slot" (fixed in the flow, cursor + click live here) holding an animated
        // "visual" child (the colored block that springs/rises) plus an inset selection "ring". Separating the
        // hit target from the moving visual means the slot never slides out from under the cursor — so the bar's
        // rise animation can't make clicks miss or flicker the cursor, and clicks land in the gap it leaves.
        private static VisualElement CreateColorPalette(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (!TryReadPaletteColor(prop, out _))
            {
                return new PropertyField(prop) { label = resolvedLabel };
            }

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-swatch-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Column;
            wrapper.Add(input);

            // Palettes live as entries in the single project library (not per-asset). The field references
            // one by stable id; blank/unknown falls back to the library's default palette.
            var library = SasquatchPaletteLibraryManager.Current;
            var palette = library.GetPalette(node.ColorPaletteId) ?? library.DefaultPalette;

            if (palette == null || palette.Swatches == null || palette.Swatches.Count == 0)
            {
                input.Add(BuildNoPalettePrompt(node, wrapper));
                return wrapper;
            }

            bool barMode = node.ColorPaletteLayout == ColorPaletteLayout.Bar;
            // HDR only applies to a real float `Color` (a Color32 palette can't hold >1 values). When on, the
            // "current color" bar's picker gains an intensity control and an "HDR" badge is stamped so it reads at
            // a glance — on the bar when it's shown, else in the widget's corner (covers both Grid and Bar layouts).
            bool paletteHdr = node.ColorFieldMode == ColorFieldMode.HDR && prop.propertyType == SerializedPropertyType.Color;
            float size = Mathf.Max(10f, node.ColorSwatchSize);
            int columns = Mathf.Max(0, node.ColorPaletteColumns);
            float spacing = Mathf.Max(0f, node.ColorSwatchSpacing); // grid gap around each swatch (0 = touch)
            float cellPitch = size + spacing * 2f;                   // grid: one cell = the swatch plus its gap

            // ---- GRID CELL METRICS ----
            // A grid cell is a FIXED slot and the gap lives on the drawn block's INSET, not on the cell's
            // margin. A margin sits outside the flex basis, so `columns` margined cells never fit one line:
            // the shipped `100/cols - 2%` basis rendered ColorPaletteColumns = 5 as 3-3-3-1 at every width
            // from 180px to 600px (measured 2026-08-27), and the short last row — which has slack to spare —
            // drew its cells 15px wider than the full rows'. With the gap inset instead, `columns` cells sum
            // to exactly 100% and a partly-filled row keeps its columns in register.
            void ApplyGridCellMetrics(VisualElement cell)
            {
                cell.style.marginTop = 0;
                cell.style.marginRight = 0;
                cell.style.marginBottom = 0;
                cell.style.marginLeft = 0;
                cell.style.height = cellPitch;
                cell.style.flexGrow = 0; // a partly-filled last row must NOT stretch its cells to fill it
                if (columns > 0)
                {
                    // Exactly 1/cols. Zero slack is safe here — and is what holds the columns in register —
                    // because the row is NoWrap: it cannot reflow, at worst a rounding overshoot shrinks a hair.
                    cell.style.width = Length.Percent(100f / columns);
                    cell.style.flexShrink = 1;
                }
                else
                {
                    cell.style.width = cellPitch;
                    cell.style.flexShrink = 0;
                }
            }

            void InsetGridVisual(VisualElement drawn) // the absolutely-filled block inside a grid cell
            {
                drawn.style.left = spacing;
                drawn.style.top = spacing;
                drawn.style.right = spacing;
                drawn.style.bottom = spacing;
            }

            var entries = new List<(VisualElement visual, VisualElement ring, Color color)>();
            ColorField colorBar = null;

            // Transient "Copied #hex" toast that springs in over the field and fades out. This widget grew
            // the flourish first; it now lives in SasquatchToast so the other places that want one cannot
            // drift away from it in hold time or fade curve.
            void Flash(Color c) => SasquatchToast.Show(wrapper, node.ColorCopyOnClick
                ? "Copied  " + ColorHexUtility.ToHex(c)
                : ColorHexUtility.ToHex(c));

            VisualElement selectedRing = null;
            void SetSelectedRing(VisualElement ring)
            {
                if (selectedRing == ring) return;
                if (selectedRing != null) selectedRing.style.display = DisplayStyle.None;
                selectedRing = ring;
                if (selectedRing != null) selectedRing.style.display = DisplayStyle.Flex;
            }

            // Highlight exactly ONE swatch for the current value. If the current selection still matches (e.g.
            // we just clicked a specific duplicate), keep it; otherwise fall to the FIRST match — so identical
            // colors never all light up at once.
            void UpdateSelectionFromValue()
            {
                if (MixedValueUI.IsMixed(prop)) { SetSelectedRing(null); return; } // no single value → no active swatch
                if (!TryReadPaletteColor(prop, out var current)) { SetSelectedRing(null); return; }
                foreach (var e in entries)
                    if (e.ring == selectedRing && ColorApproximately(e.color, current)) return;
                foreach (var e in entries)
                    if (ColorApproximately(e.color, current)) { SetSelectedRing(e.ring); return; }
                SetSelectedRing(null);
            }

            void Pick(Color c, VisualElement visual, VisualElement ring)
            {
                WritePaletteColor(prop, c);
                prop.serializedObject.ApplyModifiedProperties();
                if (node.ColorCopyOnClick) EditorGUIUtility.systemCopyBuffer = ColorHexUtility.ToHex(c);
                SetSelectedRing(ring); // the SPECIFIC swatch clicked, not every duplicate
                if (EditorSpringFX.MotionEnabled) visual.PunchScale(0.4f, EditorSpringFX.Tune(SpringConfig.Bouncy));
                if (colorBar != null)
                {
                    colorBar.SetValueWithoutNotify(c); // snap the color bar instantly...
                    if (EditorSpringFX.MotionEnabled) colorBar.PunchScale(0.6f, EditorSpringFX.Tune(SpringConfig.Bouncy)); // ...and pop it UP
                }
                Flash(c);
            }

            VisualElement MakeSwatch(Color c, string swatchName, bool first, bool last)
            {
                var slot = new VisualElement();
                slot.AddToClassList("sasq-swatch");
                slot.AddToClassList(barMode ? "sasq-swatch--bar" : "sasq-swatch--grid");
                if (barMode)
                {
                    slot.style.height = size;
                    slot.style.flexGrow = 1;
                    slot.style.flexBasis = 0;
                }
                else ApplyGridCellMetrics(slot);
                slot.tooltip = string.IsNullOrEmpty(swatchName)
                    ? ColorHexUtility.ToHex(c)
                    : swatchName + "  ·  " + ColorHexUtility.ToHex(c);

                var visual = new VisualElement();
                visual.AddToClassList("sasq-swatch-visual");
                if (!barMode) visual.AddToClassList("sasq-swatch-visual--grid");
                visual.style.backgroundColor = c;
                visual.pickingMode = PickingMode.Ignore; // pointer stays on the stable slot
                if (barMode)
                {
                    if (first) { visual.style.borderTopLeftRadius = 5f; visual.style.borderBottomLeftRadius = 5f; }
                    if (last) { visual.style.borderTopRightRadius = 5f; visual.style.borderBottomRightRadius = 5f; }
                    visual.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100));
                }
                else
                {
                    visual.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
                    InsetGridVisual(visual);
                }
                slot.Add(visual);

                // Inset selection ring — adaptive (dark on light swatches, white on dark) and inset a couple of
                // px so neighbours can't clip it (an edge-hugging border is invisible on the flush bar).
                var ring = new VisualElement();
                ring.AddToClassList("sasq-swatch-ring");
                ring.pickingMode = PickingMode.Ignore;
                ring.style.display = DisplayStyle.None;
                var ringColor = Luminance(c) > 0.6f ? new Color(0f, 0f, 0f, 0.85f) : Color.white;
                ring.style.borderTopColor = ringColor;
                ring.style.borderBottomColor = ringColor;
                ring.style.borderLeftColor = ringColor;
                ring.style.borderRightColor = ringColor;
                visual.Add(ring);

                entries.Add((visual, ring, c));

                // Rim colors are written INLINE, per swatch, because the right rim depends on the swatch (see
                // SwatchRimColor). That beats the USS `:hover` rule, so hover is driven from here too — and
                // BEFORE the motion early-out below, or turning animations off would take the hover edge with
                // them, which it never did when USS owned it.
                void SetRim(Color rim)
                {
                    visual.style.borderTopColor = rim;
                    visual.style.borderBottomColor = rim;
                    visual.style.borderLeftColor = rim;
                    visual.style.borderRightColor = rim;
                }
                SetRim(SwatchRimColor(c));

                slot.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    SetRim(SwatchRimHoverColor());
                    if (!EditorSpringFX.MotionEnabled) return;
                    visual.SpringScale(barMode ? 1.12f : 1.16f, EditorSpringFX.Tune(SpringConfig.Snappy));
                    if (barMode) visual.SpringTranslate(new Vector2(0f, -5f), EditorSpringFX.Tune(SpringConfig.Snappy));
                });
                slot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    SetRim(SwatchRimColor(c));
                    if (!EditorSpringFX.MotionEnabled) return;
                    visual.SpringScale(1f, EditorSpringFX.Tune(SpringConfig.Snappy));
                    if (barMode) visual.SpringTranslate(Vector2.zero, EditorSpringFX.Tune(SpringConfig.Snappy));
                });
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    Pick(c, visual, ring);
                    evt.StopPropagation();
                });
                return slot;
            }

            var surface = new VisualElement();
            surface.AddToClassList(barMode ? "sasq-swatch-bar" : "sasq-swatch-grid");
            if (barMode)
            {
                surface.style.flexDirection = FlexDirection.Row;
                surface.style.height = size;
            }
            else
            {
                // ---- WHY THE GRID IS A COLUMN OF NoWrap ROWS AND NEVER `flexWrap` ----
                // A wrapping row's height is measured by the parent through a DIFFERENT width than the row
                // finally lays out at. The "current color" bar beside it is `width: 34%`, and a percentage has
                // no containing block during intrinsic sizing, so the measure pass hands the swatches the FULL
                // row width, fits them on fewer lines, and the parent commits a height ONE SWATCH LINE short.
                // The real pass then wraps for real and that line draws OUTSIDE the field's own box — and
                // because the owning row centers its children it splits half above / half below, which is the
                // palette visibly sitting inside the field ABOVE it.
                // Measured 2026-08-27 on SxDisplayTypes (10-swatch `nord` grid, Columns cell, 415pt): the field
                // reported height 96 against a 35px cell and started 31px above its own parent, landing inside
                // the rich-text box above. Forcing NoWrap on the same tree took the overflow to 0 at every
                // width; a fixed-px bar (instead of 34%) did too, which is what named the percentage as the
                // trigger. Rows also make ColorPaletteColumns real — see ApplyGridCellMetrics.
                surface.style.flexDirection = FlexDirection.Column;
                surface.style.alignItems = Align.Stretch; // Center collapses each row to its content width
            }

            // Every grid tile in reading order (the swatches, then "+"), and the controls that trail them.
            // Bar mode ignores both and keeps adding straight to the surface.
            var tiles = new List<VisualElement>();
            var trailing = new List<VisualElement>();
            int packedPerRow = 0;

            VisualElement NewGridRow()
            {
                var row = new VisualElement();
                row.AddToClassList("sasq-swatch-row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.NoWrap;
                return row;
            }

            void PackGrid(int perRow)
            {
                perRow = Mathf.Max(1, perRow);
                packedPerRow = perRow;
                surface.Clear();
                VisualElement row = null;
                int inRow = 0;
                foreach (var tile in tiles)
                {
                    if (row == null || inRow == perRow) { row = NewGridRow(); surface.Add(row); inRow = 0; }
                    row.Add(tile);
                    inRow++;
                }
                if (trailing.Count > 0)
                {
                    // The gear / HDR badge ride the last row when it still has a free cell, else their own
                    // row — never crammed into a full one, which a NoWrap row cannot absorb.
                    if (row == null || inRow >= perRow) { row = NewGridRow(); surface.Add(row); }
                    foreach (var t in trailing) row.Add(t);
                }
            }

            var list = palette.Swatches;
            for (int i = 0; i < list.Count; i++)
            {
                var slot = MakeSwatch(list[i].color, list[i].name, i == 0, i == list.Count - 1);
                if (barMode) surface.Add(slot); else tiles.Add(slot);
            }

            // Grid-only "+" tile that adds the field's current color to the palette (a quick convenience).
            // Inserts the new swatch IN PLACE (no full inspector rebuild) so the hierarchy doesn't scroll-jump,
            // and skips colors already present so the palette doesn't fill with duplicates.
            if (!barMode)
            {
                var addTile = new VisualElement();
                addTile.AddToClassList("sasq-swatch");
                addTile.AddToClassList("sasq-swatch--add");
                ApplyGridCellMetrics(addTile);
                addTile.tooltip = "Add the field's current color to this palette";
                // The drawn box is a child, inset like a swatch's visual, so the "+" tile occupies exactly one
                // grid cell and still shows the same gap around it as its neighbours.
                var addFill = new VisualElement();
                addFill.AddToClassList("sasq-swatch--add-fill");
                addFill.pickingMode = PickingMode.Ignore;
                InsetGridVisual(addFill);
                addTile.Add(addFill);
                var plus = new Label("+");
                plus.AddToClassList("sasq-swatch-add-glyph");
                plus.pickingMode = PickingMode.Ignore;
                addFill.Add(plus);
                addTile.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    if (TryReadPaletteColor(prop, out var current))
                    {
                        bool exists = false;
                        foreach (var e in entries)
                            if (ColorApproximately(e.color, current)) { exists = true; break; }
                        if (!exists)
                        {
                            // The resolved palette is an entry inside the library; persist via the library asset
                            // (GetOrCreate so a transient default library is written to disk on first edit).
                            var lib = SasquatchPaletteLibraryManager.GetOrCreate();
                            Undo.RecordObject(lib, "Add Color To Palette");
                            palette.Swatches.Add(new SasquatchColorPaletteLibrary.Swatch(
                                ColorHexUtility.ToHex(current, false), current));
                            EditorUtility.SetDirty(lib);
                            AssetDatabase.SaveAssetIfDirty(lib);

                            var newSlot = MakeSwatch(current, ColorHexUtility.ToHex(current, false), false, false);
                            tiles.Insert(Mathf.Max(0, tiles.IndexOf(addTile)), newSlot); // stay ahead of "+"
                            PackGrid(packedPerRow);                                      // re-flow the rows in place
                            if (EditorSpringFX.MotionEnabled) EditorSpringFX.PopIn(newSlot[0]);
                        }
                        UpdateSelectionFromValue();
                    }
                    evt.StopPropagation();
                });
                tiles.Add(addTile);
            }

            // ---- Palette picker (gear) ----
            // Rebuilds ONLY this field in place so a palette change never tears down the whole inspector — that
            // full teardown (SasquatchEditorRegistrar.RefreshInspectors) is what resets scroll and reflows
            // everything, making the inspector "jump". We rebuild through the SAME pipeline LayoutTreeBuilder
            // used (CreateBindingField → label alignment, header wrap, tooltips) and swap it into the exact same
            // slot, then re-stamp the node/owner registries the builder sets right after building — so scope
            // resolution, label-column alignment, and right-click quick-edit all keep working and nothing
            // outside this single field moves a pixel.
            void RebuildSelfInPlace()
            {
                // The stamped element is THIS wrapper (no [Header]) or an ancestor header wrapper — find it by
                // the owner scope the builder attached, so we replace exactly what the builder placed.
                VisualElement stamped = wrapper;
                LayoutOwnerScope scope = null;
                for (var el = wrapper; el != null; el = el.parent)
                {
                    var s = LayoutOwnerRegistry.GetScope(el);
                    if (s != null) { stamped = el; scope = s; break; }
                }

                var parent = stamped.parent;
                if (parent == null) { SasquatchEditorRegistrar.RefreshInspectors(); return; } // detached fallback
                int slot = parent.IndexOf(stamped);

                // Reproduce the original label visibility (a cell/collection field was built forceHideLabel:true,
                // which is exactly when resolvedLabel is empty) so the rebuilt field doesn't suddenly show a label.
                var fresh = CreateBindingField(node, prop.propertyPath, prop.serializedObject,
                    forceHideLabel: string.IsNullOrEmpty(resolvedLabel));
                if (slot >= 0) parent.Insert(slot, fresh); else parent.Add(fresh);
                stamped.RemoveFromHierarchy();

                LayoutNodeRegistry.SetNode(fresh, node);
                if (scope != null) LayoutOwnerRegistry.SetScope(fresh, scope);
            }

            // Writes the per-FIELD palette choice (node.ColorPaletteId) and persists it to the owning layout the
            // same way the quick-edit popup does (the node aliases the persisted database entry). Falls back to
            // setting the value directly when no owning scope is resolvable (unscoped / test seam).
            void ApplyFieldPalette(string paletteId)
            {
                var scope = LayoutOwnerRegistry.ResolveScope(wrapper);
                if (scope?.LayoutAsset != null)
                {
                    LayoutRegistry.BeginLayoutEditUndo(scope.LayoutAsset, "Change Color Palette");
                    node.ColorPaletteId = paletteId;
                    LayoutRegistry.PersistLayoutEdit(scope.LayoutAsset);
                    // A designer window holding this type is on a clone taken before this write, and its
                    // Save would put the old palette back. No index path: this is a single-property write,
                    // so a dirty designer clone simply keeps its own value until the user saves it.
                    SasquatchLayoutDesignerWindow.AdoptLiveLayoutEdit(scope.Target != null ? scope.Target.GetType() : null);
                }
                else
                {
                    node.ColorPaletteId = paletteId;
                }
                RebuildSelfInPlace();
            }

            void SetLibraryDefault(string paletteId)
            {
                var lib = SasquatchPaletteLibraryManager.GetOrCreate();
                Undo.RecordObject(lib, "Set Default Palette");
                lib.DefaultPaletteId = paletteId;
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssetIfDirty(lib);
                RebuildSelfInPlace();
            }

            void CreateAndAssignPalette()
            {
                var created = SasquatchColorPaletteWindow.CreatePalette("New Palette");
                if (created == null) return;
                ApplyFieldPalette(created.Id); // assigns + refreshes inspectors
                SasquatchColorPaletteWindow.Open(created);
            }

            void ShowPaletteMenu(Button anchor)
            {
                var lib = SasquatchPaletteLibraryManager.Current;
                var menu = new GenericMenu();
                string currentId = node.ColorPaletteId;
                var def = lib.DefaultPalette;
                string defName = def != null ? def.DisplayName : "none";

                menu.AddItem(new GUIContent($"Use Default Palette  ({defName})"),
                    string.IsNullOrEmpty(currentId), () => ApplyFieldPalette(""));
                menu.AddSeparator("");

                foreach (var p in lib.Palettes)
                {
                    string capturedId = p.Id;
                    bool on = !string.IsNullOrEmpty(currentId) && currentId == capturedId;
                    menu.AddItem(new GUIContent(p.DisplayName), on, () => ApplyFieldPalette(capturedId));
                }

                var resolved = lib.GetPalette(currentId) ?? def;
                menu.AddSeparator("");
                if (resolved != null && lib.DefaultPaletteId != resolved.Id)
                    menu.AddItem(new GUIContent($"Set “{resolved.DisplayName}” as Default"),
                        false, () => SetLibraryDefault(resolved.Id));
                else
                    menu.AddDisabledItem(new GUIContent("Set as Default"));

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Open Color Palette Window"), false,
                    () => SasquatchColorPaletteWindow.Open(resolved));
                menu.AddItem(new GUIContent("Create New Palette…"), false, CreateAndAssignPalette);

                menu.DropDown(anchor.worldBound);
            }

            Button MakeGearButton()
            {
                Button gear = null;
                gear = new Button(() => ShowPaletteMenu(gear));
                gear.AddToClassList("sasq-swatch-gear");
                gear.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Settings, 11));
                gear.tooltip = "Choose palette, set the default, or open the Color Palette window";
                gear.style.flexShrink = 0;
                return gear;
            }

            // ---- THE COLOR BAR HAS A HARD FLOOR, AND A PERCENTAGE WIDTH DOES NOT KNOW IT ----
            // A ColorField cannot shrink past its own content: Unity's eyedropper is a FIXED 18px and
            // `unity-color-field__input` carries 1px of padding either side, so the input box bottoms out at
            // 23px. Below that it does not shrink — it hangs OUT of the field's right edge, and the field's
            // clip shears the rounded corners off into a flat vertical cut. Measured live 2026-08-31 in a
            // two-track Columns inspector: `width: 34%` of a 128px track gave the field 19.5px, it held a 23px
            // input (3.5px of overhang), the swatch collapsed to 3px and only the LEFT corners stayed round —
            // while the wider track beside it rendered perfectly, which is what makes it read as a draw bug
            // rather than a sizing one. Diagnose it the way [[readout-affix-reserve-must-target-the-pinning-element]]
            // says: compare the input's worldBound.xMax against its own parent's.
            // So pin a floor — and set it high enough that the swatch stays READABLE, not merely uncut.
            const float kColorBarMinWidth = 40f;   // 1px pad + a ~20px swatch + the 18px eyedropper + 1px pad
            const float kGearLaneWidth = 23f;      // .sasq-swatch-gear: 20px wide + its 3px left margin

            // Optional "current color" bar (standard ColorField + eyedropper) with a gear at its right edge to
            // pick the palette / open the window. Left/Right keep the bar on the SAME ROW (~34% wide); Above/Below
            // stack it on its own row.
            if (node.ColorShowBar)
            {
                TryReadPaletteColor(prop, out var initial);
                colorBar = new ColorField();
                colorBar.AddToClassList("sasq-swatch-colorbar");
                colorBar.showAlpha = node.ColorShowAlpha; // honor the Show Alpha option, same as the plain Color field
                colorBar.hdr = paletteHdr;                 // HDR intensity picker when the mode is on
                colorBar.SetValueWithoutNotify(initial);
                colorBar.showMixedValue = MixedValueUI.IsMixed(prop); // native em-dash on the bar when mixed
                colorBar.style.flexGrow = 1;
                colorBar.style.flexShrink = 1;
                colorBar.style.minWidth = kColorBarMinWidth; // never below its own content — see the floor above
                colorBar.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(50));
                colorBar.RegisterValueChangedCallback(evt =>
                {
                    WritePaletteColor(prop, evt.newValue);
                    prop.serializedObject.ApplyModifiedProperties();
                    UpdateSelectionFromValue();
                });

                if (paletteHdr) ApplyColorFieldHdrBadge(colorBar); // "HDR" on the color bar itself (own badge)

                // Bar + gear share a row so the gear is always glued to the bar's right edge.
                var barRow = new VisualElement();
                barRow.style.flexDirection = FlexDirection.Row;
                barRow.style.alignItems = Align.Center;
                barRow.Add(colorBar);
                barRow.Add(MakeGearButton());

                var placement = node.ColorBarPlacement;
                bool sameRow = placement == ColorBarPlacement.Left || placement == ColorBarPlacement.Right;
                if (sameRow)
                {
                    input.style.flexDirection = FlexDirection.Row;
                    // CENTERED against the palette, not pinned to its top. Top-alignment was chosen to line the
                    // bar up with the grid's FIRST swatch row, but that only reads as alignment when the grid is
                    // exactly ONE row tall — and at one row the two choices are indistinguishable anyway. At the
                    // two or three rows a real palette actually has, a 18px bar pinned to the top of a 60px block
                    // just looks stranded. The exported kit has centered since it was written, so this also
                    // closes a live/export parity gap rather than opening one.
                    input.style.alignItems = Align.Center;
                    surface.style.flexGrow = 1;
                    surface.style.flexShrink = 1;
                    surface.style.minWidth = 0;
                    barRow.style.flexShrink = 0;
                    barRow.style.width = Length.Percent(34);
                    // The percentage is the PREFERENCE; this is the floor. The bar's own minWidth alone is not
                    // enough — barRow is fixed-width and NoShrink, so a bar that refused to shrink would push the
                    // gear straight out of the row instead. Reserve the whole lane and let `surface` give it up.
                    barRow.style.minWidth = kColorBarMinWidth + kGearLaneWidth;
                    if (placement == ColorBarPlacement.Left)
                    {
                        barRow.style.marginRight = 6;
                        input.Add(barRow);
                        input.Add(surface);
                    }
                    else
                    {
                        barRow.style.marginLeft = 6;
                        input.Add(surface);
                        input.Add(barRow);
                    }
                }
                else
                {
                    input.style.flexDirection = FlexDirection.Column;
                    colorBar.style.height = 16; // thinner on its own row
                    if (placement == ColorBarPlacement.Above)
                    {
                        barRow.style.marginBottom = 4;
                        input.Add(barRow);
                        input.Add(surface);
                    }
                    else
                    {
                        barRow.style.marginTop = 4;
                        input.Add(surface);
                        input.Add(barRow);
                    }
                }
            }
            else
            {
                // No "current color" bar to host it — but the gear must ALWAYS be available so the palette stays
                // swappable in a click. Place it inline instead: glued to the continuous bar's right edge, or
                // trailing the grid's "+" tile (so the order reads "+", gear, then the HDR badge).
                var gear = MakeGearButton();
                if (barMode)
                {
                    // Bar + gear share a row so the gear sits just past the bar's rounded right end.
                    var barControlsRow = new VisualElement();
                    barControlsRow.style.flexDirection = FlexDirection.Row;
                    barControlsRow.style.alignItems = Align.Center;
                    surface.style.flexGrow = 1;
                    surface.style.flexShrink = 1;
                    surface.style.minWidth = 0;
                    barControlsRow.Add(surface);
                    barControlsRow.Add(gear);
                    input.Add(barControlsRow);

                    // A continuous bar carries the "HDR" tag centered on itself (both axes) — clear of the gear.
                    if (paletteHdr) AttachHdrBadgeCentered(surface);
                }
                else
                {
                    input.Add(surface);
                    // The "+" tile is the last grid tile, so the gear trails it — vertically centered in its
                    // row to line up with the tiles. It rides the last row when that row has a free cell.
                    gear.style.alignSelf = Align.Center;
                    trailing.Add(gear);
                    // ...then the HDR badge trails the gear, so left-to-right reads "+", gear, HDR.
                    if (paletteHdr)
                    {
                        var badge = CreateHdrBadge();
                        badge.style.alignSelf = Align.Center;
                        badge.style.marginLeft = 6;
                        trailing.Add(badge);
                    }
                }
            }

            // ---- LAY THE GRID OUT IN ROWS ----
            if (!barMode)
            {
                if (columns > 0)
                {
                    // Authored: pure arithmetic on the tile count. Identical at every width, never re-derived,
                    // so the parent can never measure it short.
                    PackGrid(columns);
                }
                else
                {
                    // Auto: the line count genuinely IS a function of width — so COMPUTE it from the surface's
                    // own resolved width and re-pack when the answer changes, rather than let flexWrap discover
                    // it (whose height the parent measures a line short — see the surface comment above). The
                    // surface's width is set by its parent in every placement, never by its content, so this
                    // cannot feed back on itself.
                    PackGrid(tiles.Count); // seeded as one row; the first geometry pass settles it
                    surface.RegisterCallback<GeometryChangedEvent>(_ =>
                    {
                        float w = surface.contentRect.width;
                        if (float.IsNaN(w) || w <= 1f) return;
                        int perRow = Mathf.Max(1, Mathf.FloorToInt((w + 0.5f) / Mathf.Max(1f, cellPitch)));
                        if (perRow != packedPerRow) PackGrid(perRow);
                    });
                }
            }

            // Keep the bar + selection ring honest if the value changes elsewhere (another widget, an undo, etc.).
            wrapper.TrackPropertyValueWithRefresh(prop, _ =>
            {
                if (colorBar != null)
                {
                    colorBar.showMixedValue = MixedValueUI.IsMixed(prop);
                    if (TryReadPaletteColor(prop, out var cur)) colorBar.SetValueWithoutNotify(cur);
                }
                UpdateSelectionFromValue();
            });
            wrapper.RegisterCallback<AttachToPanelEvent>(_ => UpdateSelectionFromValue());
            UpdateSelectionFromValue();

            return wrapper;
        }

        // Friendly stand-in shown only if the palette library somehow has no palettes at all (the library seeds
        // three defaults, so this is essentially unreachable). Creating one here assigns it to THIS field and
        // persists the choice to the owning layout (same path as the gear picker).
        private static VisualElement BuildNoPalettePrompt(BindingNode node, VisualElement wrapper)
        {
            var box = new VisualElement();
            box.AddToClassList("sasq-swatch-empty");

            var msg = new Label("No color palettes in the library.");
            msg.AddToClassList("sasq-swatch-empty-msg");
            box.Add(msg);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            var createBtn = new Button(() =>
            {
                var palette = SasquatchColorPaletteWindow.CreatePalette("New Palette");
                if (palette == null) return;
                var scope = LayoutOwnerRegistry.ResolveScope(wrapper);
                if (scope?.LayoutAsset != null)
                {
                    LayoutRegistry.BeginLayoutEditUndo(scope.LayoutAsset, "Assign Color Palette");
                    node.ColorPaletteId = palette.Id;
                    LayoutRegistry.PersistLayoutEdit(scope.LayoutAsset);
                    SasquatchLayoutDesignerWindow.AdoptLiveLayoutEdit(scope.Target != null ? scope.Target.GetType() : null);
                }
                else
                {
                    node.ColorPaletteId = palette.Id;
                }
                SasquatchColorPaletteWindow.Open(palette);
                SasquatchEditorRegistrar.RefreshInspectors();
            })
            { text = "Create Palette" };
            createBtn.AddToClassList("sasq-swatch-empty-btn");
            row.Add(createBtn);

            var openBtn = new Button(() => SasquatchColorPaletteWindow.Open(null)) { text = "Open Window" };
            openBtn.AddToClassList("sasq-swatch-empty-btn");
            row.Add(openBtn);

            box.Add(row);
            return box;
        }

        // Reads a Color from either a `Color` (colorValue) or a `Color32` (byte r/g/b/a children) property.
        private static bool TryReadPaletteColor(SerializedProperty prop, out Color color)
        {
            if (prop.propertyType == SerializedPropertyType.Color)
            {
                color = prop.colorValue;
                return true;
            }
            var r = prop.FindPropertyRelative("r");
            var g = prop.FindPropertyRelative("g");
            var b = prop.FindPropertyRelative("b");
            var a = prop.FindPropertyRelative("a");
            if (r != null && g != null && b != null && a != null)
            {
                color = new Color32(
                    (byte)Mathf.Clamp(r.intValue, 0, 255),
                    (byte)Mathf.Clamp(g.intValue, 0, 255),
                    (byte)Mathf.Clamp(b.intValue, 0, 255),
                    (byte)Mathf.Clamp(a.intValue, 0, 255));
                return true;
            }
            color = Color.white;
            return false;
        }

        private static void WritePaletteColor(SerializedProperty prop, Color color)
        {
            if (prop.propertyType == SerializedPropertyType.Color)
            {
                prop.colorValue = color;
                return;
            }
            var c = (Color32)color;
            var r = prop.FindPropertyRelative("r");
            var g = prop.FindPropertyRelative("g");
            var b = prop.FindPropertyRelative("b");
            var a = prop.FindPropertyRelative("a");
            if (r != null) r.intValue = c.r;
            if (g != null) g.intValue = c.g;
            if (b != null) b.intValue = c.b;
            if (a != null) a.intValue = c.a;
        }

        private static bool ColorApproximately(Color a, Color b)
        {
            const float e = 1.5f / 255f;
            return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e &&
                   Mathf.Abs(a.b - b.b) < e && Mathf.Abs(a.a - b.a) < e;
        }

        // Perceived brightness (Rec. 601) — picks a contrasting selection-ring color per swatch.
        private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>
        /// The 1px rim laid over a swatch. It is a translucent white (dark skin) / black (light skin) drawn
        /// OVER the swatch's own color, so on most swatches it composites back onto the fill and the swatch
        /// reads its full width.
        ///
        /// <para><b>The bug this fixes.</b> On a NEAR-BLACK swatch a fixed 16% white rim composites to almost
        /// exactly the editor's own panel grey, so the rim reads as BACKGROUND and the swatch looks a pixel
        /// narrower on each side than its light neighbours — visibly shorter in a flush Bar, where the
        /// neighbour's rim vanishes into its own fill. Measured 2026-08-27 off a zoomed capture of the Nord
        /// palette: fill #191724 → rim #3E3C47 at luminance <b>0.243</b>, against a panel at <b>0.241</b>.
        /// Two thousandths apart. A light swatch had a rim 0.004 from its own fill and read full width.</para>
        ///
        /// <para><b>The rule.</b> A rim may safely blend into the FILL — that just reads as more swatch. It
        /// must never blend into the PANEL. Only a LIGHTENING rim can do that, because it moves toward the
        /// panel's own brightness, so the ramp applies to the dark skin alone: a black rim on a light skin
        /// cannot reach a lighter panel from any fill (0.55 × fill is below any light panel by construction).
        /// Alpha therefore ramps up only as the swatch darkens, and swatches at or above
        /// <see cref="RimRampCeiling"/> luminance keep the shipped 16% exactly — so everything that already
        /// read correctly is pixel-identical.</para>
        /// </summary>
        internal static Color SwatchRimColor(Color c)
        {
            if (WidgetPalette.IsLight) return new Color(0f, 0f, 0f, 0.45f);
            float t = Mathf.Clamp01(Luminance(c) / RimRampCeiling);
            return new Color(1f, 1f, 1f, Mathf.Lerp(RimAlphaDarkest, RimAlphaBase, t));
        }

        /// <summary>Hover rim — unchanged in intent (a brighter edge), but written inline alongside
        /// <see cref="SwatchRimColor"/>, because an inline border color beats the USS <c>:hover</c> rule and
        /// a swatch that set one and not the other would simply stop responding to hover.</summary>
        internal static Color SwatchRimHoverColor() =>
            WidgetPalette.IsLight ? new Color(0f, 0f, 0f, 0.72f) : new Color(1f, 1f, 1f, 0.6f);

        // The shipped rim alpha, kept for every swatch light enough not to need help, and the alpha a pure
        // black swatch gets. At 0.42 a black fill rims to luminance 0.42 — clear of every dark editor panel
        // (Unity's #383838 is 0.22; the tinted section in the report was 0.24).
        private const float RimAlphaBase = 0.16f;
        private const float RimAlphaDarkest = 0.42f;
        private const float RimRampCeiling = 0.35f;

    }
}
