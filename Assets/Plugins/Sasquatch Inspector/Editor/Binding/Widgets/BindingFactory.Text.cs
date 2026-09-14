using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        private static VisualElement CreateTextField(BindingNode node, SerializedProperty prop, string resolvedLabel, out VisualElement alignmentTarget)
        {
            // Set only when the field gets wrapped (the Path/Browse row): the label-column alignment class
            // must ride the INNER TextField, not the plain wrapper (same contract as the Create-button row).
            alignmentTarget = null;

            // A SasquatchString binds its INNER template string, so the whole Text widget — toolbar, rich
            // text preview, resize, path mode — works on it unchanged. The outer property stays the
            // attribute source: [TextArea] / [Multiline] sit on the field the user declared, not on the
            // struct's private `template`.
            string attributePath = prop.propertyPath;
            var sasquatchStringProp = SasquatchStringTemplate(prop) != null ? prop : null;
            if (sasquatchStringProp != null)
            {
                // NO owner bake here, deliberately — building an inspector must perform zero SerializedProperty
                // writes. A template with text but no owner (assigned from code) would resolve better bound, but
                // baking it at BUILD time writes to every object the user merely LOOKS at: a fake prefab override
                // on an instance, a dirtied scene on a plain object. The bake rides the template's own edit
                // callback below, and code-built instances have SasquatchString.Bind(owner).
                prop = SasquatchStringTemplate(prop);
            }

            // Text widget binds a string; on a non-string property (e.g. the field's type changed under a
            // saved layout) fall back to a plain field rather than binding a mismatched type.
            if (prop.propertyType != SerializedPropertyType.String)
                return new PropertyField(prop) { label = resolvedLabel };

            var textField = new TextField();
            textField.label = resolvedLabel;

            bool attributeMultiline = false;
            float minHeight = 0f;
            float maxHeight = 0f;
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj != null)
            {
                var fieldInfo = GetFieldInfoFromPropertyPath(targetObj.GetType(), attributePath, out _);
                if (fieldInfo != null)
                {
                    attributeMultiline = Attribute.IsDefined(fieldInfo, typeof(TextAreaAttribute))
                                      || Attribute.IsDefined(fieldInfo, typeof(MultilineAttribute));

                    var textAreaAttr = Attribute.GetCustomAttribute(fieldInfo, typeof(TextAreaAttribute)) as TextAreaAttribute;
                    if (textAreaAttr != null)
                    {
                        // 18px per line plus 6px padding/border
                        minHeight = textAreaAttr.minLines * 18f + 6f;
                        maxHeight = textAreaAttr.maxLines * 18f + 6f;
                    }
                    else
                    {
                        var multilineAttr = Attribute.GetCustomAttribute(fieldInfo, typeof(MultilineAttribute)) as MultilineAttribute;
                        if (multilineAttr != null)
                        {
                            minHeight = multilineAttr.lines * 18f + 6f;
                            maxHeight = minHeight;
                        }
                    }
                }
            }

            // Resizable forces multiline (you resize to reveal more lines); otherwise multiline follows the
            // [TextArea]/[Multiline] attribute. The rich-text preview is inherently multi-line too.
            bool multiline = attributeMultiline || node.TextResize == TextResizeMode.Resizable || node.RichTextPreview;
            textField.multiline = multiline;
            if (multiline)
            {
                textField.verticalScrollerVisibility = ScrollerVisibility.Auto;
                // Click-to-place-caret instead of click-to-select-everything, plus the stuck-drag safety net.
                // See TextAreaHelper.ApplyMultilineSelectionBehaviour — both are measured parity breaks against
                // Unity's own [TextArea] field, and both land hardest on the rich-text editor, which routes
                // through here (RichTextPreview forces multiline above).
                TextAreaHelper.ApplyMultilineSelectionBehaviour(textField);
            }

            SeedThenBind(textField, prop); // seed BEFORE bind — see SeedThenBind

            // Editing the template re-points its owner. This is where a SasquatchString normally gets bound
            // at all (typing the text IS the user's edit, so no extra dirtying), and it repairs one that was
            // duplicated onto another object still aimed at the original.
            //
            // RegisterUserValueChanged, NOT RegisterValueChangedCallback: a BOUND field is handed a ChangeEvent
            // the moment it attaches, so the plain callback ran this bake on every build — the silent write this
            // whole guard exists to stop. See RegisterUserValueChanged.
            if (sasquatchStringProp != null)
                RegisterUserValueChanged<string>(textField,
                    _ => BakeSasquatchStringSource(sasquatchStringProp, onlyWhenUnset: false));

            if (textField.labelElement != null)
            {
                textField.labelElement.style.display = DisplayStyle.Flex;
                textField.labelElement.style.color = WidgetPalette.LabelPrimary;
            }

            var input = textField.Q<VisualElement>(className: "unity-text-field__input")
                     ?? textField.Q<VisualElement>(className: "unity-base-text-field__input");

            if (input != null)
            {
                if (node.DisplayStyleData.cornerRadius.isOverridden)
                {
                    input.style.borderTopLeftRadius = node.DisplayStyleData.cornerRadius.value;
                    input.style.borderTopRightRadius = node.DisplayStyleData.cornerRadius.value;
                    input.style.borderBottomLeftRadius = node.DisplayStyleData.cornerRadius.value;
                    input.style.borderBottomRightRadius = node.DisplayStyleData.cornerRadius.value;
                }

                // Force multiline text wrap in UI Toolkit.
                if (multiline)
                {
                    textField.style.whiteSpace = WhiteSpace.Normal;
                    input.style.whiteSpace = WhiteSpace.Normal;
                    input.Query<VisualElement>().ForEach(el => el.style.whiteSpace = WhiteSpace.Normal);
                }

                // Placeholder overlay: a muted Label layered over the input, shown whenever the field is empty
                // (regardless of focus) and laid out to match the widget's own multiline/single-line geometry.
                // pickingMode Ignore keeps it from stealing clicks/typing/selection.
                // There is no public TextField placeholder API on the supported editor floor, so this hand-rolled
                // overlay is the stand-in — don't "simplify" it into a native call that doesn't exist on 6000.0.
                if (!string.IsNullOrEmpty(node.TextPlaceholder))
                {
                    var placeholder = new Label(node.TextPlaceholder) { pickingMode = PickingMode.Ignore };
                    placeholder.style.position = Position.Absolute;
                    placeholder.style.left = 4;
                    placeholder.style.right = 4;
                    placeholder.style.color = WidgetPalette.LabelSecondary;
                    placeholder.style.opacity = 0.7f;
                    placeholder.style.overflow = Overflow.Hidden;
                    if (multiline)
                    {
                        placeholder.style.top = 3;
                        placeholder.style.whiteSpace = WhiteSpace.Normal;
                    }
                    else
                    {
                        placeholder.style.top = 0;
                        placeholder.style.bottom = 0;
                        placeholder.style.whiteSpace = WhiteSpace.NoWrap;
                        placeholder.style.textOverflow = TextOverflow.Ellipsis;
                        placeholder.style.unityTextAlign = TextAnchor.MiddleLeft;
                    }
                    void SyncPlaceholder() => placeholder.style.display =
                        string.IsNullOrEmpty(textField.value) ? DisplayStyle.Flex : DisplayStyle.None;
                    textField.RegisterValueChangedCallback(_ => SyncPlaceholder());
                    input.Add(placeholder);
                    SyncPlaceholder();
                }
            }

            // Drag-to-resize handle (bottom edge): Resizable always; Fixed never.
            // The dragged height persists on the node and is re-applied on every build.
            bool resizable = node.TextResize == TextResizeMode.Resizable;
            if (node.TextResize == TextResizeMode.Fixed)
            {
                // An authored Box Height wins outright, and it is the ONLY height control a Fixed box has —
                // there is no grip to drag. So it has to beat the [TextArea]/[Multiline] box the same way a
                // dragged height does on a Resizable one; leaving the attribute's min/max in place would cap
                // the authored height right back down. 0 = unauthored ⇒ the attribute shape, as before.
                if (node.TextBoxHeight > 0f)
                {
                    textField.style.minHeight = StyleKeyword.Null;
                    textField.style.maxHeight = StyleKeyword.Null;
                    textField.style.height = node.TextBoxHeight;
                    if (input != null)
                    {
                        input.style.minHeight = StyleKeyword.Null;
                        input.style.maxHeight = StyleKeyword.Null;
                        input.style.height = StyleKeyword.Null;
                    }
                }
                else
                {
                    // Clear any explicit height that might be set (from previous resizes or styles)
                    textField.style.height = StyleKeyword.Null;
                    if (input != null)
                    {
                        input.style.height = StyleKeyword.Null;
                    }

                    // Fixed respects standard Unity [TextArea] / [Multiline] layout, without resize handles.
                    if (minHeight > 0f)
                    {
                        textField.style.minHeight = minHeight;
                        textField.style.maxHeight = maxHeight;
                        if (input != null)
                        {
                            input.style.minHeight = minHeight;
                            input.style.maxHeight = maxHeight;
                        }
                    }
                }
            }
            else if (resizable)
            {
                // Clear any minHeight/maxHeight constraints that might be lingering
                textField.style.minHeight = StyleKeyword.Null;
                textField.style.maxHeight = StyleKeyword.Null;
                if (input != null)
                {
                    input.style.minHeight = StyleKeyword.Null;
                    input.style.maxHeight = StyleKeyword.Null;
                }

                if (node.TextBoxHeight > 0f)
                {
                    textField.style.height = node.TextBoxHeight;
                }
                else if (minHeight > 0f)
                {
                    // If resizable but not manually resized yet, start at the attribute's min height
                    textField.style.height = minHeight;
                }
                AddTextResizeHandle(textField, node);
            }

            // Rich-text preview opt-in: wrap the (already fully built, resizable) text box in a split-pane with a
            // formatting toolbar and a live formatted preview. Non-string fields never reach here (guarded above).
            if (node.RichTextPreview)
            {
                // Give the raw editor a usable height when it's neither resizable nor a [TextArea] box.
                if (node.TextResize != TextResizeMode.Resizable && minHeight <= 0f)
                    textField.style.minHeight = 56f;
                return BuildRichTextWrapper(node, prop, textField, resolvedLabel);
            }

            // Path/Browse mode: pair the string field with a folder-open icon button that opens a file/folder
            // panel and writes the chosen absolute path back through the binding.
            if (node.TextPathMode == TextPathMode.Path)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                // The editor theme gives .unity-base-field flex-shrink:0, and flex-basis:auto measures the
                // CURRENT string value — a long path would size the field wider than the inspector and shove
                // the Browse button past the right edge. The field's width must come from the row, never from
                // the text it happens to hold.
                textField.style.flexGrow = 1;
                textField.style.flexShrink = 1;
                textField.style.flexBasis = 0f;
                textField.style.minWidth = 0;
                // …and pin its HORIZONTAL margins, because this row is the shape the stylesheet cannot reach.
                // `.sasq-binding-row > .unity-base-field { margin: 0 }` is a CHILD combinator, exact for every
                // other widget (there the row IS the field) — but the wrapper below puts the TextField one
                // level deeper, so the editor theme's default `.unity-base-field { margin: 0 3px }` survives.
                // The 3px LEFT is the visible one: the label column engine hands this row a 3px-narrower label
                // so the INPUT edges still register, which leaves the label's own left edge — and only it —
                // sitting 3px right of every other label in the inspector. Inside a REAL InspectorWindow the
                // field also picks up `unity-base-field__inspector-field`'s margin-right:-2 (see
                // MinMaxSlider's read-out pin), so neither margin can be trusted to be 0. An inline length is
                // the only thing that outranks the inspector's own stylesheet.
                textField.style.marginLeft = 0;
                textField.style.marginRight = 0;

                var browse = new Button(() =>
                {
                    string current = textField.value ?? "";
                    string picked;
                    if (node.PathIsFolder)
                    {
                        string start = string.IsNullOrEmpty(current) ? Application.dataPath : current;
                        picked = EditorUtility.OpenFolderPanel("Select Folder", start, "");
                    }
                    else
                    {
                        string startDir = Application.dataPath;
                        if (!string.IsNullOrEmpty(current))
                        {
                            try { startDir = System.IO.Path.GetDirectoryName(current); } catch { /* malformed path */ }
                        }
                        picked = EditorUtility.OpenFilePanel("Select File", startDir, node.PathFilter ?? "");
                    }
                    if (!string.IsNullOrEmpty(picked)) textField.value = picked; // routes through BindProperty
                });
                // Compact icon-only button (same conventions as the script-header search/debug buttons):
                // centered icon child, empty text, fixed footprint — reads cleaner than a text button
                // and costs the path field far less width.
                browse.AddToClassList("sasq-path-browse-btn");
                browse.tooltip = node.PathIsFolder ? "Browse for folder" : "Browse for file";
                browse.Add(SasquatchIcons.Create(SasquatchIcons.Ids.FolderOpen, 12));
                browse.style.alignItems = Align.Center;
                browse.style.justifyContent = Justify.Center;
                browse.style.width = 22;
                browse.style.height = 18;
                browse.style.paddingLeft = 0;
                browse.style.paddingRight = 0;
                browse.style.paddingTop = 0;
                browse.style.paddingBottom = 0;
                browse.style.marginLeft = 4;
                // The theme's `.unity-button` carries a 3px right margin, and the button is the LAST thing in
                // the row — so it stops the row 3px shy of the right edge every other field reaches. Same
                // defect as the 3px on the left, opposite end.
                browse.style.marginRight = 0;
                browse.style.flexShrink = 0;

                row.Add(textField);
                row.Add(browse);
                alignmentTarget = textField;
                return row;
            }

            return textField;
        }

        // Dragging the handle to at/below this height reverts the field to its natural (auto) height — i.e.
        // back to a single line, the state it was in before the first resize. (A hard min would otherwise
        // trap the field taller than one line forever.)
        //
        // It doubles as the designer's Box Height floor: the numeric row clamps anything in (0..24] back to
        // 0 so a typed height and a dragged one can never disagree about what "collapsed" means.
        internal const float TextBoxCollapseHeight = 24f;

        /// <summary>The Box Height clamp shared by the designer row, the drag and the exporter:
        /// 0 = auto (natural / attribute height), anything above collapses-or-clamps into
        /// [<see cref="TextBoxCollapseHeight"/>..<see cref="TextBoxMaxHeight"/>].</summary>
        internal const float TextBoxMaxHeight = 400f;

        /// <summary>Clamp for an authored Box Height. See <see cref="TextBoxCollapseHeight"/>.</summary>
        internal static float ClampTextBoxHeight(float value)
        {
            if (value <= TextBoxCollapseHeight) return 0f;   // at/below the collapse floor = auto
            return Mathf.Min(value, TextBoxMaxHeight);
        }

        // Height of the bottom strip the resize grip occupies — and, because the grip is drawn INSIDE the
        // input, the padding the text must keep clear of it. One constant so the two can't drift apart.
        private const float GripStripHeight = 8f;

        // A drag handle at the bottom edge of a multiline TextField's input area. Sized by the node's grip size —
        // Compact (28px pill), Large (35% of the width), FullWidth — and justified left/center/right by its
        // GripAlignment (FullWidth spans the whole edge, so alignment doesn't apply there).
        // Dragging sets the field height, stored on the node (TextBoxHeight) and persisted via the layout-edited
        // callback (captured at build time — the same per-build static the table column resizer uses). The
        // resize-vertical cursor comes from USS (.sasq-text-resizer in SasquatchWidgets.uss).
        private static void AddTextResizeHandle(TextField field, BindingNode node)
        {
            var onLayoutEdited = LayoutTreeBuilder.CurrentOnLayoutEdited;
            var handle = MakeGripResizeHandle(node);

            var input = field.Q<VisualElement>(className: "unity-text-field__input")
                     ?? field.Q<VisualElement>(className: "unity-base-text-field__input");

            // The strip is a SIBLING of the input — NEVER a child of it. UI Toolkit scissors an
            // overflow:hidden element to its CONTENT box (CSS clips to the padding box; this does not), and
            // the text input IS overflow:hidden. So a strip parented into the input and pinned to `bottom: 0`
            // lands exactly in the paddingBottom band the reservation below carves out — that is, entirely
            // outside the content box — and not one pixel reaches the screen. The failure is silent and every
            // assertion you would naturally write still passes: measured at 200% UI scale, the input's content
            // rect ended at y=18 and the grip drew at y 18..26, present, sized 136.5x8, display:Flex,
            // opacity 1, correct colour, correct world position, invisible. The reservation and the clip are
            // the same 8px, which is what makes the trap circular — see FieldAffixes.MountInside, which is the
            // identical bug and the identical fix, and ExportedWidgetKit.AttachResizeGrip, which already
            // sibling-mounts and was therefore the only render path drawing this grip at all.
            field.hierarchy.Add(handle);
            handle.style.bottom = 1f;   // just inside the field's bottom border — where the in-input mount drew

            if (input != null)
            {
                // RESERVE the strip the grip sits on. The handle is absolutely positioned against the input's
                // bottom edge, so without this the text flows UNDER the grab zone and the last line's
                // characters can't be clicked or drag-selected at all — the press starts a resize instead
                // (measured: grab y 94..102 inside a text area ending at 102, and a click there never reached
                // the text). Still owed from out here: the padding moves the CONTENT box only, so the grip
                // stays exactly where it was drawn. Same reservation the autocomplete chevron uses.
                input.style.paddingBottom = GripStripHeight;

                // The field is a {label, input} row and the grip belongs to the INPUT column — a FullWidth or
                // left-aligned grip must never reach under the label — so pin the strip's horizontal span to
                // the input, and keep it pinned. The input can MOVE without ever resizing (a label-column drag
                // slides it along the row), so listening on the input alone is not enough. An unresolved
                // offset is NaN, where every comparison is false, so test IsNaN rather than trusting a
                // difference check. Same contract as FieldAffixes' overlay placement.
                void SyncSpan()
                {
                    float left = input.worldBound.xMin - field.worldBound.xMin;
                    float right = field.worldBound.xMax - input.worldBound.xMax;
                    if (float.IsNaN(left) || float.IsNaN(right)) return;
                    handle.style.left = Mathf.Max(0f, left);
                    handle.style.right = Mathf.Max(0f, right);
                }
                field.RegisterCallback<GeometryChangedEvent>(_ => SyncSpan());
                input.RegisterCallback<GeometryChangedEvent>(_ => SyncSpan());
                SyncSpan();
            }

            // Only the visible bar (the "grab" zone) is draggable — the surrounding strip is non-pickable so it
            // never steals clicks meant for selecting text in the bottom row of the box.
            var grab = handle.Q<VisualElement>(className: "sasq-text-resize-grab") ?? handle;
            AttachTextResizeDrag(grab, field, node, onLayoutEdited);
        }

        // A discreet sheet-style "grabber" bar. The full-width outer strip only POSITIONS the bar and is itself
        // NON-pickable; the inner "grab" zone (sized to just the visible bar) is the sole pointer/drag target, so a
        // click anywhere else along the bottom edge falls through to the text field for selecting characters.
        private static VisualElement MakeGripResizeHandle(BindingNode node)
        {
            var size = node.TextResizeHandleSize;
            var alignment = node.GripAlignment;

            var handle = new VisualElement { pickingMode = PickingMode.Ignore };
            handle.style.position = Position.Absolute;
            handle.style.left = 0;
            handle.style.right = 0;
            handle.style.bottom = 0;
            handle.style.height = GripStripHeight;
            handle.style.flexDirection = FlexDirection.Row;
            handle.style.alignItems = Align.FlexEnd;
            if (size == TextResizeHandleSize.FullWidth)
            {
                handle.style.justifyContent = Justify.FlexStart;
            }
            else
            {
                switch (alignment)
                {
                    case GripAlignment.Left: handle.style.justifyContent = Justify.FlexStart; break;
                    case GripAlignment.Right: handle.style.justifyContent = Justify.FlexEnd; break;
                    case GripAlignment.Center:
                    default: handle.style.justifyContent = Justify.Center; break;
                }
            }

            // The pickable/draggable zone — only as wide as the visible bar. Carries the resize-vertical cursor.
            var grab = new VisualElement();
            grab.AddToClassList("sasq-text-resizer");
            grab.AddToClassList("sasq-text-resize-grab");
            // Keep the resize grip working even in design select-only mode (resizing is a layout-design action,
            // like the container/column grips) — DisableInteractivityRecursively skips this class.
            grab.AddToClassList(LayoutTreeBuilder.DesignKeepInteractiveClass);
            grab.style.height = 8;
            grab.style.flexDirection = FlexDirection.Row;
            grab.style.alignItems = Align.FlexEnd;
            grab.style.justifyContent = Justify.Center;

            if (size == TextResizeHandleSize.Compact)
            {
                grab.style.width = 28;
                if (alignment == GripAlignment.Left) grab.style.marginLeft = 12;
                else if (alignment == GripAlignment.Right) grab.style.marginRight = 12;
            }
            else if (size == TextResizeHandleSize.Large)
            {
                grab.style.width = Length.Percent(35);
                if (alignment == GripAlignment.Left) grab.style.marginLeft = 12;
                else if (alignment == GripAlignment.Right) grab.style.marginRight = 12;
            }
            else if (size == TextResizeHandleSize.FullWidth)
            {
                grab.style.flexGrow = 1;
                grab.style.marginLeft = 12;
                grab.style.marginRight = 12;
            }

            var pill = new VisualElement { pickingMode = PickingMode.Ignore };
            pill.style.height = 3;
            pill.style.marginBottom = 2;
            pill.style.borderTopLeftRadius = 2;
            pill.style.borderTopRightRadius = 2;
            pill.style.borderBottomLeftRadius = 2;
            pill.style.borderBottomRightRadius = 2;
            var pillIdle = WidgetPalette.HandleIdle;   // captured: callbacks stay on the built skin
            var pillHot = WidgetPalette.HandleHot;
            pill.style.backgroundColor = pillIdle;
            pill.style.flexGrow = 1; // fills the grab zone's width

            grab.Add(pill);
            handle.Add(grab);

            grab.RegisterCallback<MouseEnterEvent>(_ => pill.style.backgroundColor = pillHot);
            grab.RegisterCallback<MouseLeaveEvent>(_ => pill.style.backgroundColor = pillIdle);
            return handle;
        }

        private static void AttachTextResizeDrag(VisualElement handle, TextField field, BindingNode node, Action onLayoutEdited)
        {
            bool resizing = false;
            float startY = 0f, startHeight = 0f, maxDragHeight = float.PositiveInfinity;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                resizing = true;
                startY = evt.position.y;
                startHeight = field.worldBound.height > 1f
                    ? field.worldBound.height
                    : (node.TextBoxHeight > 0f ? node.TextBoxHeight : 60f);
                // Inside a FIXED-height frame (Circle always; RoundedRect/Pill with Lock Aspect) the
                // clip can't grow with content, so an uncapped drag shoves every field below the text
                // box straight out of the shape. Growing the box by Δ pushes the column flow down by Δ,
                // so the box may grow at most by the slack between the frame content area's bottom edge
                // and its lowest in-flow child. Measured once per drag; grow-capped only (an already-
                // overflowing box — saved before the cap, or the frame shrank — still shrinks freely,
                // it just can't get worse).
                maxDragHeight = float.PositiveInfinity;
                var fixedFrame = FindFixedHeightFrameRoot(field);
                if (fixedFrame != null)
                    maxDragHeight = startHeight + Mathf.Max(0f, FixedFrameGrowSlack(fixedFrame));
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing) return;
                float raw = Mathf.Min(startHeight + (evt.position.y - startY), maxDragHeight);
                if (raw <= TextBoxCollapseHeight)
                {
                    // Dragged down to ~one line: clear the explicit height so the field returns to its
                    // natural (auto) single-line height — the pre-resize state. Persisted as 0 (= auto).
                    field.style.height = StyleKeyword.Null;
                    node.TextBoxHeight = 0f;
                }
                else
                {
                    field.style.height = raw;
                    node.TextBoxHeight = raw;
                }
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!resizing) return;
                resizing = false;
                if (handle.HasPointerCapture(evt.pointerId)) handle.ReleasePointer(evt.pointerId);
                onLayoutEdited?.Invoke(); // persist: designer marks dirty + re-renders; inspector SetDirty's the asset
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => resizing = false);
        }

        /// <summary>
        /// Nearest ancestor frame root whose height CANNOT grow with content: Circle (always 1:1
        /// aspect-locked) or an aspect-locked RoundedRect/Pill. Growable frames absorb a taller text
        /// box by growing, so they don't cap — but they may themselves sit inside a fixed frame, so
        /// the walk continues past them. Frame roots carry their node as userData in BOTH the live
        /// inspector and the designer canvas (only leaf cards transfer it to a wrapper).
        /// </summary>
        private static VisualElement FindFixedHeightFrameRoot(VisualElement el)
        {
            for (var p = el.parent; p != null; p = p.parent)
            {
                if (!p.ClassListContains(FrameBuilder.FrameClass)) continue;
                if (!(p.userData is ContainerNode cn) || cn.Type != ContainerType.Frame) continue;
                var f = cn.Frame;
                bool fixedHeight = f.Shape == FrameShape.Circle
                    || ((f.Shape == FrameShape.RoundedRect || f.Shape == FrameShape.Pill) && f.LockAspect);
                if (fixedHeight) return p;
            }
            return null;
        }

        /// <summary>
        /// How many pixels of content growth a fixed-height frame can still absorb: the gap between the
        /// content host's inner bottom edge (its rect already sits inside the shape's safe-area margins)
        /// and the bottom of its lowest in-flow child. Negative when content already overflows the clip.
        /// </summary>
        private static float FixedFrameGrowSlack(VisualElement frameRoot)
        {
            VisualElement contentHost = null;
            foreach (var child in frameRoot.Children())
                if (child.ClassListContains(FrameBuilder.ContentClass)) { contentHost = child; break; }
            if (contentHost == null) return 0f;

            float bottomLimit = contentHost.worldBound.yMax - contentHost.resolvedStyle.paddingBottom;
            float contentBottom = float.NegativeInfinity;
            foreach (var child in contentHost.Children())
            {
                if (child.resolvedStyle.position == Position.Absolute) continue;
                if (child.resolvedStyle.display == DisplayStyle.None) continue;
                float childBottom = child.worldBound.yMax + child.resolvedStyle.marginBottom;
                if (!float.IsNaN(childBottom)) contentBottom = Mathf.Max(contentBottom, childBottom);
            }
            if (float.IsNegativeInfinity(contentBottom)) return 0f;
            return bottomLimit - contentBottom;
        }

    }
}
