using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The hover / selection source preview for a LIST POPUP. Two placements, in preference order — the same
    /// pair, and for the same reasons, as <see cref="QuickEditPopup"/>'s peek:
    ///
    ///   1. OVERLAY in the HOST window's panel (preferred). An absolutely-positioned card parented to the
    ///      rootVisualElement of the window the popup was opened FROM (the inspector, or the designer), hung
    ///      off the popup's bottom edge. This is what lets the code be WIDER than the popup: the card is sized
    ///      from the snippet, not from the trigger control the popup inherited its width from. No second native
    ///      window is involved, so nothing steals focus and the popup can't close itself mid-peek.
    ///
    ///   2. DOCKED CARD inside the popup, over its list (fallback). Used when there is no host panel to hang
    ///      an overlay on (a popup built headless / under test) or when the card fits neither below nor above
    ///      the popup inside that panel. It docks against the top or bottom edge — whichever is away from the
    ///      row in question — and is capped at the gap between that edge and the row, so it can never cover
    ///      what you are pointing at.
    ///
    /// Usage: build the popup's list inside <see cref="CreateListArea"/> instead of directly in the root, pass
    /// the anchor element and a getter for the popup window's screen rect so the overlay can place itself, then
    /// call <see cref="Schedule"/> whenever the selection or hover moves and <see cref="Hide"/> on close.
    /// </summary>
    internal sealed class PopupCodePeek
    {
        internal const long DelayMs = 350;          // matches AutocompleteTextField's hover delay
        private const float MaxHeightFraction = 0.55f;
        private const float RowGap = 4f;            // breathing room between a docked card and its row
        private const float MinOverlayWidth = 160f; // narrower than this the overlay can't show readable code

        private readonly VisualElement _host;
        private readonly VisualElement _anchor;
        private readonly Func<Rect> _popupScreenRect;

        private VisualElement _pane;                // placement 2 — docked inside the popup
        private VisualElement _overlay;             // placement 1 — in the host window's panel
        private EditorWindow _overlayHost;          // cached window owning _anchor's panel
        private float _overlayWidth;                // grow-only within one peek session
        private IVisualElementScheduledItem _timer;

        /// <summary>
        /// Wraps <paramref name="listArea"/> — the element the FALLBACK card docks into. Everything the popup
        /// scrolls should live inside it, and nothing else should: the card's edges are ITS edges, which is
        /// what keeps a docked peek off the popup's title and search row.
        /// <paramref name="anchor"/> is the control that opened the popup; it is only ever used to find the
        /// window whose panel the overlay hangs in. <paramref name="popupScreenRect"/> reports the popup
        /// window's live screen rect (i.e. <c>editorWindow.position</c>) — the edge the overlay attaches to.
        /// Omit either and the peek simply uses the docked placement.
        /// </summary>
        internal PopupCodePeek(VisualElement listArea, VisualElement anchor = null, Func<Rect> popupScreenRect = null)
        {
            _host = listArea;
            _anchor = anchor;
            _popupScreenRect = popupScreenRect;
        }

        /// <summary>Builds the list area a popup should put its ScrollView in.</summary>
        internal static VisualElement CreateListArea()
        {
            var area = new VisualElement();
            area.style.flexGrow = 1;
            area.style.overflow = Overflow.Hidden;
            return area;
        }

        /// <summary>The live card — the overlay when one is placed, otherwise the docked one — or null when
        /// nothing is showing (test seam).</summary>
        internal VisualElement Pane => _overlay ?? _pane;

        /// <summary>The overlay card specifically, or null when the peek fell back to docking (test seam).</summary>
        internal VisualElement Overlay => _overlay;

        /// <summary>The element a docked card docks into (test seam).</summary>
        internal VisualElement Host => _host;

        /// <summary>Whether a dwell timer is currently waiting to show a card (test seam) — i.e. something is
        /// selected or hovered that the peek intends to preview.</summary>
        internal bool Armed => _timer != null;

        /// <summary>
        /// Arms the dwell timer for <paramref name="row"/> — the row under the cursor OR the one the keyboard
        /// just selected; the peek makes no distinction. The snippet is resolved LATE, when the timer fires, so
        /// arrowing past a row never pays for a file read.
        /// </summary>
        internal void Schedule(Func<string> snippet, VisualElement row)
        {
            _timer?.Pause();
            if (_host == null || snippet == null) return;
            _timer = _host.schedule.Execute(() => Show(snippet(), row)).StartingIn(DelayMs);
        }

        /// <summary>Shows <paramref name="rich"/> (highlighted source) for <paramref name="row"/>: as an overlay
        /// under the popup when the host panel can take one, otherwise docked clear of the row itself.</summary>
        internal void Show(string rich, VisualElement row)
        {
            if (string.IsNullOrEmpty(rich)) { Hide(); return; }

            if (TryShowOverlay(rich)) { HideDocked(); return; }

            HideOverlay();
            ShowDocked(rich, row);
        }

        /// <summary>Retires both cards and any armed timer. Safe to call when nothing is showing.</summary>
        internal void Hide()
        {
            _timer?.Pause();
            _timer = null;
            HideOverlay();
            HideDocked();
        }

        // ---- Placement 1: overlay in the host window's panel ------------------------------------------

        /// <summary>
        /// The window that owns the anchor's panel — i.e. the inspector or designer this popup was opened
        /// from. Cached because placement re-runs per peek and the scan walks every EditorWindow; a destroyed
        /// window compares null, so the cache self-invalidates.
        /// </summary>
        private EditorWindow ResolveOverlayHost()
        {
            if (_overlayHost != null && _overlayHost.rootVisualElement?.panel != null) return _overlayHost;
            _overlayHost = null;
            var panel = _anchor?.panel;
            if (panel == null) return null;
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.rootVisualElement?.panel == panel) { _overlayHost = w; break; }
            }
            return _overlayHost;
        }

        private bool TryShowOverlay(string rich)
        {
            if (_anchor == null || _popupScreenRect == null) return false;

            var host = ResolveOverlayHost();
            var hostRoot = host?.rootVisualElement;
            if (hostRoot == null || hostRoot.panel == null) return false;

            var hostRect = host.position;               // screen space — same units as the popup's rect
            var popupRect = _popupScreenRect();
            if (hostRect.width <= 0f || hostRect.height <= 0f || popupRect.width <= 0f) return false;

            // Capped by what the host panel can actually show: unlike a native window, an overlay is clipped by
            // its host, so asking for more than the panel holds would only clip.
            float maxWidth = Mathf.Min(CodePeekMetrics.MaxWidth, hostRect.width - 2f * CodePeekMetrics.EdgeMargin);
            if (maxWidth < MinOverlayWidth) return false;   // panel too narrow for readable code — fall back

            // Never narrower than the popup it hangs off (the two read as one connected shape), never wider
            // than the snippet needs. Grow-only within one peek session, so moving between a long and a short
            // member doesn't shrink the card out from under the cursor mid-navigation; Hide resets it.
            float minWidth = Mathf.Min(popupRect.width, maxWidth);
            _overlayWidth = Mathf.Max(_overlayWidth,
                Mathf.Min(CodePeekMetrics.ContentWidth(rich, minWidth, CodePeekMetrics.MaxWidth), maxWidth));

            float cardWidth = Mathf.Min(_overlayWidth, maxWidth);
            float cardHeight = CodePeekMetrics.CardHeight(rich);
            if (!TryPlaceOverlay(hostRect, hostRoot, popupRect, cardWidth, cardHeight, out var placed, out bool flipped))
                return false;

            if (_overlay == null)
            {
                _overlay = BuildCard();
                _overlay.style.position = Position.Absolute;
                // Position (not Ignore) so a stray click lands on the card instead of punching through to
                // whatever inspector field it happens to cover.
                _overlay.pickingMode = PickingMode.Position;
            }
            if (_overlay.parent != hostRoot)
            {
                _overlay.RemoveFromHierarchy();
                hostRoot.Add(_overlay);
            }
            // Draw over anything the host added after us. Safe despite BringToFront reordering layout children:
            // an absolutely-positioned element is out of flow, so its sibling index has no layout effect.
            _overlay.BringToFront();

            SetCardText(_overlay, rich);
            ApplyOverlayRect(placed, flipped);

            // The hover / keypress driving the peek happens in the POPUP's panel, so the host window — unfocused
            // and otherwise idle — has no reason to redraw on its own.
            host.Repaint();
            return true;
        }

        /// <summary>
        /// The overlay's rect, in coordinates relative to the HOST ROOT's content box — which is what
        /// style.left/top on an absolutely-positioned child resolve against, and which is NOT the window's
        /// screen origin: a docked window's tab strip sits inside the panel, so hostRoot itself starts around
        /// y=24. Screen-space math must subtract that offset or the card hangs a tab-height too low. Read live,
        /// because it differs between docked and floating windows.
        ///
        /// The card hangs off the popup's bottom edge, left-aligned with it, nudged left when it would overflow
        /// the host's right edge — so a wide snippet uses the panel's full width instead of clipping. Flips
        /// above the popup when there's no room below; fails when neither fits, handing the peek to the docked
        /// fallback.
        /// </summary>
        private static bool TryPlaceOverlay(Rect hostRect, VisualElement hostRoot, Rect popupRect,
            float cardWidth, float cardHeight, out Rect placed, out bool flippedAbove)
        {
            placed = default;
            flippedAbove = false;
            if (cardWidth <= 0f || cardHeight <= 0f || hostRoot == null) return false;

            var rootBox = hostRoot.worldBound;   // NaN until the host has laid out at least once
            float offX = float.IsNaN(rootBox.x) ? 0f : rootBox.x;
            float offY = float.IsNaN(rootBox.y) ? 0f : rootBox.y;
            float boundsW = float.IsNaN(rootBox.width) || rootBox.width <= 0f ? hostRect.width : rootBox.width;
            float boundsH = float.IsNaN(rootBox.height) || rootBox.height <= 0f ? hostRect.height : rootBox.height;

            float maxLeft = Mathf.Max(CodePeekMetrics.EdgeMargin, boundsW - CodePeekMetrics.EdgeMargin - cardWidth);
            float left = Mathf.Clamp(popupRect.x - hostRect.x - offX, CodePeekMetrics.EdgeMargin, maxLeft);

            // Touching, not floating: the card's top border laps the popup's bottom border by exactly
            // JoinOverlap, so the seam reads as one shared hairline rather than two stacked 1px borders.
            float top = popupRect.yMax - CodePeekMetrics.JoinOverlap - hostRect.y - offY;
            if (top + cardHeight > boundsH - CodePeekMetrics.EdgeMargin)
            {
                top = popupRect.y - cardHeight + CodePeekMetrics.JoinOverlap - hostRect.y - offY; // flip above
                flippedAbove = true;
            }
            if (top < 0f) return false;

            placed = new Rect(left, top, cardWidth, cardHeight);
            return true;
        }

        /// <summary>Squares off the two corners that meet the popup so the join is seamless; the far edge keeps
        /// its radius.</summary>
        private void ApplyOverlayRect(Rect r, bool flippedAbove)
        {
            _overlay.style.left = r.x;
            _overlay.style.top = r.y;
            _overlay.style.width = r.width;
            _overlay.style.height = r.height;
            _overlay.style.borderTopLeftRadius = flippedAbove ? 4 : 0;
            _overlay.style.borderTopRightRadius = flippedAbove ? 4 : 0;
            _overlay.style.borderBottomLeftRadius = flippedAbove ? 0 : 4;
            _overlay.style.borderBottomRightRadius = flippedAbove ? 0 : 4;
        }

        private void HideOverlay()
        {
            if (_overlay != null)
            {
                bool wasLive = _overlay.parent != null;
                _overlay.RemoveFromHierarchy();
                _overlay = null;
                // The card lives in ANOTHER window's tree, and that window is unfocused — without this it stays
                // painted until something else happens to dirty the inspector.
                if (wasLive && _overlayHost != null) _overlayHost.Repaint();
            }
            _overlayWidth = 0f;
        }

        // ---- Placement 2: card docked inside the popup (fallback) -------------------------------------

        private void ShowDocked(string rich, VisualElement row)
        {
            HideDocked();
            if (_host == null) return;

            _pane = BuildCard();
            _pane.pickingMode = PickingMode.Ignore;     // rows underneath keep their hover; the card can't flicker itself off
            _pane.style.position = Position.Absolute;
            SetCardText(_pane, rich);

            // Spans the list area, so the snippet gets the popup's full width without any placement maths.
            _pane.style.left = 0f;
            _pane.style.right = 0f;

            // Dock away from the row in question, then cap the card at the gap left between that edge and the
            // row itself. Docking alone isn't enough: a row just past the middle is inside the half the card
            // grows into, so a tall snippet would still cover the very row being pointed at.
            var hostBound = _host.worldBound;
            float cap = hostBound.height * MaxHeightFraction;
            if (row != null)
            {
                var rowBound = row.worldBound;
                bool docksTop = DocksTop(rowBound, hostBound);
                if (docksTop) _pane.style.top = 0f;
                else _pane.style.bottom = 0f;
                cap = docksTop ? rowBound.yMin - hostBound.yMin - RowGap
                               : hostBound.yMax - rowBound.yMax - RowGap;
            }
            else _pane.style.bottom = 0f;
            if (cap > 40f) _pane.style.maxHeight = cap;

            _host.Add(_pane);
            _pane.BringToFront();   // absolute + out of flow, so this can't reorder the list
        }

        /// <summary>
        /// Which edge of the list a docked card sits against: away from the row in question, so it never
        /// covers the row you are reading. Past the list's middle it flips to the top edge.
        /// </summary>
        internal static bool DocksTop(Rect rowWorld, Rect hostWorld)
            => rowWorld.center.y > hostWorld.center.y;

        private void HideDocked()
        {
            if (_pane == null) return;
            _pane.RemoveFromHierarchy();
            _pane = null;
        }

        // ---- shared chrome ---------------------------------------------------------------------------

        private const string CodeName = "sasq-popup-peek-code";

        /// <summary>The bordered code card itself, shared by both placements so the peek looks identical
        /// whichever one wins.</summary>
        private static VisualElement BuildCard()
        {
            var card = new VisualElement();
            card.style.backgroundColor = DesignerPalette.CodeBlockBg;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = DesignerPalette.BorderMid;
            card.style.borderBottomColor = DesignerPalette.BorderMid;
            card.style.borderLeftColor = DesignerPalette.BorderMid;
            card.style.borderRightColor = DesignerPalette.BorderMid;
            card.style.borderTopLeftRadius = 4;
            card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.overflow = Overflow.Hidden;   // long lines (NoWrap) clip at the card width

            var code = new Label { enableRichText = true, pickingMode = PickingMode.Ignore, name = CodeName };
            code.style.color = DesignerPalette.CodeText;
            code.style.whiteSpace = WhiteSpace.NoWrap;
            code.style.fontSize = 11;
            CodeHighlighter.ApplyCodeFont(code);
            card.Add(code);
            return card;
        }

        /// <summary>Updates a live card's text in place — no dismiss-then-show, so moving between rows never
        /// flickers.</summary>
        private static void SetCardText(VisualElement card, string rich)
        {
            var code = card?.Q<Label>(CodeName);
            if (code != null) code.text = rich;
        }
    }
}
