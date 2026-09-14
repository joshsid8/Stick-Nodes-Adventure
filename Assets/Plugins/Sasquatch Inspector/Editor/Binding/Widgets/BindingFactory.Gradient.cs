using System;
using System.Collections.Generic;
using SasquatchSpring;
using SasquatchInspector.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // Gradient widget: a modern, spring-driven alternative to Unity's GradientField. The bound gradient renders
        // into a composited strip (gradient over a checkerboard) with draggable color/alpha pins
        // (SasquatchGradientControl), native blend modes (Blend/Fixed/PerceptualBlend), a preset ribbon, an inline
        // palette gear, and an expand button to a freely-resizable editor window. Inline shows the strip directly;
        // Expandable and ExpandableWithPreview wrap it in the shared expandable group box.
        // forceMode/forceStartExpanded/includeExpand let a non-Gradient-binding host (e.g. the progress-bar
        // Value-Based Fill, which stores a Gradient on the node) reuse the FULL editor in Expandable style.
        internal static VisualElement CreateGradientEditor(BindingNode node, SerializedProperty prop, string resolvedLabel,
            GradientEditorMode? forceMode = null, bool forceStartExpanded = false, bool includeExpand = true,
            bool? forceEditable = null)
        {
            // Fall back to a plain field when the property isn't actually a Gradient.
            if (prop.propertyType != SerializedPropertyType.Gradient)
                return new PropertyField(prop) { label = resolvedLabel };

            var onLayoutEdited = LayoutTreeBuilder.CurrentOnLayoutEdited;
            // forceEditable overrides the node's read-only state — a hosted gradient (e.g. the progress-bar fill
            // ramp) is design config, editable even when the bound VALUE is read-only.
            bool editableInline = forceEditable ?? (!node.GradientReadOnlyInline && !node.IsReadOnly);

            var control = new SasquatchGradientControl(prop, node, editableInline);
            control.PaletteColorsProvider = () => GetGradientPaletteColors(node);
            if (node.GradientPinLayout == GradientPinLayout.Merged) control.NormalizeForMerged();

            VisualElement root = new VisualElement();
            root.AddToClassList("sasq-gradient-field");

            control.OnWarn = msg => FlashGradientMessage(root, msg);
            control.OpenPinRequested = (ch, idx) => OpenGradientPinPopover(control, node, ch, idx, onLayoutEdited);

            var well = BuildGradientWell(control, node);
            MixedValueUI.AttachGraphGhost(well, control, prop); // honest multi-object mixed: ghost + em-dash

            var content = new VisualElement();
            content.Add(well);
            if (editableInline) content.Add(BuildGradientPresetStrip(control));
            content.Add(BuildGradientActionRow(control, node, includeExpand: includeExpand,
                onExpand: () => SasquatchGradientEditorWindow.Open(prop, node), onLayoutEdited: onLayoutEdited));

            var mode = forceMode ?? node.GradientEditorMode;
            if (mode == GradientEditorMode.Expandable || mode == GradientEditorMode.ExpandableWithPreview)
            {
                VisualElement headerExtra = null;
                if (mode == GradientEditorMode.ExpandableWithPreview)
                {
                    var previewWell = new VisualElement();
                    previewWell.AddToClassList("sasq-gradient-well");
                    previewWell.AddToClassList("sasq-gradient-preview");
                    // No fixed width — see the curve preview: BuildExpandableGroup grows it from the shared
                    // label column to the header's right edge so it tracks the inspector.
                    previewWell.style.minWidth = 80;
                    previewWell.style.height = 18;
                    var preview = new SasquatchGradientControl(prop, node, editable: false) { Minimal = true };
                    previewWell.Add(preview);
                    headerExtra = previewWell;
                }

                string title = string.IsNullOrEmpty(resolvedLabel) ? "Gradient" : resolvedLabel;
                // Shared expandable group box — identical to the Curve + Particle expandable widgets.
                root.Add(BuildExpandableGroup(title, headerExtra, content,
                    stateKey: FoldoutStateStore.Key(prop.serializedObject.targetObject, prop.propertyPath, title),
                    fallbackExpanded: forceStartExpanded || node.WidgetDefaultToOpen));
            }
            else // Inline
            {
                if (!string.IsNullOrEmpty(resolvedLabel))
                {
                    var lbl = new Label(resolvedLabel);
                    lbl.AddToClassList("sasq-gradient-label");
                    // Tall custom widget with no unity-base-field of its own: without an anchor the
                    // universal affix decorator would park the suffix at the very BOTTOM of the widget.
                    lbl.AddToClassList(FieldAffixes.AffixAnchorClass);
                    root.Add(lbl);
                }
                root.Add(content);
            }

            return root;
        }

        // True when any color key carries HDR intensity (a channel > 1). Drives the strip's "HDR" tag: it shows only
        // once real intensity has been applied, not merely because the widget is in HDR mode. Small epsilon so plain
        // white (1,1,1) never trips it.
        internal static bool GradientHasHdrColor(Gradient g)
        {
            if (g == null) return false;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                var c = keys[i].color;
                if (c.r > 1.0001f || c.g > 1.0001f || c.b > 1.0001f) return true;
            }
            return false;
        }

        // ----- Strip well + optional resize handle (mirrors BuildCurveWell / AttachCurveResize) -----
        private static VisualElement BuildGradientWell(SasquatchGradientControl control, BindingNode node)
        {
            var well = new VisualElement();
            well.AddToClassList("sasq-gradient-well");
            ApplyGradientWellBorder(well, node);
            well.Add(control);

            // NOTE: the strip's "HDR" tag is drawn INSIDE the control's mesh (SasquatchGradientControl), BEFORE the
            // pins, so the color pins always draw on top of it — an element overlay here would sit over the pins.
            // It appears only when a key actually carries HDR intensity (see GradientHasHdrColor), not just when HDR
            // mode is on.

            if (!node.GradientResizable)
            {
                well.style.height = Mathf.Clamp(node.GradientWellHeight, 18f, 80f);
                return well;
            }

            well.style.height = Mathf.Clamp(node.GradientWellHeight, 18f, 160f);

            var wrap = new VisualElement();
            wrap.Add(well);

            var handle = new VisualElement();
            handle.AddToClassList("sasq-gradient-resize-handle");
            handle.AddToClassList(LayoutTreeBuilder.DesignKeepInteractiveClass); // resize works in design mode too
            var grip = new GripGlyph();
            grip.style.alignSelf = Align.Center;
            handle.Add(grip);
            handle.RegisterCallback<PointerEnterEvent>(_ => grip.Tint = GripGlyph.HoverTint);
            handle.RegisterCallback<PointerLeaveEvent>(_ => grip.Tint = GripGlyph.DefaultTint);
            AttachGradientResize(handle, well, node);
            wrap.Add(handle);
            return wrap;
        }

        private static void AttachGradientResize(VisualElement handle, VisualElement well, BindingNode node)
        {
            var onEdited = LayoutTreeBuilder.CurrentOnLayoutEdited;
            bool resizing = false;
            float startY = 0f, startH = 0f;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                startY = evt.position.y;
                startH = well.resolvedStyle.height > 1f ? well.resolvedStyle.height : node.GradientWellHeight;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float raw = Mathf.Clamp(startH + (evt.position.y - startY), 18f, 160f);
                well.style.height = raw;
                node.GradientWellHeight = raw;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
                onEdited?.Invoke();
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
        }

        // Applies the node's border color + width to a gradient well (shared by the inline widget + the window).
        internal static void ApplyGradientWellBorder(VisualElement well, BindingNode node)
        {
            float w = Mathf.Clamp(node.GradientBorderWidth, 0f, 6f);
            well.style.borderTopWidth = w;
            well.style.borderBottomWidth = w;
            well.style.borderLeftWidth = w;
            well.style.borderRightWidth = w;
            well.style.borderTopColor = node.GradientBorderColor;
            well.style.borderBottomColor = node.GradientBorderColor;
            well.style.borderLeftColor = node.GradientBorderColor;
            well.style.borderRightColor = node.GradientBorderColor;
        }

        // ----- Action row: blend-mode segmented control (left) + palette gear + expand (right) -----
        internal static VisualElement BuildGradientActionRow(SasquatchGradientControl control, BindingNode node, bool includeExpand, Action onExpand, Action onLayoutEdited)
        {
            var row = new VisualElement();
            row.AddToClassList("sasq-gradient-actionrow");

            var seg = new VisualElement();
            seg.AddToClassList("sasq-gradient-seg");

            var modes = new[] { GradientMode.Blend, GradientMode.Fixed, GradientMode.PerceptualBlend };
            var tips = new Dictionary<GradientMode, string>
            {
                { GradientMode.Blend, "Smooth linear blend between keys." },
                { GradientMode.Fixed, "Hard stepped bands (each key holds until the next)." },
                { GradientMode.PerceptualBlend, "Perceptually-uniform (Oklab) smoothing — avoids muddy mid-tones." },
            };
            var segButtons = new List<(GradientMode mode, Button btn)>();
            foreach (var m in modes)
            {
                var mm = m;
                var b = new Button(() => control.SetMode(mm)) { text = SasquatchGradientControl.NiceMode(m), tooltip = tips[m] };
                b.AddToClassList("sasq-gradient-seg-btn");
                // The seg buttons are a flush pill — a gentle scale (vs the 1.16 used elsewhere) keeps the
                // springy feel while limiting the hover overflow that a neighbor would otherwise clip.
                ApplyGradientButtonSpring(b, 1.05f, 0.97f);
                segButtons.Add((m, b));
                seg.Add(b);
            }
            // Flush-pill end caps: Unity USS has no :first-child/:last-child, so tag the first/last
            // segment explicitly for the rounded outer corners (see .sasq-gradient-seg-btn--first/--last).
            if (segButtons.Count > 0)
            {
                segButtons[0].btn.AddToClassList("sasq-gradient-seg-btn--first");
                segButtons[segButtons.Count - 1].btn.AddToClassList("sasq-gradient-seg-btn--last");
            }
            row.Add(seg);

            // Spacer pushes the ruler controls + expand button to the right edge.
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            row.Add(spacer);

            // --- Ruler + Snap (editing aids; blue when on, like the blend buttons) — right-aligned, left of expand ---
            // Lines count (2–10), visible only while the ruler is on (sits left of the Ruler/Snap buttons).
            var linesRow = new VisualElement();
            linesRow.style.flexDirection = FlexDirection.Row;
            linesRow.style.alignItems = Align.Center;
            linesRow.style.marginRight = 6;
            var linesLabel = new Label("Lines");
            linesLabel.style.fontSize = 10;
            linesLabel.style.marginRight = 4;
            linesLabel.style.color = WidgetPalette.LabelMuted;
            var linesSlider = new SliderInt(2, 10) { value = control.RulerLines };
            linesSlider.style.width = 64;
            var linesVal = new Label(control.RulerLines.ToString());
            linesVal.style.fontSize = 10;
            linesVal.style.marginLeft = 4;
            linesVal.style.minWidth = 12;
            linesRow.Add(linesLabel);
            linesRow.Add(linesSlider);
            linesRow.Add(linesVal);

            // Ruler = an icon button (lucide "ruler"); blue when active.
            var rulerBtn = new Button { tooltip = "Show evenly-spaced vertical guide lines on the strip" };
            rulerBtn.AddToClassList("sasq-curve-btn");
            rulerBtn.AddToClassList("sasq-curve-iconbtn");
            rulerBtn.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Ruler, 13));
            ApplyGradientButtonSpring(rulerBtn);

            var snapBtn = new Button { text = "Snap", tooltip = "Magnetically snap dragged keys to the nearest ruler line — you can still pull past it" };
            snapBtn.AddToClassList("sasq-curve-btn");
            ApplyGradientButtonSpring(snapBtn);

            void RefreshRulerUi()
            {
                rulerBtn.EnableInClassList("sasq-curve-btn--active", control.ShowRuler);
                snapBtn.EnableInClassList("sasq-curve-btn--active", control.SnapToRuler && control.ShowRuler);
                snapBtn.SetEnabled(control.ShowRuler); // snap is meaningless without the ruler lines
                linesRow.style.display = control.ShowRuler ? DisplayStyle.Flex : DisplayStyle.None;
            }
            rulerBtn.clicked += () => { control.ShowRuler = !control.ShowRuler; RefreshRulerUi(); };
            snapBtn.clicked += () => { control.SnapToRuler = !control.SnapToRuler; RefreshRulerUi(); };
            linesSlider.RegisterValueChangedCallback(e => { control.RulerLines = e.newValue; linesVal.text = control.RulerLines.ToString(); });
            RefreshRulerUi();

            row.Add(linesRow);
            row.Add(rulerBtn);
            row.Add(snapBtn);

            // No palette gear in this row: palette swapping lives only inside the pin color popover, which the
            // caller wires onLayoutEdited to directly.

            if (includeExpand && onExpand != null)
            {
                var expand = new Button(onExpand) { tooltip = "Open the resizable gradient editor" };
                expand.AddToClassList("sasq-curve-btn");
                expand.AddToClassList("sasq-curve-iconbtn");
                expand.style.marginLeft = 6;
                expand.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ExternalLink, 13));
                ApplyGradientButtonSpring(expand);
                row.Add(expand);
            }

            void Refresh()
            {
                var cur = control.Mode;
                foreach (var (m, b) in segButtons)
                    b.EnableInClassList("sasq-gradient-seg-btn--active", m == cur);
            }
            control.Changed += Refresh;
            Refresh();
            return row;
        }

        internal static Button BuildGradientPaletteGear(BindingNode node, Action onLayoutEdited)
        {
            Button gear = null;
            gear = new Button(() => ShowGradientPaletteMenu(node, gear, onLayoutEdited))
            { tooltip = "Choose the color palette used by the pin picker, or open the Color Palette window" };
            gear.AddToClassList("sasq-curve-btn");
            gear.AddToClassList("sasq-curve-iconbtn");
            gear.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Settings, 13));
            ApplyGradientButtonSpring(gear);
            return gear;
        }

        // A uniform springy hover + press for every button in the gradient widget, matching the pins'
        // bouncy feel. Transform-only (scale), motion-gated by EditorSpringFX — never reorders flex siblings.
        // hoverScale/pressDepress are tunable so flush controls (the seg pill) can use a gentler pop.
        internal static void ApplyGradientButtonSpring(VisualElement btn, float hoverScale = 1.16f, float pressDepress = 0.92f)
            => EditorSpringFX.ButtonSpring(btn, hoverScale, pressDepress);

        private static void ShowGradientPaletteMenu(BindingNode node, Button anchor, Action onLayoutEdited)
        {
            var lib = SasquatchPaletteLibraryManager.Current;
            var menu = new GenericMenu();
            var def = lib?.DefaultPalette;
            string defName = def != null ? def.DisplayName : "none";

            // The palette choice is LAYOUT data (it lives on the node and persists through onLayoutEdited),
            // so it owes an undo record like every other layout edit — the Color Palette widget's own
            // ApplyFieldPalette is the reference. Without the pairing the write persists to disk and the
            // next Ctrl+Z pops some earlier unrelated record instead. Resolved from the anchor's ancestor
            // chain so a node inside an inline editor records against the layout that actually owns it.
            void Assign(string paletteId)
            {
                var layout = LayoutOwnerRegistry.ResolveScope(anchor)?.LayoutAsset;
                if (layout != null) LayoutRegistry.BeginLayoutEditUndo(layout, "Change Gradient Palette");
                node.GradientColorPaletteId = paletteId;
                onLayoutEdited?.Invoke();
            }

            menu.AddItem(new GUIContent($"Use Default Palette  ({defName})"), string.IsNullOrEmpty(node.GradientColorPaletteId),
                () => Assign(""));
            if (lib?.Palettes != null && lib.Palettes.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var p in lib.Palettes)
                {
                    var id = p.Id;
                    menu.AddItem(new GUIContent(p.DisplayName), node.GradientColorPaletteId == id, () => Assign(id));
                }
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open Color Palette Window"), false,
                () => SasquatchColorPaletteWindow.Open(lib?.GetPalette(node.GradientColorPaletteId) ?? lib?.DefaultPalette));
            menu.DropDown(anchor.worldBound);
        }

        // ----- Preset ribbon (mirrors the curve's BuildPresetStrip) -----
        // includeMenu controls whether the trailing "⋮" library-menu button is part of the strip. The fullscreen
        // editor window passes false and hosts the ⋮ in its top action row instead.
        internal static VisualElement BuildGradientPresetStrip(SasquatchGradientControl control, bool includeMenu = true)
        {
            var outer = new VisualElement();
            outer.AddToClassList("sasq-curve-swatch-row");

            void Rebuild()
            {
                outer.Clear();
                var lib = SasquatchGradientPresetLibrary.Instance;
                for (int i = 0; i < lib.Presets.Count; i++)
                {
                    int idx = i;
                    var entry = lib.Presets[i];
                    var sw = BuildGradientPresetSwatch(entry.name, entry.gradient);
                    sw.AddManipulator(new ContextualMenuManipulator(m =>
                    {
                        m.menu.AppendAction("Apply", _ => control.ApplyGradientFull(entry.gradient));
                        m.menu.AppendAction("Delete Preset", _ => { lib.RemoveAt(idx); Rebuild(); });
                    }));
                    // Click = apply; drag out of the strip = delete (with a "release to delete" tip); drag within =
                    // reorder; Escape mid-drag cancels (nothing is committed until release).
                    AttachPresetDrag(sw, idx, outer, lib, () => control.ApplyGradientFull(entry.gradient), Rebuild);
                    outer.Add(sw);
                }
                outer.Add(BuildGradientAddSwatch(() =>
                {
                    var lib2 = SasquatchGradientPresetLibrary.Instance;
                    lib2.Add("Preset " + (lib2.Presets.Count + 1), control.SnapshotGradient());
                    Rebuild();
                }));
                if (includeMenu) outer.Add(BuildGradientPresetMenuButton(control, Rebuild));
            }
            Rebuild();
            return outer;
        }

        // ---- SHARED preset swatch rasters -------------------------------------------------------------
        //
        // Every preset swatch used to rasterise its own 64x24 strip: 156 us apiece, 9 swatches to a ribbon,
        // so 1.418 ms per gradient widget and 99% of the ribbon's build. On a target with ten gradients that
        // measured 21.74 ms in situ — 44% of the whole inspector build — plus ~54 KB of texture churn each.
        // The ribbon is also rebuilt wholesale on every preset add, delete and reorder, so the same nine
        // strips were re-rasterised for every one of those.
        //
        // Nothing about a preset swatch varies per widget: same renderer, same gradient, same 64x24, same
        // checker colours (CheckerA/CheckerB are static readonly, not skin-derived). Identical inputs, so the
        // cached texture is pixel-identical by construction — pinned by TestGradientSwatchCacheMatchesAFreshRender.
        //
        // THE LIFETIME RULE, AND WHY THE DETACH DESTROY HAD TO GO. The old comment above this method said
        // "own the texture ... never share a cache that could destroy it underneath", and it was right about
        // the hazard: with a shared texture, the FIRST swatch to be torn down would destroy the instance every
        // other swatch is still painting with, and they would all go pink. So the destroy is gone — this cache
        // is swept only on domain reload and quit, exactly like SasquatchIcons'. Any future site that receives
        // one of these textures must NOT destroy it either.
        //
        // Keyed on the gradient's CONTENT signature rather than its preset index, so editing a preset yields
        // a different key on its own and no invalidation hook is needed; a rename or a reorder correctly
        // changes nothing, because neither changes a pixel. Bounded in practice by the distinct gradients a
        // session previews (one 64x24 RGBA each, ~6 KB), and cleared wholesale on reload.
        private static readonly Dictionary<string, Texture2D> _presetSwatchCache = new Dictionary<string, Texture2D>();

        [InitializeOnLoadMethod]
        private static void HookGradientSwatchCacheSweep()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearGradientSwatchCache;
            EditorApplication.quitting += ClearGradientSwatchCache;
        }

        /// <summary>Destroys every cached preset swatch raster (reload / quit). The ONLY place these die.</summary>
        internal static void ClearGradientSwatchCache()
        {
            foreach (var tex in _presetSwatchCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _presetSwatchCache.Clear();
        }

        /// <summary>
        /// The shared 64x24 raster for <paramref name="g"/>, rendered once per distinct gradient. The caller
        /// must NEVER destroy the result — see the cache comment above.
        /// </summary>
        internal static Texture2D GetGradientSwatchTexture(Gradient g, int width, int height)
        {
            if (g == null) return null;
            // Content signature + every other input the renderer reads. The checker colours are invariant
            // today; they are in the key anyway so a future skin-derived checker cannot silently alias.
            string key = SasquatchGradientPresetLibrary.Signature(g)
                         + "|" + width + "x" + height
                         + "|" + (Color32)SasquatchGradientControl.CheckerA
                         + "|" + (Color32)SasquatchGradientControl.CheckerB;

            // A destroyed entry is treated as a miss, not trusted: Unity's overloaded == catches the fake-null
            // an out-of-band DestroyImmediate would leave, so even a rule violation degrades to a re-render
            // rather than to a pink swatch.
            if (_presetSwatchCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var made = SasquatchGradientStripRenderer.Create(g, width, height, true,
                SasquatchGradientControl.CheckerA, SasquatchGradientControl.CheckerB);
            if (made != null) _presetSwatchCache[key] = made;
            return made;
        }

        private static VisualElement BuildGradientPresetSwatch(string tooltip, Gradient g)
        {
            var el = new VisualElement { tooltip = tooltip };
            el.AddToClassList("sasq-curve-swatch");
            el.AddToClassList("sasq-gradient-preset-swatch");
            // SHARED, not owned. There is deliberately no PanelLifetime.OnRealDetach destroy here: this
            // texture belongs to _presetSwatchCache and is painting in every other swatch showing the same
            // gradient. Destroying it on this element's teardown is what turns the rest of them pink.
            var tex = GetGradientSwatchTexture(g, 64, 24);
            if (tex != null) el.style.backgroundImage = Background.FromTexture2D(tex);
            ApplyGradientButtonSpring(el);
            return el; // click / drag / reorder / delete wired by AttachPresetDrag
        }

        // Drag a preset: a short press = apply; drag within the strip = LIVE reorder (the other swatches shuffle
        // aside with a spring BEFORE you release, like the canvas); drag out of the strip (up/down) = delete with a
        // spring "release to delete" card pinned where the swatch was; Escape mid-drag cancels (nothing is committed
        // until release — the live preview springs back).
        private static void AttachPresetDrag(VisualElement sw, int index, VisualElement strip,
            SasquatchGradientPresetLibrary lib, Action apply, Action rebuild)
        {
            const float DeleteOffset = 18f; // px past the strip's top/bottom before a drag arms a delete
            var slideCfg = EditorSpringFX.Tune(new SpringConfig(0.30f, 0.12f)); // gentle slot-shuffle (low bounce)
            bool armed = false, dragging = false, pendingDelete = false, canceled = false;
            Vector2 startWorld = default;
            int pointerId = -1, fromSlot = -1, lastTarget = -1;
            Label tip = null;
            VisualElement dropInd = null;               // blue outline showing where the dragged preset will land
            EventCallback<KeyDownEvent> escHandler = null;
            VisualElement panelRoot = null;
            var slotEls = new List<VisualElement>();   // preset swatches in order, captured at drag start
            var slotPos = new List<Vector2>();          // their layout positions (world, transform-free) at drag start

            void ClearEsc()
            {
                if (escHandler != null && panelRoot != null) panelRoot.UnregisterCallback(escHandler, TrickleDown.TrickleDown);
                escHandler = null; panelRoot = null;
            }
            void RemoveTip() { if (tip != null) { tip.RemoveFromHierarchy(); tip = null; } }
            void RemoveDropIndicator() { if (dropInd != null) { dropInd.RemoveFromHierarchy(); dropInd = null; } }

            // A blue outline at the physical slot the dragged preset will land in (new-order index t → slotPos[t]).
            void ShowDropIndicator(int t)
            {
                if (slotEls.Count == 0) return;
                t = Mathf.Clamp(t, 0, slotEls.Count - 1);
                if (dropInd == null)
                {
                    dropInd = new VisualElement { pickingMode = PickingMode.Ignore };
                    dropInd.AddToClassList("sasq-gradient-drop-indicator");
                    dropInd.style.position = Position.Absolute;
                    strip.Add(dropInd);
                    dropInd.SendToBack(); // behind the swatches so it reads as a slot outline, not over them
                }
                var local = strip.WorldToLocal(slotPos[t]);
                dropInd.style.left = local.x;
                dropInd.style.top = local.y;
                dropInd.style.width = Mathf.Max(4f, sw.layout.width);
                dropInd.style.height = Mathf.Max(4f, sw.layout.height);
                dropInd.style.display = DisplayStyle.Flex;
            }

            void CaptureSlots()
            {
                slotEls.Clear(); slotPos.Clear(); fromSlot = -1;
                foreach (var c in strip.Children())
                {
                    if (!c.ClassListContains("sasq-gradient-preset-swatch")) continue;
                    if (c == sw) fromSlot = slotEls.Count;
                    slotEls.Add(c);
                    slotPos.Add(c.worldBound.position); // translate is 0 here, so this is the true layout slot
                }
            }

            // Nearest captured slot center to the pointer (uses the FIXED start positions, so mid-animation
            // transforms don't perturb the target). Works across wrapped rows (2D distance).
            int TargetFor(Vector2 worldPos)
            {
                int best = fromSlot; float bestDist = float.MaxValue;
                for (int i = 0; i < slotEls.Count; i++)
                {
                    Vector2 center = slotPos[i] + new Vector2(slotEls[i].layout.width * 0.5f, slotEls[i].layout.height * 0.5f);
                    float dx = center.x - worldPos.x, dy = center.y - worldPos.y, dist = dx * dx + dy * dy;
                    if (dist < bestDist) { bestDist = dist; best = i; }
                }
                return best;
            }

            // Spring every NON-dragged swatch to the slot it would occupy if the dragged item were dropped at t.
            void PreviewReorder(int t)
            {
                var order = new List<int>();
                for (int i = 0; i < slotEls.Count; i++) order.Add(i);
                order.RemoveAt(fromSlot);
                order.Insert(Mathf.Clamp(t, 0, order.Count), fromSlot);
                for (int s = 0; s < slotEls.Count; s++)
                {
                    if (s == fromSlot) continue;
                    int targetSlot = order.IndexOf(s);
                    slotEls[s].SpringTranslate(slotPos[targetSlot] - slotPos[s], slideCfg);
                }
            }
            void ResetSiblings()
            {
                for (int s = 0; s < slotEls.Count; s++)
                    if (s != fromSlot) slotEls[s].SpringTranslate(Vector2.zero, slideCfg);
            }
            void ResetAllVisual()
            {
                sw.style.translate = new Translate(0, 0);
                sw.style.scale = new Scale(Vector3.one);
                for (int s = 0; s < slotEls.Count; s++) slotEls[s].style.translate = new Translate(0, 0);
            }

            sw.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                armed = true; dragging = false; pendingDelete = false; canceled = false;
                startWorld = e.position; pointerId = e.pointerId;
                sw.CapturePointer(e.pointerId);
            });

            sw.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!armed || !sw.HasPointerCapture(e.pointerId)) return;
                Vector2 d = (Vector2)e.position - startWorld;
                if (!dragging)
                {
                    if (d.magnitude < DesignerPalette.DragThreshold) return;
                    dragging = true;
                    SasquatchSpring.Spring.CancelAll(sw); // stop the hover spring so it can't fight our transform
                    CaptureSlots();
                    lastTarget = fromSlot;
                    panelRoot = sw.panel?.visualTree;
                    escHandler = ev =>
                    {
                        if (ev.keyCode != KeyCode.Escape || !dragging) return;
                        canceled = true; // release → PointerCaptureOut cleans up; nothing committed
                        if (sw.HasPointerCapture(pointerId)) sw.ReleasePointer(pointerId);
                        ev.StopPropagation();
                    };
                    panelRoot?.RegisterCallback(escHandler, TrickleDown.TrickleDown);
                }
                sw.style.translate = new Translate(d.x, d.y); // grabbed item follows the pointer
                sw.style.scale = new Scale(new Vector3(1.12f, 1.12f, 1f));

                float localY = strip.WorldToLocal((Vector2)e.position).y;
                bool outside = (localY < -DeleteOffset || localY > strip.contentRect.height + DeleteOffset) && lib.Presets.Count > 1;
                if (outside)
                {
                    if (!pendingDelete) { pendingDelete = true; ResetSiblings(); RemoveDropIndicator(); lastTarget = fromSlot; } // clear the reorder preview
                    if (tip == null) tip = ShowPresetDeleteTip(strip, slotPos.Count > fromSlot ? slotPos[fromSlot] : (Vector2)e.position);
                }
                else
                {
                    if (pendingDelete) { pendingDelete = false; RemoveTip(); }
                    int t = TargetFor((Vector2)e.position);
                    if (t != lastTarget) { lastTarget = t; PreviewReorder(t); } // shuffle siblings BEFORE release
                    ShowDropIndicator(lastTarget); // blue outline where it will land
                }
                e.StopPropagation();
            });

            sw.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!armed) return;
                bool wasDragging = dragging, del = pendingDelete, wasCanceled = canceled;
                int target = lastTarget;
                // Disarm BEFORE releasing so the PointerCaptureOut that fires from ReleasePointer is a no-op.
                armed = false; dragging = false; pendingDelete = false; canceled = false;
                ClearEsc(); RemoveTip(); RemoveDropIndicator(); ResetAllVisual();
                if (sw.HasPointerCapture(e.pointerId)) sw.ReleasePointer(e.pointerId);

                if (wasCanceled) { rebuild(); return; }
                if (!wasDragging) { apply(); return; }       // a click, not a drag → apply the preset
                if (del) { lib.RemoveAt(index); rebuild(); return; }
                if (target >= 0 && target != fromSlot) lib.Move(index, target);
                rebuild();
            });

            sw.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!armed) return; // normal release / escape already handled
                ClearEsc(); RemoveTip(); RemoveDropIndicator(); ResetAllVisual();
                armed = false; dragging = false; pendingDelete = false;
            });
        }

        // The "release to delete" card, pinned where the dragged swatch WAS (it doesn't follow the cursor).
        private static Label ShowPresetDeleteTip(VisualElement strip, Vector2 anchorWorld)
        {
            var tip = new Label("Release to delete") { pickingMode = PickingMode.Ignore };
            tip.AddToClassList("sasq-gradient-tip"); // reuse the gradient-key drag tip style
            tip.style.position = Position.Absolute;
            var local = strip.WorldToLocal(anchorWorld);
            tip.style.left = Mathf.Clamp(local.x - 18f, 0f, Mathf.Max(0f, strip.contentRect.width - 84f));
            tip.style.top = Mathf.Clamp(local.y, -2f, Mathf.Max(-2f, strip.contentRect.height - 16f));
            strip.Add(tip);
            EditorSpringFX.PopIn(tip); // spring the card in
            return tip;
        }

        private static VisualElement BuildGradientAddSwatch(Action onClick)
        {
            var el = new VisualElement { tooltip = "Save the current gradient as a new preset" };
            el.AddToClassList("sasq-curve-swatch");
            el.AddToClassList("sasq-curve-swatch-add");
            var plus = new Label("+");
            plus.AddToClassList("sasq-curve-swatch-plus");
            el.Add(plus);
            el.RegisterCallback<ClickEvent>(_ => onClick());
            ApplyGradientButtonSpring(el);
            return el;
        }

        internal static VisualElement BuildGradientPresetMenuButton(SasquatchGradientControl control, Action rebuild)
        {
            var btn = new Button(() =>
            {
                var lib = SasquatchGradientPresetLibrary.Instance;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Current Gradient as Preset"), false, () =>
                {
                    lib.Add("Preset " + (lib.Presets.Count + 1), control.SnapshotGradient());
                    rebuild();
                });
                menu.AddItem(new GUIContent("Add Factory Presets"), false, () => { lib.AddFactoryPresets(); rebuild(); });
                menu.AddItem(new GUIContent("Import Unity Presets"), false, () =>
                {
                    var (added, skipped) = UnityPresetImporter.ImportGradients(lib);
                    EditorUtility.DisplayDialog("Import Unity Presets",
                        $"Imported {added} new preset(s), skipped {skipped} duplicate(s).", "OK");
                    rebuild();
                });
                menu.AddItem(new GUIContent("Reset to Factory Presets"), false, () => { lib.ResetToFactory(); rebuild(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Reveal Library Asset"), false, () => lib.Reveal());
                menu.ShowAsContext();
            })
            { tooltip = "Preset library options" };
            btn.AddToClassList("sasq-curve-btn");
            btn.AddToClassList("sasq-curve-iconbtn");
            btn.Add(BuildDotsGlyph()); // reuse the curve factory's 3-dot glyph
            ApplyGradientButtonSpring(btn);
            return btn;
        }

        // ----- Palette colors (for the pin popover + match dot) -----
        internal static List<Color> GetGradientPaletteColors(BindingNode node)
        {
            var list = new List<Color>();
            var library = SasquatchPaletteLibraryManager.Current;
            var palette = library?.GetPalette(node.GradientColorPaletteId) ?? library?.DefaultPalette;
            if (palette?.Swatches != null)
                foreach (var s in palette.Swatches) list.Add(s.color);
            return list;
        }

        // ----- The pin edit popover (color section and/or alpha section) -----
        internal static void OpenGradientPinPopover(SasquatchGradientControl control, BindingNode node, int ch, int idx,
            Action onLayoutEdited = null)
        {
            bool merged = node.GradientPinLayout == GradientPinLayout.Merged;
            bool showColor = merged || ch == 0;
            bool showAlpha = merged || ch == 1;
            var rect = control.PinScreenRect(ch, idx);
            Color cur = showColor ? control.PinColor(0, idx) : Color.white;
            float alpha = control.PinAlpha(ch, idx);
            GradientPinPopupWindow.Open(rect, node, showColor, showAlpha, cur, alpha,
                c => control.SetPinColor(0, idx, c),
                a => control.SetPinAlpha(ch, idx, a),
                onLayoutEdited,
                onClosed: control.RefocusForKeyboard,
                onDelete: () => control.RequestDeleteKey(ch, idx));
        }

        // Transient "..." toast that springs in over the field and fades (mirrors the ColorPalette Flash).
        private static void FlashGradientMessage(VisualElement host, string text)
        {
            if (host == null) return;
            var toast = new Label(text);
            toast.AddToClassList("sasq-swatch-toast");
            toast.pickingMode = PickingMode.Ignore;
            host.Add(toast);
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
            }).ExecuteLater(900);
        }
    }

    /// <summary>
    /// A tiny borderless popover anchored to a clicked pin. Color pins show a palette gear + 1-click palette grid +
    /// a native color picker; alpha pins show a 0–100% slider. Merged pins show both. Auto-closes on focus loss
    /// (returning focus to the gradient so the selected key stays Delete-able), and Delete/Backspace removes the key.
    /// </summary>
    internal sealed class GradientPinPopupWindow : EditorWindow
    {
        private BindingNode _node;
        private bool _showColor, _showAlpha;
        private Color _color;
        private float _alpha;
        private Action<Color> _onColor;
        private Action<float> _onAlpha;
        private Action _onLayoutEdited;
        private Action _onClosed;
        private Action _onDelete;
        private VisualElement _swatchGrid;

        public static void Open(Rect screenRect, BindingNode node, bool showColor, bool showAlpha,
            Color color, float alpha, Action<Color> onColor, Action<float> onAlpha,
            Action onLayoutEdited, Action onClosed, Action onDelete)
        {
            var w = CreateInstance<GradientPinPopupWindow>();
            w._node = node;
            w._showColor = showColor;
            w._showAlpha = showAlpha;
            w._color = color;
            w._alpha = alpha;
            w._onColor = onColor;
            w._onAlpha = onAlpha;
            w._onLayoutEdited = onLayoutEdited;
            w._onClosed = onClosed;
            w._onDelete = onDelete;

            int count = showColor ? BindingFactory.GetGradientPaletteColors(node).Count : 0;
            const int cols = 6;
            int rows = showColor ? Mathf.Max(1, Mathf.CeilToInt(count / (float)cols)) : 0;
            // +26 for the palette gear header row whenever a color section is shown.
            float height = 10f + (showColor ? 26f + rows * 24f + 30f : 0f) + (showAlpha ? 28f : 0f);
            w.ShowAsDropDown(screenRect, new Vector2(200f, height));
            EditorSpringFX.PopIn(w.rootVisualElement);
        }

        // Return focus to the gradient control when the dropdown closes, so its still-selected key answers Delete.
        private void OnDisable() { try { _onClosed?.Invoke(); } catch { } }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            // Widget sheet + skin token sheet (the popover root sits outside any build root, so the
            // --sasq-* variables must be applied here to reach its widget classes).
            SasquatchSkin.ApplyWithWidgetSheet(root);
            root.style.paddingLeft = 6; root.style.paddingRight = 6; root.style.paddingTop = 6; root.style.paddingBottom = 6;
            root.style.backgroundColor = WidgetPalette.PopoverBg;

            // Delete/Backspace removes the key being edited (the popover, not the control, holds focus here).
            // TrickleDown so we see it before any child field would.
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) && _onDelete != null)
                {
                    _onDelete.Invoke();
                    evt.StopPropagation();
                    Close();
                }
            }, TrickleDown.TrickleDown);

            if (_showColor)
            {
                // Palette gear — swap the palette feeding these swatches without leaving the picker (rebuilds live).
                var gearRow = new VisualElement();
                gearRow.style.flexDirection = FlexDirection.Row;
                gearRow.style.alignItems = Align.Center;
                gearRow.style.marginBottom = 4;
                var palLbl = new Label("Palette");
                palLbl.style.fontSize = 10;
                palLbl.style.color = WidgetPalette.LabelMuted;
                palLbl.style.flexGrow = 1;
                gearRow.Add(palLbl);
                gearRow.Add(BindingFactory.BuildGradientPaletteGear(_node, () =>
                {
                    _onLayoutEdited?.Invoke();
                    RebuildSwatches();
                }));
                root.Add(gearRow);

                _swatchGrid = new VisualElement();
                _swatchGrid.AddToClassList("sasq-gradient-popover-grid");
                root.Add(_swatchGrid);
                RebuildSwatches();

                // HDR gradients let a color key exceed 1 — swap in the intensity-aware picker (alpha still lives on
                // its own pins, so the color picker never shows alpha).
                var field = new ColorField { showAlpha = false, hdr = _node != null && _node.GradientHdr };
                field.SetValueWithoutNotify(new Color(_color.r, _color.g, _color.b, 1f));
                field.style.marginTop = 6;
                field.RegisterValueChangedCallback(e => _onColor?.Invoke(e.newValue));
                root.Add(field);
            }

            if (_showAlpha)
            {
                var alphaRow = new VisualElement();
                alphaRow.style.flexDirection = FlexDirection.Row;
                alphaRow.style.alignItems = Align.Center;
                alphaRow.style.marginTop = _showColor ? 6 : 0;
                var lbl = new Label("Alpha");
                lbl.style.width = 42;
                lbl.style.fontSize = 11;
                var slider = new Slider(0f, 1f) { value = Mathf.Clamp01(_alpha) };
                slider.style.flexGrow = 1;
                slider.style.marginRight = 4;
                var pct = new IntegerField { value = Mathf.RoundToInt(Mathf.Clamp01(_alpha) * 100f) };
                pct.style.width = 40;
                slider.RegisterValueChangedCallback(e =>
                {
                    _onAlpha?.Invoke(e.newValue);
                    pct.SetValueWithoutNotify(Mathf.RoundToInt(e.newValue * 100f));
                });
                pct.RegisterValueChangedCallback(e =>
                {
                    float a = Mathf.Clamp01(e.newValue / 100f);
                    slider.SetValueWithoutNotify(a);
                    if (Mathf.RoundToInt(a * 100f) != e.newValue) pct.SetValueWithoutNotify(Mathf.RoundToInt(a * 100f));
                    _onAlpha?.Invoke(a);
                });
                alphaRow.Add(lbl);
                alphaRow.Add(slider);
                alphaRow.Add(pct);
                root.Add(alphaRow);
            }

            // The popover root grabs focus so Delete works immediately; clicking a field re-focuses it as usual.
            root.schedule.Execute(() => { if (root.panel != null) root.Focus(); }).ExecuteLater(0);
        }

        // Repopulate the swatch grid from the node's CURRENT palette — called on open and after the gear swaps it.
        private void RebuildSwatches()
        {
            if (_swatchGrid == null) return;
            _swatchGrid.Clear();
            var colors = BindingFactory.GetGradientPaletteColors(_node);
            foreach (var c in colors)
            {
                var col = c;
                var sw = new VisualElement { tooltip = ColorHexUtility.ToHex(col) };
                sw.AddToClassList("sasq-gradient-popover-swatch");
                sw.style.backgroundColor = new Color(col.r, col.g, col.b, 1f);
                sw.RegisterCallback<ClickEvent>(_ => { _onColor?.Invoke(col); if (!_showAlpha) Close(); });
                BindingFactory.ApplyGradientButtonSpring(sw);
                _swatchGrid.Add(sw);
            }
        }
    }
}
