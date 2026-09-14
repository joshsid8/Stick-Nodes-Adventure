# SasquatchSpring

A tiny, fast spring-physics animation library for Unity. Every motion is a real damped-harmonic
spring (no curves to author), the math is Burst-jobbed, and animations `await` cleanly. It runs in
builds **and** in the editor (so editor tooling can animate too), and auto-cleans up when the thing
it's animating is destroyed.

Namespace: `SasquatchSpring` · Ships in `SasquatchInspector.Runtime` (player-build safe).

---

## Quick start (code)

```csharp
using SasquatchSpring;

// Spring a transform — pick a preset feel:
transform.MoveTo(new Vector3(0, 3, 0), SpringConfig.Bouncy);
transform.ScaleTo(1.5f, SpringConfig.Snappy);
transform.RotateTo(Quaternion.Euler(0, 180, 0), SpringConfig.Boing); // shortest-arc, gimbal-free

// UI:
canvasGroup.FadeTo(1f, SpringConfig.Soft);
rectTransform.AnchorTo(new Vector2(0, 120), SpringConfig.Default);
image.ColorTo(Color.red, 0.4f, bounce: 0.6f);

// Entrances — snap to a start, then spring in:
canvasGroup.FadeFrom(0f, 1f, SpringConfig.Soft);
transform.ScaleFromTo(0f, 1f, SpringConfig.Bouncy);

// await completion:
await transform.MoveTo(target, SpringConfig.Heavy);
Debug.Log("arrived");
```

There are typed `...To` helpers for Transform, RectTransform, CanvasGroup, Image/Graphic, Slider,
SpriteRenderer, Camera (FOV/ortho), Light/Light2D, AudioSource (volume/pitch), Rigidbody/Rigidbody2D,
Material properties, TextMeshPro, and a generic `Spring.To(owner, getter, setter, target, config)` for
*any* float / Vector2 / Vector3 / Vector4 / Color property.

Helpers for an optional package or a removable built-in module compile out when it isn't installed —
uGUI (Image/Slider/Graphic), TextMeshPro, URP (Light2D), Physics, Physics2D and Audio. Everything else
is always available.

Rigidbody springs advance on the render clock but deliver their `MovePosition`/`MoveRotation` writes in
`FixedUpdate`, so they stay smooth above the fixed rate without breaking the physics contract.

## No code? Use the component

Add **SasquatchSpring ▸ Spring Tween** (`SasquatchSpringTween`) to any GameObject, choose an action
(Move / Scale / Rotate / Fade / Color / AnchoredPosition / SizeDelta), a target, a feel, and a trigger
(OnEnable / OnStart / Manual). Options: `relative`, `useFromValue` (entrances), and `Yoyo` looping.
Drive it from code with `Play()` / `Stop()` / `await PlayAsync()`.

Try it instantly: **Tools ▸ Sasquatch Inspector ▸ Spring ▸ Create Demo Objects**, then press Play.

## Presets (`SpringConfig`)

`Default · Snappy · Bouncy · Boing · Glide · Heavy · Direct · Soft` — or build your own
`new SpringConfig(duration, bounce)`. In the inspector a `SpringConfig` field shows a preset dropdown
and a live curve preview; the **Advanced** foldout exposes `SettleEpsilon`, `FrequencyScale`, and
`Unscaled`.

- **Duration** — time to settle. **Bounce** — 0 (none) … 1 (elastic) … 2 (very springy).
- **FrequencyScale** — wobble tightness without changing settle time.
- **Unscaled** — keep animating while the game is paused (`Time.timeScale == 0`): pause menus, time-stop UI.

```csharp
var cfg = SpringConfig.Snappy; cfg.Unscaled = true;
pauseMenu.FadeFrom(0f, 1f, cfg); // animates even at timeScale 0
```

## Handles: retarget, punch, query, cancel

The `Spring.Move/Scale/To/RotateTo` entry points return a **handle** you can keep:

```csharp
var h = Spring.Move(transform, a, SpringConfig.Default);
Spring.Move(transform, b, SpringConfig.Default); // retargets smoothly, keeping momentum

h.OnComplete(() => Debug.Log("done"));            // await-free callback
if (h.IsActive) { var v = h.Velocity; }           // query live state
h.Cancel();                                        // stop it
```

- **Retarget** any time — velocity carries over.
- **Punch** for impulse jiggles: `Spring.GetVector3Handle(...).Punch(velocity, cfg)` (UI Toolkit:
  `ve.PunchScale(...)`).
- **Await strategies**: `AwaitStrategy.PhysicalSettling` (default) vs `VisualArrival` (resolves earlier).
- **Loops/stagger**: `SpringLoop.Yoyo(...)`, `SpringLoop.Repeat(...)`, and `list.Stagger(owner, gap, ...)`.

## Notes

- Springs use **scaled** time by default and freeze at `timeScale 0` unless `config.Unscaled` is set.
- When the owning Component/GameObject is destroyed mid-spring, the spring cancels itself — no null refs.
- Try it live: **Tools ▸ Sasquatch Inspector ▸ Showcase Sandbox** (Animations page), or drop a
  **SasquatchSpring ▸ Spring Tween** (`SasquatchSpringTween`) component on any GameObject and press Play.
