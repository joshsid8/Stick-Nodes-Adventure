using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    // ===== Copy/Paste Style + the format painter (paint roller) =====
    // Copy Style snapshots the selected node's visual styling into the in-memory StyleClipboard;
    // Paste Style applies it to the current selection. The paint roller is the Office-style
    // format painter: single-click arms ONE application (copies the selected node's style, next
    // node click paints it), double-click arms STICKY (keeps painting until cancelled), and
    // clicking the roller again or pressing Esc disarms. Painting intercepts SelectFromClick, so
    // it works identically on canvas cards and hierarchy rows (both funnel through it).
    //
    // WHAT A PAINT CARRIES: appearance only (StyleScope.Appearance). Calling it a "format painter"
    // borrows Office's contract wholesale, and Office's has never touched what a cell ACCEPTS — so
    // neither does this one. Slider bounds, step size, path filters, per-widget interactivity and
    // per-field content (unit suffix, placeholder, dropdown source) stay on the destination; only
    // authored preset templates carry those. See StyleScope for the full taxonomy.
    //
    // The widget KIND does travel, gated on the target field type — in a layout designer the widget
    // kind IS the format, and "make these five floats look like that progress bar" is the whole
    // point of the tool. It is also the loudest thing a paint does, so it gets named in the toast:
    // a visible main effect is never the surprise, an invisible side effect always is.
    public partial class SasquatchLayoutDesignerWindow
    {
        private Button _paintRollerButton;
        private SasquatchIconElement _paintRollerIcon;
        private bool _painterArmed;
        private bool _painterSticky;

        // ---- Armed-mode feedback ----
        // The painter is a MODE, and a mode the user can enter by accident (a double-click on the roller) needs
        // to announce itself continuously, not once in a toast that has already faded. Three signals, because a
        // tester who double-clicked the roller by mistake reported the window as broken:
        //   1. the paint-roller CURSOR over both node surfaces — "you are holding a tool";
        //   2. a gold ring + chip on the node the style is being copied FROM — "this is what you're painting";
        //   3. a flash + scale punch on the roller each time a style lands — "that click did something".
        private LayoutNode _painterSourceNode;
        private bool _painterChromeApplied;   // true while chrome is on the tree, so a disarm still gets one sweep

        /// <summary>Name of the gold overlay marking the node the painter is copying FROM.</summary>
        internal const string PaintSourceRingName = "sasq-paint-source-ring";
        /// <summary>Marker class on a node surface currently wearing the painter cursor (keeps the tick idempotent).</summary>
        internal const string PaintTargetClass = "sasq-paint-target";
        /// <summary>Class on every hierarchy row, so the painter can find rows without walking the whole tree.</summary>
        internal const string HierarchyRowClass = "sasq-hier-row";

        // The toolbar chrome shared with the Save button (22px centered), icon-only.
        private Button BuildStylePainterButton()
        {
            _paintRollerButton = new Button(() => { /* handled via ClickEvent for clickCount */ });
            _paintRollerButton.style.height = 22;
            _paintRollerButton.style.width = 26;
            _paintRollerButton.style.marginTop = 0;
            _paintRollerButton.style.marginBottom = 0;
            _paintRollerButton.style.marginRight = 8;
            _paintRollerButton.style.alignItems = Align.Center;
            _paintRollerButton.style.justifyContent = Justify.Center;
            _paintRollerIcon = SasquatchIcons.Create(SasquatchIcons.Ids.PaintRoller, 14);
            _paintRollerButton.Add(_paintRollerIcon);
            EditorSpringFX.ButtonSpring(_paintRollerButton);

            _paintRollerButton.RegisterCallback<ClickEvent>(evt =>
            {
                // A double-click arrives as clickCount 1 then 2: the first click toggles, the
                // second upgrades whatever state that left into sticky mode.
                if (evt.clickCount >= 2) ArmStylePainter(sticky: true);
                else if (_painterArmed) DisarmStylePainter();
                else ArmStylePainter(sticky: false);
            });

            // Both node surfaces rebuild on their own schedule, so the armed-mode chrome reconciles on a tick
            // instead of being pinned at the arm/paint call sites. Costs a single bool test while disarmed.
            _paintRollerButton.schedule.Execute(SyncPainterChrome).Every(120);

            UpdatePainterAffordances();
            return _paintRollerButton;
        }

        private void ArmStylePainter(bool sticky)
        {
            // The painter needs a single, unambiguous source to copy from.
            if (_selectedNode == null || HasMultiSelection)
            {
                ShowToolToast("Format Painter: select ONE node to copy its style from first.", ToastKind.Info);
                DisarmStylePainter();
                return;
            }

            StyleClipboard.Copy(_selectedNode);
            _painterSourceNode = _selectedNode;
            _painterArmed = true;
            _painterSticky = sticky;
            UpdatePainterAffordances();
            ShowToolToast(sticky
                ? "Format Painter (sticky): click nodes to paint the copied style — Esc or the roller to stop."
                : "Format Painter: click a node to paint the copied style — Esc cancels.", ToastKind.Info);
        }

        private void DisarmStylePainter()
        {
            if (!_painterArmed && !_painterSticky) { UpdatePainterAffordances(); return; }
            _painterArmed = false;
            _painterSticky = false;
            _painterSourceNode = null;
            UpdatePainterAffordances();
        }

        // Accent-tint the roller while armed + flag the canvas so USS can accent card hovers.
        private void UpdatePainterAffordances()
        {
            if (_paintRollerButton == null) return;
            bool armed = _painterArmed;
            _paintRollerButton.style.backgroundColor = armed
                ? (_painterSticky ? DesignerPalette.Accent : DesignerPalette.AccentSoft)
                : StyleKeyword.Null;
            if (_paintRollerIcon != null)
                // Sticky = opaque accent fill (white glyph both skins); non-sticky = translucent accent
                // WASH over the chrome, so the glyph needs the skin's strong ink to stay visible on light.
                _paintRollerIcon.Tint = armed
                    ? (_painterSticky ? Color.white : DesignerPalette.IconStrong)
                    : SasquatchIcons.DefaultTint;
            _paintRollerButton.tooltip = armed
                ? "Format Painter is armed — click a node to paint, Esc to cancel. (Double-click = keep painting)"
                : "Format Painter: click, then click a node to copy this node's style onto it. Double-click to paint repeatedly.";
            _previewContainer?.EnableInClassList("sasq-painting", armed);
            SyncPainterChrome();
        }

        // ---- The armed-mode chrome: source ring, painter cursor -----------------------------------------------

        /// <summary>
        /// The paint-roller mouse cursor, rasterized once per session. A <c>Cursor</c> with a TEXTURE is the only
        /// custom-cursor route the public UI Toolkit API offers (the built-in ids are internal), and it is worth
        /// the raster: the stock pointer said nothing about being in a mode, which is exactly how an accidental
        /// double-click on the roller read as "the designer is broken".
        /// </summary>
        private static StyleCursor PainterCursor()
        {
            var tex = SasquatchIcons.RenderCursorTexture(SasquatchIcons.Ids.PaintRoller);
            if (tex == null) return new StyleCursor(StyleKeyword.Null);
            // The roller HEAD is the business end of the tool and sits top-left in the Lucide glyph, so the
            // hotspot goes there — pointing with the handle would feel a whole glyph off.
            // Fully qualified: UnityEngine.Cursor (the input-system one) is in scope here too.
            return new StyleCursor(new UnityEngine.UIElements.Cursor
            {
                texture = tex,
                hotspot = new Vector2(tex.width * 0.2f, tex.height * 0.2f)
            });
        }

        /// <summary>
        /// Reconciles every armed-mode marker against the CURRENT tree. Runs on a slow tick rather than at the
        /// arm/paint call sites because both node surfaces rebuild from scratch on their own schedule (every
        /// paint refreshes the canvas, and any edit can refresh the hierarchy) — chrome pinned at the call site
        /// would survive exactly until the next rebuild and then silently vanish mid-mode. Self-healing, and a
        /// hard no-op once everything already matches.
        /// </summary>
        private void SyncPainterChrome()
        {
            bool armed = _painterArmed;
            if (!armed && !_painterChromeApplied) return; // nothing on the tree and nothing wanted

            var source = armed ? _painterSourceNode : null;
            SyncPainterSurfaces(_previewContainer, "designer-card", source, withChip: true);
            SyncPainterSurfaces(_hierarchyContainer, HierarchyRowClass, source, withChip: false);
            _painterChromeApplied = armed;
        }

        // One node surface (canvas cards or hierarchy rows). `armed` is implied by a non-null `source` OR by the
        // marker class already present — a disarm sweep must still find and strip surfaces.
        private void SyncPainterSurfaces(VisualElement scope, string surfaceClass, LayoutNode source, bool withChip)
        {
            if (scope == null) return;
            bool armed = _painterArmed;

            foreach (var surface in scope.Query<VisualElement>(className: surfaceClass).ToList())
            {
                // Cursor: inline, so it beats any USS rule on the card, and marker-class-guarded so this tick
                // never re-writes an unchanged style (which would dirty the element eight times a second).
                if (surface.ClassListContains(PaintTargetClass) != armed)
                {
                    surface.EnableInClassList(PaintTargetClass, armed);
                    surface.style.cursor = armed ? PainterCursor() : new StyleCursor(StyleKeyword.Null);
                }

                bool isSource = source != null && ReferenceEquals(surface.userData, source);
                var ring = DirectChild(surface, PaintSourceRingName);
                if (isSource && ring == null) surface.Add(BuildPaintSourceRing(withChip));
                else if (!isSource && ring != null) ring.RemoveFromHierarchy();
            }
        }

        // Direct children only: cards NEST, so a descendant search would find an inner card's ring and let the
        // outer card both skip its own and strip its child's.
        private static VisualElement DirectChild(VisualElement host, string name)
        {
            foreach (var child in host.Children())
                if (child.name == name) return child;
            return null;
        }

        // Brand-gold, because blue is already spoken for by selection — and the source node is USUALLY the
        // selected one, so a second blue would have been invisible. Absolute + PickingMode.Ignore so it can
        // neither re-flow the card nor eat a click meant for the node underneath.
        private static VisualElement BuildPaintSourceRing(bool withChip)
        {
            var gold = DesignerPalette.BrandGold;

            var ring = new VisualElement { name = PaintSourceRingName };
            ring.pickingMode = PickingMode.Ignore;
            ring.style.position = Position.Absolute;
            ring.style.left = 0;
            ring.style.top = 0;
            ring.style.right = 0;
            ring.style.bottom = 0;
            ring.style.backgroundColor = new Color(gold.r, gold.g, gold.b, 0.12f);
            ring.style.borderTopWidth = 2;
            ring.style.borderBottomWidth = 2;
            ring.style.borderLeftWidth = 2;
            ring.style.borderRightWidth = 2;
            ring.style.borderTopColor = gold;
            ring.style.borderBottomColor = gold;
            ring.style.borderLeftColor = gold;
            ring.style.borderRightColor = gold;
            ring.style.borderTopLeftRadius = 4;
            ring.style.borderTopRightRadius = 4;
            ring.style.borderBottomLeftRadius = 4;
            ring.style.borderBottomRightRadius = 4;

            if (withChip)
            {
                // A hierarchy row is ~20px tall, so only the canvas card gets the label — the ring alone is the
                // signal on a row.
                var chip = new Label("COPYING STYLE") { pickingMode = PickingMode.Ignore };
                chip.style.position = Position.Absolute;
                chip.style.top = 2;
                chip.style.right = 2;
                chip.style.paddingLeft = 5;
                chip.style.paddingRight = 5;
                chip.style.paddingTop = 1;
                chip.style.paddingBottom = 1;
                chip.style.fontSize = 9;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.backgroundColor = gold;
                chip.style.color = DesignerPalette.IsLight ? Color.white : Color.black;
                chip.style.borderTopLeftRadius = 3;
                chip.style.borderTopRightRadius = 3;
                chip.style.borderBottomLeftRadius = 3;
                chip.style.borderBottomRightRadius = 3;
                ring.Add(chip);
            }
            return ring;
        }

        // A flash + punch on the roller itself, so a click that paints somewhere off-screen (or onto a card whose
        // change is subtle) still reads as "that did something". The scheduled restore re-derives the button's
        // colors from the CURRENT state, so a one-shot paint settles into the disarmed look on its own.
        private void FlashPaintRoller()
        {
            if (_paintRollerButton == null) return;
            EditorSpringFX.PingScale(_paintRollerButton, 1.35f);
            _paintRollerButton.style.backgroundColor = DesignerPalette.BrandGold;
            if (_paintRollerIcon != null)
                _paintRollerIcon.Tint = DesignerPalette.IsLight ? Color.white : Color.black;
            _paintRollerButton.schedule.Execute(UpdatePainterAffordances).StartingIn(150);
        }

        // The clipboard needs the TARGET's bound field type to decide whether the copied widget
        // kind applies (float→Color paste keeps the kind but carries everything else).
        //
        // `kindChange` reports the one change a paste makes that the user cannot predict from the source
        // card alone: re-typing the widget. It is by far the loudest thing a paint does, and the flash on
        // the roller only ever said "something happened" — so the caller names it in the toast.
        private bool ApplyClipboardTo(LayoutNode node, out string kindChange)
        {
            kindChange = null;
            var binding = node as BindingNode;
            var fieldType = binding != null ? ResolveSelectedNodeFieldType(binding) : null;
            var kindBefore = binding?.DisplayType;

            // StyleClipboard.ApplyTo assigns the WHOLE LayoutStyle struct, and that struct carries the
            // resize-class fields (Style.width / Style.height) — TextBoxHeight travels explicitly beside it.
            // So a paste is a resize edit, and without claiming authority Save merges the registry's old sizes
            // straight back over it: every other pasted property sticks and the size alone snaps back, which
            // reads as an intermittent bug because a cross-KIND paste trips the merge's identity guard and does
            // stick. This is the single funnel for all three paste entry points (format painter, Paste Style,
            // Paste Style to All), so claiming here covers them together.
            ClaimResizeAuthority();

            // Appearance scope, deliberately: a paste carries the LOOK. Value bounds, step size, path filters
            // and per-widget interactivity are field-specific decisions about the DATA and stay put — painting
            // a slider's colors must not re-base the destination's track under its knob. Preset templates ask
            // for StyleScope.Full instead.
            bool applied = StyleClipboard.ApplyTo(node, fieldType, StyleScope.Appearance);

            if (applied && binding != null && kindBefore.HasValue && binding.DisplayType != kindBefore.Value)
                kindChange = DisplayTypeCatalog.Format(binding.DisplayType);
            return applied;
        }

        // Intercept for SelectFromClick: while armed, a node click PAINTS instead of selecting.
        // Returns true when the click was consumed.
        private bool TryApplyStylePainter(LayoutNode node)
        {
            if (!_painterArmed) return false;
            if (node == null || _clonedLayoutAsset == null || node == _clonedLayoutAsset.Root) return true;

            Undo.RecordObject(_clonedLayoutAsset, "Paint Style");
            string kind = StyleClipboard.SourceKindLabel;
            ApplyClipboardTo(node, out string kindChange);
            OnModification();
            RefreshPreview();
            // Same reason PasteStyleToSelection carries this: style rows of the open panel must reflect the
            // painted values. The painter KEEPS the selection, so painting onto the node whose panel is open
            // (most often the source onto itself) left that panel live but stale. It is not cosmetic for
            // Frames: ContainerTransfers REPLACES ContainerNode.Frame with a clone, and the Frame section
            // captures the settings object once at build, so ~20 rows kept writing to the detached instance —
            // silently dead, and the Shape row then restructured children while the shape write went nowhere.
            RefreshProperties();
            // A paint can restructure children (a container re-type, a frame shape's pane wrap), so the tree
            // shown beside the panel can be stale for the same click.
            RefreshHierarchy();
            // Disarm FIRST, flash second: the disarm re-derives the roller's colors, so flashing before it would
            // be wiped in the same frame on a one-shot paint. The flash's own scheduled restore then settles the
            // button into whichever state it ended in.
            if (!_painterSticky) DisarmStylePainter();
            FlashPaintRoller();
            // The flash says "that click did something"; the toast says WHAT. A re-type is the one outcome the
            // source card can't predict, so it gets named — otherwise the paint reports itself plainly. In
            // sticky mode each paint replaces the previous toast, so this stays one line however long you paint.
            ShowToolToast(PaintedStyleMessage(kind, kindChange), ToastKind.Success);
            return true;
        }

        // Shared by the painter and Paste Style so both narrate a paste the same way.
        private static string PaintedStyleMessage(string kindLabel, string kindChange)
        {
            string what = string.IsNullOrEmpty(kindLabel) ? "style" : kindLabel.ToLowerInvariant() + " style";
            return string.IsNullOrEmpty(kindChange)
                ? $"Painted {what}."
                : $"Painted {what} — widget is now {kindChange}.";
        }

        // Esc cancels an armed painter (wired into the root KeyDown handler).
        private bool HandleStylePainterKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || !_painterArmed) return false;
            DisarmStylePainter();
            ShowToolToast("Format Painter cancelled.", ToastKind.Info);
            return true;
        }

        // Shared menu items for the hierarchy context menu and the canvas right-click menu.
        private void AddStyleClipboardMenuItems(GenericMenu menu, LayoutNode node, bool isRoot)
        {
            if (!isRoot)
            {
                menu.AddItem(new GUIContent("Copy Style"), false, () => CopyStyleOf(node));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Copy Style"));
            }

            int count = (HasMultiSelection && _selectedNodes.Contains(node)) ? _selectedNodes.Count : 1;
            string pasteLabel = StyleClipboard.HasStyle
                ? $"Paste Style ({StyleClipboard.SourceKindLabel}){(count > 1 ? $" ({count})" : "")}"
                : "Paste Style";
            if (StyleClipboard.HasStyle && !isRoot)
            {
                menu.AddItem(new GUIContent(pasteLabel), false, () => PasteStyleToSelection(node));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(pasteLabel));
            }
        }

        private void CopyStyleOf(LayoutNode node)
        {
            if (node == null) return;
            StyleClipboard.Copy(node);
            ShowToolToast($"Copied {StyleClipboard.SourceKindLabel} style.", ToastKind.Success);
        }

        // Pastes onto the clicked node — or the WHOLE multi-selection when the clicked node is part
        // of it — under a single undo record.
        private void PasteStyleToSelection(LayoutNode node)
        {
            if (node == null || _clonedLayoutAsset == null || !StyleClipboard.HasStyle) return;

            Undo.RecordObject(_clonedLayoutAsset, "Paste Style");
            string kind = StyleClipboard.SourceKindLabel;
            int applied = 0;
            int retyped = 0;
            string lastKindChange = null;
            if (HasMultiSelection && _selectedNodes.Contains(node))
            {
                foreach (var n in _selectedNodes)
                {
                    if (n == null || n == _clonedLayoutAsset.Root) continue;
                    if (!ApplyClipboardTo(n, out string change)) continue;
                    applied++;
                    if (string.IsNullOrEmpty(change)) continue;
                    retyped++;
                    lastKindChange = change;
                }
            }
            else if (ApplyClipboardTo(node, out lastKindChange))
            {
                applied = 1;
                if (!string.IsNullOrEmpty(lastKindChange)) retyped = 1;
            }

            OnModification();
            RefreshPreview();
            RefreshProperties(); // style rows of the open panel must reflect the pasted values

            // Paste re-types silently for exactly the same reason the painter did, so it narrates the same way.
            // Across a multi-selection only the COUNT is honest — the targets can be different field types, so
            // they need not have landed on the same widget kind.
            if (applied == 1)
                ShowToolToast(PaintedStyleMessage(kind, lastKindChange), ToastKind.Success);
            else if (applied > 1)
                ShowToolToast(retyped > 0
                    ? $"Pasted style onto {applied} nodes ({retyped} re-typed)."
                    : $"Pasted style onto {applied} nodes.", ToastKind.Success);
        }

        // The canvas card's right-click menu: wrap, the style clipboard, Duplicate, and the jump back to this
        // card's row in the hierarchy. Deliberately the mirror of the hierarchy row's menu — the same actions
        // named for where they land, so neither panel is the "real" one you have to switch to.
        private void ShowCanvasNodeContextMenu(LayoutNode node)
        {
            if (node == null || _clonedLayoutAsset == null) return;
            if (!IsNodeSelected(node)) SelectSingle(node);

            var menu = new GenericMenu();
            bool isRoot = node == _clonedLayoutAsset.Root;
            // Wrapping — and its two inverses — is a CANVAS gesture as much as a hierarchy one: the canvas is
            // where you can see the run of cards you are grouping, or the container you are dissolving, so the
            // entries lead here exactly as they do on a row. Skipped for the root, which has nothing to be
            // wrapped into and nothing to be unwrapped from (the hierarchy hides them there too).
            if (!isRoot)
            {
                AddWrapSelectedMenuItems(menu);
                AddUnwrapMenuItems(menu);
                menu.AddSeparator("");
            }
            AddStyleClipboardMenuItems(menu, node, isRoot);
            menu.AddSeparator("");
            AddDuplicateMenuItem(menu, node);
            menu.AddSeparator("");
            // The twin of the hierarchy row's "Ping on Canvas" — and of double-clicking this card.
            menu.AddItem(new GUIContent("Ping in Hierarchy"), false, () => SelectAndPingNode(node));
            // Drill into the script this node embeds an in-line editor for. Appends nothing for other nodes.
            // The canvas is where you're LOOKING at the embedded editor, so this menu matters more than the
            // hierarchy's — leaving it out of here was the gap that made the feature read as not working.
            AppendInlineEditorNavItems(menu, node);
            menu.ShowAsContext();
        }
    }
}
