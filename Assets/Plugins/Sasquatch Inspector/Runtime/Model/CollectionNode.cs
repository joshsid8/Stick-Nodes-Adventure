using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A layout node for an array/list property, rendered as a list or table with reorder, optional search,
    /// per-element item layout, and polymorphic ([SerializeReference]) element type pickers.
    /// </summary>
    [Serializable]
    public class CollectionNode : LayoutNode
    {
        [SerializeField] private string propertyPath;
        [SerializeReference] private LayoutNode itemLayout;
        [SerializeField] private CollectionDisplayMode displayMode = CollectionDisplayMode.List;
        [SerializeField] private CollectionReorderStyle reorderStyle = CollectionReorderStyle.DragAndDrop;
        [SerializeField] private bool required;
        [SerializeField] private string requiredMessage;
        [SerializeField] private string validationMethod;
        [SerializeField] private string validationMessage;
        [SerializeField] private bool searchable = true;
        [UnityEngine.Serialization.FormerlySerializedAs("minimizable")] [SerializeField] private bool expandable = true;
        [UnityEngine.Serialization.FormerlySerializedAs("startExpanded")] [SerializeField] private bool defaultToOpen = true;
        // Per-ELEMENT in-line editing for object-reference elements (see ElementInlineEditor). Appended and
        // default-false → old layouts deserialize to the previous "bare object field per row" behavior.
        [SerializeField] private bool elementInlineEditor;
        [SerializeField] private bool elementDefaultToOpen;
        // --- Large-collection ergonomics (all ON by default; gated by size at render time so small lists
        // stay unchanged). searchProperty scopes the existing search to ONE element field; pagination only
        // builds the current page's rows (the perf win for 100+ item lists); sorting adds clickable Table
        // headers + a List-mode sort dropdown (view-only — the array is never mutated). New fields with
        // these initializers default ON for both new nodes AND old saved layouts (SerializeReference). ---
        [SerializeField] private string searchProperty = "";
        [SerializeField] private bool enablePagination = true;
        [SerializeField] private int pageSize = 25;
        // Page-navigator style. ButtonsOnly preserves the original prev/next look for every existing layout.
        [SerializeField] private PaginationDisplayStyle paginationDisplayStyle = PaginationDisplayStyle.ButtonsOnly;
        [SerializeField] private bool enableSorting = true;
        // --- Polymorphic ([SerializeReference]) element menu options. Read only when the collection's
        // elements are managed references rendered as type pickers (auto ItemLayout); propagated onto each
        // element's BindingNode by LayoutTreeBuilder. Mirror the DropDown widget's menu styling. ---
        [SerializeField] private bool polymorphicSearchable = true;
        // Alternating-row (zebra) striping for List/Table rows. Opt-in (default off) so existing collections are
        // unchanged; when on, odd visible rows get zebraColor (unset => a faint theme wash). Appended → old
        // layouts deserialize clean.
        [SerializeField] private bool zebraStriping;
        [SerializeField] private OverridableColor zebraColor;
        // Per-element separator chrome. Zebra tells rows apart by TINT, which stops working once a row is tall
        // (an in-line editor row loses its boundary somewhere in the middle); this marks the boundary itself.
        // Default None (2026-07-31, user call): separators are chrome you opt into, not chrome you clear away.
        // A table already carries a header rule and a divider per column, so a per-row hairline there is noise
        // on top of noise; a short list rarely needs one either. Reach for Line on a busy list, and Card on
        // tall in-line-editor rows (its left rail is the only cue that scales with row height).
        // None is also 0, so a layout that predates the field revives on it too.
        [SerializeField] private CollectionRowSeparator rowSeparator = CollectionRowSeparator.None;
        [SerializeField] private OverridableColor rowSeparatorColor;
        // The List's container-level look: shell, index gutter, density, ordinal treatment. Deliberately
        // ORTHOGONAL to rowSeparator above, which owns the ROW surface — every style works with every frame,
        // so the two never have to be reasoned about together. Rows is 0, so a layout that predates the field
        // revives on the default and looks like every other list.
        [SerializeField] private CollectionListStyle listStyle = CollectionListStyle.Rows;
        // Custom header text. Empty/null = the property's own display name (the original behavior, so old
        // layouts are unchanged); HiddenSentinel = render no title at all. Appended → old layouts deserialize
        // clean. The BindingNode.LabelOverride convention, one level up: a collection is a named group, and its
        // name had no reason to be locked to the field it binds.
        [SerializeField] private string titleOverride;
        [SerializeField] private OverridableColor dropdownPopupBackground;
        [SerializeField] private OverridableColor dropdownItemHoverBackground;
        [SerializeField] private OverridableColor dropdownItemHoverColor;
        [SerializeField] private TextStyle dropdownItemTextStyle;
        // --- Custom Widget display mode. When DisplayMode == CustomWidget the whole array is rendered by the
        // referenced SasquatchCustomWidget (the array feeds its primary list input); non-primary inputs map to
        // sibling properties like the field-level widget. Appended → old layouts deserialize clean (guid "" /
        // empty list). Mirrors BindingNode's customWidgetGuid/customWidgetInputs. ---
#if SASQ_CUSTOM_WIDGET
        [SerializeField] private string customWidgetGuid;
        [SerializeField] private List<CustomWidgetInputMap> customWidgetInputs = new List<CustomWidgetInputMap>();
#endif

        public string PropertyPath
        {
            get => propertyPath;
            set => propertyPath = value;
        }

        public LayoutNode ItemLayout
        {
            get => itemLayout;
            set => itemLayout = value;
        }

        /// <summary>
        /// Render the collection as stacked rows (<see cref="CollectionDisplayMode.List"/>, default)
        /// or a column-per-field table (<see cref="CollectionDisplayMode.Table"/>). Defaults to List,
        /// which is also what a layout saved without this field deserializes to.
        /// </summary>
        public CollectionDisplayMode DisplayMode
        {
            get => displayMode;
            set => displayMode = value;
        }

        /// <summary>
        /// How the live inspector lets the user reorder elements (drag handle, up/down arrows, or both).
        /// Defaults to <see cref="CollectionReorderStyle.DragAndDrop"/>.
        /// </summary>
        public CollectionReorderStyle ReorderStyle
        {
            get => reorderStyle;
            set => reorderStyle = value;
        }

        public bool Required
        {
            get => required;
            set => required = value;
        }

        public string RequiredMessage
        {
            get => requiredMessage;
            set => requiredMessage = value;
        }

        public string ValidationMethod
        {
            get => validationMethod;
            set => validationMethod = value;
        }

        public string ValidationMessage
        {
            get => validationMessage;
            set => validationMessage = value;
        }

        [SerializeField] private TextStyle headerTextStyle;

        /// <summary>Text styling for this collection's header/label text.</summary>
        public TextStyle HeaderTextStyle
        {
            get => headerTextStyle;
            set => headerTextStyle = value;
        }

        /// <summary>
        /// Custom title for this collection's header row. Empty/null means "use the bound property's display
        /// name" (the default); <see cref="LayoutNode.HiddenSentinel"/> means "render no title" — the designer
        /// stores it when you clear the Title field. Read through <see cref="EffectiveTitleOverride"/> /
        /// <see cref="IsTitleHidden"/> rather than directly.
        /// </summary>
        public string TitleOverride
        {
            get => titleOverride;
            set => titleOverride = value;
        }

        /// <summary>True when <see cref="TitleOverride"/> is explicitly blanked (no title text at all) rather
        /// than merely unset. The element-count suffix belongs to the title, so it is hidden along with it.</summary>
        public bool IsTitleHidden => titleOverride == HiddenSentinel;

        /// <summary>The custom title text, or <c>null</c> when the title is unset (use the property's display
        /// name) or hidden (callers check <see cref="IsTitleHidden"/>). Never returns the hidden sentinel.</summary>
        public string EffectiveTitleOverride =>
            (string.IsNullOrEmpty(titleOverride) || titleOverride == HiddenSentinel) ? null : titleOverride;

        public bool Searchable
        {
            get => searchable;
            set => searchable = value;
        }

        public bool Expandable
        {
            get => expandable;
            set => expandable = value;
        }

        public bool DefaultToOpen
        {
            get => defaultToOpen;
            set => defaultToOpen = value;
        }

        /// <summary>
        /// Draw each element that is a <see cref="UnityEngine.Object"/> reference as an In-Line Editor row —
        /// the object field plus that object's own inspector, under a chevron — instead of a bare object
        /// field you can only reassign. Off by default, so existing collections are unchanged; ignored when
        /// an <see cref="ItemLayout"/> is designed (that template already says how a row looks) and when the
        /// elements aren't object references.
        /// </summary>
        public bool ElementInlineEditor
        {
            get => elementInlineEditor;
            set => elementInlineEditor = value;
        }

        /// <summary>Whether <see cref="ElementInlineEditor"/> rows start expanded. Off by default: a long
        /// list of open editors is unreadable, and a collapsed row costs nothing until you open it.</summary>
        public bool ElementDefaultToOpen
        {
            get => elementDefaultToOpen;
            set => elementDefaultToOpen = value;
        }

        /// <summary>
        /// Optional element field path the search box filters on (e.g. "itemName"). Empty/null = search
        /// every field (the original behavior). Honored by both List and Table search.
        /// </summary>
        public string SearchProperty
        {
            get => searchProperty;
            set => searchProperty = value;
        }

        /// <summary>Render the collection one page at a time (default ON). Only takes effect once the
        /// visible row count exceeds <see cref="PageSize"/>, so small lists are unaffected.</summary>
        public bool EnablePagination
        {
            get => enablePagination;
            set => enablePagination = value;
        }

        /// <summary>Rows per page when <see cref="EnablePagination"/> is on. Clamped to at least 1.</summary>
        public int PageSize
        {
            get => pageSize;
            set => pageSize = Mathf.Max(1, value);
        }

        /// <summary>How the page navigator is shown (prev/next buttons, clickable dots, or both) when
        /// <see cref="EnablePagination"/> is on. Defaults to <see cref="PaginationDisplayStyle.ButtonsOnly"/>
        /// so existing layouts keep their original look.</summary>
        public PaginationDisplayStyle PaginationDisplayStyle
        {
            get => paginationDisplayStyle;
            set => paginationDisplayStyle = value;
        }

        /// <summary>Allow sorting the displayed rows (Table column-header clicks + a List-mode sort
        /// dropdown). View-only: the underlying array is never reordered. Default ON.</summary>
        public bool EnableSorting
        {
            get => enableSorting;
            set => enableSorting = value;
        }

        /// <summary>Whether polymorphic element type pickers show a type-to-search box. Defaults ON.</summary>
        public bool PolymorphicSearchable
        {
            get => polymorphicSearchable;
            set => polymorphicSearchable = value;
        }

        /// <summary>Alternating-row (zebra) striping for List/Table rows. Off by default; when on, odd visible
        /// rows are tinted with <see cref="ZebraColor"/>.</summary>
        public bool ZebraStriping
        {
            get => zebraStriping;
            set => zebraStriping = value;
        }

        /// <summary>The odd-row tint used when <see cref="ZebraStriping"/> is on; unset resolves to a faint
        /// theme wash.</summary>
        public OverridableColor ZebraColor
        {
            get => zebraColor;
            set => zebraColor = value;
        }

        /// <summary>How each element is separated from the next — nothing
        /// (<see cref="CollectionRowSeparator.None"/>, the default), a hairline between rows
        /// (<see cref="CollectionRowSeparator.Line"/>), or a per-row card with a left edge rail
        /// (<see cref="CollectionRowSeparator.Card"/>, the one that survives tall in-line editor rows).
        /// A Table honours Card too — the tray is drawn tighter and its header is inset to match, so the
        /// columns stay under their names.</summary>
        public CollectionRowSeparator RowSeparator
        {
            get => rowSeparator;
            set => rowSeparator = value;
        }

        /// <summary>Color of the separator: the hairline in <see cref="CollectionRowSeparator.Line"/> mode,
        /// the left edge rail in <see cref="CollectionRowSeparator.Card"/> mode. Unset resolves to a theme
        /// default for each (a faint ink rule / a quiet border rail).</summary>
        public OverridableColor RowSeparatorColor
        {
            get => rowSeparatorColor;
            set => rowSeparatorColor = value;
        }

        /// <summary>The List's container-level look — shell, index gutter, density and ordinal treatment
        /// (<see cref="CollectionListStyle"/>). Orthogonal to <see cref="RowSeparator"/>, which owns the row
        /// surface: every style composes with every frame. List mode only; Table has its own grid chrome.</summary>
        public CollectionListStyle ListStyle
        {
            get => listStyle;
            set => listStyle = value;
        }

        /// <summary>Background color of the polymorphic element type-picker popup.</summary>
        public OverridableColor DropdownPopupBackground
        {
            get => dropdownPopupBackground;
            set => dropdownPopupBackground = value;
        }

        /// <summary>Hover/selected background color of items in the polymorphic element type-picker popup.</summary>
        public OverridableColor DropdownItemHoverBackground
        {
            get => dropdownItemHoverBackground;
            set => dropdownItemHoverBackground = value;
        }

        /// <summary>Hover/selected text color of items in the polymorphic element type-picker popup.</summary>
        public OverridableColor DropdownItemHoverColor
        {
            get => dropdownItemHoverColor;
            set => dropdownItemHoverColor = value;
        }

        /// <summary>Text styling for items in the polymorphic element type-picker popup (the 'T' button).</summary>
        public TextStyle DropdownItemTextStyle
        {
            get => dropdownItemTextStyle;
            set => dropdownItemTextStyle = value;
        }

#if SASQ_CUSTOM_WIDGET
        /// <summary>GUID of the <c>SasquatchCustomWidget</c> that renders the whole array when
        /// <see cref="DisplayMode"/> is <see cref="CollectionDisplayMode.CustomWidget"/>. Empty = none (renders
        /// as the stock list).</summary>
        public string CustomWidgetGuid
        {
            get => customWidgetGuid;
            set => customWidgetGuid = value;
        }

        /// <summary>Maps the custom widget's NON-primary inputs to sibling property paths (the primary input is
        /// the array itself). Sibling-relative like conditions. Never null after construction; use-site guards
        /// cover legacy layouts that predate the field.</summary>
        public List<CustomWidgetInputMap> CustomWidgetInputs
        {
            get => customWidgetInputs;
            set => customWidgetInputs = value;
        }
#endif
    }
}
