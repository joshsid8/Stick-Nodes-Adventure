using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Duplicate a widget: <c>Ctrl</c>/<c>Cmd</c>+<c>D</c>, or right-click ▸ <em>Duplicate</em> in the hierarchy
    /// or on the canvas. The copy lands directly BELOW the original in the same container and takes the
    /// selection, so a duplicate reads exactly like it does in Unity's own Hierarchy — press it twice and you
    /// have three, each one row down.
    ///
    /// The copy itself is <see cref="LayoutPresetApplier.DeepCloneNode"/> — the same serialization vehicle the
    /// preset system clones bindings through, so a container duplicates its ENTIRE subtree and every serialized
    /// widget option comes along without this file knowing a single field name. A hand-rolled copy would drift
    /// the first time a widget gained an option, and silently: the copy would just quietly lose it.
    ///
    /// <see cref="CanDuplicateNode(LayoutNode, out string)"/> is the SOLE gate. Both menus and both key routes
    /// ask it, so a node that can't be copied is greyed out and explains itself in one place rather than four.
    /// It refuses exactly four things: the root (it IS the layout), the Script Header (a layout renders one),
    /// a collection's item template (a list has exactly one), and a split frame's panes (structural — a
    /// DiagonalSplit always has two). Everything else, bindings and whole containers alike, copies.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        // One physical Ctrl+D can reach a focused EditorWindow twice — as a KeyDown AND as Unity's own
        // "Duplicate" command (Edit ▸ Duplicate is bound to the same chord). Delete survives that
        // double-delivery because deleting an already-deleted selection is a no-op; duplicating twice would
        // quietly make two copies.
        //
        // The guard is deliberately ASYMMETRIC rather than a debounce on both routes. The raw key is the
        // route that actually fires here (the same one Delete and F2 ride), and it is completely unguarded —
        // hammer the chord and every press duplicates, with no throttle to explain. Only the COMMAND route
        // asks "did the key just do this?", because a command echoing a press we already handled is the one
        // way the same gesture arrives twice. A Unity version that routes ONLY the command never sets the
        // stamp, so there the command always acts: no version can end up with a dead Ctrl+D.
        //
        // The window is generous on purpose — duplicating rebuilds the hierarchy, canvas and properties
        // panels, which on a large layout can outlast a tight debounce and let the echo through. (A 50ms
        // window did exactly that: it held in isolation and failed under load.)
        private const double DuplicateCommandEchoSeconds = 0.5;
        private double _lastDuplicateKeyAt;

        // ---- The gate ------------------------------------------------------------------------------------

        /// <summary>
        /// Whether <paramref name="node"/> can be duplicated, and if not, the sentence to show the user.
        /// Static so the rules are testable without a window; <paramref name="parent"/> is the node's owning
        /// container (null when it isn't a child of one — i.e. a collection's item template).
        /// </summary>
        internal static bool CanDuplicateNode(LayoutNode node, LayoutNode root, ContainerNode parent, out string reason)
        {
            reason = null;
            if (node == null)
            {
                reason = "Select a widget to duplicate.";
                return false;
            }
            if (root != null && node == root)
            {
                reason = "The root can't be duplicated — duplicate the widgets inside it.";
                return false;
            }
            // The header is a singleton by construction: it's pinned to the top, sorting treats it as
            // position-locked, and the inspector draws one. A second one is a layout that can't be reasoned about.
            if (node is ScriptHeaderNode)
            {
                reason = "A layout has just one Script Header.";
                return false;
            }
            // No owning Children list ⇒ a collection's ItemLayout (the only in-tree node held by reference
            // rather than by list). A list has exactly one template, so there's nowhere for a copy to go.
            if (parent?.Children == null || !parent.Children.Contains(node))
            {
                reason = "A template can't be duplicated — a list has exactly one item template, and a "
                       + "dictionary has exactly one Key and one Value cell template.";
                return false;
            }
            // A DiagonalSplit frame is defined by having exactly two panes; a third would have no geometry.
            if (node is ContainerNode && parent.Type == ContainerType.Frame
                && parent.Frame.Shape == FrameShape.DiagonalSplit)
            {
                reason = "A split frame's panes are structural — duplicate the Frame itself.";
                return false;
            }
            return true;
        }

        /// <summary>Instance form of the gate: resolves the node's parent in the working layout.</summary>
        private bool CanDuplicateNode(LayoutNode node, out string reason)
        {
            if (_clonedLayoutAsset?.Root == null)
            {
                reason = "Open a layout first.";
                return false;
            }
            var parent = node != null ? FindParent(node, _clonedLayoutAsset.Root) : null;
            return CanDuplicateNode(node, _clonedLayoutAsset.Root, parent, out reason);
        }

        // ---- The copy ------------------------------------------------------------------------------------

        /// <summary>
        /// Cleans up a fresh clone before it joins the tree. Two things in a layout are OWNED rather than free,
        /// and a second copy of either would be a layout the rest of the system treats as invalid:
        ///
        /// • A <c>[Space]</c>-owned spacer belongs to a field in your code — the sync engine creates it when the
        ///   attribute appears and removes it when it goes away, keyed by <c>OwnerFieldPath</c>. The original
        ///   keeps that claim; the copy is released into a plain designer space you can move, resize and delete
        ///   like any other (rather than a locked twin fighting the original over one attribute).
        /// • A Script Header nested inside a duplicated container would give the layout two.
        /// </summary>
        internal static void SanitizeDuplicate(LayoutNode clone)
        {
            if (clone == null) return;

            if (clone is SpaceNode space) space.OwnerFieldPath = "";

            if (clone is ContainerNode container && container.Children != null)
            {
                container.Children.RemoveAll(c => c is ScriptHeaderNode);
                foreach (var child in container.Children) SanitizeDuplicate(child);
            }
            else if (clone is CollectionNode collection && collection.ItemLayout != null)
            {
                SanitizeDuplicate(collection.ItemLayout);
            }
        }

        /// <summary>
        /// Inserts a deep copy of every node in <paramref name="targets"/> directly after itself. Callers filter
        /// through <see cref="CanDuplicateNode(LayoutNode, out string)"/> first; anything that slips through
        /// (a stale selection whose node has since left the tree) is skipped rather than dropped somewhere odd.
        /// </summary>
        /// <param name="selectCopies">
        /// True for the main selection — the copies take it, so Ctrl+D twice stacks three widgets rather than
        /// copying the original again. False for the Tab Organizer, where the primary selection must stay on the
        /// Tabs container or the organizer navigates away from itself mid-edit.
        /// </param>
        private List<LayoutNode> DuplicateNodes(IReadOnlyList<LayoutNode> targets, bool selectCopies)
        {
            var copies = new List<LayoutNode>();
            if (_clonedLayoutAsset?.Root == null || targets == null || targets.Count == 0) return copies;

            Undo.RecordObject(_clonedLayoutAsset,
                targets.Count > 1 ? "Duplicate Layout Nodes" : "Duplicate Layout Node");

            foreach (var node in targets)
            {
                var parent = node != null ? FindParent(node, _clonedLayoutAsset.Root) : null;
                if (parent?.Children == null) continue;
                int index = parent.Children.IndexOf(node);
                if (index < 0) continue;

                var copy = LayoutPresetApplier.DeepCloneNode(node);
                if (copy == null) continue;
                SanitizeDuplicate(copy);

                // Directly below the original. Each insert is resolved against the CURRENT list, so
                // duplicating several siblings at once leaves every copy paired with its own original.
                parent.Children.Insert(index + 1, copy);
                copies.Add(copy);
            }

            if (copies.Count == 0) return copies;

            if (selectCopies)
            {
                // Land the selection on the copies, primary = the first one (the one that gets pinged).
                SetPrimarySelection(copies[0]);
                if (copies.Count > 1)
                {
                    _selectedNodes.AddRange(copies);
                    _selectedNode = copies[0];
                    _selectionAnchor = copies[0];
                }
                ExpandAncestorsOf(copies[0]); // before the rebuild, so the copy's row renders open
            }
            else
            {
                _organizerSelection.Clear();
                _organizerSelection.AddRange(copies);
            }

            // The ping owns the scroll from here — don't let the rebuild restore the pre-duplicate offset
            // and fight it (the same handshake SelectAndPingNode uses).
            _disableScrollPreservation = selectCopies;
            try
            {
                OnModification();
                RefreshHierarchy();
                RefreshProperties();
                RefreshTokens();   // a duplicated binding re-locks its token in the Bindings panel
                RefreshPreview();
            }
            finally
            {
                _disableScrollPreservation = false;
            }

            // Flash the new copy in both panels — in a long layout that's the difference between "did that
            // work?" and seeing exactly where it landed.
            if (selectCopies) PingNodeInPanels(copies[0]);

            if (copies.Count > 1) ShowToolToast($"Duplicated {copies.Count} widgets.", ToastKind.Success);
            return copies;
        }

        /// <summary>
        /// The Ctrl/Cmd+D action: duplicates the current selection (the Tab Organizer's cards when that's what
        /// is selected, exactly as Delete does). Reports why in a toast when nothing can be copied — silence on
        /// a shortcut reads as "broken" rather than "not that one".
        /// </summary>
        private bool DuplicateSelectedNodes()
        {
            if (_clonedLayoutAsset?.Root == null) return false;

            // Organizer cards first — same precedence as the Delete shortcut, and the same reason: those cards
            // are what the user is looking at, while _selectedNode is the Tabs container hosting them.
            if (_organizerSelection.Count > 0)
            {
                var cards = _organizerSelection.Where(n => CanDuplicateNode(n, out _)).ToList();
                if (cards.Count == 0) return false;
                return DuplicateNodes(cards, selectCopies: false).Count > 0;
            }

            var targets = TopmostSelected().Where(n => CanDuplicateNode(n, out _)).ToList();
            if (targets.Count == 0)
            {
                CanDuplicateNode(_selectedNode, out var reason);
                ShowToolToast(reason ?? "Select a widget to duplicate.", ToastKind.Warning);
                return false;
            }
            return DuplicateNodes(targets, selectCopies: true).Count > 0;
        }

        /// <summary>
        /// The Ctrl/Cmd+D KEY route, shared by both key paths (OnGUI when nothing is focused, the root UITK
        /// handler when a canvas element holds focus). Unguarded: every press duplicates.
        ///
        /// Returns true whenever the designer claims the gesture — INCLUDING when it can't copy the selection,
        /// because letting Ctrl+D fall through would hand it to Unity's Edit ▸ Duplicate, which would quietly
        /// duplicate whatever GameObject or asset happens to be selected in the project.
        /// </summary>
        private bool TryHandleDuplicateShortcut()
        {
            if (!CanRunDuplicateShortcut()) return false;
            _lastDuplicateKeyAt = EditorApplication.timeSinceStartup; // so the command echo below knows
            DuplicateSelectedNodes();
            return true;
        }

        /// <summary>
        /// Unity's own <c>Duplicate</c> command (Edit ▸ Duplicate, same chord), for editor versions or focus
        /// states where the chord reaches the window as a command rather than as a key. Skips the work when the
        /// key route just did it for this same press — see <see cref="DuplicateCommandEchoSeconds"/>.
        /// </summary>
        private bool TryHandleDuplicateCommand()
        {
            if (!CanRunDuplicateShortcut()) return false;
            // Claimed either way: swallowing the command is what stops it falling through to the Hierarchy or
            // Project window, which would duplicate the GameObject or asset selected there.
            if (EditorApplication.timeSinceStartup - _lastDuplicateKeyAt < DuplicateCommandEchoSeconds) return true;
            DuplicateSelectedNodes();
            return true;
        }

        /// <summary>Whether the designer is in a state to answer the shortcut at all.</summary>
        private bool CanRunDuplicateShortcut()
        {
            if (_clonedLayoutAsset?.Root == null) return false;
            return !IsTypingInTextField(); // never steal a key mid-edit
        }

        // ---- Menu wiring ---------------------------------------------------------------------------------

        /// <summary>The nodes a Duplicate click on <paramref name="clicked"/> would copy — the whole
        /// multi-selection when the clicked node is part of it, otherwise just that node.</summary>
        private List<LayoutNode> DuplicateTargetsFor(LayoutNode clicked)
        {
            var set = (clicked != null && HasMultiSelection && IsNodeSelected(clicked))
                ? TopmostSelected()
                : new List<LayoutNode> { clicked };
            return set.Where(n => CanDuplicateNode(n, out _)).ToList();
        }

        /// <summary>
        /// Adds the Duplicate entry to a node's right-click menu (hierarchy row and canvas card share this, so
        /// the two menus can't drift). Always present — greyed out when the node can't be copied — so its
        /// position in the menu is muscle memory rather than a surprise.
        /// </summary>
        private void AddDuplicateMenuItem(GenericMenu menu, LayoutNode node)
        {
            var targets = DuplicateTargetsFor(node);
            // "%d" is GenericMenu's Ctrl/Cmd+D hint — the same shorthand "Rename _F2" uses for a bare key.
            var label = new GUIContent(targets.Count > 1 ? $"Duplicate selected ({targets.Count}) %d" : "Duplicate %d");
            if (targets.Count > 0) menu.AddItem(label, false, () => DuplicateNodes(targets, selectCopies: true));
            else menu.AddDisabledItem(label);
        }

        // ---- Test seams (internal → visible to SasquatchInspector.Tests.Editor via InternalsVisibleTo) -----

        /// <summary>Test seam: duplicates the current selection exactly as Ctrl+D does.</summary>
        internal bool DebugDuplicateSelection() => DuplicateSelectedNodes();

        /// <summary>Test seam: the Ctrl/Cmd+D key route.</summary>
        internal bool DebugTryDuplicateShortcut() => TryHandleDuplicateShortcut();

        /// <summary>Test seam: Unity's Duplicate command route, including its "echo of the key" guard.</summary>
        internal bool DebugTryDuplicateCommand() => TryHandleDuplicateCommand();
    }
}
