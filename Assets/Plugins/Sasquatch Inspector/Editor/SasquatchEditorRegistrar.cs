using System;
using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Helpers for Sasquatch Inspector's inspector takeover, which works through editor resolution: two
    /// compile-time <c>isFallback</c> child-class editors (see <see cref="VisualInspectorEditor"/>) make our
    /// inspector the resolved editor for every MonoBehaviour/ScriptableObject that has no editor of its own, while
    /// Unity's own resolver defers to any real <c>[CustomEditor]</c> (the developer's or a third party's). That
    /// deference is automatic.
    ///
    /// <para>A blanket editor another inspector framework injects for every MonoBehaviour/ScriptableObject is not
    /// beaten by editor resolution but by PANEL DISPLACEMENT (see <see cref="SasquatchForceTakeover"/>), which
    /// draws the layout over the other editor's body with no code generation and no recompile.</para>
    ///
    /// <para>What lives here are the helpers around that: <see cref="ShouldAutoRegisterLayoutType"/>, the
    /// deference test the designer uses for its conflict notice, plus <see cref="RefreshInspectors"/> and
    /// <see cref="RefreshInspectorsForType"/>, which force live inspectors to rebuild after a layout/theme change
    /// so the result shows without a manual reselect.</para>
    /// </summary>
    public static class SasquatchEditorRegistrar
    {
        /// <summary>
        /// True if a layout for <paramref name="t"/> will actually draw. We defer ONLY to a genuine per-type
        /// <c>[CustomEditor]</c> the developer or a third party wrote for the exact type; a blanket editor another
        /// inspector framework injects for every MonoBehaviour/ScriptableObject is NOT a reason to defer — the
        /// layout is drawn over it by displacement (see <see cref="SasquatchForceTakeover"/>). When false, a real
        /// per-type editor wins and the designer surfaces that as an informational conflict notice.
        /// </summary>
        public static bool ShouldAutoRegisterLayoutType(Type t)
            => t != null && !SasquatchGeneratedEditors.HasGenuinePerTypeEditor(t);

        private static bool _nudgePending;

        /// <summary>
        /// True while a self-induced selection "nudge" (clear → restore) from <see cref="RefreshInspectors"/> is
        /// in flight. The designer window checks this so it can ignore the resulting selection-change events and
        /// not reload/rebuild itself mid-edit.
        /// </summary>
        public static bool IsSelectionNudgePending => _nudgePending;

        /// <summary>
        /// Force live inspectors to fully rebuild so a layout/theme change shows immediately (not "stale until you
        /// reselect"). The reliable trick: briefly clear the selection and restore it on the next editor tick.
        /// That "nudge" is exactly the manual "click off and back on" that reliably redraws — it makes Unity tear
        /// down and recreate every inspector editor. <c>ActiveEditorTracker.ForceRebuild()</c> alone does not
        /// always recreate the UI Toolkit panel, so on its own it can leave the change looking stuck.
        /// </summary>
        public static void RefreshInspectors()
        {
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            RepaintAllEditorWindows();

            // Re-entrancy guard: if a restore is already queued, the current Selection may be the transient empty
            // one — re-capturing it here would restore nothing and lose the user's selection. The pending nudge
            // already covers this refresh.
            if (_nudgePending) return;

            var objects = Selection.objects;
            if (objects == null || objects.Length == 0) return; // nothing selected → nothing to nudge

            _nudgePending = true;
            Selection.objects = Array.Empty<UnityEngine.Object>();
            EditorApplication.delayCall += () =>
            {
                Selection.objects = objects;
                _nudgePending = false;
                RepaintAllEditorWindows();
            };
        }

        /// <summary>
        /// TARGETED post-save refresh for a SINGLE type's layout change. Rebuilds only the inspector editors that
        /// actually render <paramref name="savedType"/> — instead of the global selection nudge in
        /// <see cref="RefreshInspectors"/>, which tears down and recreates EVERY editor on the selection twice
        /// (every component, custom editor, and preview), the source of the multi-hundred-ms post-save lag.
        ///
        /// <para>Covers BOTH inspector render paths (they have drifted before — any change here must hold on both):
        ///   • FALLBACK (<see cref="VisualInspectorEditor"/>, types with no genuine [CustomEditor]): the live editor
        ///     instance is rebuilt in place via <see cref="VisualInspectorEditor.RebuildForLayoutChange"/> — the same
        ///     reliable path the undo handler uses — for exactly the editors whose type matches.
        ///   • DISPLACEMENT (<see cref="SasquatchForceTakeover"/>, types with their OWN [CustomEditor] and Force
        ///     Takeover, or a blanket third-party editor): the type's layout generation is bumped
        ///     (<see cref="SasquatchForceTakeover.InvalidateType"/>) and an immediate header scan is requested so the
        ///     displaced body rebuilds against the new graph this frame.</para>
        ///
        /// <para>Unrelated components, previews, and other inspectors are left untouched (the explicit goal: churn
        /// LESS, never more). No selection nudge — so <see cref="IsSelectionNudgePending"/> stays false and the
        /// designer's own selection-change guard is simply not exercised (nothing to ignore). Falls back to the
        /// global <see cref="RefreshInspectors"/> when <paramref name="savedType"/> is null.</para>
        /// </summary>
        public static void RefreshInspectorsForType(Type savedType)
        {
            if (savedType == null) { RefreshInspectors(); return; }

            // NESTED path first: live UI that renders ANOTHER type's layout inside an inspector row can never be
            // found by the two sweeps below — they match on the ROW's type, and a nested editor's type is not it.
            RaiseLayoutChangedForType(savedType);

            // DISPLACEMENT path: mark this type's displaced bodies (and any subclass showing its layout) stale, then
            // run the scan now so the rebuild happens immediately rather than on the next 0.15s throttle tick.
            SasquatchForceTakeover.InvalidateType(savedType);
            GlobalInspectorHeaderGUI.RequestImmediateScan();

            // FALLBACK path: rebuild in place every live VisualInspectorEditor rendering this type (exact type, or a
            // subclass that may inherit the layout). In-place — no editor teardown, no selection nudge — so unrelated
            // editors don't churn. Reading activeEditors here gets the current instances (the save's asset reimport
            // may have already rebuilt the tracker); a not-yet-built editor no-ops and builds fresh when Unity gets to it.
            var editors = ActiveEditorTracker.sharedTracker.activeEditors;
            if (editors != null)
                foreach (var ed in editors)
                    if (ed is VisualInspectorEditor vie && vie.target != null && IsLayoutRelevant(vie.target.GetType(), savedType))
                        vie.RebuildForLayoutChange();

            // (A Sasquatch-exported standalone [CustomEditor] needs no leg of its own: taken-over, it IS a
            // displaced body — covered above; released, it renders its baked copy, which a layout save never
            // changes until the user re-exports.)

            RepaintAllEditorWindows();
        }

        /// <summary>
        /// Raised by <see cref="RefreshInspectorsForType"/> with the type whose layout just changed, for live UI
        /// that draws a DIFFERENT type's layout nested inside an inspector row and therefore cannot be reached by
        /// the per-type editor sweeps — today that is the In-Line Editor widget, which embeds the assigned object's
        /// own layout (see <c>BindingFactory.WireNestedLayoutSync</c>). Without it, designing the embedded script's
        /// layout from the widget's pencil and saving left the nested fields stale until the user deselected and
        /// reselected the host object.
        ///
        /// <para>Subscribers must drop their handler when their element leaves the panel for good
        /// (<see cref="PanelLifetime.OnRealDetach"/>) — this is a static event, so a kept handler pins a dead
        /// subtree and its SerializedObject.</para>
        /// </summary>
        internal static event Action<Type> LayoutChangedForType;

        // Invoked per handler so one bad nested rebuild never starves the rest (a multicast Invoke stops at the
        // first throw) — same contract as PanelLifetime's drain.
        private static void RaiseLayoutChangedForType(Type savedType)
        {
            var handlers = LayoutChangedForType;
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<Type>)handler)(savedType); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        /// <summary>True when an inspector row of type <paramref name="rowType"/> could render
        /// <paramref name="savedType"/>'s layout: the same type, or a subclass that may inherit it via Apply To
        /// Subclasses. Over-approximates the subclass case (doesn't consult the flag) on purpose — a harmless extra
        /// in-place rebuild beats leaving a real subclass inspector stale.</summary>
        internal static bool IsLayoutRelevant(Type rowType, Type savedType)
            => rowType != null && savedType != null &&
               (rowType == savedType || savedType.IsAssignableFrom(rowType));

        // Repaint every open editor window.
        private static void RepaintAllEditorWindows()
        {
            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window != null) window.Repaint();
        }
    }
}
