using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Resolves the C# field type a <see cref="BindingNode"/> points at, honoring the scope along the
    /// node's index path. Single source of truth shared by the designer's Display Type popup and the
    /// inspector quick-edit popup so both filter widget kinds against the same resolved type.
    ///
    /// Index-path convention (matches the designer's GetNodeIndexPath and the quick-edit popup's
    /// _indexPath): a non-negative step descends into ContainerNode.Children[index]; a -1 step descends
    /// into a CollectionNode.ItemLayout, which switches the scope to the collection's ELEMENT type and
    /// resets the path prefix. A scoped ContainerNode (one carrying its own PropertyPath) contributes
    /// that path as a prefix. A -2 / -3 step descends into a BindingNode's dictionary KEY / VALUE cell
    /// template, switching the scope to that half's type — the dictionary's answer to the -1 step, and the
    /// same idea: one authored node that styles every row. An EMPTY <c>PropertyPath</c> binds the scope
    /// itself — after a -1 step that is the collection's element type (which is how a
    /// <c>List&lt;Vector2&gt;</c> gets a stylable binding at all), and after a -2 / -3 step it is the
    /// dictionary's key or value type. Returns null whenever anything is unresolvable, so callers fall back
    /// to the unfiltered widget list rather than hiding options on a bad guess.
    /// </summary>
    public static class LayoutFieldTypeResolver
    {
        /// <summary>
        /// Index-path step meaning "descend into this binding's dictionary KEY cell template" (and
        /// <see cref="DictionaryValueStep"/> for the VALUE half). The siblings of the collection's -1
        /// ItemLayout step, and appended BELOW it so every existing saved path keeps its meaning.
        /// <para>
        /// Shared with the designer's <c>GetNodeIndexPath</c> / <c>LocateNodeByIndexPath</c> and the
        /// quick-edit popup's own path, which is why they are constants here rather than three literals.
        /// </para>
        /// </summary>
        public const int DictionaryKeyStep = -2;
        public const int DictionaryValueStep = -3;

        /// <summary>Scope-aware resolution of a binding node's bound field <see cref="Type"/> (null when unresolvable). See <see cref="ResolveBoundFieldInfo"/> for the FieldInfo.</summary>
        public static Type ResolveBoundFieldType(Type rootScopeType, LayoutNode root,
                                                 IReadOnlyList<int> indexPath, BindingNode bn)
        {
            var fieldInfo = ResolveBoundFieldInfo(rootScopeType, root, indexPath, bn, out var fieldType);
            return fieldInfo != null ? fieldType : null;
        }

        /// <summary>
        /// Same scope-aware resolution as <see cref="ResolveBoundFieldType"/>, but returns the
        /// <see cref="System.Reflection.FieldInfo"/> (e.g. to read attributes like [Range]) along with the
        /// field type via <paramref name="fieldType"/>. Returns null (and fieldType null) when unresolvable.
        /// </summary>
        public static System.Reflection.FieldInfo ResolveBoundFieldInfo(Type rootScopeType, LayoutNode root,
                                                 IReadOnlyList<int> indexPath, BindingNode bn, out Type fieldType)
        {
            fieldType = null;
            if (rootScopeType == null || root == null || indexPath == null || bn == null) return null;

            Type scopeType = rootScopeType;
            string prefix = string.Empty;
            LayoutNode current = root;
            // The collection FieldInfo whose elements are the current scope, set by each -1 step. An
            // ELEMENT-TEMPLATE binding (empty PropertyPath) has no field of its own to report, and this is
            // the honest answer for it: attributes that shape the elements of a list — [Range] on a
            // List<float>, a [Tooltip] — live on the collection field, so that is what callers must read.
            System.Reflection.FieldInfo scopeField = null;

            foreach (int index in indexPath)
            {
                if (index == DictionaryKeyStep || index == DictionaryValueStep)
                {
                    // A dictionary CELL TEMPLATE. Same move a -1 step makes for a collection — the scope stops
                    // being the object and becomes one half of a pair — except the owner is a BindingNode, not
                    // a CollectionNode, because a dictionary is a WIDGET on a binding rather than a collection.
                    if (!(current is BindingNode dictNode) || string.IsNullOrEmpty(dictNode.PropertyPath)) return null;
                    string dictPath = string.IsNullOrEmpty(prefix) ? dictNode.PropertyPath : prefix + "." + dictNode.PropertyPath;
                    var dictField = BindingFactory.GetFieldInfoFromPropertyPath(scopeType, dictPath, out var dictType);
                    // The SHAPE test is the shared predicate, not a local guess: a node whose Display Type is
                    // not Dictionary (and whose type is not our own SerializedDictionary) renders no table, so
                    // it has no cell to resolve against and the honest answer is null.
                    if (!DisplayTypeCatalog.SupportsDictionaryCellTemplates(dictNode.DisplayType, dictType)) return null;
                    var halfType = BindingFactory.DictionaryHalfType(dictType, index == DictionaryKeyStep ? 0 : 1);
                    if (halfType == null) return null;
                    scopeType = halfType;
                    prefix = string.Empty;
                    current = index == DictionaryKeyStep ? dictNode.DictionaryKeyTemplate : dictNode.DictionaryValueTemplate;
                    // Attributes that shape a dictionary's cells live on the DICTIONARY field, exactly as a
                    // list's [Range] lives on the list field — so that is the FieldInfo a template reports.
                    scopeField = dictField;
                }
                else if (index == -1)
                {
                    if (!(current is CollectionNode col) || string.IsNullOrEmpty(col.PropertyPath)) return null;
                    string colPath = string.IsNullOrEmpty(prefix) ? col.PropertyPath : prefix + "." + col.PropertyPath;
                    var colField = BindingFactory.GetFieldInfoFromPropertyPath(scopeType, colPath, out var colType);
                    Type elementType =
                        colType == null ? null
                        : colType.IsArray ? colType.GetElementType()
                        : (colType.IsGenericType && colType.GetGenericArguments().Length > 0) ? colType.GetGenericArguments()[0]
                        : null;
                    if (elementType == null) return null;
                    scopeType = elementType;
                    prefix = string.Empty;
                    current = col.ItemLayout;
                    scopeField = colField;
                }
                else
                {
                    if (!(current is ContainerNode container) || container.Children == null
                        || index < 0 || index >= container.Children.Count) return null;
                    current = container.Children[index];
                    if (current is ContainerNode scoped && !string.IsNullOrEmpty(scoped.PropertyPath))
                        prefix = string.IsNullOrEmpty(prefix) ? scoped.PropertyPath : prefix + "." + scoped.PropertyPath;
                }
            }

            if (string.IsNullOrEmpty(bn.PropertyPath))
            {
                // An empty path binds THE SCOPE ITSELF rather than a field under it — for a collection's
                // item layout that is the element (the only way to style a List<Vector2>, which has no
                // sub-field to bind). Mirrors LayoutTreeBuilder's resolution exactly, including the
                // root-scope case: with no scope to bind, nothing is bound and the answer is null.
                if (!string.IsNullOrEmpty(prefix))
                    return BindingFactory.GetFieldInfoFromPropertyPath(scopeType, prefix, out fieldType);
                if (scopeField == null) return null;
                fieldType = scopeType;
                return scopeField;
            }

            string fullPath = prefix.Length == 0 ? bn.PropertyPath : prefix + "." + bn.PropertyPath;
            return BindingFactory.GetFieldInfoFromPropertyPath(scopeType, fullPath, out fieldType);
        }
    }
}
