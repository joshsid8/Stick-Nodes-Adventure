using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>How one indexed entry relates to the current query after <see cref="InspectorSearchIndex.Resolve"/>.</summary>
    public enum SearchEntryState
    {
        /// <summary>Neither a match nor related to one — the filter hides it.</summary>
        Hidden,
        /// <summary>Its own haystack matched every query token.</summary>
        Match,
        /// <summary>An ancestor of a match — stays visible so the match has a path to render in.</summary>
        AncestorOfMatch,
        /// <summary>Inside a matched CONTAINER — matching a group's title reveals its whole contents.</summary>
        DescendantOfMatch,
    }

    /// <summary>One searchable node element in the built inspector tree.</summary>
    public sealed class SearchEntry
    {
        public VisualElement Element;
        public LayoutNode Node;
        /// <summary>Nearest node-bearing ancestor's entry (null at the root).</summary>
        public SearchEntry Parent;
        /// <summary>Precomputed lowercase searchable text (label + raw member name + tooltip, camelCase-split).</summary>
        public string Haystack;
        /// <summary>True for grouping containers whose title-match reveals their contents.</summary>
        public bool IsContainer;
        /// <summary>Resolution result for the current query (see <see cref="InspectorSearchIndex.Resolve"/>).</summary>
        public SearchEntryState State;
    }

    /// <summary>
    /// The inspector field search's model: one walk of the BUILT element tree per build collects every
    /// node-bearing element with a precomputed haystack, and <see cref="Resolve"/> classifies each entry
    /// against a query. Pure logic — applying visibility/stripes/expansion is <see cref="InspectorFieldSearch"/>'s
    /// job. The element walk (not a node-tree walk) gives text and hierarchy in a single O(n) pass; the only
    /// node→element lookup in the codebase is O(n) per node, which would make a node walk O(n²).
    /// </summary>
    public sealed class InspectorSearchIndex
    {
        public readonly List<SearchEntry> Entries = new List<SearchEntry>();

        /// <summary>How many non-container entries exist — the "is search worth offering" gate input.</summary>
        public int SearchableLeafCount
        {
            get
            {
                int n = 0;
                foreach (var e in Entries)
                    if (!e.IsContainer)
                        n++;
                return n;
            }
        }

        /// <summary>
        /// Walks the built tree from <paramref name="buildRoot"/> and indexes every element that carries a
        /// <see cref="LayoutNode"/> (registry first, <c>userData</c> fallback — the documented pairing from
        /// LayoutTreeBuilder). Deliberate boundaries: the walk does NOT descend into a collection's rows
        /// (collections own a working row search of their own; the collection is indexed as one leaf), and it
        /// does not cross into a nested/embedded editor's own built tree (that editor indexes itself).
        /// </summary>
        /// <param name="includeTypeNames">
        /// Also index WHAT each node is — "divider", "slider", "image" and their synonyms. On for the Layout
        /// Designer's canvas (you navigate a layout by structure), off for the live inspector (you navigate an
        /// inspector by data). See <see cref="BuildHaystack(LayoutNode, bool)"/>.
        /// </param>
        public static InspectorSearchIndex Build(VisualElement buildRoot, bool includeTypeNames = false)
        {
            var index = new InspectorSearchIndex { _includeTypeNames = includeTypeNames };
            if (buildRoot != null) Walk(buildRoot, null, index);
            return index;
        }

        private bool _includeTypeNames;

        private static void Walk(VisualElement element, SearchEntry parent, InspectorSearchIndex index)
        {
            // Never cross into another editor's content: an embedded/in-line editor hosts its OWN layout
            // (and may host its own search bar) — indexing it from out here would double-filter it. The walk
            // starts at the build root (never itself an InspectorElement), so any InspectorElement below is a
            // nested editor by definition; the two class checks cover the codebase's nested-scope stamps.
            if (element is UnityEditor.UIElements.InspectorElement) return;
            if (element.ClassListContains(EmbeddedEditorHost.ContainerClass)) return;
            if (element.ClassListContains(SasquatchInspectorHeaderPencil.NestedEditorScopeClass)) return;

            var node = LayoutNodeRegistry.GetNode(element) ?? element.userData as LayoutNode;

            // The Script Header now HOSTS the inline search field (InspectorFieldSearch), so it must never be
            // indexed — a decorative header carries no haystack and would resolve to Hidden on any query,
            // collapsing the very bar the user is typing into. Skip it and its subtree entirely.
            if (node is ScriptHeaderNode) return;

            var current = parent;
            if (node != null && node != parent?.Node)
            {
                current = new SearchEntry
                {
                    Element = element,
                    Node = node,
                    Parent = parent,
                    Haystack = BuildHaystack(node, index._includeTypeNames),
                    IsContainer = node is ContainerNode,
                };
                index.Entries.Add(current);

                // Collection boundary: index the collection itself (its label is searchable), never its rows —
                // rows are paginated/rebuilt per page and the collection carries its own row search.
                if (node is CollectionNode) return;
            }

            foreach (var child in element.hierarchy.Children())
                Walk(child, current, index);
        }

        /// <summary>
        /// Builds the searchable text for <paramref name="node"/>: the visible label, the raw member name
        /// (so <c>moveSpeed</c> is findable even under a label override), and — for bound fields — the tooltip.
        /// Everything is lowercased and camelCase-split so the query <c>move speed</c> matches all spellings.
        /// </summary>
        public static string BuildHaystack(LayoutNode node) => BuildHaystack(node, includeTypeNames: false);

        /// <summary>
        /// As above, plus — when <paramref name="includeTypeNames"/> — what the node IS: its widget kind and
        /// display style, with the everyday synonyms ("divider" also answers to "separator", "space" to "gap").
        ///
        /// That dimension is for the LAYOUT DESIGNER, whose canvas you navigate by structure — "where's my
        /// divider?" is the natural first query there. It stays OFF for the live inspector, which you navigate
        /// by DATA: there, "toggle" means "the field called toggle", and matching every bool in the component
        /// instead would bury the row the user actually wanted.
        /// </summary>
        public static string BuildHaystack(LayoutNode node, bool includeTypeNames)
        {
            var sb = new StringBuilder();
            switch (node)
            {
                case BindingNode b:
                    Append(sb, b.EffectiveLabelOverride ?? Nicify(Leaf(b.PropertyPath)));
                    Append(sb, Leaf(b.PropertyPath));
                    Append(sb, b.Tooltip);
                    break;
                case ContainerNode c:
                    Append(sb, c.Title);
                    Append(sb, c.TabLabel);
                    break;
                case CollectionNode col:
                    Append(sb, Nicify(Leaf(col.PropertyPath)));
                    Append(sb, Leaf(col.PropertyPath));
                    break;
                case ButtonNode btn:
                    Append(sb, btn.ButtonText);
                    Append(sb, btn.MethodName);
                    break;
                case PropertyNode p:
                    Append(sb, string.IsNullOrEmpty(p.LabelOverride) ? Nicify(p.PropertyName) : p.LabelOverride);
                    Append(sb, p.PropertyName);
                    break;

                // Decorative nodes carry no editable VALUE, but the ones that show WORDS are absolutely
                // searchable by those words — a divider captioned "Combat" is exactly how its author thinks of
                // it, and the manual has always promised headers and titles are matched. (Spaces and image
                // banners have no text; they surface via the type dimension below, or as trail.)
                case LabeledDividerNode div:
                    Append(sb, div.Title);
                    break;
                case TextBlockNode txt:
                    Append(sb, txt.Text);
                    break;
                case HelpBoxNode help:
                    Append(sb, help.Message);
                    Append(sb, help.CollapsedSummary);
                    break;
            }

            if (includeTypeNames) Append(sb, TypeTerms(node));
            return sb.ToString();
        }

        /// <summary>
        /// What the node IS, in the words a designer would reach for. Deliberately generous with synonyms —
        /// the failure this fixes is a user typing a perfectly reasonable name for a widget and getting
        /// nothing back, so every kind answers to more than one word.
        /// </summary>
        private static string TypeTerms(LayoutNode node)
        {
            switch (node)
            {
                case BindingNode b:
                    // The DISPLAY style only ("slider", "progress bar", "rating"). `Default` is skipped: it's
                    // most fields, so including it would make "default" select the entire layout.
                    return b.DisplayType == DisplayType.Default ? "field" : "field " + SplitWords(b.DisplayType.ToString());
                case ContainerNode c:
                    return "container group " + SplitWords(c.Type.ToString());
                case CollectionNode col:
                    return "collection " + SplitWords(col.DisplayMode.ToString());
                case ButtonNode _:          return "button action";
                case LabeledDividerNode _:  return "divider separator rule line";
                case SpaceNode _:           return "space spacer gap padding";
                case HelpBoxNode _:         return "help box note callout message";
                case ImageBannerNode _:     return "image banner picture texture logo";
                case TextBlockNode _:       return "text label paragraph note";
                case EmbeddedEditorNode _:  return "embedded editor nested inline";
                case PropertyNode _:        return "property";
                case ScriptHeaderNode _:    return "script header";
            }
            return null;
        }

        private static string Leaf(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath)) return null;
            int dot = propertyPath.LastIndexOf('.');
            return dot >= 0 ? propertyPath.Substring(dot + 1) : propertyPath;
        }

        // UnityEditor.ObjectNames.NicifyVariableName equivalent for haystack purposes ("m_MoveSpeed" →
        // "Move Speed"-ish). SplitWords below already inserts the camel spaces, so this only strips the
        // common serialized-name prefixes; display-name parity beyond that isn't needed for matching.
        private static string Nicify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (raw.StartsWith("m_", StringComparison.Ordinal)) raw = raw.Substring(2);
            return raw.TrimStart('_');
        }

        private static void Append(StringBuilder sb, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (sb.Length > 0) sb.Append(' ');
            string lower = text.ToLowerInvariant();
            sb.Append(lower);
            // Also append the camelCase/PascalCase-split form when it differs, so "moveSpeed" is reachable
            // by the query "move speed" (and vice versa — tokens match against BOTH spellings).
            string split = SplitWords(text).ToLowerInvariant();
            if (split != lower)
            {
                sb.Append(' ');
                sb.Append(split);
            }
        }

        /// <summary>Inserts spaces at camelCase word boundaries and converts underscores ("moveSpeed" → "move Speed").</summary>
        public static string SplitWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length + 8);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '_')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && (char.IsLower(text[i - 1]) || char.IsDigit(text[i - 1])))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Splits a query into its whitespace-separated tokens (empty array for a blank query).</summary>
        public static string[] Tokenize(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            return query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// True when EVERY token appears in <paramref name="haystack"/> as a case-insensitive SUBSTRING.
        /// Substring, not subsequence — subsequence matching ("mvsp" → MoveSpeed) lights up half the
        /// inspector on short queries; predictability wins.
        /// </summary>
        public static bool Matches(string haystack, string[] tokens)
        {
            if (string.IsNullOrEmpty(haystack) || tokens == null || tokens.Length == 0) return false;
            foreach (var token in tokens)
            {
                if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Classifies every entry against <paramref name="query"/> in three passes: own-haystack matches,
        /// then ancestors of matches (kept visible so the match can render), then descendants of matched
        /// CONTAINERS (matching a group title reveals its whole contents). Everything else is Hidden.
        /// An empty query marks everything Match (the caller treats that as "no filter").
        /// </summary>
        public void Resolve(string query)
        {
            var tokens = Tokenize(query);
            if (tokens.Length == 0)
            {
                foreach (var e in Entries) e.State = SearchEntryState.Match;
                return;
            }

            foreach (var e in Entries)
                e.State = Matches(e.Haystack, tokens) ? SearchEntryState.Match : SearchEntryState.Hidden;

            // Ancestors of every match stay visible (upgrade only from Hidden — a matched ancestor stays Match).
            foreach (var e in Entries)
            {
                if (e.State != SearchEntryState.Match) continue;
                for (var a = e.Parent; a != null; a = a.Parent)
                {
                    if (a.State == SearchEntryState.Hidden)
                        a.State = SearchEntryState.AncestorOfMatch;
                }
            }

            // Contents of a matched container are revealed wholesale.
            foreach (var e in Entries)
            {
                if (e.State != SearchEntryState.Hidden) continue;
                for (var a = e.Parent; a != null; a = a.Parent)
                {
                    if (a.State == SearchEntryState.Match && a.IsContainer)
                    {
                        e.State = SearchEntryState.DescendantOfMatch;
                        break;
                    }
                }
            }
        }
    }
}
