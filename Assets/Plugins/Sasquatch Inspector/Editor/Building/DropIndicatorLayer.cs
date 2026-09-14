using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Draws the drag drop indicators: a parent outline around the receiving container, full-rect landing
    /// cells at the target spots (one per held card; a tinted outline means nest/append), plus the
    /// insertion line(s) used by the surfaces that reorder rather than fill (hierarchy, collection
    /// reorder). Lives on the window overlay so nothing clips it.
    /// </summary>
    public class DropIndicatorLayer
    {
        private readonly VisualElement _overlay;
        private readonly VisualElement _outline;
        // Thin secondary outline: the container/frame AROUND a cell/pane target. Sits under the
        // main outline so the bold "lands here" ring always draws over the "inside this" whisper.
        private readonly VisualElement _contextOutline;
        // Grow-on-demand pool: each entry is a line with its bead cap inside. Never shrinks — a
        // drag's card count is small and stable.
        private readonly List<VisualElement> _lines = new();
        // Phantom "+" cells: the next grid/columns row that will exist if a new-row drop lands.
        private readonly List<VisualElement> _phantomCells = new();
        // Landing cells: one bold full-rect per held card at the exact spot it will occupy.
        private readonly List<VisualElement> _landingCells = new();
        private readonly List<VisualElement> _displacedCells = new();
        private VisualElement _escapeRing;

        /// <summary>Creates the indicator layer on <paramref name="overlay"/>.</summary>
        public DropIndicatorLayer(VisualElement overlay)
        {
            _overlay = overlay;

            _contextOutline = new VisualElement { pickingMode = PickingMode.Ignore };
            _contextOutline.AddToClassList("drop-parent-outline");
            _contextOutline.AddToClassList("drop-parent-outline--thin");
            _overlay.Add(_contextOutline);

            _outline = new VisualElement { pickingMode = PickingMode.Ignore };
            _outline.AddToClassList("drop-parent-outline");
            _overlay.Add(_outline);

            EnsureLines(1);
            HideAll();
        }

        /// <summary>Shows a single insertion line at <paramref name="world"/>.</summary>
        public void ShowLine(Rect world, bool vertical) => ShowLines(world, vertical, null);

        // Line 0 at `world`; line i offset extraOffsets[i-1] further down (same x/width — the extra
        // lines only ever apply to vertical stacks).
        /// <summary>Shows insertion lines at <paramref name="world"/> plus <paramref name="extraOffsets"/> (one per held card).</summary>
        public void ShowLines(Rect world, bool vertical, float[] extraOffsets)
        {
            if (world == Rect.zero) { HideLine(); return; }

            int count = 1 + (extraOffsets?.Length ?? 0);
            EnsureLines(count);

            Vector2 p = _overlay.WorldToLocal(world.position);
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (i >= count) { line.style.display = DisplayStyle.None; continue; }

                line.style.display = DisplayStyle.Flex;
                line.style.left = p.x;
                line.style.top = p.y + (i > 0 ? extraOffsets[i - 1] : 0f);
                line.style.width = world.width;
                line.style.height = world.height;

                // One bead at the line's start (left for horizontal, top for vertical).
                var cap = line[0];
                cap.style.left = vertical ? -2.5f : -3.5f;
                cap.style.top = vertical ? -3.5f : -2.5f;
            }
        }

        /// <summary>Shows the receiving-parent outline at <paramref name="world"/> (tinted means nest/append;
        /// <paramref name="radius"/> &gt; 0 rounds it to hug shaped containers like Circle/Pill frames).</summary>
        public void ShowOutline(Rect world, bool tint, bool thin = false, float radius = 0f)
        {
            if (world == Rect.zero) { HideOutline(); return; }
            Vector2 p = _overlay.WorldToLocal(world.position);
            _outline.style.display = DisplayStyle.Flex;
            _outline.style.left = p.x;
            _outline.style.top = p.y;
            _outline.style.width = world.width;
            _outline.style.height = world.height;
            _outline.EnableInClassList("drop-parent-outline--tint", tint);
            _outline.EnableInClassList("drop-parent-outline--thin", thin);
            ApplyCornerRadius(_outline, radius);
        }

        /// <summary>Shows the thin context outline (the container/frame around a cell/pane target).</summary>
        public void ShowContextOutline(Rect world, float radius = 0f)
        {
            if (world == Rect.zero) { HideContextOutline(); return; }
            Vector2 p = _overlay.WorldToLocal(world.position);
            _contextOutline.style.display = DisplayStyle.Flex;
            _contextOutline.style.left = p.x;
            _contextOutline.style.top = p.y;
            _contextOutline.style.width = world.width;
            _contextOutline.style.height = world.height;
            ApplyCornerRadius(_contextOutline, radius);
        }

        // Phantom "+" cells for a new-row drop: one cell per column of the row that will exist.
        // With no landing cells the first one is highlighted (that's where the item lands — appends
        // fill left-to-right); when landing cells are shown they carry the "lands here" look, so
        // every phantom stays in its whisper state.
        /// <summary>Shows the phantom "+" cells of the row a drop would create (<paramref name="activeFirst"/> highlights the landing cell).</summary>
        public void ShowPhantomCells(Rect[] world, bool activeFirst = true)
        {
            if (world == null || world.Length == 0) { HidePhantomCells(); return; }
            EnsurePhantomCells(world.Length);
            for (int i = 0; i < _phantomCells.Count; i++)
            {
                var cell = _phantomCells[i];
                if (i >= world.Length) { cell.style.display = DisplayStyle.None; continue; }
                Vector2 p = _overlay.WorldToLocal(world[i].position);
                cell.style.display = DisplayStyle.Flex;
                cell.style.left = p.x;
                cell.style.top = p.y;
                cell.style.width = world[i].width;
                cell.style.height = world[i].height;
                cell.EnableInClassList("drop-phantom-cell--active", activeFirst && i == 0);
            }
        }

        // Landing cells: the full-rect "lands HERE" preview — one bold "+" cell per held card at the
        // exact rect that card will occupy (cell-fill targets, predicted columns, stacked inserts).
        /// <summary>Shows the bold landing-cell rects. <paramref name="counts"/> (optional, parallel) labels a
        /// rect that stands for several held cards — "＋3" — when a big multi-drag has been collapsed. A count
        /// of ZERO draws no glyph at all: a pure REORDER is not adding anything, and a "＋" floating over the
        /// slot a column is moving into says the wrong word (it also lands on top of that column's own header,
        /// which the full-height rect deliberately encloses).</summary>
        public void ShowLandingCells(Rect[] world, int[] counts = null)
        {
            if (world == null || world.Length == 0) { HideLandingCells(); return; }
            EnsureLandingCells(world.Length);
            for (int i = 0; i < _landingCells.Count; i++)
            {
                var cell = _landingCells[i];
                if (i >= world.Length) { cell.style.display = DisplayStyle.None; continue; }
                Vector2 p = _overlay.WorldToLocal(world[i].position);
                cell.style.display = DisplayStyle.Flex;
                cell.style.left = p.x;
                cell.style.top = p.y;
                cell.style.width = world[i].width;
                cell.style.height = world[i].height;
                // A collapsed rect must SAY how many it stands for, or it reads as one card landing in a
                // suspiciously large cell.
                int n = (counts != null && i < counts.Length) ? counts[i] : 1;
                var glyph = cell.Q<Label>(className: "drop-phantom-plus");
                if (glyph != null) glyph.text = n <= 0 ? "" : n > 1 ? $"＋{n}" : "＋";
            }
        }

        // Displaced cells: where EXISTING cards get pushed when a drop reflows a full tracked container
        // (a 4th card into a 3-track Rows starts a second column and carries a card into it). The landing
        // cells say where the held card goes; these say what MOVES because of it — the half a reflow
        // preview cannot leave out without becoming a surprise on release.
        /// <summary>Shows the rects existing cards are pushed to by a reflowing drop.</summary>
        public void ShowDisplacedCells(Rect[] world)
        {
            if (world == null || world.Length == 0) { HideDisplacedCells(); return; }
            EnsureDisplacedCells(world.Length);
            for (int i = 0; i < _displacedCells.Count; i++)
            {
                var cell = _displacedCells[i];
                if (i >= world.Length) { cell.style.display = DisplayStyle.None; continue; }
                Vector2 p = _overlay.WorldToLocal(world[i].position);
                cell.style.display = DisplayStyle.Flex;
                cell.style.left = p.x;
                cell.style.top = p.y;
                cell.style.width = world[i].width;
                cell.style.height = world[i].height;
            }
        }

        /// <summary>Hides the displaced-cell rects.</summary>
        public void HideDisplacedCells()
        {
            foreach (var cell in _displacedCells) cell.style.display = DisplayStyle.None;
        }

        private void EnsureDisplacedCells(int count)
        {
            while (_displacedCells.Count < count)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("drop-displaced-cell");
                var arrow = new Label("→") { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("drop-displaced-arrow");
                cell.Add(arrow);
                cell.style.display = DisplayStyle.None;
                // Area fills draw UNDER the outline + lines, like the phantom/landing cells.
                _overlay.Insert(_overlay.IndexOf(_outline), cell);
                _displacedCells.Add(cell);
            }
        }

        // The container a drop is passing OVER to land beside it. Neutral by design — see the USS note.
        /// <summary>Rings the container an escaping drop is being placed beside (Rect.zero hides it).</summary>
        public void ShowEscapeRing(Rect world, float radius = 0f)
        {
            if (world == Rect.zero) { HideEscapeRing(); return; }
            EnsureEscapeRing();
            Vector2 p = _overlay.WorldToLocal(world.position);
            _escapeRing.style.display = DisplayStyle.Flex;
            _escapeRing.style.left = p.x;
            _escapeRing.style.top = p.y;
            _escapeRing.style.width = world.width;
            _escapeRing.style.height = world.height;
            ApplyCornerRadius(_escapeRing, radius);
        }

        /// <summary>Hides the escape ring.</summary>
        public void HideEscapeRing()
        {
            if (_escapeRing != null) _escapeRing.style.display = DisplayStyle.None;
        }

        private void EnsureEscapeRing()
        {
            if (_escapeRing != null) return;
            _escapeRing = new VisualElement { pickingMode = PickingMode.Ignore };
            _escapeRing.AddToClassList("drop-escape-ring");
            _escapeRing.style.display = DisplayStyle.None;
            // Under the outline + lines, like the other area fills.
            _overlay.Insert(_overlay.IndexOf(_outline), _escapeRing);
        }

        /// <summary>Hides the insertion line(s).</summary>
        public void HideLine()
        {
            foreach (var line in _lines) line.style.display = DisplayStyle.None;
        }

        /// <summary>Hides the parent outline.</summary>
        public void HideOutline() => _outline.style.display = DisplayStyle.None;

        /// <summary>Hides the thin context outline.</summary>
        public void HideContextOutline() => _contextOutline.style.display = DisplayStyle.None;

        /// <summary>Hides the phantom "+" cells.</summary>
        public void HidePhantomCells()
        {
            foreach (var cell in _phantomCells) cell.style.display = DisplayStyle.None;
        }

        /// <summary>Hides the landing-cell rects.</summary>
        public void HideLandingCells()
        {
            foreach (var cell in _landingCells) cell.style.display = DisplayStyle.None;
        }

        /// <summary>Hides every indicator (lines, outlines, phantom + landing cells).</summary>
        public void HideAll()
        {
            HideLine();
            HideOutline();
            HideContextOutline();
            HidePhantomCells();
            HideLandingCells();
            HideDisplacedCells();
            HideEscapeRing();
        }

        // Radius 0 clears back to the USS default so pooled elements never keep a stale shape.
        private static void ApplyCornerRadius(VisualElement el, float radius)
        {
            StyleLength r = radius > 0.5f ? radius : (StyleLength)StyleKeyword.Null;
            el.style.borderTopLeftRadius = r;
            el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r;
            el.style.borderBottomRightRadius = r;
        }

        private void EnsureLines(int count)
        {
            while (_lines.Count < count)
            {
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.AddToClassList("drop-indicator-line");
                var cap = new VisualElement { pickingMode = PickingMode.Ignore };
                cap.AddToClassList("drop-indicator-cap");
                line.Add(cap);
                line.style.display = DisplayStyle.None;
                // Keep all lines together just above the outline — a line pooled mid-drag must not
                // land above the ghost (which sits at the overlay's end).
                int idx = _lines.Count > 0
                    ? _overlay.IndexOf(_lines[_lines.Count - 1]) + 1
                    : _overlay.IndexOf(_outline) + 1;
                _overlay.Insert(idx, line);
                _lines.Add(line);
            }
        }

        private void EnsurePhantomCells(int count)
        {
            while (_phantomCells.Count < count)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("drop-phantom-cell");
                var plus = new Label("＋") { pickingMode = PickingMode.Ignore };
                plus.AddToClassList("drop-phantom-plus");
                cell.Add(plus);
                cell.style.display = DisplayStyle.None;
                // Area fills draw UNDER the outline + lines (insert just before the main outline).
                _overlay.Insert(_overlay.IndexOf(_outline), cell);
                _phantomCells.Add(cell);
            }
        }

        private void EnsureLandingCells(int count)
        {
            while (_landingCells.Count < count)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.AddToClassList("drop-landing-cell");
                var plus = new Label("＋") { pickingMode = PickingMode.Ignore };
                plus.AddToClassList("drop-phantom-plus");
                cell.Add(plus);
                cell.style.display = DisplayStyle.None;
                // Above the phantom cells + main outline (it's the primary "lands here" signal),
                // but below the pooled lines/ghost, which sit after the outline.
                _overlay.Insert(_overlay.IndexOf(_outline) + 1, cell);
                _landingCells.Add(cell);
            }
        }
    }
}
