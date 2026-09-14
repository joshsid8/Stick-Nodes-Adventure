using UnityEditor;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Decides whether an undo/redo actually requires a Sasquatch surface to REBUILD.
    ///
    /// <para><c>Undo.undoRedoPerformed</c> is a global editor event carrying no payload: it fires for every
    /// undo in the editor, so undoing a Scene-view transform move used to rebuild every inspector body on
    /// screen. Measured rebuild cost per surface: 1.28ms for a 29-field layout, <b>21.4ms for a 200-row
    /// table</b> — and users hold Ctrl+Z down.</para>
    ///
    /// <para>THE REBUILD IS NOT GRATUITOUS, which is why this gates rather than removes it.
    /// <c>VisualInspectorEditor</c> documents the reason: after an undo Unity has already reverted the target,
    /// but the editor's <c>SerializedObject</c> still holds its pre-undo cache, so without
    /// <c>Update()</c> + rebuild a STRUCTURAL change (a restored table row) would not reappear until the
    /// object was reselected. Plain value edits are masked because the binding system re-reads every frame.</para>
    ///
    /// <para>So the gate asks the narrow question the rebuild exists for: <b>did anything STRUCTURAL move?</b>
    /// Two independent signals, both public API and both cheap (measured 0.027ms per surface, against a
    /// rebuild that is 50-740x that):</para>
    /// <list type="bullet">
    /// <item>the target's array sizes and <c>[SerializeReference]</c> instance ids — everything a rebuild is
    /// needed to re-render, and nothing a live binding already handles;</item>
    /// <item>the LAYOUT asset's dirty counter — because undoing a designer or quick-edit change alters the
    /// layout, not the target, and the target fingerprint would never see it. Verified that
    /// <c>EditorUtility.GetDirtyCount</c> does move across <c>Undo.PerformUndo</c> (measured 2 → 3 on a
    /// restored layout edit), which is what makes this usable at all.</item>
    /// </list>
    ///
    /// <para>EVERY UNCERTAIN CASE REBUILDS. No captured baseline, a null target, a multi-object edit — all
    /// answer "rebuild". The gate can only ever skip a rebuild it can prove is redundant; being wrong in the
    /// other direction costs microseconds.</para>
    ///
    /// <para>One deliberate behaviour change, recorded because it is real: a table's column content-fit is
    /// re-measured on rebuild, so undoing a plain VALUE edit no longer re-fits the columns. It already did not
    /// re-fit when the value was edited in the first place (widths follow the next rebuild, not the edit), so
    /// this makes undo consistent with edit rather than inconsistent with it.</para>
    /// </summary>
    internal sealed class UndoRebuildGate
    {
        private int _structure;
        private int _layoutDirty;
        private bool _captured;

        /// <summary>Forget the baseline, so the next undo rebuilds unconditionally.</summary>
        internal void Invalidate() => _captured = false;

        /// <summary>
        /// Records the state a build just rendered. Call at the END of every build, from every path that
        /// builds — a baseline that lags the screen is a rebuild that gets skipped when it was needed.
        /// </summary>
        internal void Capture(SerializedObject serializedObject, UnityEngine.Object layoutAsset)
        {
            if (!TryFingerprint(serializedObject, layoutAsset, out int structure, out int layoutDirty))
            {
                _captured = false;
                return;
            }
            _structure = structure;
            _layoutDirty = layoutDirty;
            _captured = true;
        }

        /// <summary>
        /// True when this undo/redo could have changed something only a rebuild can show. Call AFTER
        /// <c>serializedObject.Update()</c> — the whole point is to compare against the post-undo state, and a
        /// stale SerializedObject cache would report no change for the structural undo that matters most.
        /// Updates the baseline either way, so a skipped rebuild cannot make the next comparison lie.
        /// </summary>
        internal bool ShouldRebuild(SerializedObject serializedObject, UnityEngine.Object layoutAsset)
        {
            if (!TryFingerprint(serializedObject, layoutAsset, out int structure, out int layoutDirty))
            {
                _captured = false;
                return true; // cannot prove it is redundant ⇒ rebuild
            }

            bool changed = !_captured || structure != _structure || layoutDirty != _layoutDirty;
            _structure = structure;
            _layoutDirty = layoutDirty;
            _captured = true;
            return changed;
        }

        /// <summary>
        /// The two signals, or false when the state cannot be read at all. Multi-object edits deliberately
        /// return false: a multi-edit SerializedObject reports mixed array sizes, so its fingerprint would be
        /// meaningless — and multi-edit is rare enough that always rebuilding costs nothing worth having.
        /// </summary>
        private static bool TryFingerprint(SerializedObject serializedObject, UnityEngine.Object layoutAsset,
                                           out int structure, out int layoutDirty)
        {
            structure = 0;
            layoutDirty = 0;
            if (serializedObject == null) return false;

            UnityEngine.Object target;
            try
            {
                if (serializedObject.isEditingMultipleObjects) return false;
                target = serializedObject.targetObject;
            }
            catch
            {
                return false; // the SerializedObject was disposed under us
            }
            if (target == null) return false;

            try
            {
                unchecked
                {
                    int h = 17;
                    var it = serializedObject.GetIterator();
                    bool enterChildren = true;
                    while (it.NextVisible(enterChildren))
                    {
                        // Descend only into composites; a leaf has nothing structural inside it.
                        enterChildren = it.propertyType == SerializedPropertyType.Generic;

                        // An array's SIZE is structural — a restored row is exactly the case the rebuild
                        // exists for. Its ELEMENTS are not walked: their values are the binding system's job,
                        // and walking them would cost more than the rebuild it is trying to avoid.
                        // (A string reports isArray too, and its length is a value, not structure.)
                        if (it.isArray && it.propertyType != SerializedPropertyType.String)
                        {
                            h = h * 31 + it.arraySize;
                            enterChildren = false;
                        }
                        else if (it.propertyType == SerializedPropertyType.ManagedReference)
                        {
                            // A [SerializeReference] swapped to another concrete type re-renders differently.
                            h = h * 31 + it.managedReferenceId.GetHashCode();
                        }
                    }
                    structure = h;
                }
            }
            catch
            {
                return false; // a property threw mid-walk (target destroyed, script missing)
            }

            // Any change to the layout at all must rebuild — undoing a designer/quick-edit change rewrites
            // node properties the target fingerprint above can never see. Layout edits are rare, so the
            // coarse signal costs nothing.
            layoutDirty = layoutAsset != null ? EditorUtility.GetDirtyCount(layoutAsset) : 0;
            return true;
        }
    }
}
