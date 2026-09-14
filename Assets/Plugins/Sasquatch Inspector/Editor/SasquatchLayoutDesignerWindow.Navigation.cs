using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Drill-in navigation: styling a layout that embeds an In-Line Editor, and hopping into the embedded
    /// script's OWN layout without losing your place. A there-and-back, so it is a STACK — the breadcrumb
    /// reads <c>PlayerStats › WeaponData</c> and Back lands you on the exact node you left from.
    ///
    /// Deliberately NOT tabs. One live clone per window is load-bearing: <see cref="IsEditingType"/> and
    /// <see cref="RearmCloneReconcile"/> ask each window for its ONE target type, and that single answer gates
    /// the real inspectors' auto-sync suppression, the field reconcile, the orphan scan and the missing-method
    /// warning. A window holding several clones would answer "not editing" for every background one, and a
    /// Save from a stale clone silently clobbers fields the sync just added. So the stack never holds more
    /// than one live session: a parked frame is re-cloned from the registry on the way back.
    /// </summary>
    public partial class SasquatchLayoutDesignerWindow
    {
        /// <summary>
        /// One parked designer session. Holds only what Back has to restore — the layout itself is re-cloned
        /// from the registry on return, which is what keeps a single clone live at a time.
        /// </summary>
        [Serializable]
        internal class NavFrame
        {
            public string TypeName;                  // AssemblyQualifiedName — resolved through the same path as a layout's
            public string ScriptGuid;                // rename-proof, preferred over TypeName (mirrors ResolveTargetType)
            public string DisplayName;               // the crumb's text, captured while the type was resolvable
            public UnityEngine.Object TestTarget;    // preview stand-in to restore
            public List<int> SelectedIndexPath;      // the node drilled FROM — re-selected and pinged on return
            public int ActiveLeftTab;
            public bool DesignMode;
        }

        // Frames BELOW the current session, oldest first. Empty = not nested, and the breadcrumb collapses to
        // the plain type button it replaced. Serialized so a domain reload mid-drill doesn't strand the user.
        [SerializeField] private List<NavFrame> _navStack = new();

        // Set only while DrillIntoType / NavigateToDepth drive a load, telling LoadTypeLayout's chokepoint to
        // KEEP the trail. Everything else that retargets the window drops it. Not serialized: it is never true
        // across a call boundary, let alone a domain reload.
        private bool _navPreserveStack;

        private VisualElement _breadcrumbBar;
        private Button _navBackButton;

        // Past this many crumbs the middle collapses to an ellipsis — a 6-deep trail would otherwise push the
        // rest of the toolbar off the edge.
        private const int MaxVisibleCrumbs = 4;

        /// <summary>True when a Back destination exists — drives the Back button and the Alt+← shortcut.</summary>
        internal bool CanNavigateBack => _navStack.Count > 0;

        /// <summary>Depth of the drill-in stack (0 = a normal, un-nested session). Test seam.</summary>
        internal int NavDepth => _navStack.Count;

        /// <summary>The crumb labels, oldest first, INCLUDING the session currently open. Test seam.</summary>
        internal List<string> NavTrail()
        {
            var trail = new List<string>();
            foreach (var frame in _navStack) trail.Add(frame.DisplayName);
            var current = ResolveTargetType();
            if (current != null) trail.Add(current.Name);
            return trail;
        }

        // ---- drilling ------------------------------------------------------------------------------------

        /// <summary>
        /// Opens <paramref name="type"/>'s layout, parking the current session on the stack so Back returns to
        /// it with its selection and scroll intact. <paramref name="cameFrom"/> is the node being drilled
        /// through — Back re-selects and pings it, which is the whole "where was I" answer.
        ///
        /// A dirty session is SAVED first, without a dialog: a drill-in is "park this, I'm coming back", not
        /// "abandon it", so Save is right essentially always and Back is then guaranteed lossless. A modal on
        /// every hop is exactly the friction this feature exists to remove.
        /// </summary>
        internal void DrillIntoType(Type type, UnityEngine.Object testTarget, LayoutNode cameFrom)
        {
            if (type == null || _clonedLayoutAsset == null) return;
            if (!SasquatchContextMenu.IsTypeEditable(type)) return;

            var current = ResolveTargetType();
            if (current == null) return;
            if (current == type) return;   // already here; a no-op beats a stack frame that goes nowhere

            // Preset and Showcase sessions save to somewhere else entirely (a preset asset / a per-page
            // override), so parking one and re-cloning from the registry on the way back would lose it.
            if (_presetMode || !string.IsNullOrEmpty(_showcaseOverrideKey)) return;

            // Capture the return path BEFORE saving. A manual save RE-CLONES the working asset
            // (SaveActiveAsset → Instantiate), which strands every node reference taken before it — `cameFrom`
            // would then resolve to no path at all and Back would silently land on the root. Positional paths
            // survive the re-clone, which is the same reason SaveActiveAsset itself snapshots the selection as
            // paths and re-points it afterwards.
            var returnPath = cameFrom != null ? GetNodeIndexPath(cameFrom, _clonedLayoutAsset.Root) : null;
            string scriptGuid = _clonedLayoutAsset.ScriptGuid;

            if (_isDirty) SaveActiveAsset();

            _navStack.Add(new NavFrame
            {
                TypeName = current.AssemblyQualifiedName,
                ScriptGuid = scriptGuid,
                DisplayName = current.Name,
                TestTarget = _testTarget,
                SelectedIndexPath = returnPath,
                ActiveLeftTab = _activeLeftTab,
                DesignMode = _designMode,
            });

            // LoadTypeLayout is the single retarget chokepoint (clone release, sync reconcile, ledger
            // snapshot). Going through it is what keeps drill-in from being a second, divergent load path.
            _navPreserveStack = true;
            try { LoadTypeLayout(type, testTarget); }
            finally { _navPreserveStack = false; }
            RefreshBreadcrumb();
        }

        /// <summary>Pops one frame and returns to it. No-op at depth 0.</summary>
        internal void NavigateBack() => NavigateToDepth(_navStack.Count - 1);

        // The Alt+← shortcut's body, shared by the OnGUI and UITK key routes (both fire, in different focus
        // states — see the F2/Delete pair). Returns true when it consumed the key, so neither route swallows
        // an Alt+← that means something else. Guards text editing itself: Alt+← is a word-left caret move in
        // some layouts, and stealing it mid-type would be maddening.
        private bool TryHandleNavBackShortcut()
        {
            if (!CanNavigateBack || IsTypingInTextField()) return false;
            NavigateBack();
            return true;
        }

        /// <summary>
        /// Returns to the frame at <paramref name="depth"/>, discarding everything above it — the breadcrumb's
        /// click target, where clicking crumb 0 of a 3-deep trail unwinds two levels at once.
        /// </summary>
        internal void NavigateToDepth(int depth)
        {
            if (depth < 0 || depth >= _navStack.Count) return;

            var frame = _navStack[depth];
            _navStack.RemoveRange(depth, _navStack.Count - depth);

            var type = ResolveFrameType(frame);
            if (type == null)
            {
                // The type was renamed away or deleted while we were nested. Unwinding to nothing would strand
                // the user in a layout with a lie for a breadcrumb, so drop to the picker instead.
                RefreshBreadcrumb();
                ReturnToScriptPicker();
                return;
            }

            if (_isDirty) SaveActiveAsset();   // same rule on the way out as on the way in

            _activeLeftTab = frame.ActiveLeftTab;
            _designMode = frame.DesignMode;
            _navPreserveStack = true;   // the frames BELOW this one are still the trail
            try { LoadTypeLayout(type, frame.TestTarget); }
            finally { _navPreserveStack = false; }

            // Land on the node that was drilled through, pinged — LoadTypeLayout has already reset the
            // selection to the root, so this runs after it, not before.
            if (frame.SelectedIndexPath != null && frame.SelectedIndexPath.Count > 0)
                SelectNodeByIndexPath(frame.SelectedIndexPath);

            RefreshBreadcrumb();
        }

        // Mirrors ResolveTargetType's preference order: the rename-proof GUID first, the type name second.
        private static Type ResolveFrameType(NavFrame frame)
        {
            if (frame == null) return null;
            var byGuid = ScriptGuidUtility.ResolveType(frame.ScriptGuid);
            if (byGuid != null) return byGuid;
            return ResolveTypeFromName(frame.TypeName);
        }

        /// <summary>
        /// Clears the trail. Every path that abandons the session for an UNRELATED type — the script picker,
        /// the ancestor switcher, selection auto-follow — has to call this, or the breadcrumb would keep
        /// offering a Back that no longer relates to what is on screen.
        /// </summary>
        internal void ClearNavStack()
        {
            if (_navStack.Count == 0) return;
            _navStack.Clear();
            RefreshBreadcrumb();
        }

        // ---- the breadcrumb ------------------------------------------------------------------------------

        // Built once into the toolbar, in place of the old "Type: X ▾" button. At depth 0 it renders exactly
        // that one button, so an un-nested session looks unchanged.
        private VisualElement BuildBreadcrumbBar()
        {
            _breadcrumbBar = new VisualElement();
            _breadcrumbBar.style.flexDirection = FlexDirection.Row;
            _breadcrumbBar.style.alignItems = Align.Center;
            _breadcrumbBar.style.marginLeft = 8;
            _breadcrumbBar.style.flexShrink = 1;
            _breadcrumbBar.style.minWidth = 0;   // UITK defaults flex-shrink to 0; without this the trail pushes the toolbar wide
            return _breadcrumbBar;
        }

        // What the trail currently renders. The refresh funnel it hangs off (UpdateSaveButtonText) fires on
        // every dirty change, and rebuilding buttons out from under the cursor loses hover/press state — so a
        // repaint that would produce the same trail is skipped outright.
        private string _breadcrumbSignature;

        /// <summary>Repaints the trail. Cheap and idempotent — called from the same funnel that keeps the
        /// type button's label honest, plus after every push/pop.</summary>
        private void RefreshBreadcrumb()
        {
            if (_breadcrumbBar == null) return;

            var current = ResolveTargetType();
            bool show = current != null && !_presetMode && string.IsNullOrEmpty(_showcaseOverrideKey);

            string signature = show ? string.Join("›", NavTrail()) : "";
            if (signature == _breadcrumbSignature) return;
            _breadcrumbSignature = signature;

            _breadcrumbBar.Clear();
            _navBackButton = null;

            _breadcrumbBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                _typeSwitcherButton = null;   // never leave a detached button behind for the menu to anchor to
                return;
            }

            if (_navStack.Count > 0)
            {
                _navBackButton = new Button(NavigateBack) { text = "←" };
                _navBackButton.tooltip = "Back to " + _navStack[_navStack.Count - 1].DisplayName + "   (Alt+←)";
                _navBackButton.style.height = 22;
                _navBackButton.style.width = 24;
                _navBackButton.style.marginTop = 0;
                _navBackButton.style.marginBottom = 0;
                _navBackButton.style.marginLeft = 0;
                _navBackButton.style.marginRight = 2;
                _navBackButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                EditorSpringFX.ButtonSpring(_navBackButton);
                _breadcrumbBar.Add(_navBackButton);
            }

            // Which parked frames get a crumb. Past the cap the MIDDLE collapses: the first crumb says where
            // the trail started and the last few say where you are, which is the pair that carries meaning.
            int total = _navStack.Count;
            int firstShown = 0, lastHiddenBefore = -1;
            if (total > MaxVisibleCrumbs - 1)
            {
                firstShown = total - (MaxVisibleCrumbs - 2);
                lastHiddenBefore = firstShown;
            }

            if (lastHiddenBefore > 0)
            {
                AddCrumb(_navStack[0].DisplayName, 0);
                AddSeparator();
                var ellipsis = new Label("…");
                ellipsis.tooltip = BuildHiddenCrumbTooltip(1, lastHiddenBefore);
                ellipsis.style.color = DesignerPalette.TextMuted;
                ellipsis.style.marginLeft = 2;
                ellipsis.style.marginRight = 2;
                _breadcrumbBar.Add(ellipsis);
                AddSeparator();
            }

            for (int i = firstShown; i < total; i++)
            {
                AddCrumb(_navStack[i].DisplayName, i);
                AddSeparator();
            }

            // The LAST crumb is the session actually open, and it keeps the ancestor dropdown the plain
            // "Type: X ▾" button carried — same menu, same behaviour, just rendered as the tail of the trail.
            _typeSwitcherButton = new Button(ShowTypeSwitcherMenu) { text = current.Name + " ▾" };
            _typeSwitcherButton.tooltip = _navStack.Count > 0
                ? "The type this layout targets (nested from " + _navStack[_navStack.Count - 1].DisplayName +
                  "). Click to switch to an ancestor, or to pick a different script."
                : "The type this layout targets. Click to switch to an ancestor (or back to the test target's " +
                  "type) — author a base layout once, then turn on Apply to Subclasses in Tools so every " +
                  "subclass inherits it.";
            _typeSwitcherButton.style.height = 22;
            _typeSwitcherButton.style.marginTop = 0;
            _typeSwitcherButton.style.marginBottom = 0;
            _typeSwitcherButton.style.marginLeft = 0;
            _typeSwitcherButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _typeSwitcherButton.style.flexShrink = 0;
            EditorSpringFX.ButtonSpring(_typeSwitcherButton);
            _breadcrumbBar.Add(_typeSwitcherButton);
        }

        private string BuildHiddenCrumbTooltip(int from, int toExclusive)
        {
            var parts = new List<string>();
            for (int i = from; i < toExclusive; i++) parts.Add(_navStack[i].DisplayName);
            return string.Join("  ›  ", parts);
        }

        // A parked frame's crumb: click to unwind straight to it, however many levels up that is.
        private void AddCrumb(string label, int depth)
        {
            int target = depth;
            var crumb = new Button(() => NavigateToDepth(target)) { text = label };
            crumb.tooltip = "Back to " + label + "'s layout";
            crumb.style.height = 22;
            crumb.style.marginTop = 0;
            crumb.style.marginBottom = 0;
            crumb.style.marginLeft = 0;
            crumb.style.marginRight = 0;
            crumb.style.paddingLeft = 6;
            crumb.style.paddingRight = 6;
            crumb.style.backgroundColor = Color.clear;
            crumb.style.borderTopWidth = 0; crumb.style.borderBottomWidth = 0;
            crumb.style.borderLeftWidth = 0; crumb.style.borderRightWidth = 0;
            crumb.style.color = DesignerPalette.TextMuted;   // parked frames read quieter than the open one
            crumb.style.unityTextAlign = TextAnchor.MiddleCenter;
            crumb.style.flexShrink = 0;
            EditorSpringFX.ButtonSpring(crumb);
            _breadcrumbBar.Add(crumb);
        }

        private void AddSeparator()
        {
            var sep = new Label("›");
            sep.pickingMode = PickingMode.Ignore;
            sep.style.color = DesignerPalette.TextMuted;
            sep.style.marginLeft = 1;
            sep.style.marginRight = 1;
            sep.style.flexShrink = 0;
            _breadcrumbBar.Add(sep);
        }

        // ---- what an In-Line Editor node points at -------------------------------------------------------

        /// <summary>
        /// The type a node embeds an in-line editor FOR, or null when it embeds nothing. Two node kinds qualify:
        ///
        ///  • a <see cref="BindingNode"/> whose Display Type is In-Line Editor — the embedded type is the
        ///    bound field's own type;
        ///  • a <see cref="CollectionNode"/> with <c>In-Line Editor Rows</c> on — the embedded type is the
        ///    collection's ELEMENT type, and every row is an editor for it.
        ///
        /// <paramref name="declared"/> is the type as declared in C#; <paramref name="assigned"/> is the
        /// concrete type of whatever currently sits in that field on the preview target, non-null ONLY when it
        /// differs (a subclass asset). The return value is the default drill target: what is actually
        /// rendering in front of you when something is assigned, the declared type otherwise. A collection has
        /// no single assignment, so it reports the element type alone.
        /// </summary>
        internal Type ResolveInlineEditorType(LayoutNode node, out Type declared, out Type assigned)
        {
            declared = null;
            assigned = null;

            if (node is CollectionNode collection)
            {
                if (!collection.ElementInlineEditor) return null;
                declared = ResolveCollectionElementType(collection);
                // The same gate the Properties panel uses to OFFER In-Line Editor Rows, so the menu entry can
                // never appear for a list that couldn't draw them in the first place.
                if (!SupportsInlineEditorRows(collection, declared, collection.DisplayMode)) return null;
                return SasquatchContextMenu.IsTypeEditable(declared) ? declared : null;
            }

            if (!(node is BindingNode bn) || bn.DisplayType != DisplayType.InlineEditor) return null;

            declared = ResolveSelectedNodeFieldType(bn);
            if (declared == null || !typeof(UnityEngine.Object).IsAssignableFrom(declared)) return null;

            var live = ResolveInlineEditorAssignedObject(bn);
            if (live != null && live.GetType() != declared) assigned = live.GetType();

            var primary = assigned ?? declared;
            return SasquatchContextMenu.IsTypeEditable(primary) ? primary : null;
        }

        // The object currently in the bound field on the preview target, or null. Best-effort: no preview
        // object, an unresolvable path or a non-reference field all just mean "no assigned type", which
        // degrades to the declared type rather than to no menu item at all. A collection has no single
        // assignment (each row has its own), so it always answers null here.
        private UnityEngine.Object ResolveInlineEditorAssignedObject(LayoutNode layoutNode)
        {
            if (!(layoutNode is BindingNode node)) return null;
            var so = _previewSerializedObject;
            if (so == null || so.targetObject == null || string.IsNullOrEmpty(node.PropertyPath)) return null;
            try
            {
                var prop = so.FindProperty(node.PropertyPath);
                if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return null;
                return prop.objectReferenceValue;
            }
            catch (Exception)
            {
                return null;   // a disposed SerializedObject mid-teardown is not worth a throw
            }
        }

        /// <summary>
        /// Appends the drill-in entries for <paramref name="node"/> to a context menu, or nothing at all when
        /// the node embeds no in-line editor. Shared by the hierarchy row and the canvas card menus so the two
        /// can never drift on wording or on when the entry appears.
        /// </summary>
        internal void AppendInlineEditorNavItems(GenericMenu menu, LayoutNode node)
        {
            var target = ResolveInlineEditorType(node, out var declared, out var assigned);
            if (target == null) return;

            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Edit {target.Name} Layout"), false,
                () => DrillIntoType(target, ResolveInlineEditorAssignedObject(node), node));

            // When the assigned asset is a SUBCLASS of the field's type, both are legitimate things to want:
            // the subclass is what you are looking at, the base is what every other asset in that field uses.
            if (assigned != null && SasquatchContextMenu.IsTypeEditable(declared))
                menu.AddItem(new GUIContent($"Edit {declared.Name} Layout  (field type)"), false,
                    () => DrillIntoType(declared, null, node));
        }

        /// <summary>
        /// The Properties panel's "Edit … Layout →" row for an In-Line Editor binding. Returns an element that
        /// hides ITSELF when the embedded type can't be resolved (unassigned + unresolvable field, a Unity
        /// built-in, a non-Object field left over from a type change) rather than the caller having to know —
        /// the caller only knows it's an In-Line Editor, which isn't enough to say whether there's anywhere
        /// to go.
        /// </summary>
        private VisualElement BuildInlineEditorDrillInRow(LayoutNode node)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var target = ResolveInlineEditorType(node, out _, out _);
            if (target == null || _presetMode || !string.IsNullOrEmpty(_showcaseOverrideKey))
            {
                row.style.display = DisplayStyle.None;
                return row;
            }

            var open = new Button(() => DrillIntoType(target, ResolveInlineEditorAssignedObject(node), node))
            {
                text = $"Edit {target.Name} Layout  →"
            };
            open.tooltip = $"Design {target.Name}'s own inspector layout. Your place here is kept — the " +
                           "breadcrumb walks you back (Alt+←).";
            open.style.flexGrow = 1;
            open.style.height = 22;
            open.style.marginLeft = 0;
            open.style.marginRight = 0;
            // NO hover spring. The spring is a transform SCALE, and a full-width button in a narrow panel
            // scales past the viewport — which grows a horizontal scrollbar under the cursor every time you
            // hover it. Pre-marking the sweep's own class is how the other full-width action buttons opt out
            // ("+ Add Animation", "+ Add Condition"), and it must be set here because ApplyOptionButtonSprings
            // runs over the FINISHED panel and would otherwise spring this button after the fact.
            open.AddToClassList(OptionSprungClass);
            row.Add(open);

            return row;
        }

    }
}
