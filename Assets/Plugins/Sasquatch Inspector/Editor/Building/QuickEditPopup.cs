using System;
using System.Collections.Generic;
using System.Linq;
using SasquatchSpring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The compact quick-edit popup opened from the inspector's right-click menu (QuickEditMenu).
    /// One small panel per node type — the handful of most-used properties, NOT the designer's full
    /// Properties panel — plus an "Open in Designer" footer for everything deeper. Shown as a
    /// borderless dropdown window (auto-closes on focus loss, like Unity's own dropdowns).
    ///
    /// Every edit runs the same pipeline: record undo on the persisted object → mutate the node
    /// (which ALIASES the database entry, so the mutation is the persisted data) → mark dirty →
    /// targeted rebuild of just this inspector → re-anchor the glow. The rebuilt panel never
    /// contains the control being edited (it lives in this window), so the designer's "callback
    /// rebuilds its own control" bug class can't occur here. Text fields defer the rebuild to
    /// blur/Enter so the inspector doesn't rebuild per keystroke.
    /// </summary>
    public class QuickEditPopup : EditorWindow
    {
        private const float Width = 280f;
        // The menu CONTROLS stay this wide (Width minus the card's h-padding + border) even when the shared window
        // widens for a code peek: the controls sit in a left-aligned column capped at this width, so the code drives
        // the panel width while the fields never stretch. The card itself still fills the window (no empty corner).
        private const float MenuContentWidth = Width - 18f;
        // The popup has no fixed per-node height: the curated Display-Style/Options make the BindingNode
        // panel's height vary a lot by DisplayType, so the window is MEASURED and sized to fit its content
        // (springing on rebuilds). Past MaxPopupHeight the body ScrollView scrolls instead of the window growing.
        private const float MaxPopupHeight = 520f;
        private const float MinPopupHeight = 120f;

        private QuickEditContext _ctx;
        private LayoutOwnerScope _scope; // the layout/target this node actually belongs to (nested inline editor → its own asset)
        private LayoutNode _node;
        private List<int> _indexPath;
        // The DEEPEST path the popup was opened at — the full breadcrumb trail. Breadcrumb clicks move _node/
        // _indexPath along this trail but never shorten it, so navigating field → root leaves the field crumb
        // still there to jump BACK to (bidirectional). Set once in Open; never mutated by navigation.
        private List<int> _fullPath;
        private bool _rebuildPending;
        private bool _hasTabLabelRow; // tab pages get an extra "Tab Label" row (set during BuildContent)
        private Rect _anchorRect;     // screen rect of the element the dropdown is anchored to
        private Rect _dropdownRect;   // the popup's REAL screen rect (see ReadActualScreenRect)

        // The originally-clicked node + its live element, so the popup stays PINNED to the bottom of that element's
        // selection outline and follows it when an edit changes its height (e.g. Field Placement growing the row).
        // The node is fixed (breadcrumb navigation doesn't move the popup); the element is re-resolved after each
        // inspector rebuild (which recreates it).
        private LayoutNode _anchorNode;
        private VisualElement _anchorElement;

        private VisualElement _crumbRow;   // the breadcrumb header, which stands in for a title
        private ScrollView _bodyScroll;    // the body, cached so we can measure its natural content height
        private VisualElement _footerRow;  // the footer container, measured for the fit-to-content height
        private VisualElement _card;       // the main quick-edit card; the window root is a transparent column so the peek can sit BELOW it
        private VisualElement _peekCard;   // the code-peek FALLBACK card, inside this window (null unless the fallback path is showing)
        private float _peekExtraHeight;    // extra window height the fallback peek card contributes (gap + card height); 0 when hidden

        // The PREFERRED code-peek placement: the card lives in the inspector's own panel instead of this window, so
        // the menu keeps its compact footprint and everything around the card stays see-through (see the Code peek
        // section below). Null whenever no overlay peek is showing.
        private VisualElement _peekOverlay;
        private EditorWindow _peekHost;      // cached window owning _ctx.StableRoot's panel (the inspector)
        private float _peekOverlayWidth;     // grow-only desired width within ONE peek session; reset on hide
        private Rect _peekOverlayRect;       // the applied panel-space rect, so the card can be re-placed when the menu moves
        private float _windowWidth = Width; // current native window width; widens past Width to fit a code-peek snippet, back to Width when hidden
        private float _animatedHeight;     // authoritative popup CONTENT height (excludes the peek); driven by the resize spring
        private bool _firstMeasure = true; // first open snaps (don't fight the pop-in); later changes spring
        private bool _breadcrumbExpanded;  // a deep chain's "…" was clicked → show every crumb (this node only)

        // The asset / root / target / SerializedObject this node belongs to. For a node inside an inline
        // editor that's the NESTED layout and nested object (carried via the chain entry's scope), so undo,
        // persistence, field-type resolution and the "Open in Designer" deep-link all route to the owner
        // instead of the outer ctx's root. Fall back to ctx for root-layout nodes (and the test seam).
        private InspectorLayoutAsset OwningLayout => _scope?.LayoutAsset ?? _ctx.LayoutAsset;
        private LayoutNode OwningRoot => OwningLayout?.Root;
        private UnityEngine.Object OwningTarget => _scope?.Target ?? _ctx.Target;
        private SerializedObject OwningSerializedObject => _scope?.SerializedObject ?? _ctx.SerializedObject;

        /// <summary>Opens the compact quick-edit popup for <paramref name="node"/>, anchored to <paramref name="anchorElement"/>.</summary>
        public static void Open(QuickEditContext ctx, LayoutOwnerScope scope, LayoutNode node, List<int> indexPath, VisualElement anchorElement)
        {
            if (ctx == null || node == null) return;

            QuickEditGlow.Show(anchorElement);

            var window = CreateInstance<QuickEditPopup>();
            window.Initialize(ctx, scope, node, indexPath, anchorElement);

            // ShowAsDropDown wants the anchor in SCREEN coordinates. We're in a menu-action callback
            // (no live GUI context), so convert the element's panel-space worldBound ourselves by
            // locating the EditorWindow that hosts the element's panel.
            window._anchorRect = window.ScreenRectOf(anchorElement);
            window.ShowAsDropDown(window._anchorRect, new Vector2(Width, window.HeightForNode()));
            window._dropdownRect = window.ReadActualScreenRect(); // the settled rect, for in-place resizes

            // Keep the popup pinned to the bottom of the anchor element's outline: when an edit changes that
            // element's height (Field Placement, etc.) the row grows and would otherwise be hidden behind the
            // popup, so we follow its bottom edge. Cheap: it only re-pins when the edge actually moves, and
            // deciding that costs a worldBound read plus a cached window origin — no per-tick registry scan
            // (see ResolveRectHost, which is what made this claim true rather than aspirational).
            window.rootVisualElement.schedule.Execute(window.FollowAnchor).Every(16);

            // Pop-in flourish: the panel scales up from its anchored top edge with a slight
            // overshoot. Only the root element animates — never the window rect.
            if (EditorSpringFX.MotionEnabled)
            {
                var root = window.rootVisualElement;
                root.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(0f));
                root.style.scale = new Scale(new Vector3(0.92f, 0.92f, 1f));
                root.style.opacity = 0f;
                root.SpringScale(1f, EditorSpringFX.Tune(new SpringConfig(0.3f, 0.5f)));
                root.SpringOpacityIn(EditorSpringFX.Tune(SpringConfig.Snappy)); // release the pin on settle

            }
        }

        // Everything Open does that doesn't need the native dropdown window: adopt the node and build the panel.
        // Split out so a test can drive a real popup (content, keyboard wiring and all) in an ordinary window —
        // a ShowAsDropDown one would close the instant the test runner took focus back.
        private void Initialize(QuickEditContext ctx, LayoutOwnerScope scope, LayoutNode node,
            List<int> indexPath, VisualElement anchorElement)
        {
            _ctx = ctx;
            _scope = scope;
            _node = node;
            _indexPath = indexPath;
            _fullPath = indexPath != null ? new List<int>(indexPath) : null; // the persistent breadcrumb trail
            _anchorNode = node;          // stays pinned to the clicked element even as you breadcrumb around
            _anchorElement = anchorElement;
            BuildContent();
        }

        internal void InitializeForTests(QuickEditContext ctx, LayoutNode node, List<int> indexPath)
        {
            Initialize(ctx, null, node, indexPath, null);
        }

        // The window hosting `panel`, cached. Same lookup and the same reason as ResolvePeekHost below: the scan
        // walks every EditorWindow and allocates a managed array, and FollowAnchor asks for it 60x a second for
        // the popup's whole lifetime — so uncached it was 34ms per idle second, to discover nothing had moved.
        //
        // Memoising is output-identical rather than merely close: at most ONE window can be attached to a given
        // panel (a background dock tab's root is detached, so its `panel` is null), so re-validating the cached
        // window against the panel identifies the same window the scan's first match would have. Even in the
        // impossible case of two, tabbed windows share a dock rect and so share `position` — the same origin.
        // A closed window compares null, so the cache self-invalidates exactly like _peekHost's.
        private EditorWindow _rectHost;

        private EditorWindow ResolveRectHost(IPanel panel)
        {
            if (panel == null) return null;
            if (_rectHost != null && _rectHost.rootVisualElement?.panel == panel) return _rectHost;
            _rectHost = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.rootVisualElement?.panel == panel) { _rectHost = w; break; }
            }
            return _rectHost;
        }

        private Rect ScreenRectOf(VisualElement element)
        {
            if (element == null || element.panel == null)
            {
                var mouse = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
                return new Rect(mouse.x, mouse.y, 1, 1);
            }

            var wb = element.worldBound;
            var host = ResolveRectHost(element.panel);
            if (host != null)
            {
                var origin = host.position.position;
                return new Rect(origin.x + wb.x, origin.y + wb.y, wb.width, wb.height);
            }
            return wb; // best effort — dropdown still opens, just relative to the main display
        }

        // ---- Dismissal (Escape) ----
        // Escape closes the menu, because that is the reflex a dropdown trains: Unity hands a ShowAsDropDown window
        // no Escape handling of its own (it only auto-closes on focus loss), so we wire it here. Our own pickers all
        // close on Escape (SearchableDropdownPopup, the LayerMask and method popups), and this menu has no "cancel"
        // semantics to protect — every row commits as you touch it, so dismissing is never a discard.
        //
        // Two halves, both load-bearing (same shape as SearchableDropdownPopup):
        //   * TRICKLEDOWN, at the window ROOT — the handler sees the key before whatever field it landed in, so no
        //     control can swallow it on the way past.
        //   * root.focusable + a claimed focus — UI Toolkit dispatches KeyDownEvent to the FOCUSED element, and when
        //     a panel holds no focus at all it dispatches nothing: a freshly-opened menu nobody has clicked in would
        //     never see the key. focusable on the ROOT also means a click on a Label or the bare card background
        //     re-homes focus HERE (the focus controller walks up for a focusable parent) rather than clearing it, so
        //     Escape keeps working as you click around inside the menu.
        private const long FocusClaimDelayMs = 50;
        private bool _dismissKeysWired;

        // Called from the tail of BuildContent — one site covers both the first open and every in-place rebuild.
        // Re-arming per build is the point: a rebuild (breadcrumb hop, Display Type switch, an Expandable toggle)
        // destroys whoever held focus, and without a fresh claim Escape would quietly go dead from then on.
        private void ArmDismissKeys()
        {
            var root = rootVisualElement;
            if (root == null) return;

            if (!_dismissKeysWired)
            {
                _dismissKeysWired = true; // the root survives BuildContent's Clear(), so register exactly once
                root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            }
            root.focusable = true;

            // Deferred: on the first build the window isn't shown yet (Open builds first so it can measure), and
            // focus taken in the same frame as a fresh tree gets dropped. A schedule on a not-yet-attached element
            // runs once its panel exists, which covers both cases.
            root.schedule.Execute(() =>
            {
                // Never steal focus from a control the user has since clicked into.
                var focused = root.panel?.focusController?.focusedElement as VisualElement;
                if (focused != null && (focused == root || root.Contains(focused))) return;
                root.Focus();
            }).ExecuteLater(FocusClaimDelayMs);
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            // An open autocomplete list owns the FIRST Escape: dismiss the suggestions, keep the menu (the next
            // Escape then closes the menu). The list's own Escape handler is TrickleDown as well, but registered on
            // the FIELD — deeper than us — so without this check the menu would close out from under the list.
            if (HasOpenSuggestionList()) return;
            evt.StopPropagation();
            Close(); // OnDestroy flushes a pending text edit, strips the peek overlay and hides the glow
        }

        // Both autocomplete flavours park their list on the PANEL root (so it can overhang the field it belongs to)
        // and name it — that name is the only handle on "a list is open right now".
        private bool HasOpenSuggestionList()
        {
            var tree = rootVisualElement?.panel?.visualTree;
            if (tree == null) return false;
            return tree.Q(AutocompleteTextField.SuggestionPopupName) != null
                || tree.Q(AutocompleteTextField.TokenPopupName) != null;
        }

        private void OnDestroy()
        {
            // A deferred text-edit rebuild may still be pending when the popup closes on focus loss.
            if (_rebuildPending) _ctx?.RebuildInspector?.Invoke();
            // The overlay peek lives in the INSPECTOR's panel, not ours — closing this window does not take it with
            // us, so it would be stranded on the inspector forever. Must run before the glow hides.
            HidePeekOverlay();
            QuickEditGlow.Hide();
        }

        private float HeightForNode()
        {
            // Every kind but the Script Header (and the root) also carries the shared "Visible in Inspector"
            // row from BuildVisibilityRow. Added once here instead of to each arm below, so the two stay in
            // lockstep by construction. Provisional either way — MeasureAndResize springs to real content —
            // but a too-short open flashes before it settles.
            float visibilityRow = (_node == null || _node is ScriptHeaderNode
                                   || (OwningLayout != null && ReferenceEquals(_node, OwningLayout.Root))
                                   || SasquatchLayoutDesignerWindow.IsDictionaryCellTemplate(_node, OwningRoot))
                ? 0f : 22f;

            return visibilityRow + HeightForNodeKind();
        }

        private float HeightForNodeKind()
        {
            switch (_node)
            {
                case BindingNode _: return 178f;
                // Mode-dependent since BuildCollectionPanel started SKIPPING the row-ergonomics set on
                // Default / Tag Chips / Custom Widget: a flat 194f opened those ~8 rows too tall, then snapped
                // down. Provisional either way — MeasureAndResize springs to the real content height.
                case CollectionNode col:
                    return col.DisplayMode == CollectionDisplayMode.List
                        || col.DisplayMode == CollectionDisplayMode.Table
                            ? 194f                    // the full set, + the Title row
                            : col.DisplayMode == CollectionDisplayMode.Default
                                ? MinPopupHeight      // Display Type alone — Default has no Title row either
                                : MinPopupHeight + 22f; // Tag Chips / Custom Widget keep the Title row
                case ButtonNode _: return 194f; // extra room so the method autocomplete list isn't clipped
                case ContainerNode _: return 196f + (_hasTabLabelRow ? 22f : 0f);
                case ScriptHeaderNode _: return 200f; // Display Text, Hide Icon, Show Search, Show Live Debug, Custom Icon
                // Room for the wrapping/growing message field + Render Markdown row; the body ScrollView
                // handles anything taller. Expandable boxes get an extra Summary Text row.
                case HelpBoxNode hb: return 248f + (hb.Expandable ? 22f : 0f);
                // Decorative / reflected leaves (provisional — MeasureAndResize springs to the real content height).
                case PropertyNode _: return 168f;
                case TextBlockNode _: return 208f;
                case ImageBannerNode _: return 236f;
                case LabeledDividerNode _: return 172f;
                case SpaceNode _: return 132f;
                case EmbeddedEditorNode _: return 150f;
                default: return 120f;
            }
        }

        // ---- Edit pipeline ----

        private void Commit(Action mutate, bool rebuildNow = true)
        {
            LayoutRegistry.BeginLayoutEditUndo(OwningLayout, "Quick Edit Layout");
            mutate();
            // A held-open cell only means something in a tracked container, and this popup can untrack one
            // (clearing a Columns/Rows track count, switching a Grid to Flexible). Normalizing before the
            // persist keeps the invariant true in the SAVED asset — the designer's own RefreshAll never
            // runs for a live edit. See EmptyCellFill.
            if (OwningLayout?.Root != null) EmptyCellFill.Normalize(OwningLayout.Root);
            LayoutRegistry.PersistLayoutEdit(OwningLayout);
            // Hand the edit to any designer window already holding this type. It works on a CLONE taken
            // before this commit, and its Save writes that clone's whole Root back — so without this, the
            // edit is destroyed by a Save the user has every reason to trust, including the one behind this
            // popup's own "Open in Designer" footer. _indexPath identifies the node for the case where the
            // designer has unsaved work of its own and only that node may be grafted.
            SasquatchLayoutDesignerWindow.AdoptLiveLayoutEdit(OwningTarget != null ? OwningTarget.GetType() : null,
                                                              _indexPath);
            if (rebuildNow) RebuildAndReglow();
            else _rebuildPending = true;
        }

        private void RebuildAndReglow()
        {
            _rebuildPending = false;
            _ctx.RebuildInspector?.Invoke();
            QuickEditGlow.Retarget(_ctx.StableRoot, _node);
        }

        // The popup's screen rect, derived from the anchor (the row the popup opened from): placed just
        // below the anchor at its fixed width/height — the standard below-the-anchor dropdown position.
        // We compute it from the anchor rather than reading the window's own rect back; the two match
        // for a normal below-anchor dropdown.
        private Rect ReadActualScreenRect()
        {
            return new Rect(_anchorRect.x, _anchorRect.yMax, Width, HeightForNode());
        }

        // Rebuilds this popup's own content in place (for edits that change which rows exist — Expandable, a
        // DisplayType switch, a breadcrumb hop) and resizes the window to fit. MUST be called deferred
        // (schedule), never straight from a control's change callback — the rebuild destroys the control
        // mid-callback. BuildContent ends with MeasureAndResize(), which springs the window to the new content.
        private void RebuildPopupContent()
        {
            rootVisualElement.Clear(); // also detaches any live FALLBACK peek card
            _peekCard = null;
            HidePeekOverlay();     // ...but the overlay peek is parented to the inspector, so Clear() misses it
            _peekExtraHeight = 0f; // the fresh content re-measures without the peek; it re-adds on the next hover
            _windowWidth = Width;  // and the window collapses back to the compact menu width
            _hasTabLabelRow = false;
            BuildContent();
        }

        // ---- Fit-to-content sizing (measured + spring) ----
        // The popup's height is not a per-node constant (HeightForNode only supplies the provisional height the
        // window first opens at): the curated Display-Style/Options vary the panel height a lot by
        // DisplayType, so we measure the built content and size the window to fit, up to MaxPopupHeight (past
        // which the body ScrollView scrolls). First open snaps; later rebuilds glide via a SasquatchSpring.
        private void MeasureAndResize()
        {
            var body = _bodyScroll;
            if (body == null) return;

            // Measure after layout — resolvedStyle isn't valid until UI Toolkit lays the tree out. The body's
            // contentContainer sizes to its CONTENT (independent of the scroll viewport), so it gives the natural
            // height even when the content is taller than the window. One-shot: unregister on first fire.
            void OnGeom(GeometryChangedEvent _)
            {
                body.contentContainer.UnregisterCallback<GeometryChangedEvent>(OnGeom);
                float chrome = (_crumbRow?.resolvedStyle.height ?? 0f)
                             + (_footerRow?.resolvedStyle.height ?? 0f)
                             + (_card?.resolvedStyle.paddingTop ?? 0f)   // padding now lives on the card, not the window root
                             + (_card?.resolvedStyle.paddingBottom ?? 0f)
                             // Slack so the last row is never clipped. A Slider (showInputField) does a second layout
                             // pass — its input box re-measures a pixel or two taller AFTER this one-shot measurement —
                             // so 4px left a sliver of overflow and a spurious few-px scrollbar; 10px absorbs it (a
                             // couple of sliders' worth) without a visible bottom gap. Only affects sub-MaxPopupHeight
                             // panels — taller ones scroll by design.
                             + 10f;
                float content = body.contentContainer.resolvedStyle.height;
                float target = Mathf.Clamp(content + chrome, MinPopupHeight, MaxPopupHeight);

                if (_firstMeasure || !EditorSpringFX.MotionEnabled)
                {
                    _firstMeasure = false;
                    ApplyWindowHeight(target); // snap on first open (don't fight the pop-in) / when motion is off
                }
                else
                {
                    SpringWindowHeight(target);
                }
            }
            body.contentContainer.RegisterCallback<GeometryChangedEvent>(OnGeom);
        }

        private void SpringWindowHeight(float target)
        {
            if (!EditorSpringFX.MotionEnabled) { ApplyWindowHeight(target); return; }
            if (_animatedHeight <= 0f) _animatedHeight = _dropdownRect.height - _peekExtraHeight; // seed the glide from the current CONTENT size
            // settleEpsilon 0.5: a pixel-space spring rings sub-pixel for ~1s without it (see GapAnimator). Owner
            // is the root VE (ticks in the editor, auto-cancels when the window closes); same owner+key retargets.
            Spring.To((object)rootVisualElement, () => _animatedHeight, ApplyWindowHeight, target,
                EditorSpringFX.Tune(new SpringConfig(0.3f, 0.25f, 0.5f)), "quickedit.popup.height");
        }

        // Drives the native dropdown rect to height h. Two traps: (1) derive
        // the base rect from _dropdownRect/_anchorRect, NEVER the `position` getter (it lies for a ShowAsDropDown
        // window). (2) Order matters — position FIRST, then pin maxSize/minSize; if min/max change while the rect
        // still has the old size, setting them repositions the window to resolve the mismatch.
        private void ApplyWindowHeight(float h)
        {
            _animatedHeight = h; // h is the MAIN CARD (content) height; the peek card adds below it
            var p = _dropdownRect;
            p.x = _anchorRect.x;
            p.y = _anchorRect.yMax; // grows/shrinks from the anchored top edge
            p.width = _windowWidth; // the main card stays pinned at Width (left-aligned); the peek can widen the window
            p.height = h + _peekExtraHeight; // the connected code-peek card extends the window below the main card
            position = p;
            var size = new Vector2(_windowWidth, p.height);
            maxSize = size;
            minSize = size;
            _dropdownRect = p;
            RepositionPeekOverlay(); // keep an inspector-hosted peek glued under the menu as it springs / follows the anchor
            Repaint();
        }

        // ---- Code peek (a card hanging under the menu) ----
        // The method / C# property pickers carry a hover "code peek". In the roomy Designer, AutocompleteTextField
        // draws it as an in-panel pane; here the menu is a tiny ShowAsDropDown window, so an in-panel pane would be
        // clipped to it. The pickers route their peek through ShowPeekCard/HidePeekCard, which picks between two
        // placements — preferring the first, falling back to the second:
        //
        //   1. OVERLAY. The card is a VisualElement parented to the INSPECTOR window's root, absolutely positioned
        //      just below the menu. The menu window keeps its compact Width footprint and never grows for the code,
        //      because the code isn't in it — so there's no wide opaque panel with a bare corner sitting over the
        //      inspector. A UITK panel draws nothing where nothing is added, so everything around the card is the
        //      live inspector. Note this deliberately creates NO second native window: every PUBLIC
        //      EditorWindow.Show* mode activates the window it shows, and that focus steal closes this
        //      ShowAsDropDown menu mid-hover. (ShowMode.Tooltip is the one non-activating mode, and it is internal,
        //      so it isn't reachable from public API.) An overlay sidesteps the whole question: no window, no focus.
        //
        //   2. CONNECTED CARD (fallback). The original: a second card inside THIS window below the menu, with the
        //      shared window widening to fit the snippet. Used when there's no inspector panel to host the overlay
        //      (popup opened standalone / under test) or when the card can't fit inside that panel's bounds. It can
        //      spill outside the inspector, which is exactly why it stays as the safety net.

        // Sizing is shared with the list popups' peek (CodePeekMetrics) so the two cards can never drift apart.
        private const float PeekGap = 5f;         // fallback path only: the sliver between the two in-window cards
        private const float PeekJoinOverlap = CodePeekMetrics.JoinOverlap; // overlay path: the card laps the menu's border so the two TOUCH
        private const float PeekMaxWidth = CodePeekMetrics.MaxWidth;       // widest the peek may grow; longer lines clip (overflow hidden)
        private const float PeekEdgeMargin = CodePeekMetrics.EdgeMargin;   // gap kept to the screen / host-panel edge (see PeekRightEdge)
        private const float PeekMinOverlayWidth = 160f; // narrower than this the overlay can't show readable code — fall back

        internal const string PeekCodeName = "sasq-quickedit-peek-code";

        // Test seams (mirrors ShowPeekCard/HidePeekCard) for the peek placement + width regressions.
        internal float PeekWindowWidth => _windowWidth;
        internal static float CompactWidth => Width;
        internal VisualElement PeekOverlayElement => _peekOverlay;
        internal Rect PeekOverlayRect => _peekOverlayRect;

        // Test seam standing in for Open()'s wiring — the context (which is how the peek finds its host panel) plus
        // the settled window rect the placement math reads. Lets the overlay path be exercised against a real host
        // window without a live inspector takeover.
        internal void PrimePeekForTests(QuickEditContext ctx, Rect menuScreenRect)
        {
            _ctx = ctx;
            _anchorRect = new Rect(menuScreenRect.x, menuScreenRect.y - 1f, menuScreenRect.width, 1f);
            _dropdownRect = menuScreenRect;
            _animatedHeight = menuScreenRect.height;
        }

        // Test seams for the anchor-follow tick. FollowAnchor is the popup's IDLE FLOOR — it runs 60x/second for
        // the window's whole lifetime whether or not anything moves — so the perf harness has to drive the real
        // method rather than a copy of it, and needs the anchor element Open() would have supplied.
        internal void PrimeAnchorForTests(LayoutNode anchorNode, VisualElement anchorElement)
        {
            _anchorNode = anchorNode;
            _anchorElement = anchorElement;
        }

        internal void FollowAnchorTick() => FollowAnchor();
        internal VisualElement AnchorElementForTests => _anchorElement;

        // The peek's desired width from its content alone (screen room applied separately), clamped to
        // [Width, PeekMaxWidth]. Static + screen-independent so the width math is unit-testable without a live
        // window; kept as a seam here because the regression tests read it.
        internal static float PeekContentWidth(string rich)
            => CodePeekMetrics.ContentWidth(rich, Width, PeekMaxWidth);

        // Right screen edge the peek may grow to without forcing an OS re-fit: the wider of the main editor
        // window and the anchor's own host window (covers a floating / second-monitor inspector). A conservative
        // bound — never past the monitor the window sits on — so widening the window never shifts it.
        private float PeekRightEdge()
        {
            float edge = EditorGUIUtility.GetMainWindowPosition().xMax;
            var el = _anchorElement;
            if (el != null && el.panel != null)
            {
                foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                {
                    if (w != null && w.rootVisualElement?.panel == el.panel)
                    {
                        edge = Mathf.Max(edge, w.position.xMax);
                        break;
                    }
                }
            }
            return edge;
        }

        // Shows (or updates in place) the peek card with highlighted rich text. Sized from the exact line count —
        // highlighted snippets use explicit '\n' breaks with NoWrap — so the window can grow to fit in the SAME
        // layout pass, and the main card never momentarily squishes. internal for the connected-card test seam.
        internal void ShowPeekCard(string rich)
        {
            if (string.IsNullOrEmpty(rich)) { HidePeekCard(); return; }
            if (_animatedHeight <= 0f) _animatedHeight = _dropdownRect.height - _peekExtraHeight; // not measured yet — seed from current

            float cardHeight = CodePeekMetrics.CardHeight(rich);

            if (TryShowPeekOverlay(rich, cardHeight))
            {
                RetractConnectedPeekCard(); // a card from a previous fallback peek may still be extending the window
                return;
            }

            HidePeekOverlay();
            ShowConnectedPeekCard(rich, cardHeight);
        }

        internal void HidePeekCard()
        {
            HidePeekOverlay();
            RetractConnectedPeekCard();
        }

        // The bordered code card itself (chrome + the mono Label), shared by both placements so the peek looks
        // identical whichever one wins.
        private VisualElement BuildPeekCard()
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = DesignerPalette.CodeBlockBg;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            var b = DesignerPalette.PopupBorder;
            card.style.borderLeftColor = b;
            card.style.borderRightColor = b;
            card.style.borderTopColor = b;
            card.style.borderBottomColor = b;
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.overflow = Overflow.Hidden; // long lines (NoWrap) clip at the card width

            var code = new Label { enableRichText = true, pickingMode = PickingMode.Ignore, name = PeekCodeName };
            code.style.color = DesignerPalette.CodeText;
            code.style.whiteSpace = WhiteSpace.NoWrap;
            code.style.fontSize = 11;
            CodeHighlighter.ApplyCodeFont(code);
            card.Add(code);
            return card;
        }

        // ---- Placement 1: overlay in the inspector's panel ----

        // The window that owns _ctx.StableRoot's panel — i.e. the inspector this popup was opened from. The inverse
        // of ScreenRectOf's lookup. Cached: the scan walks every EditorWindow, and RepositionPeekOverlay can run per
        // spring frame. A destroyed window compares null, so the cache self-invalidates.
        private EditorWindow ResolvePeekHost()
        {
            if (_peekHost != null && _peekHost.rootVisualElement?.panel != null) return _peekHost;
            _peekHost = null;
            var panel = _ctx?.StableRoot?.panel;
            if (panel == null) return null;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w != this && w.rootVisualElement?.panel == panel) { _peekHost = w; break; }
            }
            return _peekHost;
        }

        // Tries to hang the peek off the inspector's panel. False = no host, or the card can't fit inside it — the
        // caller then falls back to the connected card (which may spill outside the inspector).
        private bool TryShowPeekOverlay(string rich, float cardHeight)
        {
            var host = ResolvePeekHost();
            var hostRoot = host?.rootVisualElement;
            if (hostRoot == null || hostRoot.panel == null) return false;

            var hostRect = host.position; // screen space — same units as _dropdownRect
            if (hostRect.width <= 0f || hostRect.height <= 0f) return false;

            // Width is content-driven and grow-only within ONE peek session (Max), so arrowing between a long and a
            // short method never shrinks the card out from under the cursor mid-navigation; HidePeekOverlay resets
            // it. Capped by what the inspector panel can actually show — unlike the connected card, the overlay is
            // clipped by its host window, so asking for more would just clip.
            float maxWidth = Mathf.Min(PeekMaxWidth, hostRect.width - 2f * PeekEdgeMargin);
            if (maxWidth < PeekMinOverlayWidth) return false; // inspector too narrow for readable code — fall back
            _peekOverlayWidth = Mathf.Max(_peekOverlayWidth, Mathf.Min(PeekContentWidth(rich), maxWidth));
            float cardWidth = Mathf.Min(_peekOverlayWidth, maxWidth);

            if (!TryPlacePeekOverlay(hostRect, hostRoot, cardWidth, cardHeight, out var placed, out bool flipped))
                return false;

            if (_peekOverlay == null)
            {
                _peekOverlay = BuildPeekCard();
                // Absolute so it floats over the inspector's content and ignores its scrolling. Position (not Ignore)
                // so a stray click lands on the card instead of punching through to whatever field it covers.
                _peekOverlay.style.position = Position.Absolute;
                _peekOverlay.pickingMode = PickingMode.Position;
            }
            if (_peekOverlay.parent != hostRoot)
            {
                _peekOverlay.RemoveFromHierarchy();
                hostRoot.Add(_peekOverlay);
            }
            // Draw over anything the inspector added after us. Safe here despite BringToFront reordering layout
            // children: an absolutely-positioned element is out of flow, so its sibling index has no layout effect.
            _peekOverlay.BringToFront();

            var code = _peekOverlay.Q<Label>(PeekCodeName);
            if (code != null) code.text = rich;
            ApplyPeekOverlayRect(placed, flipped);

            // The hover driving the peek happens in THIS window's panel, so the inspector — unfocused and otherwise
            // idle — has no reason to redraw on its own.
            host.Repaint();
            return true;
        }

        // The overlay's rect, in coordinates relative to the HOST ROOT's content box — which is what style.left/top
        // on an absolutely-positioned child resolve against, and which is NOT the same as the window's screen origin:
        // a docked window's tab strip sits inside the panel, so hostRoot itself starts at roughly y=24 (measured:
        // position.y 87 / hostRoot.worldBound.y 24; a child with top=100 lands at worldBound.y 124). Screen-space
        // math must subtract that offset or the card hangs a tab-height too low. It's read live rather than assumed
        // because it differs between docked and floating windows.
        //
        // The card hangs off the menu's bottom edge, left-aligned with it, nudged left when it would overflow the
        // host's right edge — so a wide snippet uses the inspector's full width instead of clipping. Flips above the
        // menu when there's no room below; fails when neither fits, handing the peek to the connected-card fallback.
        private bool TryPlacePeekOverlay(Rect hostRect, VisualElement hostRoot, float cardWidth, float cardHeight,
            out Rect placed, out bool flippedAbove)
        {
            placed = default;
            flippedAbove = false;
            if (cardWidth <= 0f || cardHeight <= 0f || hostRoot == null) return false;

            var rootBox = hostRoot.worldBound; // NaN until the host has laid out at least once
            float offX = float.IsNaN(rootBox.x) ? 0f : rootBox.x;
            float offY = float.IsNaN(rootBox.y) ? 0f : rootBox.y;
            float boundsW = float.IsNaN(rootBox.width) || rootBox.width <= 0f ? hostRect.width : rootBox.width;
            float boundsH = float.IsNaN(rootBox.height) || rootBox.height <= 0f ? hostRect.height : rootBox.height;

            // The MENU's own bottom edge: _dropdownRect.height would include a live connected card's extra height.
            float menuBottom = _dropdownRect.y + _animatedHeight;

            float maxLeft = Mathf.Max(PeekEdgeMargin, boundsW - PeekEdgeMargin - cardWidth);
            float left = Mathf.Clamp(_dropdownRect.x - hostRect.x - offX, PeekEdgeMargin, maxLeft);

            // Touching, not floating: the card's top border laps the menu's bottom border by exactly PeekJoinOverlap,
            // so the seam reads as ONE shared hairline rather than two stacked 1px borders.
            float top = menuBottom - PeekJoinOverlap - hostRect.y - offY;
            if (top + cardHeight > boundsH - PeekEdgeMargin)
            {
                top = _dropdownRect.y - cardHeight + PeekJoinOverlap - hostRect.y - offY; // no room below — flip above
                flippedAbove = true;
                if (top < 0f) return false;
            }
            if (top < 0f) return false;

            placed = new Rect(left, top, cardWidth, cardHeight);
            return true;
        }

        // Squares off the two corners that meet the menu so the join is seamless; the far edge keeps its radius.
        private void ApplyPeekOverlayRect(Rect r, bool flippedAbove)
        {
            _peekOverlayRect = r;
            _peekOverlay.style.left = r.x;
            _peekOverlay.style.top = r.y;
            _peekOverlay.style.width = r.width;
            _peekOverlay.style.height = r.height;
            _peekOverlay.style.borderTopLeftRadius = flippedAbove ? 4 : 0;
            _peekOverlay.style.borderTopRightRadius = flippedAbove ? 4 : 0;
            _peekOverlay.style.borderBottomLeftRadius = flippedAbove ? 0 : 4;
            _peekOverlay.style.borderBottomRightRadius = flippedAbove ? 0 : 4;
        }

        // Re-places a live overlay when the menu moves or resizes under it (the height spring, FollowAnchor re-pinning
        // to a grown row). Cheap no-op in the common case — the overlay is null unless a peek is actually showing.
        private void RepositionPeekOverlay()
        {
            if (_peekOverlay == null || _peekOverlay.parent == null) return;
            var host = ResolvePeekHost();
            if (host == null) { HidePeekOverlay(); return; }
            if (TryPlacePeekOverlay(host.position, host.rootVisualElement, _peekOverlayRect.width,
                    _peekOverlayRect.height, out var placed, out bool flipped))
                ApplyPeekOverlayRect(placed, flipped);
        }

        private void HidePeekOverlay()
        {
            if (_peekOverlay != null)
            {
                bool wasLive = _peekOverlay.parent != null;
                _peekOverlay.RemoveFromHierarchy();
                _peekOverlay = null;
                if (wasLive && _peekHost != null) _peekHost.Repaint(); // erase it even though the inspector isn't focused
            }
            _peekOverlayWidth = 0f;
            _peekOverlayRect = default;
        }

        // ---- Placement 2: connected card inside this window (fallback) ----

        private void ShowConnectedPeekCard(string rich, float cardHeight)
        {
            if (_peekCard == null)
            {
                _peekCard = BuildPeekCard();
                _peekCard.style.marginTop = PeekGap;
                rootVisualElement.Add(_peekCard); // sibling BELOW the main card in the window-root column
            }

            var code = _peekCard.Q<Label>(PeekCodeName);
            if (code != null) code.text = rich;

            _peekCard.style.height = cardHeight;
            _peekExtraHeight = PeekGap + cardHeight;

            // Widen the window so the snippet's longest line fits — but ONLY into the room available to the
            // right of the menu's fixed left edge. The menu must NEVER move: if we grew past the screen edge the
            // OS would shift the whole (shared) window left to fit, dragging the menu out from under the cursor
            // and flip-flopping the hover that drives the peek. So a snippet with no room just clips (acceptable)
            // rather than nudging the menu. Grow-only within one peek session (Max), so arrowing between methods of
            // different lengths never SHRINKS the menu out from under the cursor mid-navigation; HidePeekCard
            // resets it to Width when the peek leaves. The menu card fills whatever width we settle on.
            float screenCap = Mathf.Max(Width, Mathf.Min(PeekMaxWidth, PeekRightEdge() - _anchorRect.x - PeekEdgeMargin));
            _windowWidth = Mathf.Max(_windowWidth, Mathf.Clamp(PeekContentWidth(rich), Width, screenCap));

            ApplyWindowHeight(_animatedHeight); // grow the window to reveal the peek card (content height unchanged)
        }

        private void RetractConnectedPeekCard()
        {
            if (_peekCard != null)
            {
                _peekCard.RemoveFromHierarchy();
                _peekCard = null;
            }
            if (_peekExtraHeight != 0f || _windowWidth != Width)
            {
                _peekExtraHeight = 0f;
                _windowWidth = Width; // collapse the width back to the compact menu
                ApplyWindowHeight(_animatedHeight); // shrink the window back to just the main card
            }
        }

        // ---- Anchor follow ----
        // Re-pins the popup's top to the bottom of the anchor element's outline whenever that element moves or
        // resizes (an edit grew the field's row, the inspector scrolled, …). Runs on a light 16ms tick while the
        // popup is open; a no-op unless the edge actually moved. The anchor element is recreated by each inspector
        // rebuild, so re-resolve it (by node) once the cached one detaches.
        private void FollowAnchor()
        {
            if (_animatedHeight <= 0f) return; // wait until the first measure has sized the window

            if (_anchorElement == null || _anchorElement.panel == null)
                _anchorElement = ResolveAnchorElementGated();
            var el = _anchorElement;
            if (el == null || el.panel == null) return;

            var wb = el.worldBound;
            if (float.IsNaN(wb.height) || wb.height <= 0f) return; // not laid out yet — try again next tick

            var rect = ScreenRectOf(el);
            if (Mathf.Abs(rect.x - _anchorRect.x) < 0.5f && Mathf.Abs(rect.yMax - _anchorRect.yMax) < 0.5f) return;
            _anchorRect = rect;
            ApplyWindowHeight(_animatedHeight); // re-pin the top to the anchor's new bottom, keep current height
        }

        // ResolveAnchorElement, but not repeated over a tree that provably still doesn't contain the node.
        //
        // The walk normally runs for ONE tick — an inspector rebuild recreates the anchor element and the next
        // tick finds it. When the node is genuinely gone (deleted, or a row the current page no longer renders)
        // the walk finds nothing and, ungated, repeats a full stableRoot.Query<VisualElement>() DFS on every one
        // of the 60 ticks a second, for as long as the popup stays open.
        //
        // The gate is exact, not a throttle: EVERY path that could put the element back rebuilds the stable root
        // wholesale (VisualInspectorEditor.RebuildInspectorGUI and SasquatchForceTakeover both Clear() it and
        // repopulate — and while this popup is open they are the only rebuild paths, since a ShowAsDropDown
        // window closes the moment anything else takes focus). A Clear()+repopulate necessarily replaces the
        // first child with a different object, so (childCount, first child) is an O(1) fingerprint of "the tree
        // the last failed walk searched". Unchanged fingerprint ⇒ unchanged tree ⇒ the walk would fail again.
        private VisualElement _searchedRootFirstChild;
        private int _searchedRootChildCount = -1;
        private bool _anchorSearchExhausted;

        private VisualElement ResolveAnchorElementGated()
        {
            var stableRoot = _ctx?.StableRoot;
            if (stableRoot == null) return null;

            int count = stableRoot.childCount;
            var firstChild = count > 0 ? stableRoot[0] : null;
            if (_anchorSearchExhausted && count == _searchedRootChildCount
                && ReferenceEquals(firstChild, _searchedRootFirstChild))
                return null;

            var found = ResolveAnchorElement();
            _anchorSearchExhausted = found == null;
            _searchedRootChildCount = count;
            _searchedRootFirstChild = firstChild;
            return found;
        }

        // Finds the live element carrying the anchor node under the inspector's stable root. Mirrors QuickEditGlow's
        // resolution but also consults the LayoutNodeRegistry (Unity's controls overwrite userData on binding leaves).
        private VisualElement ResolveAnchorElement()
        {
            var stableRoot = _ctx?.StableRoot;
            if (stableRoot == null || _anchorNode == null) return null;
            return stableRoot.Query<VisualElement>()
                .Where(e => ReferenceEquals(LayoutNodeRegistry.GetNode(e), _anchorNode) || ReferenceEquals(e.userData, _anchorNode))
                .First();
        }

        // Text edits persist per keystroke but only rebuild the inspector on blur/Enter.
        private void FlushTextEditOn(VisualElement row)
        {
            row.RegisterCallback<FocusOutEvent>(_ => { if (_rebuildPending) RebuildAndReglow(); }, TrickleDown.TrickleDown);
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && _rebuildPending)
                    RebuildAndReglow();
            }, TrickleDown.TrickleDown);
        }

        // A drag-release/typing-pause debounce for the sliders below. Well above a frame interval, so a continuous
        // drag never commits mid-stroke (the whole drag stays a single undo step); a deliberate pause or release
        // lands the commit. Mirrors the designer's HandleDebouncedValueEdit debounce.
        private const long SliderCommitDebounceMs = 120;

        // A slider for a CLAMPED [min..max] numeric value: track + a typed input box (showInputField), so a bounded
        // value reads as a slider instead of a bare number box. It commits like the delayed FloatFields — ONCE per
        // adjustment, not per drag frame — via SliderCommitDebounceMs, so a drag is a single undo step; a typed value
        // (blur) or the popup closing (detach) flushes immediately. `apply` runs the node's own clamping setter.
        private Slider SliderRow(VisualElement root, string label, float value, float min, float max,
            string tooltip, Action<float> apply)
        {
            var s = new Slider(label, min, max) { value = Mathf.Clamp(value, min, max), showInputField = true };
            if (!string.IsNullOrEmpty(tooltip)) s.tooltip = tooltip;

            float committed = Mathf.Clamp(value, min, max);
            IVisualElementScheduledItem debounce = null;
            void CommitNow()
            {
                if (Mathf.Approximately(s.value, committed)) return; // nothing new since the last commit
                committed = s.value;
                Commit(() => apply(s.value));
            }

            s.RegisterValueChangedCallback(_ =>
            {
                debounce?.Pause();
                debounce = s.schedule.Execute(CommitNow);
                debounce.ExecuteLater(SliderCommitDebounceMs);
            });
            // Typed value (Enter/tab-away) and popup teardown flush any pending edit so it's never lost in the debounce window.
            s.RegisterCallback<FocusOutEvent>(_ => { debounce?.Pause(); CommitNow(); }, TrickleDown.TrickleDown);
            s.RegisterCallback<DetachFromPanelEvent>(_ => { debounce?.Pause(); CommitNow(); });

            root.Add(s);
            return s;
        }

        // ---- Breadcrumb ----
        // The clicked node's ancestor chain (Root › … › current node) as a compact, wrapping row that REPLACES
        // the title. Earlier crumbs are clickable links that retarget the popup to that ancestor in place (rebuild
        // + spring + move the glow); the last crumb is the current node (bold, non-interactive). The crumbs are
        // the prefixes of _indexPath resolved against OwningRoot — a cross-asset inline-editor chain (a different
        // scope per crumb) is out of scope here and is a documented follow-up.
        private VisualElement BuildBreadcrumb()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 6;

            // Crumbs come from the PERSISTENT full trail (_fullPath) the popup opened at, NOT the current node —
            // so the whole chain stays visible and clickable in BOTH directions as you hop between levels (jump
            // field → root, then root → field again). Keep only quick-editable nodes; skip the bare collection
            // ItemLayout pseudo-step (its CollectionNode crumb already stands in for it).
            var crumbs = new List<(LayoutNode node, List<int> path)>();
            var trail = _fullPath ?? _indexPath;
            if (trail != null && OwningRoot != null)
            {
                for (int i = 0; i <= trail.Count; i++)
                {
                    var prefix = trail.GetRange(0, i);
                    if (i != trail.Count && prefix.Count > 0 && prefix[prefix.Count - 1] == -1) continue;
                    var node = QuickEditMenu.NodeAtIndexPath(OwningRoot, prefix);
                    if (node == null || !QuickEditMenu.IsQuickEditable(node)) continue;
                    crumbs.Add((node, prefix));
                }
            }

            // Fallback: if the trail didn't resolve (synthetic/unreachable node), just show the current node.
            if (crumbs.Count == 0)
            {
                row.Add(MakeCurrentCrumb(QuickEditMenu.Describe(_node, _indexPath, OwningRoot).TrimEnd('…')));
                return row;
            }

            // The crumb we're editing right now is bold + non-clickable; EVERY other crumb is a link (ancestors to
            // go up, descendants to come back down).
            int currentIdx = crumbs.Count - 1;
            for (int i = 0; i < crumbs.Count; i++)
                if (SamePath(crumbs[i].path, _indexPath)) { currentIdx = i; break; }

            // Collapse a deep trail to Root › … › Parent › Current ONLY while we're still at the deepest crumb (the
            // just-opened state, so the row starts tidy). Once you navigate up, the FULL trail shows so every level
            // stays one click away — including the leaf you came from. The "…" expands the elided middle on demand.
            const int MaxCrumbs = 4;
            bool collapse = !_breadcrumbExpanded && crumbs.Count > MaxCrumbs && currentIdx == crumbs.Count - 1;
            for (int i = 0; i < crumbs.Count; i++)
            {
                if (collapse && i >= 1 && i <= crumbs.Count - 3)
                {
                    if (i == 1) // emit a single "…" (expands the elided middle crumbs) in place of the run
                    {
                        if (row.childCount > 0) row.Add(MakeSeparator());
                        row.Add(MakeEllipsisCrumb());
                    }
                    continue;
                }
                if (row.childCount > 0) row.Add(MakeSeparator());
                string label = QuickEditMenu.CrumbLabel(crumbs[i].node, crumbs[i].path, OwningRoot);
                row.Add(i == currentIdx
                    ? MakeCurrentCrumb(label)
                    : MakeLinkCrumb(crumbs[i].node, crumbs[i].path, label));
            }
            return row;
        }

        // Retargets the popup to an ancestor crumb: swap the edited node/path (scope unchanged), then deferred
        // rebuild (+ spring to the new height) and move the glow — the same in-place switch the popup already
        // does for HelpBox Expandable, so BuildContent's node-type switch handles the new target for free.
        private void OnCrumbClick(LayoutNode node, List<int> path)
        {
            // Move along the trail — but NEVER shorten _fullPath, so the crumb we came from stays clickable.
            _node = node;
            _indexPath = new List<int>(path);
            rootVisualElement.schedule.Execute(() =>
            {
                RebuildPopupContent();
                QuickEditGlow.Retarget(_ctx.StableRoot, _node);
            }).ExecuteLater(1);
        }

        private static bool SamePath(List<int> a, List<int> b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private VisualElement MakeCurrentCrumb(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize = 11;
            l.style.color = DesignerPalette.PopupTitleText;
            // A subtle rounded pill behind the crumb you're currently editing, so it's obvious which level you're on.
            l.style.backgroundColor = DesignerPalette.SegmentHoverIdleBg;
            l.style.paddingLeft = 5;
            l.style.paddingRight = 5;
            l.style.paddingTop = 1;
            l.style.paddingBottom = 1;
            l.style.borderTopLeftRadius = 4;
            l.style.borderTopRightRadius = 4;
            l.style.borderBottomLeftRadius = 4;
            l.style.borderBottomRightRadius = 4;
            return l;
        }

        private VisualElement MakeLinkCrumb(LayoutNode node, List<int> path, string text)
        {
            var l = new Label(text);
            l.style.fontSize = 11;
            l.style.color = DesignerPalette.PopupLink;
            l.tooltip = "Edit this level";
            var capturedNode = node;
            var capturedPath = path;
            l.AddManipulator(new Clickable(() => OnCrumbClick(capturedNode, capturedPath)));
            return l;
        }

        private VisualElement MakeEllipsisCrumb()
        {
            var l = new Label("…");
            l.style.fontSize = 11;
            l.style.color = DesignerPalette.PopupLink;
            l.tooltip = "Show the hidden levels";
            // Expand every crumb in place (a deferred rebuild — never open a native menu, which would steal
            // focus and close this ShowAsDropDown window mid-navigation).
            l.AddManipulator(new Clickable(() =>
            {
                _breadcrumbExpanded = true;
                rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1);
            }));
            return l;
        }

        private VisualElement MakeSeparator()
        {
            var s = new Label("›");
            s.style.fontSize = 11;
            s.style.color = DesignerPalette.BorderMid;
            s.style.marginLeft = 3;
            s.style.marginRight = 3;
            s.pickingMode = PickingMode.Ignore;
            return s;
        }

        // ---- Content ----

        private void BuildContent()
        {
            // The window root is a transparent column that hosts the main card and, on demand, the code-peek
            // card BELOW it (see ShowPeekCard). Keeping the border/background/padding on an INNER card — not on
            // the window root itself — is what lets the peek read as a second little popup connected under the
            // quick-edit menu rather than being crammed inside it.
            var windowRoot = rootVisualElement;
            windowRoot.style.flexDirection = FlexDirection.Column;

            var card = new VisualElement();
            _card = card;
            card.style.flexGrow = 1; // fills the window above the peek card; shrinks (body scrolls) past MaxPopupHeight
            // The card fills the FULL window width (both it and the peek below stretch to the window), so when a
            // code-peek widens the window there's no empty top-right corner of bare window background sitting over
            // the inspector — every opaque pixel is either the menu or the code card. The window's left edge stays
            // pinned to the anchor (ApplyWindowHeight), so growing wider never moves the menu.
            card.style.backgroundColor = DesignerPalette.PopupBgOpaque;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            var border = DesignerPalette.PopupBorder;
            card.style.borderLeftColor = border;
            card.style.borderRightColor = border;
            card.style.borderTopColor = border;
            card.style.borderBottomColor = border;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            windowRoot.Add(card);

            // The controls live in a left-aligned column capped at MenuContentWidth, so when the window widens for
            // a code peek the fields keep their compact width (the code drives the panel width, not the menu). The
            // card fills the rest with its own background — one contiguous panel, no bare-window corner.
            var content = new VisualElement();
            content.style.flexGrow = 1;                 // fills the card's height so the body can scroll
            content.style.maxWidth = MenuContentWidth;  // ...but never wider than the compact menu
            content.style.alignSelf = Align.FlexStart;  // hug the left edge; extra card width stays empty background
            card.Add(content);

            // Header = the clicked node's ancestor breadcrumb, in place of a plain title: earlier crumbs are
            // clickable and retarget the popup to that ancestor in place, so mis-aiming "Root" vs "Field" doesn't
            // force a close + re-right-click. The last crumb (the current node) is the title.
            _crumbRow = BuildBreadcrumb();
            content.Add(_crumbRow);

            // Body scrolls so a long Message (or many rows) can't push the rest of the popup — including the
            // "Open in Designer" footer — off-screen with no way to reach it. Header + footer stay pinned.
            var body = new ScrollView(ScrollViewMode.Vertical);
            body.style.flexGrow = 1;
            body.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _bodyScroll = body;
            content.Add(body);

            BuildVisibilityRow(body);

            switch (_node)
            {
                case BindingNode bn: BuildBindingPanel(body, bn); break;
                case CollectionNode col: BuildCollectionPanel(body, col); break;
                case ButtonNode btn: BuildButtonPanel(body, btn); break;
                case ContainerNode c: BuildContainerPanel(body, c); break;
                case ScriptHeaderNode sh: BuildScriptHeaderPanel(body, sh); break;
                case HelpBoxNode hb: BuildHelpBoxPanel(body, hb); break;
                case PropertyNode pn: BuildPropertyPanel(body, pn); break;
                case TextBlockNode tb: BuildTextBlockPanel(body, tb); break;
                case ImageBannerNode ib: BuildImagePanel(body, ib); break;
                case LabeledDividerNode ld: BuildDividerPanel(body, ld); break;
                case SpaceNode sp: BuildSpacePanel(body, sp); break;
                case EmbeddedEditorNode en: BuildEmbeddedEditorPanel(body, en); break;
            }

            BuildFooter(content);

            // Size the window to fit the freshly-built content (snaps on first open, springs on later rebuilds).
            MeasureAndResize();

            // ...and (re)claim keyboard focus, so Escape still closes the menu after a rebuild swapped the rows out.
            ArmDismissKeys();
        }

        /// <summary>
        /// "Visible in Inspector" — hide this element straight from the inspector you are looking at, without a
        /// trip to the designer. Built once here rather than in each of the twelve per-kind panels: it means the
        /// same thing for every node kind, and a row that only some kinds carried would read as a capability the
        /// others lack.
        ///
        /// <para>One-way by construction, and that is fine. This popup only ever opens on something the inspector
        /// DREW, so a hidden node can never be right-clicked back — the eye on the designer's Hierarchy row is
        /// where it comes back from, and the tooltip says so. Turning it off therefore closes the popup: the node
        /// it is editing is about to stop existing, and a menu still pointing at it would be a menu pointing at
        /// nothing.</para>
        ///
        /// <para>Not offered for the Script Header. It is the inspector's title bar — hiding it from a menu you
        /// reached BY right-clicking it would leave the panel looking decapitated with no obvious undo.</para>
        /// </summary>
        private void BuildVisibilityRow(VisualElement root)
        {
            if (_node == null || _node is ScriptHeaderNode) return;
            if (OwningLayout != null && ReferenceEquals(_node, OwningLayout.Root)) return;
            // …and not on a dictionary Key / Value CELL TEMPLATE, which the designer's Properties panel hides
            // this row for as well. SerializedDictionaryDrawer builds its cells outside BuildNode, which is
            // where the hidden-node skip lives, so the flag draws nothing in either renderer. Offering it here
            // was worse than inert: the callback CLOSES the popup on un-check, and the way back — the eye on
            // the designer's Hierarchy row — is itself hidden for these nodes, so the switch was one-way with
            // no visible effect. One predicate for both surfaces (IsDictionaryCellTemplate).
            if (SasquatchLayoutDesignerWindow.IsDictionaryCellTemplate(_node, OwningRoot)) return;

            var visible = new Toggle("Visible in Inspector") { value = !_node.HiddenInInspector };
            visible.tooltip =
                "Off = this element (and anything inside it) is not drawn here at all, and everything around it " +
                "closes up as if it were deleted. Nothing is lost — bring it back with the eye on its row in the " +
                "Layout Designer's Hierarchy.";
            visible.RegisterValueChangedCallback(evt =>
            {
                var node = _node;
                bool nowHidden = !evt.newValue;
                Commit(() => node.HiddenInInspector = nowHidden);
                // Deferred, not direct: Close() tears down the panel this callback is still running inside.
                if (nowHidden) rootVisualElement.schedule.Execute(Close).ExecuteLater(1);
            });
            root.Add(visible);
        }

        private void BuildBindingPanel(VisualElement root, BindingNode bn)
        {
            Type targetType = OwningTarget != null ? OwningTarget.GetType() : null;

            // Label + Tooltip reuse the designer's overridable-row convention: code default shown as
            // normal-weight text, override bold, cleared = "(hidden)", ↺ restores the code default.
            var labelRow = OverridableTextRow.Create(
                "Label", bn.LabelOverride, OverridableTextRow.ResolveCodeLabel(bn, targetType),
                stored => Commit(() => bn.LabelOverride = stored, rebuildNow: false),
                "The field's label. Clear it to hide the label entirely.");
            FlushTextEditOn(labelRow);
            root.Add(labelRow);

            var tooltipRow = OverridableTextRow.Create(
                "Tooltip", bn.Tooltip, OverridableTextRow.ResolveCodeTooltip(bn, targetType),
                stored => Commit(() => bn.Tooltip = stored, rebuildNow: false));
            FlushTextEditOn(tooltipRow);
            root.Add(tooltipRow);

            // Label Above Field: stacks the label on its own row above the field, instead of the inspector's
            // left label column (a label-layout option, grouped with Label/Tooltip). Omitted entirely for the
            // Display Types it cannot reach (DisplayTypeCatalog.SupportsLabelAbove) — no live show/hide needed
            // here, because the popup fully rebuilds on a Display Type change. The FIELD TYPE is part of the
            // question: a SerializedDictionary reaches the dictionary table on Default, and offering the
            // toggle there would stack a second copy of the name over the table's own header.
            if (DisplayTypeCatalog.SupportsLabelAbove(bn.DisplayType, ResolveBoundFieldType(bn)))
            {
                var labelAboveToggle = new Toggle("Label Above Field") { value = bn.LabelAbove };
                labelAboveToggle.tooltip = "Put the field's label on its own row above the field, instead of the inspector's left label column.";
                labelAboveToggle.RegisterValueChangedCallback(evt => Commit(() => bn.LabelAbove = evt.newValue));
                root.Add(labelAboveToggle);
            }

            var readOnlyToggle = new Toggle("Read-Only") { value = bn.IsReadOnly };
            readOnlyToggle.RegisterValueChangedCallback(evt => Commit(() => bn.IsReadOnly = evt.newValue));
            root.Add(readOnlyToggle);

            // Prefix / Suffix. GENERAL settings, not per-type options: affixes apply to every Display Type,
            // so they belong above the divider with Label / Tooltip / Read-Only rather than in any one arm.
            QuickEditDisplayOptions.BuildAffixRows(root, new QuickEditOptionsContext
            {
                Node = bn,
                Commit = mutate => Commit(mutate),
                RequestPopupRelayout = () => rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1),
            }, bn);

            Type fieldType = ResolveBoundFieldType(bn);
            var offered = DisplayTypeCatalog.OfferedForField(bn.DisplayType, fieldType);
            // PopupField throws if the stored kind isn't offered (e.g. a SpringConfig field that now offers only
            // Spring) — fall back to the first offered kind and persist the correction.
            var currentWidget = offered.Contains(bn.DisplayType) ? bn.DisplayType : offered[0];
            if (currentWidget != bn.DisplayType) bn.DisplayType = currentWidget;
            var widgetField = new PopupField<DisplayType>("Display Type", offered, currentWidget,
                DisplayTypeCatalog.Format, DisplayTypeCatalog.Format);
            widgetField.RegisterValueChangedCallback(evt =>
            {
                Commit(() =>
                {
                    bn.DisplayType = evt.newValue;
                    if (evt.newValue == DisplayType.ProgressBar) bn.ProgressBarInteractive = true; // designer parity
                });
                // The options section below is per-DisplayType — rebuild the popup so it swaps to the new type's
                // options (and springs to the new height). Deferred (never straight from a change callback): the
                // rebuild would otherwise destroy this PopupField mid-callback. See RebuildPopupContent.
                rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1);
            });
            root.Add(widgetField);

            // "Something else already draws this field" — see CustomDrawerNoticeRow. No Sync wiring needed
            // here: a Display Type change rebuilds this whole popup, which re-creates the notice.
            var drawerNotice = CustomDrawerNoticeRow.TryCreate(ResolveBoundFieldInfo(bn), currentWidget);
            if (drawerNotice != null) root.Add(drawerNotice);

            // Curated per-DisplayType "Display Style/Options", separated from the general settings above by a
            // divider. Auto-scoped to the current type (the popup rebuilds on type change, so no live-switch is
            // needed); heavier color/gradient/palette pickers stay in the full designer via the footer. The divider
            // is dropped when the selected type has no options, so it never dangles at the bottom.
            int beforeOptions = root.childCount;
            var optionsDivider = MakeSectionDivider();
            root.Add(optionsDivider);
            QuickEditDisplayOptions.Build(root, new QuickEditOptionsContext
            {
                Node = bn,
                FieldType = fieldType,
                Commit = mutate => Commit(mutate),
                RequestPopupRelayout = () => rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1),
            });
            if (root.childCount == beforeOptions + 1) root.Remove(optionsDivider); // only the divider got added → no options
        }

        // A thin horizontal rule used to separate sections inside the popup (same look as the footer separator).
        private VisualElement MakeSectionDivider()
        {
            var d = new VisualElement();
            d.style.height = 1;
            d.style.backgroundColor = DesignerPalette.BorderMid;
            d.style.marginTop = 6;
            d.style.marginBottom = 6;
            return d;
        }

        private void BuildContainerPanel(VisualElement root, ContainerNode c)
        {
            // A tab page gets its tab-strip label as the FIRST row — that's the name the user sees and
            // the thing they almost certainly right-clicked the tab to rename.
            if (QuickEditMenu.IsTabChild(OwningRoot, _indexPath))
            {
                _hasTabLabelRow = true;
                var tabLabelField = new TextField("Tab Label") { value = c.TabLabel ?? string.Empty };
                tabLabelField.RegisterValueChangedCallback(evt => Commit(() => c.TabLabel = evt.newValue, rebuildNow: false));
                FlushTextEditOn(tabLabelField);
                root.Add(tabLabelField);
            }

            var titleField = new TextField("Title") { value = c.Title ?? string.Empty };
            titleField.RegisterValueChangedCallback(evt => Commit(() => c.Title = evt.newValue, rebuildNow: false));
            FlushTextEditOn(titleField);
            root.Add(titleField);

            var kinds = ((ContainerType[])Enum.GetValues(typeof(ContainerType))).ToList();
            var kindField = new PopupField<ContainerType>("Type", kinds, c.Type,
                k => ObjectNames.NicifyVariableName(k.ToString()),
                k => ObjectNames.NicifyVariableName(k.ToString()));
            // Through the shared migration, never a bare `c.Type =`: a Tabs container's direct children ARE
            // its tab pages and a split Frame's ARE its two panes, so a raw assignment silently re-reads the
            // existing children as a structure they were never built to be. A dropdown callback inside a
            // popup is the wrong place to raise a modal, so this call site always takes the non-destructive
            // KeepAsGroups default — leaving Tabs keeps every page as a titled group rather than flattening.
            kindField.RegisterValueChangedCallback(evt => Commit(() => ContainerTypeMigration.Apply(c, evt.newValue)));
            root.Add(kindField);

            var expandedToggle = new Toggle("Default to Open") { value = c.DefaultToOpen };

            var minimizableToggle = new Toggle("Expandable") { value = c.Expandable };
            minimizableToggle.RegisterValueChangedCallback(evt =>
            {
                Commit(() => c.Expandable = evt.newValue);
                expandedToggle.SetEnabled(evt.newValue || c.Type == ContainerType.Foldout);
            });
            root.Add(minimizableToggle);

            expandedToggle.SetEnabled(c.Expandable || c.Type == ContainerType.Foldout);
            expandedToggle.RegisterValueChangedCallback(evt => Commit(() => c.DefaultToOpen = evt.newValue));
            root.Add(expandedToggle);

            // Only the types the builder actually applies Gap to — the same three the designer's Spacing row
            // is gated to (Rows and Columns go through ApplyGap, a Grid through the grid layout helper).
            // Ungated, this offered a live-inspector number field that did nothing at all on a Card, Foldout,
            // Frame or Tabs, and quietly seeded the value that then showed up only in an exported editor.
            if (c.Type == ContainerType.Rows || c.Type == ContainerType.Columns || c.Type == ContainerType.Grid)
            {
                var gapField = new FloatField("Gap") { value = c.Gap };
                gapField.RegisterValueChangedCallback(evt => Commit(() => c.Gap = Mathf.Max(0f, evt.newValue)));
                root.Add(gapField);
            }
        }

        // Context presets: mirror the designer — restyle a collection's element fields when its Display Type is
        // switched between List and Table. Runs inside the Commit mutate (which owns undo/persist/refresh). No-op
        // unless a field type has an enabled context override; gated by the per-user "Use Layout Presets" toggle.
        private void ReseedCollectionForContext(CollectionNode col, CollectionDisplayMode oldMode, CollectionDisplayMode newMode)
        {
            if (col == null || !LayoutPresetPrefs.UseLayoutPresets) return;
            var oldCtx = LayoutPresetApplier.ContextFor(oldMode);
            var newCtx = LayoutPresetApplier.ContextFor(newMode);
            if (!oldCtx.HasValue || !newCtx.HasValue) return; // only List <-> Table carry a per-field context
            var preset = LayoutPresetManager.Current;
            if (preset == null || OwningTarget == null) return;
            var elementType = LayoutPresetApplier.ResolveElementType(OwningTarget.GetType(), col.PropertyPath);
            if (elementType == null) return;
            LayoutPresetApplier.ReseedCollectionContext(col, elementType, preset, oldCtx.Value, newCtx.Value);
        }

        private void BuildCollectionPanel(VisualElement root, CollectionNode col)
        {
            // Mirror the designer's UpdateDisplayFields gate: List and Table own the row ergonomics (reorder /
            // search / sort / paginate / expandable). Default hands the whole array to Unity's own PropertyField
            // and Tag Chips draws a chip strip, so on those every one of those rows writes to the model and
            // changes nothing on screen. Kept in lockstep with SasquatchLayoutDesignerWindow.UpdateDisplayFields.
            //
            // SKIPPED, never display-gated: MeasureAndResize sizes the window from the BUILT content, so a row
            // that is merely hidden is still paid for in popup height.
            bool active = col.DisplayMode == CollectionDisplayMode.List
                       || col.DisplayMode == CollectionDisplayMode.Table;

            // Title first, same overridable convention as a binding's Label: the property's own display name
            // shows as the normal-weight default, a rename is bold, cleared reads "(hidden)".
            // Every mode that draws a header of OURS honours it — List/Table titles, the label column on Tag
            // Chips, a whole-array Custom Widget (see ResolveCollectionTitle). Default is the one exception:
            // Unity's own PropertyField owns that header, so the row would be a control that does nothing.
            if (col.DisplayMode != CollectionDisplayMode.Default)
            {
                var titleRow = OverridableTextRow.Create(
                    "Title", col.TitleOverride, OverridableTextRow.ResolveCollectionTitle(col),
                    stored => Commit(() => col.TitleOverride = stored, rebuildNow: false),
                    "The collection's title row (its name + element count). Clear it to hide the title entirely.");
                FlushTextEditOn(titleRow);
                root.Add(titleRow);
            }

            // CustomWidget needs an asset picker the quick-edit popup doesn't host, so it's offered only in the
            // full designer. Keep it selectable here only when already set, so the popup still displays it.
            var modes = ((CollectionDisplayMode[])Enum.GetValues(typeof(CollectionDisplayMode)))
                .Where(m => m != CollectionDisplayMode.CustomWidget || col.DisplayMode == CollectionDisplayMode.CustomWidget)
                .ToList();
            var modeField = new PopupField<CollectionDisplayMode>("Display Type", modes, col.DisplayMode,
                m => ObjectNames.NicifyVariableName(m.ToString()),
                m => ObjectNames.NicifyVariableName(m.ToString()));
            modeField.RegisterValueChangedCallback(evt =>
            {
                Commit(() =>
                {
                    var oldMode = col.DisplayMode;
                    col.DisplayMode = evt.newValue;
                    ReseedCollectionForContext(col, oldMode, evt.newValue); // context presets: restyle cells for the new mode
                });
                // The new mode changes WHICH rows this panel has (List Style is List-only, and the whole
                // row-ergonomics set is List/Table-only), so without this the panel keeps offering the OLD
                // mode's controls until the popup is closed and reopened. Same deferred idiom as the binding
                // panel's DisplayType field — a straight call destroys this PopupField mid-callback.
                // See RebuildPopupContent.
                rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1);
            });
            root.Add(modeField);

            // Only the two styles still offered — Arrows / Both stay in the enum for stored layouts but are
            // no longer choosable (see LayoutTreeBuilder.ResolveReorderStyle). A node that HAS one shows the
            // value it renders as, so the popup never displays a choice absent from its own list.
            if (active)
            {
                var styles = new List<CollectionReorderStyle>
                {
                    CollectionReorderStyle.DragAndDrop, CollectionReorderStyle.None
                };
                var styleField = new PopupField<CollectionReorderStyle>("Reorder Style", styles,
                    styles.Contains(col.ReorderStyle) ? col.ReorderStyle : LayoutTreeBuilder.ResolveReorderStyle(col.ReorderStyle),
                    s => s == CollectionReorderStyle.None ? "None" : "Drag Handle",
                    s => s == CollectionReorderStyle.None ? "None" : "Drag Handle");
                styleField.RegisterValueChangedCallback(evt => Commit(() => col.ReorderStyle = evt.newValue));
                root.Add(styleField);
            }

            // The List's container-level look. List mode only — a Table has its own grid chrome.
            if (col.DisplayMode == CollectionDisplayMode.List)
            {
                var listStyleField = new EnumField("List Style", col.ListStyle);
                listStyleField.tooltip =
                    "Rows (bordered shell), Minimal (no shell, more air), or Timeline (a recessed well " +
                    "with a rail down it, each index a node on the rail).";
                listStyleField.RegisterValueChangedCallback(evt =>
                    Commit(() => col.ListStyle = (CollectionListStyle)evt.newValue));
                root.Add(listStyleField);
            }

            // Search / sort / paging / expandable — the rest of the row-ergonomics set. List and Table only.
            if (!active) return;

            var searchableToggle = new Toggle("Searchable") { value = col.Searchable };
            searchableToggle.RegisterValueChangedCallback(evt => Commit(() => col.Searchable = evt.newValue));
            root.Add(searchableToggle);

            var searchPropertyField = new TextField("Search Field") { value = col.SearchProperty ?? string.Empty };
            searchPropertyField.tooltip = "Limit search to one element field (blank = all fields).";
            searchPropertyField.RegisterValueChangedCallback(evt => Commit(() => col.SearchProperty = evt.newValue, rebuildNow: false));
            FlushTextEditOn(searchPropertyField);
            root.Add(searchPropertyField);

            var sortableToggle = new Toggle("Sortable") { value = col.EnableSorting };
            sortableToggle.tooltip = "Click a Table header (or use the List sort dropdown) to sort rows. View-only.";
            sortableToggle.RegisterValueChangedCallback(evt => Commit(() => col.EnableSorting = evt.newValue));
            root.Add(sortableToggle);

            var paginateToggle = new Toggle("Paginate") { value = col.EnablePagination };
            paginateToggle.RegisterValueChangedCallback(evt => Commit(() => col.EnablePagination = evt.newValue));
            root.Add(paginateToggle);

            var pageSizeField = new IntegerField("Page Size") { value = col.PageSize };
            pageSizeField.RegisterValueChangedCallback(evt => Commit(() => col.PageSize = Mathf.Max(1, evt.newValue)));
            root.Add(pageSizeField);

            var pagerStyles = ((PaginationDisplayStyle[])Enum.GetValues(typeof(PaginationDisplayStyle))).ToList();
            var pagerStyleField = new PopupField<PaginationDisplayStyle>("Page Navigator", pagerStyles, col.PaginationDisplayStyle,
                s => ObjectNames.NicifyVariableName(s.ToString()),
                s => ObjectNames.NicifyVariableName(s.ToString()));
            pagerStyleField.tooltip = "How the page navigator is shown: prev/next buttons, clickable page dots, or both.";
            pagerStyleField.RegisterValueChangedCallback(evt => Commit(() => col.PaginationDisplayStyle = evt.newValue));
            root.Add(pagerStyleField);

            var minimizableToggle = new Toggle("Expandable") { value = col.Expandable };
            minimizableToggle.RegisterValueChangedCallback(evt => Commit(() => col.Expandable = evt.newValue));
            root.Add(minimizableToggle);
        }

        private void BuildButtonPanel(VisualElement root, ButtonNode btn)
        {
            var textField = new TextField("Text") { value = btn.ButtonText ?? string.Empty };
            textField.RegisterValueChangedCallback(evt => Commit(() => btn.ButtonText = evt.newValue, rebuildNow: false));
            FlushTextEditOn(textField);
            root.Add(textField);

            var methodTarget = OwningTarget != null ? OwningTarget.GetType() : null;
            var methodField = new TextField("Method") { value = btn.MethodName ?? string.Empty };

            // Missing-method affordance — the compact twin of the designer's Method Name hint.
            var methodWarn = new Label();
            methodWarn.style.fontSize = 10;
            methodWarn.style.whiteSpace = WhiteSpace.Normal;
            methodWarn.style.color = DesignerPalette.ToastWarning;
            methodWarn.style.marginTop = 1;
            methodWarn.style.marginBottom = 3;
            System.Action updateMethodWarn = () =>
            {
                bool missing = methodTarget != null && !string.IsNullOrEmpty(methodField.value) &&
                               !EditorApplication.isCompiling &&
                               ButtonInvoker.ResolveMethod(methodTarget, methodField.value) == null;
                if (missing)
                {
                    methodWarn.text = ButtonInvoker.HasMethodNamed(methodTarget, methodField.value)
                        ? $"'{methodField.value}' exists but has parameters a button can't prompt for."
                        : $"'{methodField.value}' isn't on {methodTarget.Name} — renamed or removed?";
                }
                methodWarn.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
            };

            methodField.RegisterValueChangedCallback(evt => Commit(() =>
            {
                btn.MethodName = evt.newValue;
                // Same commit stamps/clears the rename-heal fingerprint (see the designer's Method Name
                // field): resolving name ⇒ its signature; non-resolving ⇒ "" so a typed-ahead name can't
                // inherit the previous method's fingerprint.
                btn.MethodSignature = ButtonMethodSync.ComputeSignature(methodTarget, evt.newValue);
            }, rebuildNow: false));
            methodField.RegisterValueChangedCallback(evt => updateMethodWarn());
            FlushTextEditOn(methodField);
            AutocompleteTextField.Attach(methodField, () => ButtonInvoker.GetMethodSuggestions(methodTarget),
                s => MethodSourcePreview.GetSnippet(methodTarget, s.Value), ShowPeekCard, HidePeekCard);
            root.Add(methodField);
            root.Add(methodWarn);
            updateMethodWarn();

            var confirmToggle = new Toggle("Confirm First") { value = btn.ConfirmBeforeExecute };
            confirmToggle.tooltip = "Show a Yes/Cancel dialog before the method runs.";
            confirmToggle.RegisterValueChangedCallback(evt => Commit(() => btn.ConfirmBeforeExecute = evt.newValue));
            root.Add(confirmToggle);

            // The one appearance row the popup carries (the rest of the button's look stays designer-only, per
            // the heavy-picker rule): a readability fix belongs where the unreadable button is — the inspector.
            var backdropToggle = new Toggle("Text Backdrop") { value = btn.TextBackdrop };
            backdropToggle.tooltip = "Sit the label — and the icon — on a soft dark pill inside the button, for " +
                                     "guaranteed readability over any accent color.";
            backdropToggle.RegisterValueChangedCallback(evt => Commit(() => btn.TextBackdrop = evt.newValue));
            root.Add(backdropToggle);
        }

        private void BuildScriptHeaderPanel(VisualElement root, ScriptHeaderNode sh)
        {
            // Display Text reuses the overridable-row convention; the code default is the script's
            // class name (resolved from the hidden m_Script, same as the designer's Properties panel).
            var textRow = OverridableTextRow.Create(
                "Display Text", sh.TextOverride, DefaultScriptHeaderName(),
                stored => Commit(() => sh.TextOverride = stored, rebuildNow: false),
                "The header's text. Clear it to hide the text entirely.");
            FlushTextEditOn(textRow);
            root.Add(textRow);

            var hideIconToggle = new Toggle("Hide Icon") { value = sh.HideIcon };
            hideIconToggle.RegisterValueChangedCallback(evt => Commit(() => sh.HideIcon = evt.newValue));
            root.Add(hideIconToggle);

            var showSearchToggle = new Toggle("Show Search") { value = sh.ShowSearch };
            showSearchToggle.tooltip = "Show the field-search magnifier in the header (hover-revealed, next to the " +
                                       "Live Debug bug). Off removes it and disables the inspector field search for this layout.";
            showSearchToggle.RegisterValueChangedCallback(evt => Commit(() => sh.ShowSearch = evt.newValue));
            root.Add(showSearchToggle);

            var showDebugToggle = new Toggle("Show Live Debug") { value = sh.ShowDebugToggle };
            showDebugToggle.tooltip = "Add the Live Debug button (bug glyph) to the header. Clicking it records every " +
                                      "field on this script while you play and graphs each one's history under it. " +
                                      "This only adds the button — nothing records until it is switched on.";
            showDebugToggle.RegisterValueChangedCallback(evt => Commit(() => sh.ShowDebugToggle = evt.newValue));
            root.Add(showDebugToggle);

            var iconField = new ObjectField("Custom Icon")
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false,
                value = sh.CustomIcon
            };
            iconField.tooltip = "Set a custom texture icon. Empty = the script's own icon; ignored when Hide Icon is on. " +
                                "(Pick a built-in icon instead from the Designer.)";
            iconField.RegisterValueChangedCallback(evt => Commit(() =>
            {
                var tex = evt.newValue as Texture2D;
                sh.CustomIcon = tex;
                // Assigning a texture here switches the header to the Custom source so it actually shows; any
                // built-in glyph is preserved and re-selectable from the Designer's Icon Source.
                if (tex != null) sh.IconSource = ScriptHeaderIconSource.Custom;
            }));
            root.Add(iconField);
        }

        private void BuildHelpBoxPanel(VisualElement root, HelpBoxNode hb)
        {
            // Summary Text first (expandable boxes only) — the line shown while the box is collapsed.
            // Same overridable convention as Label/Display Text; the code default is "{MessageType}
            // Details". The Expandable toggle below rebuilds this popup so the row tracks it live.
            if (hb.Expandable)
            {
                var summaryRow = OverridableTextRow.Create(
                    "Summary Text", hb.CollapsedSummary, hb.MessageType + " Details",
                    stored => Commit(() => hb.CollapsedSummary = stored, rebuildNow: false),
                    "The line shown while the box is collapsed. Clear it to hide the text entirely.");
                FlushTextEditOn(summaryRow);
                root.Add(summaryRow);
            }

            var msgField = new TextField("Message") { value = hb.Message ?? string.Empty };
            // Wrap long messages to the popup width and grow to show them (instead of clipping one line).
            TextAreaHelper.MakeWrappingTextArea(msgField);
            msgField.RegisterValueChangedCallback(evt => Commit(() => hb.Message = evt.newValue, rebuildNow: false));
            // Blur-only flush (not FlushTextEditOn): in a multiline field Enter types a newline, so it
            // must not trigger the rebuild the way it does for single-line rows.
            msgField.RegisterCallback<FocusOutEvent>(_ => { if (_rebuildPending) RebuildAndReglow(); }, TrickleDown.TrickleDown);
            root.Add(msgField);

            var typeField = new EnumField("Message Type", hb.MessageType);
            typeField.RegisterValueChangedCallback(evt => Commit(() => hb.MessageType = (HelpBoxMessageType)evt.newValue));
            root.Add(typeField);

            var markdownToggle = new Toggle("Render Markdown") { value = hb.RenderMarkdown };
            markdownToggle.tooltip =
                "Render the message as lightweight markdown — **bold**, *italics*, `code`, {Color|text}, " +
                "[links](https://…) — plus Unity rich text tags (<b>, <color=…>, <size=…>, …). " +
                "Turn off to show the text exactly as typed.";
            markdownToggle.RegisterValueChangedCallback(evt => Commit(() => hb.RenderMarkdown = evt.newValue));
            root.Add(markdownToggle);

            var expandableToggle = new Toggle("Expandable") { value = hb.Expandable };
            expandableToggle.tooltip = "Let the box collapse to a one-line summary (edited in the Summary Text row).";
            expandableToggle.RegisterValueChangedCallback(evt =>
            {
                Commit(() => hb.Expandable = evt.newValue);
                // The Summary Text row exists only while expandable — rebuild this popup so it
                // appears/disappears immediately (deferred: see RebuildPopupContent).
                rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1);
            });
            root.Add(expandableToggle);
        }

        // A reflected C# property (getter) — the sibling of the button node. General settings only; the label /
        // value text-style pickers ("T" rows) stay in the full designer.
        private void BuildPropertyPanel(VisualElement root, PropertyNode pn)
        {
            Type targetType = OwningTarget != null ? OwningTarget.GetType() : null;

            var nameField = new TextField("Property Name") { value = pn.PropertyName ?? string.Empty };
            nameField.tooltip = "The C# property (getter) to display live. A writable simple-typed property is editable " +
                                "(the setter runs on change); anything else shows read-only.";
            nameField.RegisterValueChangedCallback(evt => Commit(() => pn.PropertyName = evt.newValue, rebuildNow: false));
            FlushTextEditOn(nameField);
            AutocompleteTextField.Attach(nameField, () => GetReadablePropertySuggestions(targetType),
                s => MethodSourcePreview.GetPropertySnippet(targetType, s.Value), ShowPeekCard, HidePeekCard);
            root.Add(nameField);

            // Empty label override ⇒ the nicified property name (matches the designer's + builder's reading).
            var labelField = new TextField("Label") { value = pn.LabelOverride ?? string.Empty };
            labelField.tooltip = "Optional label text; empty uses a nicified property name.";
            labelField.RegisterValueChangedCallback(evt => Commit(() => pn.LabelOverride = evt.newValue, rebuildNow: false));
            FlushTextEditOn(labelField);
            root.Add(labelField);

            var hideLabelToggle = new Toggle("Hide Label") { value = pn.HideLabel };
            hideLabelToggle.tooltip = "Hide the label column so the value spans the whole row.";
            hideLabelToggle.RegisterValueChangedCallback(evt => Commit(() => pn.HideLabel = evt.newValue));
            root.Add(hideLabelToggle);

            var readOnlyToggle = new Toggle("Read-Only") { value = pn.ForceReadOnly };
            readOnlyToggle.tooltip = "Display the value read-only even if the property has a usable setter.";
            readOnlyToggle.RegisterValueChangedCallback(evt => Commit(() => pn.ForceReadOnly = evt.newValue));
            root.Add(readOnlyToggle);
        }

        // A decorative rich-text block. Text + Render Markdown + Max Width; the background CARD (color picker) and
        // its padding/rounding (which only apply with a background) stay in the full designer.
        private void BuildTextBlockPanel(VisualElement root, TextBlockNode tb)
        {
            var textField = new TextField("Text") { value = tb.Text ?? string.Empty };
            TextAreaHelper.MakeWrappingTextArea(textField);
            textField.tooltip = "The text to display. Rendered as lightweight markdown (**bold**, *italics*, `code`, " +
                                "{Color|text}, [links](https://…)) plus Unity rich text tags when Render Markdown is on.";
            textField.RegisterValueChangedCallback(evt => Commit(() => tb.Text = evt.newValue, rebuildNow: false));
            // Blur-only flush (a multiline field types a newline on Enter, so Enter must not trigger the rebuild).
            textField.RegisterCallback<FocusOutEvent>(_ => { if (_rebuildPending) RebuildAndReglow(); }, TrickleDown.TrickleDown);
            root.Add(textField);

            var mdToggle = new Toggle("Render Markdown") { value = tb.RenderMarkdown };
            mdToggle.tooltip = "Render the text as lightweight markdown plus Unity rich text tags. Off = shown exactly as typed.";
            mdToggle.RegisterValueChangedCallback(evt => Commit(() => tb.RenderMarkdown = evt.newValue));
            root.Add(mdToggle);

            var maxWidthField = new FloatField("Max Width") { value = tb.MaxWidth, isDelayed = true };
            maxWidthField.tooltip = "Maximum text width in pixels (0 = fill the inspector).";
            maxWidthField.RegisterValueChangedCallback(evt => Commit(() =>
            {
                tb.MaxWidth = evt.newValue;                       // setter clamps ≥ 0
                maxWidthField.SetValueWithoutNotify(tb.MaxWidth);
            }));
            root.Add(maxWidthField);
        }

        // A decorative image / banner leaf. All of its controls are compact enough to host here — nothing is
        // deferred to the designer.
        private void BuildImagePanel(VisualElement root, ImageBannerNode ib)
        {
            var texField = new ObjectField("Image") { objectType = typeof(Texture2D), allowSceneObjects = false };
            texField.tooltip = "The texture to display. Stored by GUID so it survives asset moves/renames.";
            if (!string.IsNullOrEmpty(ib.TextureGuid))
            {
                var p = AssetDatabase.GUIDToAssetPath(ib.TextureGuid);
                if (!string.IsNullOrEmpty(p)) texField.SetValueWithoutNotify(AssetDatabase.LoadAssetAtPath<Texture2D>(p));
            }
            texField.RegisterValueChangedCallback(evt => Commit(() =>
            {
                string guid = "";
                if (evt.newValue != null)
                {
                    var p = AssetDatabase.GetAssetPath(evt.newValue);
                    if (!string.IsNullOrEmpty(p)) guid = AssetDatabase.AssetPathToGUID(p);
                }
                ib.TextureGuid = guid;
            }));
            root.Add(texField);

            var alignField = new EnumField("Alignment", ib.Alignment);
            alignField.tooltip = "Horizontal placement of the image within the banner width (Fit scale mode).";
            alignField.RegisterValueChangedCallback(evt => Commit(() => ib.Alignment = (ImageBannerAlignment)evt.newValue));
            root.Add(alignField);

            // Only Fit / Stretch are offered (a legacy Scale-&-Crop layout reads as Fit); Crop Alpha hides for Stretch.
            Toggle cropAlphaToggle = null;
            var scaleChoices = new List<string> { "Fit", "Stretch" };
            string scaleInitial = ib.ScaleMode == ImageBannerScaleMode.StretchToFill ? "Stretch" : "Fit";
            var scaleField = new DropdownField("Scale Mode", scaleChoices, scaleInitial);
            scaleField.tooltip = "Fit = preserve aspect (honors Alignment; combine with Zoom / Crop Alpha to crop); " +
                                 "Stretch = fill the box exactly.";
            scaleField.RegisterValueChangedCallback(evt =>
            {
                var mode = evt.newValue == "Stretch" ? ImageBannerScaleMode.StretchToFill : ImageBannerScaleMode.ScaleToFit;
                Commit(() => ib.ScaleMode = mode);
                if (cropAlphaToggle != null)
                    cropAlphaToggle.style.display = mode == ImageBannerScaleMode.ScaleToFit ? DisplayStyle.Flex : DisplayStyle.None;
            });
            root.Add(scaleField);

            SliderRow(root, "Height", ib.Height, ImageBannerNode.MinHeight, ImageBannerNode.MaxHeight,
                "Banner height in pixels (8-800).", v => ib.Height = v); // setter clamps

            SliderRow(root, "Zoom", ib.Zoom, ImageBannerNode.MinZoom, ImageBannerNode.MaxZoom,
                "Uniform zoom on top of the scale mode (1 = unchanged; 0.1-8). Above 1 enlarges and crops to the box.",
                v => ib.Zoom = v); // setter clamps

            cropAlphaToggle = new Toggle("Crop Alpha") { value = ib.CropAlpha };
            cropAlphaToggle.tooltip = "Tighten the fit to the image's opaque region so transparent margins don't waste space. Fit scale mode only.";
            cropAlphaToggle.style.display = ib.ScaleMode == ImageBannerScaleMode.ScaleToFit ? DisplayStyle.Flex : DisplayStyle.None;
            cropAlphaToggle.RegisterValueChangedCallback(evt => Commit(() => ib.CropAlpha = evt.newValue));
            root.Add(cropAlphaToggle);
        }

        // A decorative horizontal divider. Title + Thickness + Corner Radius; the line-color picker and the caption
        // text-style ("T" row) stay in the full designer.
        private void BuildDividerPanel(VisualElement root, LabeledDividerNode ld)
        {
            var titleField = new TextField("Title") { value = ld.Title ?? string.Empty };
            titleField.tooltip = "Optional centered caption; leave empty for a plain rule.";
            titleField.RegisterValueChangedCallback(evt => Commit(() => ld.Title = evt.newValue, rebuildNow: false));
            FlushTextEditOn(titleField);
            root.Add(titleField);

            SliderRow(root, "Thickness", ld.Thickness, LabeledDividerNode.MinThickness, LabeledDividerNode.MaxThickness,
                "Rule thickness in pixels (1-12).", v => ld.Thickness = v); // setter clamps

            SliderRow(root, "Corner Radius", ld.CornerRadius, 0f, LabeledDividerNode.MaxCornerRadius,
                "Round the ends of the rule, in pixels (0 = square).", v => ld.CornerRadius = v); // setter clamps
        }

        // A decorative spacer. Flexible up top; Direction + fixed px only apply to a fixed-size space, so they are
        // dropped entirely when Flexible is on (the popup rebuilds + resizes on the toggle).
        private void BuildSpacePanel(VisualElement root, SpaceNode sp)
        {
            var flexToggle = new Toggle("Flexible") { value = sp.Flexible };
            flexToggle.tooltip = "Fill all leftover room instead of a fixed size (like GUILayout.FlexibleSpace). " +
                                 "Most useful in a Columns row or a fixed-height container — it pushes siblings apart.";
            flexToggle.RegisterValueChangedCallback(evt =>
            {
                Commit(() => sp.Flexible = evt.newValue);
                // Direction + Space only apply to a fixed space — rebuild so they appear/disappear (and it resizes).
                rootVisualElement.schedule.Execute(RebuildPopupContent).ExecuteLater(1);
            });
            root.Add(flexToggle);

            if (sp.Flexible) return; // a flexible space ignores Direction / Space amount

            var directionField = new EnumField("Direction", sp.Axis);
            directionField.tooltip = "Vertical = a gap between stacked (Rows) items; Horizontal = a gap between " +
                                     "side-by-side (Columns/Grid) items.";
            directionField.RegisterValueChangedCallback(evt => Commit(() => sp.Axis = (SpaceAxis)evt.newValue));
            root.Add(directionField);

            SliderRow(root, "Space (px)", sp.SpaceAmount, SpaceNode.MinSpace, SpaceNode.MaxSpace,
                "Exact size of the gap in pixels (0-800). You can also drag the grip on the placeholder's edge.",
                v => sp.SpaceAmount = v); // setter clamps
        }

        // Re-inlines a type's OWN custom editor (whole, or a single [SasquatchEmbed] slice). Mirrors the designer's
        // slice dropdown + guidance; resolves the genuine editor from the owning target's type.
        private void BuildEmbeddedEditorPanel(VisualElement root, EmbeddedEditorNode en)
        {
            Type targetType = OwningTarget != null ? OwningTarget.GetType() : null;
            Type genuineType = targetType != null ? SasquatchGeneratedEditors.GetGenuinePerTypeEditor(targetType) : null;

            if (genuineType == null)
            {
                root.Add(new HelpBox(
                    targetType == null
                        ? "Select a target type first. This node re-inlines a type's OWN custom editor."
                        : $"'{targetType.Name}' has no [CustomEditor], so nothing renders here. This node re-inlines a type's own editor (whole, or a [SasquatchEmbed] slice).",
                    HelpBoxMessageType.Info));
                return;
            }

            var slices = EmbeddedEditorHost.DiscoverSlices(genuineType);
            var choices = new List<string> { "Entire Editor" };
            foreach (var s in slices) choices.Add(s.display);

            int current = 0;
            if (!string.IsNullOrEmpty(en.SliceMethodName))
            {
                int found = slices.FindIndex(s => s.method == en.SliceMethodName);
                current = found >= 0 ? found + 1 : 0;
            }

            var sliceField = new DropdownField("Show", choices, current);
            sliceField.tooltip = "Embed the whole editor, or just one [SasquatchEmbed]-tagged slice of it.";
            sliceField.RegisterValueChangedCallback(evt => Commit(() =>
            {
                int idx = choices.IndexOf(evt.newValue);
                en.SliceMethodName = (idx <= 0 || idx - 1 >= slices.Count) ? "" : slices[idx - 1].method;
            }));
            root.Add(sliceField);

            bool takingOver = LayoutRegistry.IsForceTakeoverOwn(targetType);
            root.Add(new HelpBox(
                takingOver
                    ? $"Re-inlines {genuineType.Name}. Force-takeover is ON for this type, so this draws in place of its own editor."
                    : $"Re-inlines {genuineType.Name}. Turn ON force-takeover to displace this type's editor, then re-embed just the slice you want.",
                HelpBoxMessageType.Info));
        }

        // C# property suggestions for the Property Name field — the target type's user-declared readable properties
        // (Unity base-class noise filtered by PropertyInvoker), sorted by name, with the get/set shape as detail.
        // Mirrors the designer's GetReadablePropertySuggestions (which is private to the window).
        private static System.Collections.Generic.IEnumerable<AutocompleteTextField.Suggestion> GetReadablePropertySuggestions(Type type)
        {
            if (type == null) return Array.Empty<AutocompleteTextField.Suggestion>();
            return PropertyInvoker.GetReadableProperties(type)
                .OrderBy(p => p.Name)
                .Select(p => new AutocompleteTextField.Suggestion(
                    p.Name, p.PropertyType.Name + (p.CanWrite ? " { get; set; }" : " { get; }")));
        }

        private string DefaultScriptHeaderName()
        {
            MonoScript script = OwningSerializedObject?.FindProperty("m_Script")?.objectReferenceValue as MonoScript;
            string name = script != null ? script.GetClass()?.Name : null;
            if (string.IsNullOrEmpty(name)) name = OwningTarget != null ? OwningTarget.GetType().Name : "Script";
            return name;
        }

        private void BuildFooter(VisualElement root)
        {
            // Wrapped in a container so MeasureAndResize can read the footer's natural height as fixed chrome.
            var footer = new VisualElement();
            _footerRow = footer;

            var separator = new VisualElement();
            separator.style.height = 1;
            separator.style.backgroundColor = DesignerPalette.BorderMid;
            separator.style.marginTop = 6;
            separator.style.marginBottom = 4;
            footer.Add(separator);

            var openButton = new Button(() =>
            {
                var window = SasquatchLayoutDesignerWindow.OpenWindowForType(OwningTarget.GetType(), OwningTarget);
                if (window != null && _indexPath != null) window.SelectNodeByIndexPath(_indexPath);
                Close();
            });
            var linkColor = DesignerPalette.PopupLink;
            openButton.style.flexDirection = FlexDirection.Row;
            openButton.style.alignItems = Align.Center;
            openButton.Add(new Label("Open in Designer")
            {
                pickingMode = PickingMode.Ignore,
                style = { color = linkColor, marginRight = 3 }
            });
            openButton.Add(SasquatchIcons.Create(SasquatchIcons.Ids.ExternalLink, 10, linkColor));
            openButton.tooltip = "Open the full Sasquatch Layout Designer with this node selected.";
            openButton.style.backgroundColor = Color.clear;
            openButton.style.borderLeftWidth = 0;
            openButton.style.borderRightWidth = 0;
            openButton.style.borderTopWidth = 0;
            openButton.style.borderBottomWidth = 0;
            openButton.style.alignSelf = Align.FlexEnd;
            footer.Add(openButton);

            root.Add(footer);
        }

        // ---- Field-type resolution (for widget-kind filtering) ----

        // Resolves the C# type of the field this binding points at, honoring the scoping along the
        // node's index path. Delegates to the shared LayoutFieldTypeResolver (same logic the designer
        // uses). Returns null when anything is unresolvable — the caller then offers the unfiltered list.
        private Type ResolveBoundFieldType(BindingNode bn)
        {
            // No empty-path early-out: an ELEMENT-TEMPLATE binding has an empty path on purpose and
            // resolves to the collection's element type, which is exactly the type the widget list must be
            // filtered against. The resolver returns null for a genuinely unbound root-scope node.
            if (OwningTarget == null || _indexPath == null || bn == null) return null;
            return LayoutFieldTypeResolver.ResolveBoundFieldType(OwningTarget.GetType(), OwningRoot, _indexPath, bn);
        }

        // Scope-aware FieldInfo companion to ResolveBoundFieldType — needed to read the field's attributes
        // (the custom-drawer notice). Mirrors the designer's ResolveSelectedNodeFieldInfo.
        private System.Reflection.FieldInfo ResolveBoundFieldInfo(BindingNode bn)
        {
            if (OwningTarget == null || _indexPath == null || bn == null) return null;
            return LayoutFieldTypeResolver.ResolveBoundFieldInfo(OwningTarget.GetType(), OwningRoot, _indexPath, bn, out _);
        }

    }
}
