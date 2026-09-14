using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Rename self-heal for <see cref="ButtonNode"/> method bindings. A button stores the method NAME the
    /// user picked plus a signature fingerprint (<see cref="ComputeFingerprint"/>) stamped whenever the
    /// name resolves. When a compile renames the method, the name stops resolving but the fingerprint still
    /// matches — if exactly ONE user-invokable method matches it, the button relinks to that method
    /// automatically; zero or several matches never guess (the missing-method affordances take over).
    /// The fingerprint travels in the layout asset, so a teammate pulling the rename commit heals too, and
    /// a rename made while the editor was closed heals on the next open.
    ///
    /// Split (like <see cref="UnboundFieldSync"/>) into pure decision logic (<see cref="ComputeHeals"/>)
    /// and persistence wiring (<see cref="HealLayout"/>: BeginLayoutEditUndo → mutate → PersistLayoutEdit;
    /// <see cref="HealLayoutInMemory"/> for the designer's working clone, which only reaches disk through
    /// the user's own Save). Runs on the same triggers as the field reconcile — every inspector rebuild and
    /// designer open — but is NOT gated by the Unbound Field Handling mode: it repairs bindings the user
    /// already made rather than importing new fields, so the "keep drift visible" contract of the
    /// non-AutoAppend modes doesn't apply.
    /// </summary>
    public static class ButtonMethodSync
    {
        /// <summary>
        /// The fingerprint of a method's shape: return type + parameter types (never the name). Instance
        /// methods only, matching what a button can bind. Example: <c>System.Void(System.Int32,System.String)</c>.
        /// </summary>
        public static string ComputeFingerprint(MethodInfo method)
        {
            if (method == null) return "";
            var sb = new StringBuilder(TypeToken(method.ReturnType)).Append('(');
            var ps = method.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(TypeToken(ps[i].ParameterType));
            }
            return sb.Append(')').ToString();
        }

        // Assembly-agnostic type token, recursing into arrays/generics so a fingerprint never bakes in an
        // assembly-qualified argument name that could drift between the stamp and the heal.
        private static string TypeToken(Type t)
        {
            if (t == null) return "?";
            if (t.IsArray) return TypeToken(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                var sb = new StringBuilder(t.GetGenericTypeDefinition().FullName).Append('[');
                var args = t.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(TypeToken(args[i]));
                }
                return sb.Append(']').ToString();
            }
            return t.FullName ?? t.Name;
        }

        /// <summary>
        /// The signature to stamp for what <paramref name="name"/> currently resolves to on
        /// <paramref name="type"/> — "" when it doesn't resolve. A typed-ahead name must clear the previous
        /// fingerprint rather than inherit it, or the heal would later "fix" the button back to the old
        /// method the moment the user's not-yet-written method still doesn't exist.
        /// </summary>
        public static string ComputeSignature(Type type, string name)
            => ComputeFingerprint(ButtonInvoker.ResolveMethod(type, name));

        /// <summary>
        /// Finds the unique user-invokable method on <paramref name="type"/> whose fingerprint matches —
        /// the rename target. False when zero or 2+ distinct method names match (never guess), when there is
        /// no fingerprint to match, or when <paramref name="missingName"/> still exists in ANY form (a
        /// method that merely became un-bindable is not a rename).
        /// </summary>
        public static bool TryFindRenameTarget(Type type, string missingName, string fingerprint, out string newName)
        {
            newName = null;
            if (type == null || string.IsNullOrEmpty(fingerprint)) return false;
            if (ButtonInvoker.HasMethodNamed(type, missingName)) return false;

            string found = null;
            foreach (var m in ButtonInvoker.MethodTable(type)) // memoised; see ButtonInvoker.MethodTable
            {
                if (!ButtonInvoker.IsUserActionMethod(m)) continue;
                if (ComputeFingerprint(m) != fingerprint) continue;
                if (found == null) found = m.Name;
                else if (found != m.Name) return false; // two distinct candidates — ambiguous, never guess
            }
            if (found == null) return false;
            newName = found;
            return true;
        }

        internal enum HealKind
        {
            /// <summary>The bound name is gone and a unique fingerprint match exists — rewrite the name.</summary>
            Relink,
            /// <summary>The name resolves but the stored fingerprint is absent (pre-heal layout) or stale
            /// (the method's shape changed in place) — refresh the fingerprint, name untouched.</summary>
            Restamp
        }

        internal struct HealOp
        {
            public ButtonNode Node;
            public string NewName;
            public string NewSignature;
            public HealKind Kind;
        }

        /// <summary>PURE: everything a heal pass would change, computed without mutating anything.</summary>
        internal static List<HealOp> ComputeHeals(Type type, LayoutNode root)
        {
            var ops = new List<HealOp>();
            if (type == null || root == null) return ops;
            Visit(root, node =>
            {
                if (!(node is ButtonNode bn) || string.IsNullOrEmpty(bn.MethodName)) return;

                var resolved = ButtonInvoker.ResolveMethod(type, bn.MethodName);
                if (resolved != null)
                {
                    // Healthy binding: keep the fingerprint current so a LATER rename can still match. This
                    // both backfills layouts saved before fingerprints existed and follows in-place shape
                    // changes (e.g. a parameter added without a rename).
                    string sig = ComputeFingerprint(resolved);
                    if (bn.MethodSignature != sig)
                        ops.Add(new HealOp { Node = bn, NewName = bn.MethodName, NewSignature = sig, Kind = HealKind.Restamp });
                }
                else if (TryFindRenameTarget(type, bn.MethodName, bn.MethodSignature, out string renamed))
                {
                    ops.Add(new HealOp { Node = bn, NewName = renamed, NewSignature = bn.MethodSignature, Kind = HealKind.Relink });
                }
            });
            return ops;
        }

        // Buttons live under plain containers and inside a collection's per-item template; both are walked.
        private static void Visit(LayoutNode node, Action<LayoutNode> visit)
        {
            if (node == null) return;
            visit(node);
            if (node is ContainerNode container)
                foreach (var child in container.Children)
                    Visit(child, visit);
            if (node is CollectionNode collection)
                Visit(collection.ItemLayout, visit);
        }

        /// <summary>
        /// WIRING: heal against a registered layout — the inspector hot-path entry. Strict zero-side-effect
        /// no-op when every button resolves with a current fingerprint. Returns the number of RELINKED
        /// buttons (invisible fingerprint restamps persist too but don't count, and don't open an undo step).
        /// </summary>
        public static int HealLayout(Type type, InspectorLayoutAsset layout)
            => HealCore(type, layout, persist: true);

        /// <summary>In-memory variant for layout objects that are NOT the persisted registry asset — the
        /// designer's working clone. Same decisions, no undo record and no disk write: a clone only reaches
        /// disk through the user's own Save.</summary>
        public static int HealLayoutInMemory(Type type, InspectorLayoutAsset layout)
            => HealCore(type, layout, persist: false);

        private static int HealCore(Type type, InspectorLayoutAsset layout, bool persist)
        {
            if (type == null || layout == null || layout.Root == null) return 0;

            // Same safety window as the field reconcile: never write on the strength of a mid-compile /
            // mid-import reflection read. A skipped pass re-arms for free — this runs on every rebuild.
            if (EditorIsTransient) return 0;

            var ops = ComputeHeals(type, layout.Root);
            if (ops.Count == 0) return 0;

            int relinked = 0;
            foreach (var op in ops)
                if (op.Kind == HealKind.Relink) relinked++;

            // Restamps are invisible bookkeeping — no undo step for them (mirrors the field ledger's
            // silent maintenance). A relink changes what the button DOES, so it stays undoable.
            if (persist && relinked > 0) LayoutRegistry.BeginLayoutEditUndo(layout, "Relink Button Method");

            // ONE console entry per pass, every relink listed inside it. Renames often arrive as separate
            // IDE-driven compiles (Rider refreshes Unity per refactor), and Clear-on-Recompile wipes the
            // earlier pass's output — with per-relink entries that read as "lines went missing". A single
            // aggregated entry means whatever line IS visible always tells its pass's whole story.
            StringBuilder relinkLog = relinked > 0 ? new StringBuilder() : null;
            foreach (var op in ops)
            {
                if (op.Kind == HealKind.Relink)
                {
                    string label = string.IsNullOrEmpty(op.Node.ButtonText) ? "Execute Action" : op.Node.ButtonText;
                    relinkLog.Append($"\n  • '{label}': '{op.Node.MethodName}' → '{op.NewName}'");
                }
                op.Node.MethodName = op.NewName;
                op.Node.MethodSignature = op.NewSignature;
            }
            if (relinkLog != null)
                Debug.Log($"[Sasquatch Inspector] Relinked {relinked} button method{(relinked == 1 ? "" : "s")} " +
                          $"on {type.Name} after a script change (matched by signature):{relinkLog}");

            if (persist)
            {
                LayoutRegistry.PersistLayoutEdit(layout);
                // A designer window on this type holds a clone taken before the heal, and its Save would put
                // the dead method names straight back. No index path — a heal can touch several buttons at
                // once — so a CLEAN clone adopts the healed layout entire and a dirty one keeps its own.
                SasquatchLayoutDesignerWindow.AdoptLiveLayoutEdit(type);
            }
            return relinked;
        }

        // Mirrors UnboundFieldSync's transient gate; non-null forces the answer for deterministic tests.
        internal static bool? ForceTransientForTests;

        private static bool EditorIsTransient =>
            ForceTransientForTests ?? (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating);
    }
}
