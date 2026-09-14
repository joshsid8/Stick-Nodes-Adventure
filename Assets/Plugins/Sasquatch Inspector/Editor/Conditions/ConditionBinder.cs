using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>Binds a node's <see cref="ConditionRule"/>s to its element — watches the referenced properties and applies show/hide/enable/highlight live (scoped to the node's base path).</summary>
    public static class ConditionBinder
    {
        // Poll cadence (ms) for rules whose driver has no SerializedProperty to TrackPropertyValue against — a
        // bool-member driver (a computed property/method) or a built-in like IsInPlayMode — so it's re-evaluated on
        // a gentle tick while attached.
        private const long MemberDriverPollMs = 200;

        /// <summary>
        /// Wires <paramref name="node"/>'s condition rules onto <paramref name="element"/>, resolving each against
        /// <paramref name="basePath"/> and re-evaluating whenever the referenced properties change. When
        /// <paramref name="debugConditions"/> is true, fields that would be hidden/disabled are instead revealed
        /// in place with a <see cref="ConditionDebugOverlay"/> (red = hidden, amber = disabled) + hover breakdown.
        /// </summary>
        public static void Bind(VisualElement element, LayoutNode node, SerializedObject serializedObject, string basePath, bool debugConditions = false, bool designMode = false)
        {
            if (element == null || node == null || serializedObject == null) return;
            ConditionModel.EnsureMigrated(node);
            var rules = node.ConditionRules;
            if (rules == null || rules.Count == 0) return;
            SetupTracking(element, rules, serializedObject, basePath, debugConditions, designMode);
        }

        // Scopes a condition's property path onto the node's base path. internal so ConditionEvaluationReport
        // resolves paths identically (single source of truth).
        internal static string ResolvePath(string basePath, string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return propertyPath;
            // A built-in driver (IsInPlayMode) names EDITOR state, not a member of the inspected object, so it is
            // NEVER scoped onto the base path. Scoping it would produce "list.Array.data[2].IsInPlayMode" inside a
            // collection item — which resolves to nothing AND no longer equals the condition's own path, the exact
            // test the evaluator's non-serialized-driver fallback keys on.
            if (ConditionDrivers.IsBuiltIn(propertyPath)) return propertyPath;
            return string.IsNullOrEmpty(basePath) ? propertyPath : $"{basePath}.{propertyPath}";
        }

        // The single outcome-fold rule, shared by the live binder and ConditionEvaluationReport so the debugger's
        // verdict can never drift from the real show/hide behavior. Applied once per RULE using the rule's net
        // boolean result (its all/any expression), not per individual condition.
        internal static void ApplyOutcome(ConditionOutcome outcome, bool met, ref bool visible, ref bool enabled, ref bool highlight)
        {
            switch (outcome)
            {
                case ConditionOutcome.Show:
                    if (!met) visible = false;
                    break;
                case ConditionOutcome.Hide:
                    if (met) visible = false;
                    break;
                case ConditionOutcome.Enable:
                    if (!met) enabled = false;
                    break;
                case ConditionOutcome.Disable:
                    if (met) enabled = false;
                    break;
                case ConditionOutcome.WarningHighlight:
                    if (met) highlight = true;
                    break;
            }
        }

        // Folds a set of boolean results by an all/any match. `counted` reports whether ANY result was contributed
        // (blank/empty conditions are filtered out by the caller before they get here), so an all-blank group or
        // rule can be skipped entirely rather than defaulting to a spurious true/false.
        internal static bool FoldMatch(ConditionMatch match, List<bool> results, out bool counted)
        {
            bool all = true, any = false;
            counted = results.Count > 0;
            foreach (var r in results)
            {
                all &= r;
                any |= r;
            }
            return match == ConditionMatch.All ? all : any;
        }

        /// <summary>
        /// Evaluates one top-level item (a bare condition or a one-level group) against <paramref name="serializedObject"/>.
        /// Returns false in <paramref name="counted"/> when the item has no effective (non-blank) condition, so the
        /// caller can leave it out of the rule's fold.
        /// </summary>
        internal static bool EvaluateItem(ConditionGroup item, SerializedObject serializedObject, string basePath, out bool counted)
        {
            counted = false;
            if (item == null || item.Conditions == null) return true;

            if (!item.IsGroup)
            {
                if (item.Conditions.Count == 0) return true;
                var c = item.Conditions[0];
                if (string.IsNullOrEmpty(c.PropertyPath)) return true;
                counted = true;
                return ConditionEvaluator.Evaluate(c, serializedObject, ResolvePath(basePath, c.PropertyPath));
            }

            var results = new List<bool>();
            foreach (var c in item.Conditions)
            {
                if (string.IsNullOrEmpty(c.PropertyPath)) continue;
                results.Add(ConditionEvaluator.Evaluate(c, serializedObject, ResolvePath(basePath, c.PropertyPath)));
            }
            return FoldMatch(item.Match, results, out counted);
        }

        /// <summary>Evaluates one rule's full expression. <paramref name="counted"/> is false when the rule has no effective condition (so it applies no outcome).</summary>
        internal static bool EvaluateRule(ConditionRule rule, SerializedObject serializedObject, string basePath, out bool counted)
        {
            counted = false;
            if (rule == null || rule.Items == null) return true;

            var results = new List<bool>();
            foreach (var item in rule.Items)
            {
                bool met = EvaluateItem(item, serializedObject, basePath, out bool itemCounted);
                if (itemCounted) results.Add(met);
            }
            return FoldMatch(rule.Match, results, out counted);
        }

        /// <summary>Folds every rule into the node's net display state (visible / enabled / warning-highlight).</summary>
        internal static void EvaluateRules(List<ConditionRule> rules, SerializedObject serializedObject, string basePath,
            out bool visible, out bool enabled, out bool highlight)
        {
            visible = true;
            enabled = true;
            highlight = false;
            if (rules == null) return;

            foreach (var rule in rules)
            {
                bool met = EvaluateRule(rule, serializedObject, basePath, out bool counted);
                if (counted)
                    ApplyOutcome(rule.Outcome, met, ref visible, ref enabled, ref highlight);
            }
        }

        private static void SetupTracking(VisualElement element, List<ConditionRule> rules, SerializedObject serializedObject, string basePath, bool debugConditions, bool designMode)
        {
            // Whether THIS binding may log the evaluator's "property path not found" diagnostic, decided ONCE
            // at bind time and then carried by every re-evaluation below. Two reasons it can't be read live:
            //   1. `reevaluate` also runs from the member-driver poll and the debug-overlay self-heal — long
            //      after the synchronous build scope that set ConditionEvaluator.SuppressWarnings has ended.
            //      A designer build used to stay quiet for its first pass and then warn 200ms later anyway.
            //   2. designMode IS an authoring surface: a half-typed condition driver ("i", "in", "inv" …) is a
            //      normal intermediate state on the way to a real path, not a defect. Each keystroke commits a
            //      distinct path, so the evaluator's one-warning-per-type+path dedup does NOT save you — it
            //      logs once PER PREFIX and poisons the ledger with paths that never existed.
            // The live render paths (designMode:false, outside any suppress scope) still warn exactly as before.
            bool quiet = designMode || ConditionEvaluator.SuppressWarnings;

            // Tracks the previous visibility across re-evaluations so the field springs IN only on a live
            // hidden→visible flip — never on the initial pass (which would pop-in storm on every inspector
            // open) and never when hiding (collapse stays instant). null = no pass yet.
            bool? lastVisible = null;
            Action evaluate = () =>
            {
                // Design canvas with Debug Conditions OFF (the default authoring surface): NEVER actually hide or
                // disable — the author has to see and select every field to style it. Just show a subtle corner
                // HIDDEN/DISABLED pip so the condition state stays visible. The real hide/disable happens in Live
                // Preview; the full reveal-in-place wash + hover card is what "Debug Conditions" turns on.
                if (designMode && !debugConditions)
                {
                    var pipReport = ConditionEvaluationReport.Build(rules, serializedObject, basePath);
                    ConditionDebugOverlay.ApplyDesignPip(element, pipReport);
                    return;
                }

                if (!debugConditions)
                {
                    EvaluateRules(rules, serializedObject, basePath, out bool visible, out bool enabled, out bool highlight);

                    bool animateIn = lastVisible == false && visible;
                    // Route through the visibility arbiter — search may ALSO be hiding this element, and a
                    // direct display write here would reveal a search-filtered field on every re-evaluation.
                    ElementVisibility.SetHidden(element, ElementVisibility.Reason.Condition, !visible);
                    // The PopIn gate stays condition-owned (lastVisible), but a field the search filter is
                    // currently hiding must not spring: it isn't actually appearing on screen.
                    if (animateIn && !ElementVisibility.IsHiddenBy(element, ElementVisibility.Reason.SearchFilter))
                        EditorSpringFX.PopIn(element); // spring the field in when a condition reveals it
                    element.SetEnabled(enabled);
                    // SetEnabled's dim is subtree opacity, and UI Toolkit composites that PER ELEMENT — a
                    // slider inside a Disable-outcome node would go see-through (the track reads through
                    // the knob). Re-home it in colour. Explicit on purpose: an enabled-state change fires
                    // no event of any kind, so this is the only moment anything can react to the flip.
                    // A no-op when the node holds no slider.
                    BindingFactory.RefreshDisabledKnobsUnder(element);
                    element.EnableInClassList("sasq-warning-highlight", highlight);
                    lastVisible = visible;
                    return;
                }

                // Debug mode: never collapse — reveal the node in place and explain why it would be hidden/disabled.
                // Clearing the Condition reason (rather than writing Flex directly) keeps a concurrent search
                // filter's hide intact.
                var report = ConditionEvaluationReport.Build(rules, serializedObject, basePath);
                ElementVisibility.SetHidden(element, ElementVisibility.Reason.Condition, false);
                // Do NOT SetEnabled(false): a disabled subtree wouldn't receive the hover pointer events the card
                // needs. Read-only is enforced by the pickable overlay (PickingMode.Position) that sits on top.
                element.EnableInClassList("sasq-warning-highlight", report.Highlight);
                ConditionDebugOverlay.Apply(element, report, designMode);
            };

            // Every entry point below goes through this, so the quiet decision holds for the initial pass, the
            // property-change callbacks, the member-driver poll and the overlay self-heal alike. Save/restore
            // rather than assign/clear: a nested build inside a suppressing scope must not un-suppress it.
            Action reevaluate = evaluate;
            if (quiet)
            {
                reevaluate = () =>
                {
                    bool prevSuppress = ConditionEvaluator.SuppressWarnings;
                    ConditionEvaluator.SuppressWarnings = true;
                    try { evaluate(); }
                    finally { ConditionEvaluator.SuppressWarnings = prevSuppress; }
                };
            }

            bool hasMemberDriver = false;
            foreach (var rule in rules)
            {
                if (rule.Items == null) continue;
                foreach (var item in rule.Items)
                {
                    if (item?.Conditions == null) continue;
                    foreach (var c in item.Conditions)
                    {
                        if (string.IsNullOrEmpty(c.PropertyPath)) continue;
                        var prop = serializedObject.FindProperty(ResolvePath(basePath, c.PropertyPath));
                        if (prop != null)
                        {
                            element.TrackPropertyValueWithRefresh(prop, _ => reevaluate());
                        }
                        else
                        {
                            // No serialized property → a bool-member driver (computed property/method) or a
                            // built-in like IsInPlayMode. Neither has anything to TrackPropertyValue against, so
                            // poll them gently while attached instead. (Play mode is why the built-ins ride this
                            // path rather than an EditorApplication event: the poll is already lifetime-scoped to
                            // the element, so there is no handler to leak when the inspector goes away.)
                            hasMemberDriver = true;
                        }
                    }
                }
            }

            if (hasMemberDriver)
            {
                element.schedule.Execute(() => reevaluate()).Every(MemberDriverPollMs);
            }

            if (debugConditions)
            {
                // A live-inspector / live-preview field is a PropertyField, which CLEARS its children when its
                // serialized binding executes (on attach) — wiping an overlay added during the detached build pass,
                // and even a one-shot re-apply can lose the race with that deferred clear. Self-heal instead: on any
                // relayout, if the overlay is missing, re-evaluate to (re)add it. The check skips when the overlay
                // is already present (the common case), and Apply is geometry-neutral (the overlay is absolute), so
                // this can't loop. (Design mode wraps the field in a stable element, so it's never wiped.)
                element.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (element.Q<VisualElement>(ConditionDebugOverlay.OverlayName) == null) reevaluate();
                });
            }

            // Execute an initial evaluation pass
            reevaluate();
        }

        /// <summary>True if <paramref name="node"/> or any descendant (container children / collection item layout) carries conditions — used to gate the live-inspector debug toggle.</summary>
        public static bool LayoutHasConditions(LayoutNode node)
        {
            if (node == null) return false;
            // Query only — check both the rule list and the legacy list without forcing migration (mutation).
            if (node.ConditionRules != null && node.ConditionRules.Count > 0) return true;
            if (node.Conditions != null && node.Conditions.Count > 0) return true;

            if (node is ContainerNode container && container.Children != null)
            {
                foreach (var child in container.Children)
                    if (LayoutHasConditions(child)) return true;
            }
            else if (node is CollectionNode collection)
            {
                if (LayoutHasConditions(collection.ItemLayout)) return true;
            }

            return false;
        }
    }
}
