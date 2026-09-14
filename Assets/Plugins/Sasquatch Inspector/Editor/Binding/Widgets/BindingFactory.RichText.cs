using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using SasquatchInspector.Runtime;
using SasquatchSpring;

namespace SasquatchInspector.Editor
{
    public static partial class BindingFactory
    {
        // ===== Rich Text preview layer (used by the Text widget when node.RichTextPreview is on) =====
        // Wraps an already-built multi-line TextField in a split-pane: an optional formatting toolbar, the raw
        // tag-editable TextField, and a LIVE formatted preview Label (enableRichText). The preview is rendered by
        // UI Toolkit's own text engine (TextCore — the SAME engine TextMeshPro builds on), so it renders the vast
        // majority of rich-text tags.
        internal static VisualElement BuildRichTextWrapper(BindingNode node, SerializedProperty prop, TextField raw, string resolvedLabel)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-richtext-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel)) labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Column;
            wrapper.Add(input);

            // The wrapper supplies the label column, so hide the raw field's own label.
            raw.label = string.Empty;
            if (raw.labelElement != null) raw.labelElement.style.display = DisplayStyle.None;
            raw.AddToClassList("sasq-richtext-input");

            // --- selection tracking (so a toolbar button can wrap the HIGHLIGHTED text) ---
            // cursorIndex/selectIndex are only valid while the field is FOCUSED — clicking a toolbar control blurs
            // the field and resets them to 0, so a click would otherwise insert at index 0. We keep the latest
            // selection mirrored here WHILE focused (via a poll + key/pointer events), and a toolbar click reads
            // from this mirror, never from the just-blurred field.
            int selStart = 0, selEnd = 0;
            bool hasSel = false, focused = false;

            void Track()
            {
                if (!focused) return; // ignore reads once the field has blurred (they'd be 0/0)
                string t = raw.value ?? string.Empty;
                int a = raw.cursorIndex, b = raw.selectIndex;
                if (a < 0 || b < 0) return;
                a = Mathf.Clamp(a, 0, t.Length);
                b = Mathf.Clamp(b, 0, t.Length);
                if (a != b) { selStart = Mathf.Min(a, b); selEnd = Mathf.Max(a, b); hasSel = true; }
                else { selStart = a; selEnd = a; hasSel = false; } // a deliberate caret (no selection)
            }

            // A final, freshest read on toolbar mouse-DOWN — the field still has focus at this instant, just before
            // the click blurs it. Strengthen-only (never clears) so it can't wipe a selection the poll already has.
            void CaptureBeforeBlur()
            {
                string t = raw.value ?? string.Empty;
                int a = raw.cursorIndex, b = raw.selectIndex;
                if (a < 0 || b < 0) return;
                a = Mathf.Clamp(a, 0, t.Length);
                b = Mathf.Clamp(b, 0, t.Length);
                if (a != b) { selStart = Mathf.Min(a, b); selEnd = Mathf.Max(a, b); hasSel = true; }
            }

            raw.RegisterCallback<FocusInEvent>(_ => focused = true);
            raw.RegisterCallback<FocusOutEvent>(_ => focused = false);
            raw.RegisterCallback<KeyUpEvent>(_ => Track());
            raw.RegisterCallback<PointerUpEvent>(_ => Track());
            raw.schedule.Execute(Track).Every(50); // poll while focused so the mirror is fresh when a button is clicked

            // Standard formatting shortcuts: Ctrl+B / Ctrl+I / Ctrl+U (Cmd on macOS) toggle the tag on the current
            // selection — exactly like the toolbar buttons. TrickleDown so we intercept the key BEFORE the text field's
            // own input engine inserts the character; StopPropagation keeps the letter from being typed.
            raw.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.altKey || !(evt.ctrlKey || evt.commandKey)) return;
                string open = null, close = null;
                switch (evt.keyCode)
                {
                    case KeyCode.B: open = "<b>"; close = "</b>"; break;
                    case KeyCode.I: open = "<i>"; close = "</i>"; break;
                    case KeyCode.U: open = "<u>"; close = "</u>"; break;
                }
                if (open == null) return;
                Track();          // field is focused here, so the selection mirror is valid
                Wrap(open, close);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            // Toggle a tag on the mirrored selection (add if absent, REMOVE if already wrapping it — re-clicking a
            // format clears it instead of nesting), then re-focus and restore the selection next frame.
            void Wrap(string open, string close)
            {
                string text = raw.value ?? string.Empty;
                // selStart == selEnd whenever there's no selection, so these cover both the wrap and caret cases.
                var (updated, caretLo, caretHi) = ToggleTag(text, selStart, selEnd, open, close);
                raw.value = updated; // writes through BindProperty → SerializedProperty; fires the preview callback
                selStart = caretLo;
                selEnd = caretHi;
                hasSel = caretHi > caretLo;
                raw.schedule.Execute(() =>
                {
                    raw.Focus();
                    raw.SelectRange(caretLo, caretHi);
                }).ExecuteLater(0);
            }

            // --- free ColorField support (toolbar (A)) ---
            // The native ColorField opens its own picker window (the text field blurs), and fires valueChanged
            // continuously as the user drags. To stay idempotent we snapshot the text+selection when the picker
            // OPENS, then re-wrap from that snapshot on every change — so dragging replaces the color in place
            // instead of nesting tags or toggling the tag off when the same value repeats.
            string colorBase = null; int colorBaseLo = 0, colorBaseHi = 0;
            bool pendingReselect = false;            // re-show the highlight once focus returns from the color picker
            void CaptureColorBase()
            {
                CaptureBeforeBlur();                 // freshen the selection mirror while the field still has focus
                colorBase = raw.value ?? string.Empty;
                colorBaseLo = selStart; colorBaseHi = selEnd;
                pendingReselect = colorBaseHi > colorBaseLo;
            }
            // The native color picker is a separate window, so opening it blurs the field and drops the highlight.
            // When focus comes back to the field, re-apply the captured selection so the colored run shows highlighted.
            raw.RegisterCallback<FocusInEvent>(_ =>
            {
                if (!pendingReselect) return;
                pendingReselect = false;
                int lo = colorBaseLo, hi = colorBaseHi;
                raw.schedule.Execute(() => { if (raw.panel != null) raw.SelectRange(lo, hi); }).ExecuteLater(0);
            });
            void ApplyFreeColor(Color c)
            {
                string baseText = colorBase ?? (raw.value ?? string.Empty);
                int lo = colorBase != null ? colorBaseLo : selStart;
                int hi = colorBase != null ? colorBaseHi : selEnd;
                // Force opaque: the ColorField (showAlpha:false) can hand back alpha 0, which makes ToHex emit an
                // 8-digit RRGGBBAA (the "<color=#FF000000>" bug). A <color> tag wants 6-digit RRGGBB.
                string hex = ColorHexUtility.ToHex(new Color(c.r, c.g, c.b, 1f), false);
                // ApplyColorTag, NOT ApplyTag: strip-then-wrap. The snapshot only makes a single picker session
                // idempotent — REOPENING the picker snapshots text that already carries the last session's tag,
                // and a blind wrap there stacked one <color> block per visit.
                var (updated, caretLo, caretHi) = ApplyColorTag(baseText, lo, hi, $"<color=#{hex}>");
                raw.value = updated; // writes through BindProperty; fires the live preview
                // Move the mirror to the WRAPPED CONTENT (the inserted <color=…> tag shifts indices), so the
                // selection-highlight overlay marks the colored word — not the tag chars in front of it.
                selStart = caretLo; selEnd = caretHi; hasSel = caretHi > caretLo;
            }

            // The <color=…> open tag wrapping the current selection (or null) — drives the Clear chip + its removal.
            string SelectionColorOpenTag() => FindWrappingColorOpen(raw.value ?? string.Empty, selStart, selEnd);
            bool SelectionHasColor() => SelectionColorOpenTag() != null;
            void ClearColor()
            {
                string text = raw.value ?? string.Empty;
                // Strip EVERY colour layer, not just the innermost — Clear is the escape hatch, so a run that
                // already carries a stack (hand-typed, or from a build before the stacking fix) comes out clean.
                var (updated, lo, hi) = StripWrappingColors(text, selStart, selEnd);
                if (updated == text) return; // nothing wrapping the selection
                raw.value = updated; // writes through BindProperty; fires the live preview
                selStart = lo; selEnd = hi; hasSel = hi > lo;
                raw.schedule.Execute(() => { raw.Focus(); raw.SelectRange(lo, hi); }).ExecuteLater(0);
            }
            // Clicking a toolbar control blurs the field, which drops the visible selection highlight (the value is
            // still applied via the mirror). Re-focus + re-select so the highlighted run STAYS highlighted.
            void RestoreSelection()
            {
                if (selEnd <= selStart) return;
                int lo = selStart, hi = selEnd;
                raw.schedule.Execute(() => { raw.Focus(); raw.SelectRange(lo, hi); }).ExecuteLater(0);
            }

            // --- bulletproof selection-highlight overlay ---
            // The native TextField highlight only renders while the field's panel is focused, so the native color
            // picker (a SEPARATE window) hides it. This overlay draws our OWN translucent highlight behind the
            // selected run from the mirror, independent of focus — shown while the color row is open and the field
            // is NOT focused (when focused we defer to the native highlight so they don't double up).
            VisualElement selOverlayHost = null;
            IVisualElementScheduledItem selPoll = null;

            float MeasureW(TextElement te, string s)
                => string.IsNullOrEmpty(s) ? 0f : te.MeasureTextSize(s, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

            void EnsureOverlayHost()
            {
                if (selOverlayHost != null && selOverlayHost.panel != null) return;
                var te0 = raw.Q<TextElement>();
                var host = te0?.parent ?? raw;
                selOverlayHost = new VisualElement { pickingMode = PickingMode.Ignore };
                selOverlayHost.style.position = Position.Absolute;
                selOverlayHost.style.left = 0; selOverlayHost.style.top = 0;
                selOverlayHost.style.right = 0; selOverlayHost.style.bottom = 0;
                host.Add(selOverlayHost);
                selOverlayHost.SendToBack(); // render behind the text glyphs
            }

            void UpdateSelOverlay()
            {
                if (selOverlayHost == null) return;
                selOverlayHost.Clear();
                // Always draw while active (don't gate on `focused`): a separate-window color picker can hide the
                // native highlight WITHOUT firing FocusOut, so gating on focus would leave nothing visible. The
                // overlay sits behind the glyphs and matches the native region, so any overlap with a
                // still-showing native highlight just reads as a normal selection.
                string text = raw.value ?? string.Empty;
                int a = Mathf.Clamp(selStart, 0, text.Length), b = Mathf.Clamp(selEnd, a, text.Length);
                if (b <= a) return;
                var te = raw.Q<TextElement>();
                if (te == null) return;
                try
                {
                    float lineH = te.MeasureTextSize("Ay", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;
                    if (lineH <= 1f) lineH = Mathf.Max(12f, te.resolvedStyle.fontSize * 1.3f);
                    Vector2 baseOff = new Vector2(te.layout.x + te.contentRect.x, te.layout.y + te.contentRect.y);
                    int lineStart = 0, lineIdx = 0;
                    for (int i = 0; i <= text.Length; i++)
                    {
                        if (i != text.Length && text[i] != '\n') continue; // end of a (newline-delimited) line
                        int lineEnd = i;
                        int segA = Mathf.Max(a, lineStart), segB = Mathf.Min(b, lineEnd);
                        if (segB > segA)
                        {
                            float xs = MeasureW(te, text.Substring(lineStart, segA - lineStart));
                            float xe = MeasureW(te, text.Substring(lineStart, segB - lineStart));
                            var rect = new VisualElement { pickingMode = PickingMode.Ignore };
                            rect.AddToClassList("sasq-richtext-selhl");
                            rect.style.position = Position.Absolute;
                            rect.style.left = baseOff.x + xs;
                            rect.style.top = baseOff.y + lineIdx * lineH;
                            rect.style.width = Mathf.Max(2f, xe - xs);
                            rect.style.height = lineH;
                            selOverlayHost.Add(rect);
                        }
                        lineStart = i + 1; lineIdx++;
                    }
                }
                catch { selOverlayHost.Clear(); }
            }

            void SetColorHighlight(bool on)
            {
                if (on)
                {
                    EnsureOverlayHost();
                    UpdateSelOverlay();
                    if (selPoll == null) selPoll = raw.schedule.Execute(UpdateSelOverlay).Every(100);
                    else selPoll.Resume();
                }
                else
                {
                    selPoll?.Pause();
                    selOverlayHost?.Clear();
                }
            }

            // The {} button's action: toggle braces on the mirrored selection (mirrors Wrap, with
            // the brace-specific add/remove rules), then re-focus and restore the selection.
            void ToggleBracesOnSelection()
            {
                string text = raw.value ?? string.Empty;
                var (updated, caretLo, caretHi) = ToggleBraces(text, selStart, selEnd);
                raw.value = updated; // writes through BindProperty; fires the live preview
                selStart = caretLo;
                selEnd = caretHi;
                hasSel = caretHi > caretLo;
                raw.schedule.Execute(() =>
                {
                    raw.Focus();
                    raw.SelectRange(caretLo, caretHi);
                }).ExecuteLater(0);
            }

            // --- toolbar ---
            if (node.RichTextToolbar)
            {
                var toolbar = BuildRichTextToolbar(node, Wrap, CaptureColorBase, ApplyFreeColor,
                    SelectionHasColor, ClearColor, RestoreSelection, SetColorHighlight, ToggleBracesOnSelection);
                // Capture the selection on mouse-down on ANY toolbar control (capture/trickle-down phase fires
                // before the control's own handler and before the field blurs).
                toolbar.RegisterCallback<PointerDownEvent>(_ => CaptureBeforeBlur(), TrickleDown.TrickleDown);
                input.Add(toolbar);
            }

            input.Add(raw);

            // --- live formatted preview ("result bubble"), inside an expandable foldout (default expanded) ---
            var preview = new VisualElement();
            preview.AddToClassList("sasq-richtext-preview");
            preview.style.backgroundColor = node.RichTextPreviewBackground.Resolve(WidgetPalette.RichPreviewBg);
            float radius = node.RichTextPreviewCornerRadius.Resolve(5f);
            preview.style.borderTopLeftRadius = radius;
            preview.style.borderTopRightRadius = radius;
            preview.style.borderBottomLeftRadius = radius;
            preview.style.borderBottomRightRadius = radius;

            var previewLabel = new Label { enableRichText = true };
            previewLabel.AddToClassList("sasq-richtext-preview-label");
            previewLabel.style.whiteSpace = WhiteSpace.Normal;
            preview.Add(previewLabel);

            const string EmptyHint = "<color=#7d7d7d><i>(empty — type rich text above)</i></color>";

            // {fieldName} tokens ALWAYS resolve in the preview. When an edit makes a token resolve
            // for the FIRST time, the substituted value flashes in the accent color and the preview
            // bubble gets a small punch — unmistakable "it worked" feedback. The flash settles back
            // to plain text shortly after; a version stamp keeps a pending settle from clobbering
            // newer text.
            var resolvedBefore = new HashSet<string>();
            bool firstPreview = true;
            int previewVersion = 0;

            void SetPreview(string t)
            {
                int version = ++previewVersion;
                string display = t;
                var resolvedNow = new HashSet<string>();
                if (t != null)
                {
                    // Mid-teardown the SerializedObject can already be disposed while a tracker
                    // callback is still in flight — fall back to the literal text rather than throw.
                    try
                    {
                        display = ResolveFieldTokens(t, prop.serializedObject, resolvedNow, null, null);

                        var fresh = new HashSet<string>(resolvedNow);
                        fresh.ExceptWith(resolvedBefore);
                        if (!firstPreview && fresh.Count > 0 && EditorSpringFX.MotionEnabled)
                        {
                            string emphasized = ResolveFieldTokens(t, prop.serializedObject, null, fresh, TokenEmphasisHex);
                            string settled = display;
                            previewLabel.text = string.IsNullOrEmpty(emphasized) ? EmptyHint : emphasized;
                            EditorSpringFX.LandPunch(preview, 0.4f);
                            previewLabel.schedule.Execute(() =>
                            {
                                if (previewVersion == version)
                                    previewLabel.text = string.IsNullOrEmpty(settled) ? EmptyHint : settled;
                            }).ExecuteLater(450);
                            resolvedBefore.Clear();
                            resolvedBefore.UnionWith(resolvedNow);
                            return;
                        }
                    }
                    catch (Exception) { display = t; }
                }
                resolvedBefore.Clear();
                resolvedBefore.UnionWith(resolvedNow);
                firstPreview = false;
                previewLabel.text = string.IsNullOrEmpty(display) ? EmptyHint : display;
            }
            SetPreview(raw.value);
            firstPreview = false;
            raw.RegisterValueChangedCallback(evt => SetPreview(evt.newValue));
            // Keep the preview honest when the value changes elsewhere (undo, multi-edit, another widget): the
            // binding updates the field silently (SetValueWithoutNotify), so the change callback won't fire.
            wrapper.TrackPropertyValueWithRefresh(prop, p => SetPreview(p.stringValue));
            // Edits to REFERENCED fields (elsewhere in this inspector) must refresh the token values
            // too — TrackPropertyValue above only watches the text property itself.
            wrapper.TrackSerializedObjectValueWithRefresh(prop.serializedObject, _ => SetPreview(raw.value));

            // Typing "{" (or the toolbar's {} button) pops field-name suggestions scoped to the
            // token under the caret — the same autocomplete UX as the Method Name / condition pickers.
            var tokenTargetType = prop.serializedObject != null && prop.serializedObject.targetObject != null
                ? prop.serializedObject.targetObject.GetType() : null;
            AutocompleteTextField.AttachTokenAutocomplete(raw, () =>
            {
                var names = new System.Collections.Generic.List<string>();
                if (tokenTargetType == null) return names;
                foreach (var p in SasquatchReflectionUtility.GetTopLevelBindableProperties(tokenTargetType))
                {
                    if (p.type != null && IsTokenFormattable(p.type)) names.Add(p.path);
                }
                return names;
            },
            name => TokenFieldPeek(tokenTargetType, name));

            // Expandable "Preview" header — the SAME chevron + spring treatment as the layout's Expandable
            // groups: a ChevronRight glyph that SpringCaret spins 0°↔90°, and EditorSpringFX.Fold to spring the
            // preview open/closed. Defaults to expanded.
            var previewHeader = new VisualElement();
            previewHeader.AddToClassList("sasq-richtext-preview-header");
            previewHeader.style.flexDirection = FlexDirection.Row;
            previewHeader.style.alignItems = Align.Center;

            var chevron = SasquatchIcons.Create(SasquatchIcons.Ids.ChevronRight, 10, WidgetPalette.ChevronDim);
            chevron.AddToClassList("sasq-group-chevron");
            previewHeader.Add(chevron);

            var previewTitle = new Label("PREVIEW");
            previewTitle.AddToClassList("sasq-richtext-caption");
            previewTitle.pickingMode = PickingMode.Ignore;
            previewTitle.style.marginTop = 0;
            previewTitle.style.marginBottom = 0;
            previewTitle.style.marginLeft = 2;
            previewHeader.Add(previewTitle);

            var previewContent = new VisualElement();
            previewContent.Add(preview);

            bool previewExpanded = true;
            void ApplyPreviewExpanded(bool animate)
            {
                EditorSpringFX.SpringCaret(chevron, previewExpanded, animate);
                if (animate) EditorSpringFX.Fold(previewContent, previewExpanded);
                else previewContent.style.display = previewExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ApplyPreviewExpanded(false);

            previewHeader.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                previewExpanded = !previewExpanded;
                ApplyPreviewExpanded(true);
                evt.StopPropagation();
            });

            input.Add(previewHeader);
            input.Add(previewContent);
            return wrapper;
        }

        // ===== {fieldName} token resolution (always on in the Text widget's preview) =====
        // Single-pass Regex.Replace — a resolved value containing braces is NEVER re-scanned, so
        // self/circular references can't recurse. Unknown names, unsupported property kinds, and
        // malformed tokens (the regex simply doesn't match them) all render literally.
        private static readonly Regex FieldTokenRegex = new Regex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        // Accent hex for the "just resolved" flash in the preview (no leading '#'). A property so it
        // follows the effective skin of the build in flight.
        private static string TokenEmphasisHex => ColorHexUtility.ToHex(WidgetPalette.AccentUnity, false);

        /// <summary>Replaces <c>{fieldName}</c> tokens in <paramref name="text"/> with the live value of the
        /// named top-level field on <paramref name="so"/>. Pure + testable; used by the Text widget's preview.</summary>
        internal static string ResolveFieldTokens(string text, SerializedObject so)
            => ResolveFieldTokens(text, so, null, null, null);

        // Full form: `resolvedNames` collects every token name that produced a value; a resolved
        // token whose name is in `emphasizeNames` gets its value wrapped in a bold accent tint
        // (the just-resolved flash).
        internal static string ResolveFieldTokens(string text, SerializedObject so,
            ISet<string> resolvedNames, ISet<string> emphasizeNames, string emphasisHex)
        {
            if (string.IsNullOrEmpty(text) || so == null) return text;
            return FieldTokenRegex.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                var prop = so.FindProperty(name);
                if (prop == null) return m.Value;
                string value = FormatTokenValue(prop);
                if (value == null) return m.Value;
                resolvedNames?.Add(name);
                if (emphasizeNames != null && emphasizeNames.Contains(name) && !string.IsNullOrEmpty(emphasisHex))
                    return $"<color=#{emphasisHex}><b>{value}</b></color>";
                return value;
            });
        }

        // ===== SasquatchString support =====
        // A SasquatchString is a string that carries its token resolution into the built game (see
        // Runtime/Text/SasquatchString.cs). In the designer it is still just a Text widget: the widget binds
        // the inner `template` string, so the toolbar, the preview and every Text option work unchanged.

        /// <summary>
        /// The inner template property of a <see cref="SasquatchString"/> binding, or null when the property
        /// is a plain string (or anything else). Keyed on <c>SerializedProperty.type</c>, which reports the
        /// serialized struct name.
        /// </summary>
        internal static SerializedProperty SasquatchStringTemplate(SerializedProperty prop)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.Generic) return null;
            if (prop.type != nameof(SasquatchString)) return null;
            return prop.FindPropertyRelative("template");
        }

        /// <summary>
        /// Points a SasquatchString at the object that declares it — the binding that lets its
        /// <c>{tokens}</c> resolve once the string reaches TextMeshPro or a UI Toolkit label.
        ///
        /// EDIT TIME ONLY. Called from the template field's own change callback (<paramref name="onlyWhenUnset"/>
        /// false), where it re-points the reference — which is what binds a template as the user types it, and
        /// what repairs one that arrived by Copy/Paste Component still aimed at the object it was copied from.
        ///
        /// It used to run at BUILD time too, for a template that had text but no owner (assigned from code).
        /// That was a SerializedProperty write performed by merely DISPLAYING the object: a fake prefab override
        /// on an instance, a dirtied scene on a plain one. Code-built instances use
        /// <c>SasquatchString.Bind(owner)</c> instead. Note the callback must be registered through
        /// <c>RegisterUserValueChanged</c>, not <c>RegisterValueChangedCallback</c> — a bound field is handed a
        /// ChangeEvent the moment it attaches, which put the build-time write straight back.
        ///
        /// Writes through one SerializedObject PER target: assigning an object reference on a multi-object
        /// SerializedObject would point every selected object at the FIRST one.
        /// </summary>
        internal static void BakeSasquatchStringSource(SerializedProperty sasquatchStringProp, bool onlyWhenUnset)
        {
            if (sasquatchStringProp == null) return;
            string path = sasquatchStringProp.propertyPath;
            foreach (var target in sasquatchStringProp.serializedObject.targetObjects)
            {
                if (target == null) continue;
                var so = new SerializedObject(target);
                var sourceProp = so.FindProperty(path)?.FindPropertyRelative("source");
                if (sourceProp == null) continue;
                if (onlyWhenUnset && sourceProp.objectReferenceValue != null) continue;
                if (sourceProp.objectReferenceValue == target) continue;
                sourceProp.objectReferenceValue = target;
                // WithoutUndo: this is bookkeeping the object needs to function, not an edit the user made,
                // so it must not consume their next Ctrl+Z.
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// The hover code peek behind a <c>{fieldName}</c> suggestion: the token's field DECLARATION,
        /// attribute lines included. A bare <c>{token}</c> says nothing about what the field is — its type,
        /// its <c>[Range]</c>, its <c>[Tooltip]</c> — which is exactly what you want reminding of while
        /// writing the string. The inspected type doubles as the <c>sourceHint</c>: a token's field is
        /// declared on that type or a base, and a companion/nested declaring type normally lives in the
        /// host's own file. Null (no peek) whenever nothing resolves.
        /// </summary>
        internal static string TokenFieldPeek(Type targetType, string fieldName)
            => targetType == null || string.IsNullOrEmpty(fieldName)
                ? null
                : MethodSourcePreview.GetFieldSnippet(targetType, fieldName, targetType);

        // The reflected field types the token resolver can actually format — feeds the {…}
        // autocomplete so it only suggests names that will resolve.
        internal static bool IsTokenFormattable(Type t)
        {
            return t == typeof(string) || t == typeof(bool) || t.IsEnum
                || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)
                || t == typeof(float) || t == typeof(double)
                || t == typeof(Color) || t == typeof(Color32)
                || typeof(UnityEngine.Object).IsAssignableFrom(t);
        }

        /// <summary>
        /// The {} toolbar button's transform: wraps the selection in braces (making it a field
        /// reference), REMOVES them when the selection is already braced — whether the braces sit
        /// just outside the selection or are included in it — and with no selection inserts an
        /// empty pair with the caret inside, ready to type (which also pops the autocomplete).
        /// Pure + testable, mirroring ToggleTag's contract.
        /// </summary>
        internal static (string result, int caretLo, int caretHi) ToggleBraces(string text, int start, int end)
        {
            text ??= string.Empty;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);

            // (a) The selection INCLUDES the braces: {content} → content.
            if (end > start + 1 && text[start] == '{' && text[end - 1] == '}')
            {
                string content = text.Substring(start + 1, end - start - 2);
                string removed = text.Substring(0, start) + content + text.Substring(end);
                return (removed, start, start + content.Length);
            }
            // (b) The braces sit just OUTSIDE the selection: {[selection]} → selection.
            if (start > 0 && end < text.Length && text[start - 1] == '{' && text[end] == '}')
            {
                string content = text.Substring(start, end - start);
                string removed = text.Substring(0, start - 1) + content + text.Substring(end + 1);
                return (removed, start - 1, start - 1 + content.Length);
            }
            // (c) No selection: drop an empty pair, caret between the braces.
            if (end <= start)
            {
                string inserted = text.Substring(0, start) + "{}" + text.Substring(start);
                return (inserted, start + 1, start + 1);
            }
            // (d) Wrap the selection.
            string sel = text.Substring(start, end - start);
            string wrapped = text.Substring(0, start) + "{" + sel + "}" + text.Substring(end);
            return (wrapped, start + 1, start + 1 + sel.Length);
        }

        // Display formatting per property kind; null = unsupported → the caller keeps the literal token.
        private static string FormatTokenValue(SerializedProperty prop)
        {
            // Multi-object editing with disagreeing values → the honest em-dash (mirrors MixedValueUI).
            if (prop.serializedObject.isEditingMultipleObjects && prop.hasMultipleDifferentValues) return "—";

            switch (prop.propertyType)
            {
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Integer: return prop.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return prop.boolValue ? "True" : "False";
                case SerializedPropertyType.Float: return prop.doubleValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                {
                    int idx = prop.enumValueIndex;
                    var names = prop.enumDisplayNames;
                    // Flags combinations / out-of-range values have no single display name — show the raw int.
                    return idx >= 0 && names != null && idx < names.Length
                        ? names[idx]
                        : prop.intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                case SerializedPropertyType.Color: return "#" + ColorHexUtility.ToHex(prop.colorValue, false);
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "None";
                default: return null;
            }
        }

        // Pure, fully unit-testable transform: insert open/close around [start,end) (or an empty pair / a
        // self-contained tag at the caret when there is no selection / no close). Returns the new text plus the
        // selection to restore (the wrapped run, so repeated tags nest it).
        internal static (string result, int caretLo, int caretHi) ApplyTag(string text, int start, int end, string open, string close)
        {
            text ??= string.Empty;
            open ??= string.Empty;
            close ??= string.Empty;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);

            if (close.Length == 0)
            {
                // Self-contained tag (e.g. a hypothetical <br>): insert at the caret, leave any selection text be.
                string r = text.Substring(0, start) + open + text.Substring(start);
                int c = start + open.Length;
                return (r, c, c);
            }
            if (end <= start)
            {
                // No selection: drop an empty pair and put the caret between the tags, ready to type.
                string r = text.Substring(0, start) + open + close + text.Substring(start);
                int c = start + open.Length;
                return (r, c, c);
            }
            string sel = text.Substring(start, end - start);
            string res = text.Substring(0, start) + open + sel + close + text.Substring(end);
            int caretLo = start + open.Length;
            int caretHi = caretLo + sel.Length;
            return (res, caretLo, caretHi);
        }

        // Toggle a paired tag on the selection (the behaviour behind every toolbar button): if the selection is
        // ALREADY wrapped by this tag-kind — whether the tags sit just outside the selection OR the selection
        // includes them (even hand-typed ones) — REMOVE it instead of nesting another. For a valued tag
        // (<color=…>, <size=…>) re-picking the SAME value removes it, a DIFFERENT value replaces it. Otherwise add.
        // Pure + fully testable. A self-contained tag (empty close) just inserts.
        internal static (string result, int caretLo, int caretHi) ToggleTag(string text, int start, int end, string open, string close)
        {
            text ??= string.Empty;
            open ??= string.Empty;
            close ??= string.Empty;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);

            if (close.Length == 0) return ApplyTag(text, start, end, open, close);

            string name = TagName(open);
            if (name.Length == 0) return ApplyTag(text, start, end, open, close);

            bool valued = open.IndexOf('=') >= 0;
            string esc = Regex.Escape(name);

            // (a) The selection INCLUDES the pair: <name…>content</name>.
            if (end > start)
            {
                string sel = text.Substring(start, end - start);
                var inc = Regex.Match(sel, "^<" + esc + "(=[^>]*)?>([\\s\\S]*)</" + esc + ">$");
                if (inc.Success)
                {
                    string content = inc.Groups[2].Value;
                    string existingOpen = "<" + name + inc.Groups[1].Value + ">";
                    if (valued && existingOpen != open)
                    {
                        string r = text.Substring(0, start) + open + content + close + text.Substring(end);
                        int lo = start + open.Length;
                        return (r, lo, lo + content.Length);
                    }
                    string rem = text.Substring(0, start) + content + text.Substring(end);
                    return (rem, start, start + content.Length);
                }
            }

            // (b) The tags sit just OUTSIDE the selection: <name…>[selection]</name>.
            var before = Regex.Match(text.Substring(0, start), "<" + esc + "(=[^>]*)?>$");
            bool after = Regex.IsMatch(text.Substring(end), "^</" + esc + ">");
            if (before.Success && after)
            {
                int openStart = before.Index;
                int closeLen = name.Length + 3; // "</name>"
                string content = text.Substring(start, end - start);
                if (valued && before.Value != open)
                {
                    // Different value → replace the open tag (the existing close right after the selection stays).
                    string r = text.Substring(0, openStart) + open + content + text.Substring(end);
                    int lo = openStart + open.Length;
                    return (r, lo, lo + content.Length);
                }
                // Same value (or valueless) → remove both surrounding tags.
                string rem = text.Substring(0, openStart) + content + text.Substring(end + closeLen);
                return (rem, openStart, openStart + content.Length);
            }

            // (c) Not present → add.
            return ApplyTag(text, start, end, open, close);
        }

        // The tag name from an open tag: "<color=#hex>" → "color", "<b>" → "b", "<link=\"id\">" → "link".
        private static string TagName(string open)
        {
            int i = 0;
            if (i < open.Length && open[i] == '<') i++;
            int s = i;
            while (i < open.Length && open[i] != '=' && open[i] != '>' && open[i] != '/' && !char.IsWhiteSpace(open[i])) i++;
            return open.Substring(s, i - s);
        }

        // Returns the "<color=…>" OPEN tag wrapping [start,end) — whether the selection INCLUDES the pair or the
        // tags sit just OUTSIDE it (the two cases ToggleTag recognizes) — or null if the selection isn't colored.
        // Used to show/remove the rich-text Clear chip. Pure + testable.
        internal static string FindWrappingColorOpen(string text, int start, int end)
        {
            text ??= string.Empty;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);

            // (a) the selection itself includes <color…>content</color>
            if (end > start)
            {
                string sel = text.Substring(start, end - start);
                var inc = Regex.Match(sel, "^<color(=[^>]*)?>[\\s\\S]*</color>$");
                if (inc.Success) return "<color" + inc.Groups[1].Value + ">";
            }
            // (b) the tags sit just outside the selection: <color…>[selection]</color>
            var before = Regex.Match(text.Substring(0, start), "<color(=[^>]*)?>$");
            bool after = Regex.IsMatch(text.Substring(end), "^</color>");
            if (before.Success && after) return before.Value;
            return null;
        }

        // Peel EVERY <color…>…</color> pair that wraps [start,end) — whether the selection INCLUDES the pair or
        // the tags sit just OUTSIDE it — and return the cleaned text plus the same run's new bounds. It LOOPS, so
        // an already-nested stack collapses in one call (that is how a layout that already went wrong self-heals).
        // Pure + testable. Colors on a WIDER run (e.g. <color=A>Hello [World]</color>) are left alone: those tags
        // don't wrap the selection, they wrap its parent, and removing them would recolor text the user didn't pick.
        internal static (string result, int start, int end) StripWrappingColors(string text, int start, int end)
        {
            text ??= string.Empty;
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, start, text.Length);
            const string Close = "</color>";

            while (true)
            {
                // (a) the selection itself is <color…>content</color> — greedy, so one layer comes off per pass.
                if (end > start)
                {
                    var inc = Regex.Match(text.Substring(start, end - start), "^<color(=[^>]*)?>([\\s\\S]*)</color>$");
                    if (inc.Success)
                    {
                        string content = inc.Groups[2].Value;
                        text = text.Substring(0, start) + content + text.Substring(end);
                        end = start + content.Length;
                        continue;
                    }
                }
                // (b) the tags sit just outside the selection: <color…>[selection]</color>.
                var before = Regex.Match(text.Substring(0, start), "<color(=[^>]*)?>$");
                if (before.Success && Regex.IsMatch(text.Substring(end), "^" + Close))
                {
                    int openStart = before.Index;
                    string content = text.Substring(start, end - start);
                    text = text.Substring(0, openStart) + content + text.Substring(end + Close.Length);
                    start = openStart;
                    end = openStart + content.Length;
                    continue;
                }
                return (text, start, end);
            }
        }

        // Colour the selection with EXACTLY ONE <color> tag: strip whatever colour already wraps it, then wrap.
        // This is what the free ColorField applies, and it is deliberately NOT ToggleTag: the native picker fires
        // continuously while the user drags, and re-picking the same colour must be a no-op, never a removal.
        // Being strip-first also makes it idempotent ACROSS picker sessions — the snapshot the second session takes
        // already contains the first session's tag, and a blind wrap there is exactly how a run ended up with a
        // stack of eight <color> blocks. Pure + testable.
        internal static (string result, int caretLo, int caretHi) ApplyColorTag(string text, int start, int end, string open)
        {
            var (bare, lo, hi) = StripWrappingColors(text, start, end);
            return ApplyTag(bare, lo, hi, open, "</color>");
        }

        // The formatting toolbar: B / I / U buttons + expandable Color, Size and "More" rows. Every control is
        // non-focusable. Color/Size collapse after a pick (single choice); the "More" row STAYS OPEN so several
        // tags can be applied in a row.
        private static VisualElement BuildRichTextToolbar(BindingNode node, Action<string, string> wrap,
            Action captureColorBase, Action<Color> applyFreeColor,
            Func<bool> selectionHasColor, Action clearColor, Action restoreSelection, Action<bool> setColorHighlight,
            Action toggleBraces)
        {
            var column = new VisualElement();
            column.AddToClassList("sasq-richtext-toolbar");

            var bar = new VisualElement();
            bar.AddToClassList("sasq-richtext-bar");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.flexWrap = Wrap.Wrap;
            column.Add(bar);

            Button MakeButton(string text, string tooltip, Action onClick, FontStyle fontStyle = FontStyle.Normal)
            {
                var b = new Button(onClick) { text = text, tooltip = tooltip };
                b.focusable = false; // keep focus (and the live selection) on the text field
                b.AddToClassList("sasq-richtext-btn");
                if (fontStyle != FontStyle.Normal) b.style.unityFontStyleAndWeight = fontStyle;
                EditorSpringFX.ButtonSpring(b); // springy hover/press — transform-only, never steals focus
                return b;
            }

            // TOOLTIP TAG EXAMPLES USE ‹ › LOOKALIKES, never real < > — the editor tooltip window renders
            // with a rich-text style, so a literal tag in a tooltip gets parsed and swallowed (or collapses
            // the whole tooltip to a sliver). The wrap()/open/close strings are the REAL tags, untouched.
            bar.Add(MakeButton("B", "Bold the selection  ·  ‹b›…‹/b›", () => wrap("<b>", "</b>"), FontStyle.Bold));
            bar.Add(MakeButton("I", "Italicize the selection  ·  ‹i›…‹/i›", () => wrap("<i>", "</i>"), FontStyle.Italic));
            bar.Add(MakeButton("U", "Underline the selection  ·  ‹u›…‹/u›", () => wrap("<u>", "</u>")));

            var colorRow = MakeChipRow();
            var sizeRow = MakeChipRow();
            var moreRow = MakeChipRow();

            Button colorBtn = null, sizeBtn = null, moreBtn = null;

            void CloseRowsExcept(VisualElement keep)
            {
                if (colorRow != keep) { colorRow.style.display = DisplayStyle.None; colorBtn?.RemoveFromClassList("sasq-richtext-btn--on"); setColorHighlight?.Invoke(false); }
                if (sizeRow != keep) { sizeRow.style.display = DisplayStyle.None; sizeBtn?.RemoveFromClassList("sasq-richtext-btn--on"); }
                if (moreRow != keep) { moreRow.style.display = DisplayStyle.None; moreBtn?.RemoveFromClassList("sasq-richtext-btn--on"); }
            }
            void ToggleRow(VisualElement row, Button btn)
            {
                bool show = row.style.display == DisplayStyle.None;
                CloseRowsExcept(show ? row : null);
                row.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                {
                    btn.AddToClassList("sasq-richtext-btn--on");
                    if (EditorSpringFX.MotionEnabled) EditorSpringFX.SlideInY(row, -8f); // spring the chips in
                }
                else btn.RemoveFromClassList("sasq-richtext-btn--on");
                // Re-show the highlighted run whether OPENING or CLOSING a row — closing (e.g. "More" again) drops
                // the selection unless something restores it.
                restoreSelection?.Invoke();
            }

            // --- Color row: palette swatches + a free ColorField (any color) + a palette gear ---
            // A swatch pick collapses the row (single choice); the ColorField + gear leave it open since the native
            // picker / palette window are their own windows.
            var library = SasquatchPaletteLibraryManager.Current;
            var palette = library != null ? (library.GetPalette(node.ColorPaletteId) ?? library.DefaultPalette) : null;
            if (palette?.Swatches != null)
            {
                foreach (var sw in palette.Swatches)
                {
                    var color = sw.color;
                    string hex = ColorHexUtility.ToHex(color, false); // RRGGBB (or RRGGBBAA), no '#'
                    var chip = new VisualElement();
                    chip.AddToClassList("sasq-richtext-swatch");
                    chip.style.backgroundColor = new Color(color.r, color.g, color.b, 1f);
                    chip.tooltip = (string.IsNullOrEmpty(sw.name) ? "" : sw.name + "  ·  ") + "#" + hex;
                    chip.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        wrap($"<color=#{hex}>", "</color>");
                        colorRow.style.display = DisplayStyle.None;
                        colorBtn?.RemoveFromClassList("sasq-richtext-btn--on");
                        setColorHighlight?.Invoke(false);
                        evt.StopPropagation();
                    });
                    EditorSpringFX.ButtonSpring(chip);
                    colorRow.Add(chip);
                }
            }

            // (A) A native ColorField + picker for ANY color (not just palette swatches). focusable=false on the
            // field and its descendants so clicking it doesn't blur the text field (which would drop the selection
            // highlight); the picker still opens on click. We capture the selection on pointer-down regardless.
            var anyColor = new ColorField { showAlpha = false, focusable = false };
            anyColor.SetValueWithoutNotify(Color.white); // start opaque so the first pick can't inherit alpha 0
            anyColor.AddToClassList("sasq-richtext-colorfield");
            anyColor.tooltip = "Wrap the selection in any color  ·  ‹color=#…›";
            anyColor.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                foreach (var child in anyColor.Query<VisualElement>().ToList()) child.focusable = false;
            });
            anyColor.RegisterCallback<PointerDownEvent>(_ => captureColorBase?.Invoke(), TrickleDown.TrickleDown);
            anyColor.RegisterValueChangedCallback(evt => applyFreeColor?.Invoke(evt.newValue));
            colorRow.Add(anyColor);

            // (B) A gear to choose / edit the color palette feeding the swatches.
            colorRow.Add(MakeRichTextPaletteGear(node));

            // (C) A Clear chip that strips the color from the selection — shown only when the selection IS colored
            // (so there's no guessing which color was used, and the ColorField path is removable too).
            var clearChip = MakeTagChip("Clear", "Remove the color from the selected text", () =>
            {
                clearColor?.Invoke();
                colorRow.style.display = DisplayStyle.None;
                colorBtn?.RemoveFromClassList("sasq-richtext-btn--on");
                setColorHighlight?.Invoke(false);
            });
            clearChip.AddToClassList("sasq-richtext-clearchip");
            clearChip.style.display = DisplayStyle.None;
            colorRow.Add(clearChip);

            colorBtn = MakeButton("Color", "Color — a palette swatch, any color, or edit palettes  ·  ‹color=…›", () =>
            {
                ToggleRow(colorRow, colorBtn);
                bool open = colorRow.style.display != DisplayStyle.None;
                // Reveal Clear only while the row is open AND the selection already has a color.
                clearChip.style.display = (open && selectionHasColor != null && selectionHasColor())
                    ? DisplayStyle.Flex : DisplayStyle.None;
                setColorHighlight?.Invoke(open); // show our own selection highlight while picking a color
            });
            bar.Add(colorBtn);

            // While the color row is open, keep the Clear chip in sync with the selection — so after applying a
            // color via the ColorField (which happens out-of-band), Clear appears even though the row was already up.
            column.schedule.Execute(() =>
            {
                if (colorRow.style.display == DisplayStyle.None) return;
                clearChip.style.display = (selectionHasColor != null && selectionHasColor())
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(150);

            // --- Size chips (relative %, render in the preview; collapse after a pick) ---
            int[] sizes = { 75, 90, 100, 125, 150, 200 };
            foreach (var pct in sizes)
            {
                int p = pct;
                var chip = MakeTagChip(p + "%", $"Resize the selection to {p}%  ·  ‹size={p}%›…‹/size›", () =>
                {
                    wrap($"<size={p}%>", "</size>");
                    sizeRow.style.display = DisplayStyle.None;
                    sizeBtn?.RemoveFromClassList("sasq-richtext-btn--on");
                });
                sizeRow.Add(chip);
            }
            sizeBtn = MakeButton("Size", "Resize the selection  ·  ‹size=…›", () => ToggleRow(sizeRow, sizeBtn));
            bar.Add(sizeBtn);

            // --- {} field reference: wrap the selection in braces (a {fieldName} token resolves to
            // the field's live value in the preview); re-clicking a braced selection removes them,
            // exactly like the tag buttons. With nothing selected it drops an empty pair with the
            // caret inside — which also pops the field-name autocomplete.
            bar.Add(MakeButton("{}", "Reference a field — {fieldName} shows that field's live value in the preview. " +
                                     "Click again on a braced selection to remove the braces.", () => toggleBraces?.Invoke()));

            // --- "More" chips (STAY OPEN — apply several in a row) ---
            foreach (var tag in MoreRichTextTags())
            {
                var t = tag;
                moreRow.Add(MakeTagChip(t.label, t.tooltip, () => wrap(t.open, t.close)));
            }
            moreBtn = MakeButton("More ▾", "More rich-text tags — pick several; this menu stays open", () => ToggleRow(moreRow, moreBtn));
            bar.Add(moreBtn);

            column.Add(colorRow);
            column.Add(sizeRow);
            column.Add(moreRow);
            return column;
        }

        // The palette gear in the rich-text Color row: choose which palette feeds the swatches, or open the Color
        // Palette window to edit the swatch colors. Mirrors the gradient widget's palette gear. Changing the palette
        // re-triggers a layout edit so the swatch row rebuilds.
        private static Button MakeRichTextPaletteGear(BindingNode node)
        {
            var onLayoutEdited = LayoutTreeBuilder.CurrentOnLayoutEdited;
            Button gear = null;
            // The palette choice is LAYOUT data (node field + onLayoutEdited persist), so it owes an undo
            // record like every other layout edit — see the Color Palette widget's ApplyFieldPalette.
            // Without it the write reaches disk and the next Ctrl+Z pops an earlier unrelated record.
            // Resolved from the gear's ancestor chain, so a node inside an inline editor records against
            // the layout that actually owns it.
            void Assign(string paletteId)
            {
                var layout = LayoutOwnerRegistry.ResolveScope(gear)?.LayoutAsset;
                if (layout != null) LayoutRegistry.BeginLayoutEditUndo(layout, "Change Rich Text Palette");
                node.ColorPaletteId = paletteId;
                onLayoutEdited?.Invoke();
            }
            gear = new Button(() =>
            {
                var lib = SasquatchPaletteLibraryManager.Current;
                var menu = new GenericMenu();
                var def = lib?.DefaultPalette;
                string defName = def != null ? def.DisplayName : "none";
                menu.AddItem(new GUIContent($"Use Default Palette  ({defName})"), string.IsNullOrEmpty(node.ColorPaletteId),
                    () => Assign(""));
                if (lib?.Palettes != null && lib.Palettes.Count > 0)
                {
                    menu.AddSeparator("");
                    foreach (var p in lib.Palettes)
                    {
                        var id = p.Id;
                        menu.AddItem(new GUIContent(p.DisplayName), node.ColorPaletteId == id, () => Assign(id));
                    }
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Open Color Palette Window"), false,
                    () => SasquatchColorPaletteWindow.Open(lib?.GetPalette(node.ColorPaletteId) ?? lib?.DefaultPalette));
                menu.DropDown(gear.worldBound);
            })
            { tooltip = "Choose or edit the color palette" };
            gear.focusable = false;
            gear.AddToClassList("sasq-richtext-btn");
            gear.AddToClassList("sasq-richtext-gear");
            gear.Add(SasquatchIcons.Create(SasquatchIcons.Ids.Settings, 12));
            EditorSpringFX.ButtonSpring(gear);
            return gear;
        }

        private static VisualElement MakeChipRow()
        {
            var row = new VisualElement();
            row.AddToClassList("sasq-richtext-chiprow");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center; // vertically center swatches, the ColorField and the gear on each line
            row.style.display = DisplayStyle.None;
            return row;
        }

        private static Button MakeTagChip(string text, string tooltip, Action onClick)
        {
            var chip = new Button(onClick) { text = text, tooltip = tooltip };
            chip.focusable = false; // keep focus (and the live selection) on the text field
            chip.AddToClassList("sasq-richtext-tagchip");
            EditorSpringFX.ButtonSpring(chip);
            return chip;
        }

        // The extended tag set behind "More". Every entry renders in the UITK preview (same TextCore engine as
        // TMP) and is inserted verbatim for the runtime text. All are paired (wrap the selection); <alpha> "closes"
        // by resetting to full opacity (the common idiom). Internal so a test can verify every one wraps correctly.
        // The tooltip column shows the tag with ‹ › lookalikes — a literal tag in a tooltip gets parsed and eaten
        // by the tooltip window's rich-text style. The open/close columns are the REAL tags.
        internal static (string label, string tooltip, string open, string close)[] MoreRichTextTags() => new[]
        {
            ("Strike",   "Strikethrough  ·  ‹s›…‹/s›",                              "<s>", "</s>"),
            ("Sup",      "Superscript  ·  ‹sup›…‹/sup›",                            "<sup>", "</sup>"),
            ("Sub",      "Subscript  ·  ‹sub›…‹/sub›",                              "<sub>", "</sub>"),
            ("Mark",     "Highlight  ·  ‹mark=#ffeb3b55›…‹/mark›",                  "<mark=#ffeb3b55>", "</mark>"),
            ("UPPER",    "Uppercase  ·  ‹uppercase›…‹/uppercase›",                  "<uppercase>", "</uppercase>"),
            ("lower",    "Lowercase  ·  ‹lowercase›…‹/lowercase›",                  "<lowercase>", "</lowercase>"),
            ("SmCaps",   "Small caps  ·  ‹smallcaps›…‹/smallcaps›",                 "<smallcaps>", "</smallcaps>"),
            ("Align L",  "Align left  ·  ‹align=left›…‹/align›",                    "<align=left>", "</align>"),
            ("Align C",  "Align center  ·  ‹align=center›…‹/align›",                "<align=center>", "</align>"),
            ("Align R",  "Align right  ·  ‹align=right›…‹/align›",                  "<align=right>", "</align>"),
            ("Indent",   "Indent  ·  ‹indent=15%›…‹/indent›",                       "<indent=15%>", "</indent>"),
            ("Line ↕",   "Line height  ·  ‹line-height=120%›…‹/line-height›",       "<line-height=120%>", "</line-height>"),
            ("Spacing",  "Character spacing  ·  ‹cspace=0.3em›…‹/cspace›",          "<cspace=0.3em>", "</cspace>"),
            ("Fade",     "Fade text  ·  ‹alpha=#80›…‹alpha=#FF›",                   "<alpha=#80>", "<alpha=#FF>"),
            ("Link",     "Clickable link  ·  ‹link=\"id\"›…‹/link›",                "<link=\"id\">", "</link>"),
            ("No Parse", "Show the tags literally  ·  ‹noparse›…‹/noparse›",        "<noparse>", "</noparse>"),
        };
    }
}
