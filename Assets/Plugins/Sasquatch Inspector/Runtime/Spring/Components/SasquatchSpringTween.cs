using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
#if SASQ_UGUI
using UnityEngine.UI;
#endif

namespace SasquatchSpring
{
    /// <summary>
    /// A drop-on spring animation — configure a target, a feel (<see cref="SpringConfig"/>) and a trigger
    /// in the inspector, no scripting required. It's a thin dispatcher over the SasquatchSpring extension
    /// API (MoveTo / ScaleTo / RotateTo / FadeTo / ColorTo / AnchorTo / SizeDeltaTo), so it inherits all the
    /// engine's behaviour (retarget, await, auto-cleanup on destroy, unscaled-time via the config flag).
    /// Drive it from code with <see cref="Play"/> / <see cref="Stop"/> / <see cref="PlayAsync"/> too.
    /// </summary>
    [AddComponentMenu("SasquatchSpring/Spring Tween")]
    public class SasquatchSpringTween : MonoBehaviour
    {
        /// <summary>Which property the tween animates (move / scale / rotate / anchored position / size / fade / color).</summary>
        public enum SpringAction
        {
            MovePosition, MoveLocalPosition, Scale, Rotate,
            AnchoredPosition, SizeDelta,
            FadeCanvasGroup, FadeImage, FadeSprite, ColorGraphic
        }
        /// <summary>When the tween plays — manually, on enable, or on start.</summary>
        public enum TriggerMode { Manual, OnEnable, OnStart }
        /// <summary>Whether the tween plays once or yoyos back and forth.</summary>
        public enum LoopMode { Once, Yoyo }

        [Header("What & When")]
        public SpringAction action = SpringAction.Scale;
        public TriggerMode trigger = TriggerMode.OnEnable;
        public LoopMode loop = LoopMode.Once;

        [Header("Target")]
        [Tooltip("Target for vector actions: Move / Scale / Rotate (Euler degrees) / AnchoredPosition / SizeDelta.")]
        public Vector3 vectorTarget = Vector3.one;
        [Tooltip("Target alpha (0..1) for the Fade actions.")]
        public float floatTarget = 1f;
        [Tooltip("Target tint for the Color action.")]
        public Color colorTarget = Color.white;
        [Tooltip("Treat the vector target as an offset added to the value at play time.")]
        public bool relative = false;

        [Header("Start (optional)")]
        [Tooltip("Snap to an explicit start value before springing (e.g. fade in from 0, pop in from scale 0).")]
        public bool useFromValue = false;
        public Vector3 fromVector = Vector3.zero;
        public float fromFloat = 0f;
        public Color fromColor = new Color(1f, 1f, 1f, 0f);

        [Header("Feel")]
        public SpringConfig config = SpringConfig.Bouncy;

        [Header("Events")]
        public UnityEvent onComplete;

        // Resolved at the start of each Play() so 'relative' offsets and the Yoyo return point are stable.
        private Vector3 _startVec, _endVec;
        private float _startF, _endF;
        private Color _startC, _endC;

        // Cached driven components (only the one the current action needs is fetched).
        private CanvasGroup _cg;
#if SASQ_UGUI
        private Image _image;
#endif
        private SpriteRenderer _sprite;
#if SASQ_UGUI
        private Graphic _graphic;
#endif
        private RectTransform _rect;

        private object LoopKey => this; // unique per component instance

        private void OnEnable() { if (trigger == TriggerMode.OnEnable) Play(); }
        private void Start() { if (trigger == TriggerMode.OnStart) Play(); }
        private void OnDisable() => Stop();

        /// <summary>Starts the configured tween (or Yoyo loop). Safe to call repeatedly.</summary>
        public void Play() => _ = PlayAsync();

        /// <summary>Stops a running Yoyo loop. In-flight springs settle on their own (or are cancelled on destroy).</summary>
        public void Stop() => SpringLoop.Stop(LoopKey);

