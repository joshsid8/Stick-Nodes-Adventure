#if SASQ_AUDIO // AudioClip lives in com.unity.modules.audio, a REMOVABLE built-in module.
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
        private const string AudioPlayGlyph = "\u25B6"; // ▶
        private const string AudioStopGlyph = "\u25A0"; // ■

        // One scrub "tick" of audio, modeled on a 60 fps editor (≈ a single video frame).
        private const double ScrubTickSeconds = 1.0 / 60.0;

        // AudioPreview widget: an AudioClip field with in-editor playback. Standard mode = a play button + the
        // object field, with a thin progress bar that slides across the field while playing. ShowSoundwaves mode =
        // also renders the clip's waveform to the right of the field with a scrolling playhead and optional
        // interactive scrubbing. Falls back to the default field for non-object-reference properties, consistent
        // with the other widgets.
        private static VisualElement CreateAudioPreview(BindingNode node, SerializedProperty prop, string resolvedLabel)
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference)
                return new PropertyField(prop) { label = resolvedLabel };

            bool showWave = node.AudioPreviewMode == AudioPreviewMode.ShowSoundwaves;
            var wavePlacement = node.AudioWaveformPlacement;
            bool waveStacked = showWave && (wavePlacement == ElementPlacement.Above || wavePlacement == ElementPlacement.Below);
            bool waveInline = showWave && !waveStacked; // Left/Right — waveform shares the field's row
            float waveHeight = Mathf.Max(24f, node.AudioWaveformHeight);

            var wrapper = new VisualElement();
            wrapper.AddToClassList("unity-base-field");
            wrapper.AddToClassList("sasq-audio-preview-field");

            var labelEl = new Label(resolvedLabel);
            labelEl.AddToClassList("unity-base-field__label");
            labelEl.AddToClassList("unity-label");
            if (string.IsNullOrEmpty(resolvedLabel))
                labelEl.style.display = DisplayStyle.None;
            wrapper.Add(labelEl);

            var input = new VisualElement();
            input.AddToClassList("unity-base-field__input");
            input.style.flexDirection = FlexDirection.Row;
            input.style.alignItems = showWave ? Align.Stretch : Align.Center;
            wrapper.Add(input);

            // Object field constrained to AudioClip (resolve element type for arrays/lists, but only accept clips).
            var objField = new ObjectField { objectType = typeof(AudioClip) };
            var targetObj = prop.serializedObject.targetObject;
            if (targetObj != null)
            {
                GetFieldInfoFromPropertyPath(targetObj.GetType(), prop.propertyPath, out var fieldType);
                if (fieldType != null)
                {
                    Type elem = fieldType;
                    if (fieldType.IsArray) elem = fieldType.GetElementType();
                    else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                        elem = fieldType.GetGenericArguments()[0];
                    if (elem != null && typeof(AudioClip).IsAssignableFrom(elem))
                        objField.objectType = elem;
                }
            }
            SeedThenBind(objField, prop); // seed BEFORE bind — see SeedThenBind
            objField.style.flexGrow = 1;
            // …and it has to give width BACK too. Unity ships an ObjectField as flexShrink 0 with a
            // content-derived basis, so grow alone is a one-way street: measured 2026-09-02 at a 300pt
            // inspector, the wrapper around this correctly shrank to 101px while the field itself stayed
            // pinned at its 120px basis and hung 22px out of its own parent, ending 12px past the inspector.
            // Every audio mode goes through here, which is why all three rows reported it.
            objField.style.flexShrink = 1;
            objField.style.minWidth = 0;

            // Disposal-safe: during a Save/refresh the inspector disposes the SerializedObject before the panel
            // is torn down, so any property access (even in a DetachFromPanelEvent) can throw. Returning null
            // keeps all callers (Tick, IsPlayingThis, detach) safe.
            AudioClip CurrentClip()
            {
                try
                {
                    if (prop.serializedObject == null || prop.serializedObject.targetObject == null)
                        return null;
                    return prop.objectReferenceValue as AudioClip;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            // Per-widget identity for playback ownership. There is only ONE editor preview voice, so this token
            // lets us tell whether THIS widget (vs. another window showing the same clip) started playback.
            var playbackToken = new object();
            // Native-backend capabilities. audioAvailable is false only when no native device could be opened
            // (headless box, no sound service, or disabled in Project Settings) → controls hide and the waveform
            // shows a hint. canScrub gates the waveform scrub interaction (all native backends support it).
            bool audioAvailable = AudioPreviewPlayback.IsAvailable;
            bool canScrub = audioAvailable && AudioPreviewPlayback.SupportsScrubbing;
            bool isScrubbing = false;  // true while the user is dragging the waveform playhead

            // Local source of truth for the play/stop toggle. Decoupled from the backend's IsPlaying() (which reads
            // false for a frame right after Play(), causing double-clicks to stack looping voices).
            bool playIntent = false;
            double playStartTime = 0;        // when StartPlayback last ran (grace window for natural-end reconcile)
            int resumeSample = 0;            // sample the play button starts from (updated by scrubbing)
            bool scrubResumePlaying = false; // resume continuous playback on pointer-up (was playing when drag began)

            // ----- Buttons group: optional loop toggle, then the play/stop button -----
            var buttonsGroup = new VisualElement();
            buttonsGroup.style.flexDirection = FlexDirection.Row;
            buttonsGroup.style.alignItems = Align.Center;
            buttonsGroup.style.flexShrink = 0;

            // Play / stop button — a glyph (not text), styled with the project accent-blue convention.
            var playBtn = new Button { text = AudioPlayGlyph, tooltip = "Play / stop preview" };
            playBtn.AddToClassList("sasq-audio-play-button");
            playBtn.style.width = 24;
            playBtn.style.minWidth = 24;
            playBtn.style.height = 20;
            playBtn.style.paddingLeft = 0;
            playBtn.style.paddingRight = 0;
            playBtn.style.marginLeft = 0;
            playBtn.style.marginRight = 0;
            playBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            playBtn.style.fontSize = 11;
            playBtn.style.backgroundColor = WidgetPalette.BadgeAccent;
            playBtn.style.color = Color.white;

            // Editor preview is one-shot: native one-shot playback is the cross-platform-safe model, so there
            // is no loop toggle here.
            buttonsGroup.Add(playBtn);

            // ----- Field wrap: the object field plus (Standard mode) a thin playhead sliding across the field -----
            var fieldWrap = new VisualElement();
            fieldWrap.style.position = Position.Relative;
            fieldWrap.style.flexDirection = FlexDirection.Row;
            fieldWrap.style.alignItems = Align.Center;
            // Shrinkable in EVERY mode, not just the inline-waveform one. flexShrink 0 here kept the object
            // field at its content width in Standard mode too, so the row painted 16px past the inspector on
            // a 300pt pane. The play button beside it stays rigid (it is 24px and a squashed play glyph is
            // worse than a shorter field), so this is the part that has to give.
            fieldWrap.style.flexShrink = 1;
            fieldWrap.style.minWidth = 0;
            fieldWrap.Add(objField);

            VisualElement fieldPlayhead = null;
            if (!showWave)
            {
                fieldPlayhead = new VisualElement();
                fieldPlayhead.AddToClassList("sasq-audio-playhead");
                fieldPlayhead.style.position = Position.Absolute;
                fieldPlayhead.style.top = 0;
                fieldPlayhead.style.bottom = 0;
                fieldPlayhead.style.width = 2;
                fieldPlayhead.style.left = Length.Percent(0);
                fieldPlayhead.style.display = DisplayStyle.None;
                fieldPlayhead.pickingMode = PickingMode.Ignore;
                fieldWrap.Add(fieldPlayhead);
            }

            // ----- Waveform area (ShowSoundwaves only) -----
            VisualElement waveArea = null;
            VisualElement wavePlayhead = null;
            Image waveImg = null;
            Label waveHint = null; // shown over the wave area when preview is unavailable (no device / compressed clip)
            if (showWave)
            {
                waveArea = new VisualElement();
                waveArea.AddToClassList("sasq-audio-waveform");
                waveArea.style.position = Position.Relative;
                waveArea.style.flexGrow = 1;
                waveArea.style.flexShrink = 1;
                waveArea.style.height = waveHeight;
                waveArea.style.overflow = Overflow.Hidden;

                waveImg = new Image { scaleMode = ScaleMode.StretchToFill };
                waveImg.style.position = Position.Absolute;
                waveImg.style.top = 0;
                waveImg.style.left = 0;
                waveImg.style.right = 0;
                waveImg.style.bottom = 0;
                waveImg.pickingMode = PickingMode.Ignore;
                waveArea.Add(waveImg);

                wavePlayhead = new VisualElement();
                wavePlayhead.AddToClassList("sasq-audio-playhead");
                wavePlayhead.style.position = Position.Absolute;
                wavePlayhead.style.top = 0;
                wavePlayhead.style.bottom = 0;
                wavePlayhead.style.width = 2;
                wavePlayhead.style.left = Length.Percent(0);
                wavePlayhead.pickingMode = PickingMode.Ignore;
                waveArea.Add(wavePlayhead);

                // Centered hint shown when audio preview is unavailable (no native backend on this platform, or a
                // streaming/compressed clip that can't be read for playback). Overlays the (then-empty) wave area.
                waveHint = new Label();
                waveHint.AddToClassList("sasq-audio-hint");
                waveHint.style.position = Position.Absolute;
                waveHint.style.left = 0;
                waveHint.style.right = 0;
                waveHint.style.top = 0;
                waveHint.style.bottom = 0;
                waveHint.style.unityTextAlign = TextAnchor.MiddleCenter;
                waveHint.style.whiteSpace = WhiteSpace.Normal;
                waveHint.style.fontSize = 10;
                waveHint.style.color = WidgetPalette.WaveHint;
                waveHint.style.display = DisplayStyle.None;
                waveHint.pickingMode = PickingMode.Ignore;
                waveArea.Add(waveHint);
            }

            // ----- Arrange field + transport buttons + (optional) waveform per the waveform placement -----
            // The play-button alignment orders the buttons vs the field (Left = buttons first; Right = buttons
            // after; Center = the pair centered — distinct from Right in a full-width row because the field stays
            // compact instead of growing). The waveform placement then positions the waveform Left/Right (inline, sharing
            // the row) or Above/Below (its own full-width row for maximum scrub room).
            void AddFieldAndButtons(VisualElement into, bool fieldGrows)
            {
                switch (node.AudioPlayAlignment)
                {
                    case ImageAlignment.Left:
                        buttonsGroup.style.marginLeft = 0;
                        buttonsGroup.style.marginRight = 4;
                        fieldWrap.style.flexGrow = fieldGrows ? 1 : 0;
                        into.Add(buttonsGroup); into.Add(fieldWrap);
                        break;
                    case ImageAlignment.Center:
                        buttonsGroup.style.marginLeft = 4;
                        buttonsGroup.style.marginRight = 0;
                        fieldWrap.style.flexGrow = 0; // keep the pair compact so the centering is visible
                        into.style.justifyContent = Justify.Center;
                        into.Add(fieldWrap); into.Add(buttonsGroup);
                        break;
                    case ImageAlignment.Right:
                    default:
                        buttonsGroup.style.marginLeft = 4;
                        buttonsGroup.style.marginRight = 0;
                        fieldWrap.style.flexGrow = fieldGrows ? 1 : 0;
                        into.Add(fieldWrap); into.Add(buttonsGroup);
                        break;
                }
            }

            if (waveStacked)
            {
                // Field+buttons on one row; the waveform on its own full-width row above or below.
                input.style.alignItems = Align.Center; // override the Stretch set for inline wave mode above
                AddFieldAndButtons(input, fieldGrows: true);

                // Reparent label + input into a field row so the waveform can be a full-width sibling row.
                wrapper.style.flexDirection = FlexDirection.Column;
                wrapper.style.alignItems = Align.Stretch;
                var fieldRow = new VisualElement();
                fieldRow.style.flexDirection = FlexDirection.Row;
                fieldRow.style.alignItems = Align.Center;
                fieldRow.Add(labelEl); // labelEl + input were Add()ed to wrapper earlier; Add() here reparents them
                fieldRow.Add(input);

                // Full-width waveform row (flexGrow 0 so it doesn't grow vertically inside the column).
                waveArea.style.flexGrow = 0;
                waveArea.style.flexShrink = 0;
                waveArea.style.width = Length.Percent(100);
                waveArea.style.marginLeft = 0;
                waveArea.style.marginTop = wavePlacement == ElementPlacement.Below ? 4 : 0;
                waveArea.style.marginBottom = wavePlacement == ElementPlacement.Above ? 4 : 0;

                if (wavePlacement == ElementPlacement.Above) { wrapper.Add(waveArea); wrapper.Add(fieldRow); }
                else { wrapper.Add(fieldRow); wrapper.Add(waveArea); }
            }
            else if (waveInline)
            {
                // Field+buttons as a PREFERRED-width group; the waveform fills the rest to the chosen side.
                //
                // A hard `width = 150` plus `flexShrink = 0` here made this group rigid at ~178px (field 150
                // + the 24px play button + gutter). Measured 2026-09-01 at a 300pt inspector, which leaves
                // this input lane 129px: the group painted 49px past the row and the clip cut the object
                // field in half. A preferred BASIS with a floor keeps the wide look identical — flex only
                // takes width back once there is none spare — while letting the row stay inside itself when
                // there is not. The floor is what stops the object field collapsing to an unclickable sliver;
                // the waveform (flexShrink 1, basis 0) gives up its width first, which is the right order.
                fieldWrap.style.width = StyleKeyword.Null;
                fieldWrap.style.flexBasis = 150;
                fieldWrap.style.flexShrink = 1;
                fieldWrap.style.minWidth = 56;
                var fbGroup = new VisualElement();
                fbGroup.style.flexDirection = FlexDirection.Row;
                fbGroup.style.alignItems = Align.Center;
                fbGroup.style.flexShrink = 1;
                fbGroup.style.minWidth = 0;
                AddFieldAndButtons(fbGroup, fieldGrows: false);

                waveArea.style.marginLeft = wavePlacement == ElementPlacement.Right ? 4 : 0;
                waveArea.style.marginRight = wavePlacement == ElementPlacement.Left ? 4 : 0;

                if (wavePlacement == ElementPlacement.Left) { input.Add(waveArea); input.Add(fbGroup); }
                else { input.Add(fbGroup); input.Add(waveArea); }
            }
            else
            {
                // Standard (no waveform): just field + buttons.
                AddFieldAndButtons(input, fieldGrows: true);
            }

            // ----- Waveform rendering + interactive scrubbing -----
            if (showWave)
            {
                Color waveColor = node.DisplayStyleData.accent.isOverridden
                    ? node.DisplayStyleData.accent.value
                    : WidgetPalette.WaveformDefault;
                Color waveBg = new Color(0f, 0f, 0f, 0f);

                void RebuildWaveform()
                {
                    var clip = CurrentClip();
                    if (clip == null) { waveImg.image = null; return; }
                    int w = Mathf.RoundToInt(waveArea.resolvedStyle.width);
                    int h = Mathf.RoundToInt(waveArea.resolvedStyle.height);
                    if (w <= 1) w = 256;
                    if (h <= 1) h = Mathf.RoundToInt(waveHeight);
                    waveImg.image = AudioWaveformRenderer.GetWaveform(clip, w, h, waveColor, waveBg);
                }

                waveArea.RegisterCallback<GeometryChangedEvent>(_ => RebuildWaveform());
                waveArea.TrackPropertyValueWithRefresh(prop, _ => RebuildWaveform());

                // The renderer hands out cached textures BY REFERENCE, and its overflow eviction /
                // Invalidate destroy them. Without re-fetching here, another widget's eviction leaves this
                // Image pointing at a destroyed texture and the waveform silently goes blank forever. The
                // event fires BEFORE the destroy, so this re-fetch mints (and re-caches) a live one.
                System.Action onEvicted = () => { if (waveArea.panel != null) RebuildWaveform(); };
                AudioWaveformRenderer.CacheEvicted += onEvicted;
                PanelLifetime.OnRealDetach(waveArea, () => AudioWaveformRenderer.CacheEvicted -= onEvicted);

                if (node.AudioInteractive && canScrub)
                {
                    waveArea.AddToClassList("sasq-audio-interactive");

                    int SampleAt(IPointerEvent evt, AudioClip clip, out float pct)
                    {
                        var rect = waveArea.worldBound;
                        pct = rect.width <= 0f ? 0f : Mathf.Clamp01((evt.position.x - rect.xMin) / rect.width);
                        return Mathf.Clamp(Mathf.RoundToInt(pct * clip.samples), 0, Mathf.Max(0, clip.samples - 1));
                    }

                    waveArea.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        var clip = CurrentClip();
                        if (clip == null) return;
                        waveArea.CapturePointer(evt.pointerId);
                        isScrubbing = true;

                        // If we were already playing, "pause" the continuous voice on the backend; on release we
                        // resume from the drop position. If we weren't playing, scrubbing just previews ticks and
                        // leaves playback stopped at the drop position (the play button can then resume from there).
                        scrubResumePlaying = playIntent;
                        if (playIntent)
                        {
                            AudioPreviewPlayback.Pause(playbackToken);
                            playIntent = false;
                            SetGlyph(false);
                        }

                        int sample = SampleAt(evt, clip, out float pct);
                        wavePlayhead.style.left = Length.Percent(pct * 100f);
                        resumeSample = sample;
                        AudioPreviewPlayback.BeginScrub(clip, sample, playbackToken); // continuous, seekable scrub stream
                        evt.StopPropagation();
                    });
                    waveArea.RegisterCallback<PointerMoveEvent>(evt =>
                    {
                        if (!waveArea.HasPointerCapture(evt.pointerId)) return;
                        var clip = CurrentClip();
                        if (clip == null) return;
                        int sample = SampleAt(evt, clip, out float pct);
                        wavePlayhead.style.left = Length.Percent(pct * 100f);
                        resumeSample = sample;
                        AudioPreviewPlayback.ScrubSeek(clip, sample); // move the read cursor — smooth, no device restart
                        evt.StopPropagation();
                    });
                    waveArea.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (!waveArea.HasPointerCapture(evt.pointerId)) return;
                        waveArea.ReleasePointer(evt.pointerId);
                        isScrubbing = false;
                        AudioPreviewPlayback.Stop();
                        // Resume continuous playback from the drop position only if we were playing when the drag began.
                        if (scrubResumePlaying)
                            StartPlayback(resumeSample);
                        evt.StopPropagation();
                    });
                }
            }

            // ----- Playback wiring -----
            // Ownership is by token, NOT clip equality, so a second window showing the same clip never thinks
            // it is the one playing — clip equality makes the designer's playhead move while you scrub in the inspector.
            bool IsPlayingThis() => AudioPreviewPlayback.IsOwnedBy(playbackToken);

            void SetGlyph(bool playing) => playBtn.text = playing ? AudioStopGlyph : AudioPlayGlyph;

            // Start/stop go through a local intent flag so the toggle is deterministic regardless of the backend's
            // IsPlaying() start-up latency. ALWAYS Stop() before Play() so mashing can never stack looping voices.
            void StartPlayback(int sample)
            {
                var clip = CurrentClip();
                if (clip == null) return;
                int start = sample;
                if (clip.samples > 0)
                {
                    int tickSamples = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * (float)ScrubTickSeconds));
                    if (start < 0 || start >= clip.samples - tickSamples) start = 0; // at/after end → restart from 0
                }
                AudioPreviewPlayback.Stop();
                AudioPreviewPlayback.Play(clip, start, false, playbackToken);
                playIntent = true;
                playStartTime = EditorApplication.timeSinceStartup;
                SetGlyph(true);
            }

            void StopPlayback()
            {
                AudioPreviewPlayback.Stop();
                playIntent = false;
                SetGlyph(false);
            }

            playBtn.clicked += () =>
            {
                if (CurrentClip() == null) return;
                if (playIntent) StopPlayback();
                else StartPlayback(resumeSample);
            };

            // ----- Preview availability: native backend present on this platform AND the clip's samples readable -----
            void UpdateAvailability()
            {
                var clip = CurrentClip();
                bool clipPlayable = clip != null && AudioPreviewPlayback.IsClipPlayable(clip);
                bool playable = audioAvailable && clipPlayable;
                string hint =
                    !audioAvailable ? "Audio preview is unavailable — no audio output device was found, or it is disabled in Project Settings." :
                    (clip != null && !clipPlayable) ? "Set this clip's Load Type to “Decompress On Load” to preview." :
                    null;

                playBtn.SetEnabled(playable);
                playBtn.tooltip = hint ?? "Play / stop preview";

                if (waveHint != null)
                {
                    waveHint.text = hint ?? string.Empty;
                    waveHint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }

            // No audible backend (no device / disabled in settings): hide the transport entirely; waveform + hint remain.
            if (!audioAvailable) buttonsGroup.style.display = DisplayStyle.None;
            UpdateAvailability();

            // Stop the running voice when the BOUND CLIP changes. The transport is tied to the field's
            // current clip, but playback is a global editor voice that keeps going on the OLD one — and
            // clearing the field to None disables the stop button (UpdateAvailability), so there was no way
            // left to stop it short of waiting it out or tearing the inspector down. Also reset the resume
            // point: a sample offset into the previous clip means nothing in the new one.
            var lastClip = CurrentClip();
            wrapper.TrackPropertyValueWithRefresh(prop, _ =>
            {
                var now = CurrentClip();
                if (!ReferenceEquals(now, lastClip))
                {
                    lastClip = now;
                    resumeSample = 0;
                    if (playIntent || isScrubbing)
                    {
                        isScrubbing = false;
                        StopPlayback();
                    }
                }
                UpdateAvailability();
            });

            void Tick()
            {
                // Keep the editor repainting at full rate while we are animating. Otherwise the UI Toolkit panel
                // scheduler is serviced only at the editor's IDLE frame rate (~10 FPS) when nothing else requests
                // repaints, which makes the playhead visibly stutter. Requesting a repaint each frame sustains a
                // smooth ~60 FPS during playback/scrub; once both stop we stop requesting and the editor idles
                // again (no wasted CPU).
                if (playIntent || isScrubbing)
                    wrapper.MarkDirtyRepaint();

                double now = EditorApplication.timeSinceStartup;

                // While scrubbing, the drag drives the playhead and the continuous scrub stream directly.
                if (isScrubbing) return;

                // Reconcile a natural end (non-loop): once playback is no longer owned by us, drop intent and
                // rewind. The 0.15 s grace avoids cancelling a clip during the backend's start-up spin (where
                // IsPlaying() briefly reads false).
                if (playIntent && now - playStartTime > 0.15 && !IsPlayingThis())
                {
                    playIntent = false;
                    resumeSample = 0;
                    SetGlyph(false);
                    if (fieldPlayhead != null) fieldPlayhead.style.display = DisplayStyle.None;
                    return;
                }

                bool playing = playIntent;
                SetGlyph(playing);
                float pct = playing ? AudioPreviewPlayback.GetNormalizedPosition(CurrentClip()) : 0f;
                if (fieldPlayhead != null)
                {
                    fieldPlayhead.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
                    if (playing)
                    {
                        var display = objField.Q<VisualElement>(className: "unity-object-field-display");
                        if (display != null)
                        {
                            var fieldBound = fieldWrap.worldBound;
                            var displayBound = display.worldBound;
                            if (fieldBound.width > 0 && displayBound.width > 0)
                            {
                                float relativeLeft = displayBound.xMin - fieldBound.xMin;
                                float playheadX = relativeLeft + (pct * displayBound.width);
                                float playheadPct = (playheadX / fieldBound.width) * 100f;
                                fieldPlayhead.style.left = Length.Percent(playheadPct);

                                float relativeTop = displayBound.yMin - fieldBound.yMin;
                                float relativeBottom = fieldBound.yMax - displayBound.yMax;
                                fieldPlayhead.style.top = relativeTop;
                                fieldPlayhead.style.bottom = relativeBottom;
                            }
                            else
                            {
                                fieldPlayhead.style.left = Length.Percent(pct * 100f);
                                fieldPlayhead.style.top = 0;
                                fieldPlayhead.style.bottom = 0;
                            }
                        }
                        else
                        {
                            fieldPlayhead.style.left = Length.Percent(pct * 100f);
                            fieldPlayhead.style.top = 0;
                            fieldPlayhead.style.bottom = 0;
                        }
                    }
                }
                if (wavePlayhead != null && playing)
                {
                    wavePlayhead.style.left = Length.Percent(pct * 100f);
                }
            }

            // No detach Pause on the playhead tick: UI Toolkit pauses a scheduled item on detach and resumes
            // it on re-attach, while an explicit Pause() never resumes — it would freeze the playhead for good
            // after any re-parent (window tab drag). See PanelLifetime.
            wrapper.schedule.Execute(Tick).Every(16);
            PanelLifetime.OnRealDetach(wrapper, () =>
            {
                // Stop playback only if THIS widget owns it — never read the (possibly disposed) property here.
                if (AudioPreviewPlayback.IsOwnedBy(playbackToken))
                {
                    AudioPreviewPlayback.Stop();
                }
            });

            return wrapper;
        }
    }
}
#endif // SASQ_AUDIO
