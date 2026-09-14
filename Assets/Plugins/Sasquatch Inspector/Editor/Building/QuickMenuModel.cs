using System;
using System.Collections.Generic;

namespace SasquatchInspector.Editor
{
    /// <summary>One entry of the unified right-click menu: a labeled action, a disabled item, or a separator.</summary>
    public struct QuickMenuItem
    {
        public string Label;
        public Action Action;
        public bool Enabled;
        public bool IsSeparator;
    }

    /// <summary>
    /// Pure, renderer-agnostic model of the unified right-click menu. Composition code
    /// (QuickMenuController.BuildMenu and its section builders) appends items and separators;
    /// QuickMenuController.Show renders the result (GenericMenu in production, a capture hook in
    /// tests). Keeping the model data-only is what makes menu composition unit-testable without
    /// opening any UI.
    /// </summary>
    public sealed class QuickMenuModel
    {
        public readonly List<QuickMenuItem> Items = new List<QuickMenuItem>();

        /// <summary>Appends an actionable (or, when <paramref name="enabled"/> is false, greyed-out) menu item.</summary>
        public void Add(string label, Action action, bool enabled = true)
        {
            if (string.IsNullOrEmpty(label)) return;
            Items.Add(new QuickMenuItem { Label = label, Action = action, Enabled = enabled });
        }

        /// <summary>
        /// Separator between sections. Leading and doubled separators collapse to nothing, so every
        /// section can call this unconditionally without knowing whether its neighbors contributed.
        /// </summary>
        public void AddSeparator()
        {
            if (Items.Count == 0) return;
            if (Items[Items.Count - 1].IsSeparator) return;
            Items.Add(new QuickMenuItem { IsSeparator = true });
        }

        public bool HasActionableItems
        {
            get
            {
                foreach (var item in Items)
                    if (!item.IsSeparator) return true;
                return false;
            }
        }

        /// <summary>True when a (non-separator) item with <paramref name="label"/> exists.</summary>
        public bool Contains(string label)
        {
            foreach (var item in Items)
                if (!item.IsSeparator && item.Label == label) return true;
            return false;
        }

        /// <summary>The index of the item with <paramref name="label"/>, or -1.</summary>
        public int IndexOf(string label)
        {
            for (int i = 0; i < Items.Count; i++)
                if (!Items[i].IsSeparator && Items[i].Label == label) return i;
            return -1;
        }
    }
}