        /// <summary>
        /// Starts the tween. The returned Task completes when a one-shot settles (it never completes for a
        /// Yoyo loop, which runs until <see cref="Stop"/> or the object is destroyed).
        /// </summary>
        public async Task PlayAsync()
        {
            Resolve();

            if (useFromValue) ApplyImmediate(_startVec, _startF, _startC);

            if (loop == LoopMode.Yoyo)
            {
                SpringLoop.Yoyo(LoopKey, this,
                    _ => SpringTo(_endVec, _endF, _endC),
                    _ => SpringTo(_startVec, _startF, _startC));
                return;
            }

            await SpringTo(_endVec, _endF, _endC);
            onComplete?.Invoke();
        }

        // Captures the current value (home), then resolves the start and end for this play.
        private void Resolve()
        {
            Vector3 homeVec = CurrentVector();
            float homeF = CurrentFloat();
            Color homeC = CurrentColor();

            _startVec = useFromValue ? fromVector : homeVec;
            _startF = useFromValue ? fromFloat : homeF;
            _startC = useFromValue ? fromColor : homeC;

            _endVec = relative ? homeVec + vectorTarget : vectorTarget;
            _endF = floatTarget;
            _endC = colorTarget;
        }

        private Vector3 CurrentVector()
        {
            switch (action)
            {
                case SpringAction.MovePosition: return transform.position;
                case SpringAction.MoveLocalPosition: return transform.localPosition;
                case SpringAction.Scale: return transform.localScale;
                case SpringAction.Rotate: return transform.eulerAngles;
                case SpringAction.AnchoredPosition: return Rect ? (Vector3)Rect.anchoredPosition : Vector3.zero;
                case SpringAction.SizeDelta: return Rect ? (Vector3)Rect.sizeDelta : Vector3.zero;
                default: return Vector3.zero;
            }
        }

        private float CurrentFloat()
        {
            switch (action)
            {
                case SpringAction.FadeCanvasGroup: return CG ? CG.alpha : 0f;
#if SASQ_UGUI
                case SpringAction.FadeImage: return Img ? Img.color.a : 0f;
#endif
                case SpringAction.FadeSprite: return Sprite ? Sprite.color.a : 0f;
                default: return 0f;
            }
        }

#if SASQ_UGUI
        private Color CurrentColor()
        {
            // Only the Color action drives a Graphic. Resolving this unconditionally (Resolve() calls all
            // three Current* readers on every Play) fired the lazy `Graphic` getter for EVERY action, so a
            // plain Scale tween on an object with no Graphic logged "'Scale' on 'Foo' needs a Graphic
            // component" — in the player, for a component it was never going to touch.
            if (action != SpringAction.ColorGraphic) return Color.white;
            return Graphic ? Graphic.color : Color.white;
        }
#else
        private Color CurrentColor() => Color.white;
#endif

        /// <summary>
        /// <see cref="config"/> with a settle epsilon appropriate to the action's UNITS, when the user left it
        /// at 0 ("use the engine default").
        /// </summary>
        /// <remarks>
        /// The engine default (0.001) is tuned for normalized values like alpha or a 0..1 scale. In pixel space
        /// (anchored position, sizeDelta) or degree space it is far below one visible step, so the spring rings
        /// on for roughly another second after it has visibly arrived — and <see cref="onComplete"/>, which is
        /// the whole point of the component, fires that late. SpringConfig's own field comment calls for ~0.5
        /// in pixel space; this applies it instead of leaving it to every user to discover.
        /// </remarks>
        private SpringConfig EffectiveConfig
        {
            get
            {
                // .Resolved first: SpringConfig's never-configured sentinel requires EVERY field to still be
                // zero, so writing SettleEpsilon below would silently destroy it and leave Duration 0 (an
                // instant snap) on any component whose config was never authored.
                var c = config.Resolved;
                if (c.SettleEpsilon > 0f) return c; // the user set one — never override it

                switch (action)
                {
                    case SpringAction.AnchoredPosition:
                    case SpringAction.SizeDelta:
                        c.SettleEpsilon = 0.5f;  // pixels
                        break;
                    case SpringAction.Rotate:
                        c.SettleEpsilon = 0.05f; // degrees
                        break;
                }
                return c;
            }
        }

        private void ApplyImmediate(Vector3 vec, float f, Color col)
        {
            switch (action)
            {
                case SpringAction.MovePosition: transform.position = vec; break;
                case SpringAction.MoveLocalPosition: transform.localPosition = vec; break;
                case SpringAction.Scale: transform.localScale = vec; break;
                case SpringAction.Rotate: transform.eulerAngles = vec; break;
                case SpringAction.AnchoredPosition: if (Rect) Rect.anchoredPosition = vec; break;
                case SpringAction.SizeDelta: if (Rect) Rect.sizeDelta = vec; break;
                case SpringAction.FadeCanvasGroup: if (CG) CG.alpha = f; break;
#if SASQ_UGUI
                case SpringAction.FadeImage: if (Img) { var c = Img.color; c.a = f; Img.color = c; } break;
#endif
                case SpringAction.FadeSprite: if (Sprite) { var c = Sprite.color; c.a = f; Sprite.color = c; } break;
#if SASQ_UGUI
                case SpringAction.ColorGraphic: if (Graphic) Graphic.color = col; break;
#endif
            }
        }

        private Task SpringTo(Vector3 vec, float f, Color col)
        {
            var cfg = EffectiveConfig;
            switch (action)
            {
                case SpringAction.MovePosition: return transform.MoveTo(vec, cfg);
                case SpringAction.MoveLocalPosition: return Spring.MoveLocal(transform, vec, cfg).WaitFor(AwaitStrategy.PhysicalSettling);
                case SpringAction.Scale: return transform.ScaleTo(vec, cfg);
                case SpringAction.Rotate: return transform.RotateTo(Quaternion.Euler(vec), cfg);
                case SpringAction.AnchoredPosition: return Rect ? Rect.AnchorTo(vec, cfg) : Task.CompletedTask;
                case SpringAction.SizeDelta: return Rect ? Rect.SizeDeltaTo(vec, cfg) : Task.CompletedTask;
                case SpringAction.FadeCanvasGroup: return CG ? CG.FadeTo(f, cfg) : Task.CompletedTask;
#if SASQ_UGUI
                case SpringAction.FadeImage: return Img ? Img.FadeTo(f, cfg) : Task.CompletedTask;
#endif
                case SpringAction.FadeSprite: return Sprite ? Sprite.FadeTo(f, cfg) : Task.CompletedTask;
#if SASQ_UGUI
                case SpringAction.ColorGraphic: return Graphic ? Graphic.ColorTo(col, cfg) : Task.CompletedTask;
#endif
                default: return Task.CompletedTask;
            }
        }

        // Lazily-fetched driven components, with a friendly warning when the action needs one that's absent.
        private RectTransform Rect
        {
            get { if (_rect == null) { _rect = transform as RectTransform; if (_rect == null) Warn("RectTransform"); } return _rect; }
        }
        private CanvasGroup CG
        {
            get { if (_cg == null) { _cg = GetComponent<CanvasGroup>(); if (_cg == null) Warn("CanvasGroup"); } return _cg; }
        }
#if SASQ_UGUI
        private Image Img
        {
            get { if (_image == null) { _image = GetComponent<Image>(); if (_image == null) Warn("Image"); } return _image; }
        }
#endif
        private SpriteRenderer Sprite
        {
            get { if (_sprite == null) { _sprite = GetComponent<SpriteRenderer>(); if (_sprite == null) Warn("SpriteRenderer"); } return _sprite; }
        }
#if SASQ_UGUI
        private Graphic Graphic
        {
            get { if (_graphic == null) { _graphic = GetComponent<Graphic>(); if (_graphic == null) Warn("Graphic"); } return _graphic; }
        }
#endif

        // Warn-once per missing component type. Without this, a Yoyo loop whose target component is absent
        // makes SpringTo return Task.CompletedTask instantly, so the loop re-fires (and re-warns) roughly every
        // frame — ~60 warnings/sec in a shipped build. The component won't materialize on its own, so a single
        // warning is all the user needs.
        private HashSet<string> _warnedComponents;
        private void Warn(string component)
        {
            _warnedComponents ??= new HashSet<string>();
            if (!_warnedComponents.Add(component)) return;
            Debug.LogWarning($"[Sasquatch Spring] '{action}' on '{name}' needs a {component} component.", this);
        }
    }
}
