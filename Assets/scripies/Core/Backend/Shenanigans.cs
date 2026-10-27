using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using scripies.Core;
using UnityEngine.Search;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
///  This script is meant to manage literally every small function that occurs in this game, similar to the player manager.
///  If it cant be managed anywhere else without it being inconvenient, add it here!
///  Keep in mind it that this derives from Core. Tomfoolery is the script that derives from Main
/// </summary>
public class Shenanigans : MonoBehaviour
{
    public static Shenanigans Instance {get; private set;}

    [Header("Jump Scaring")] 
    [SerializeField] private Image jsObjImage;
    
    [SearchContext("dir:Sprites/unn", "t:Sprite")] 
    [SerializeField] private Sprite[] jsImages;

    public AudioClip[] jumpScareClips;
    
    [Header("Stored Object")]
    public GameObject storedObj;
    
    [Header("Freeze Camera")]
    public bool freezeCamera;
    [SerializeField] private GameObject camObj;

    public enum HitstopMode
    {
        LocalParticipants,
        GlobalWorld,
        Hybrid
    }

    [Header("Hitstop / Impact Freeze")]
    [Tooltip("LocalParticipants: Only hitter, victim, and nearby FX freeze.\nGlobalWorld: Everything freezes.\nHybrid: Local freeze on normal hits, Global freeze on heavy hits/counters.")]
    [EnumButtons] public HitstopMode hitstopMode = HitstopMode.Hybrid;
    [Tooltip("Minimum damage required to trigger impact freeze frames.")]
    public float minDamageThreshold = 5f;
    [Tooltip("Damage threshold where Hybrid mode switches to a full global freeze.")]
    public float heavyDamageThreshold = 25f;
    [Tooltip("Damage value at which freeze duration reaches maxFreezeDuration.")]
    public float maxDamageReference = 60f;
    [Tooltip("Shortest impact freeze duration (in seconds) at minDamageThreshold.")]
    public float minFreezeDuration = 0.045f;
    [Tooltip("Maximum impact freeze duration cap (in seconds) for massive hits.")]
    public float maxFreezeDuration = 0.16f;
    [Tooltip("Subtle horizontal shake amplitude on the victim during impact freeze.")]
    public float victimShakeIntensity = 0.07f;

    private class FrozenEntityState
    {
        public GameObject Target;
        public Animator Animator;
        public float OriginalAnimSpeed;
        public Rigidbody2D Rb;
        public float OriginalGravityScale;
        public Vector2 StoredVelocity;
        public Knockback Knockback;
        public Vector3 LockedPosition;
        public float RemainingTime;
        public bool IsVictim;
        public bool IsPlayer;
    }

    private readonly Dictionary<GameObject, FrozenEntityState> frozenEntities = new();
    private readonly List<GameObject> expiredKeys = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        GameEvents.JumpScare += JumpScare;
        GameEvents.OnApplyDamage += ApplyDamage;
        GameEvents.OnHitImpact += HandleHitImpact;
        GameEvents.StopTime += TriggerGlobalHitstop;
    }
    
    private void OnDisable()
    {
        GameEvents.JumpScare -= JumpScare;
        GameEvents.OnApplyDamage -= ApplyDamage;
        GameEvents.OnHitImpact -= HandleHitImpact;
        GameEvents.StopTime -= TriggerGlobalHitstop;
        ReleaseAllFrozenEntities();
        freezeCamera = false;
        Time.timeScale = 1f;
    }

    private void OnDestroy()
    {
        ReleaseAllFrozenEntities();
        freezeCamera = false;
        Time.timeScale = 1f;
    }
    
    private void Update()
    {
        //camObj.SetActive(!freezeCamera);
        Time.timeScale = freezeCamera ? 0 : 1;
    }

    private void FixedUpdate()
    {
        if (frozenEntities.Count == 0) return;

        foreach (var kvp in frozenEntities)
        {
            var obj = kvp.Key;
            var state = kvp.Value;
            if (obj == null || state.RemainingTime <= 0f) continue;

            if (state.Rb != null)
            {
                state.Rb.linearVelocity = Vector2.zero;
                state.Rb.gravityScale = 0f;
            }
        }
    }

    private void LateUpdate()
    {
        if (frozenEntities.Count == 0) return;

        expiredKeys.Clear();
        var dt = Time.unscaledDeltaTime;

        foreach (var kvp in frozenEntities)
        {
            var obj = kvp.Key;
            var state = kvp.Value;

            if (obj == null)
            {
                expiredKeys.Add(obj);
                continue;
            }

            state.RemainingTime -= dt;
            if (state.RemainingTime <= 0f)
            {
                expiredKeys.Add(obj);
                continue;
            }

            if (state.Animator != null) state.Animator.speed = 0f;
            if (state.Rb != null)
            {
                state.Rb.linearVelocity = Vector2.zero;
                state.Rb.gravityScale = 0f;
            }

            if (state.IsVictim && victimShakeIntensity > 0f)
            {
                var jitterX = Mathf.Sin(Time.unscaledTime * 95f) * victimShakeIntensity;
                obj.transform.position = new Vector3(state.LockedPosition.x + jitterX, state.LockedPosition.y, state.LockedPosition.z);
            }
        }

        foreach (var key in expiredKeys)
        {
            if (frozenEntities.TryGetValue(key, out var state))
            {
                RestoreEntityState(state);
                frozenEntities.Remove(key);
            }
        }
    }

    #region Hitstop

    public float CalculateHitstopDuration(Damage damage)
    {
        if (damage == null) return 0f;
        var effectiveDamage = damage.damageAmount * Mathf.Max(1f, damage.elementFactor);
        if (effectiveDamage < minDamageThreshold) return 0f;

        var t = Mathf.InverseLerp(minDamageThreshold, Mathf.Max(minDamageThreshold + 1f, maxDamageReference), effectiveDamage);
        var duration = Mathf.Lerp(minFreezeDuration, maxFreezeDuration, t);

        if (damage.damageFactor >= 1)
            duration = Mathf.Min(maxFreezeDuration * 1.25f, duration * (1f + 0.15f * damage.damageFactor));

        return duration;
    }

    private void HandleHitImpact(GameObject attacker, GameObject victim, Damage damage)
    {
        var duration = CalculateHitstopDuration(damage);
        if (duration <= 0f) return;

        var effectiveDamage = damage.damageAmount * Mathf.Max(1f, damage.elementFactor);
        var useGlobal = hitstopMode == HitstopMode.GlobalWorld ||
                        (hitstopMode == HitstopMode.Hybrid && (effectiveDamage >= heavyDamageThreshold || damage.damageFactor >= 1));

        if (useGlobal)
        {
            TriggerGlobalHitstop(duration, victim);
        }
        else
        {
            TriggerLocalHitstop(attacker, victim, duration);
        }
    }

    public void TriggerLocalHitstop(GameObject attacker, GameObject victim, float duration)
    {
        if (duration <= 0f) return;

        if (attacker != null)
        {
            FreezeEntity(attacker, duration, false);
            if (attacker.transform.parent != null && attacker.GetComponent<AbilityBase>() != null)
                FreezeEntity(attacker.transform.parent.gameObject, duration, false);
        }

        if (victim != null)
        {
            FreezeEntity(victim, duration, true);

            // Also freeze any active FX/ability objects around the impact point
            var fxLayers = LayerMask.GetMask("FX", "PVFX", "EVFX");
            if (fxLayers != 0)
            {
                var nearbyFx = Physics2D.OverlapCircleAll(victim.transform.position, 3.5f, fxLayers);
                foreach (var col in nearbyFx)
                {
                    if (col != null) FreezeEntity(col.gameObject, duration, false);
                }
            }
        }
    }

    public void TriggerGlobalHitstop(float duration) => TriggerGlobalHitstop(duration, null);

    public void TriggerGlobalHitstop(float duration, GameObject primaryVictim)
    {
        if (duration <= 0f) return;

        if (ObjectDatabase.Instance != null)
        {
            foreach (var obj in ObjectDatabase.Instance.everything)
            {
                if (obj == null) continue;
                FreezeEntity(obj, duration, obj == primaryVictim);
            }
        }

        var abilities = FindObjectsByType<AbilityBase>(FindObjectsSortMode.None);
        foreach (var ab in abilities)
        {
            if (ab != null) FreezeEntity(ab.gameObject, duration, false);
        }

        if (primaryVictim != null)
            FreezeEntity(primaryVictim, duration, true);
    }

    private void FreezeEntity(GameObject obj, float duration, bool isVictim)
    {
        if (obj == null) return;

        obj.TryGetComponent<Rigidbody2D>(out var rb);

        if (frozenEntities.TryGetValue(obj, out var existing))
        {
            existing.RemainingTime = Mathf.Max(existing.RemainingTime, duration);
            existing.IsVictim |= isVictim;
            if (rb != null)
            {
                if (rb.linearVelocity.y > existing.StoredVelocity.y)
                    existing.StoredVelocity.y = rb.linearVelocity.y;
                if (Mathf.Abs(rb.linearVelocity.x) > Mathf.Abs(existing.StoredVelocity.x))
                    existing.StoredVelocity.x = rb.linearVelocity.x;
            }
            if (existing.IsPlayer)
                GameEvents.TriggerForceFreezePlayer(true, !existing.IsVictim);
            return;
        }

        obj.TryGetComponent<Animator>(out var anim);
        obj.TryGetComponent<Knockback>(out var kb);

        if (anim == null && rb == null) return;

        var isPlayer = obj.CompareTag("Player");
        var state = new FrozenEntityState
        {
            Target = obj,
            Animator = anim,
            OriginalAnimSpeed = anim != null && anim.speed > 0f ? anim.speed : 1f,
            Rb = rb,
            OriginalGravityScale = rb != null ? rb.gravityScale : 0f,
            StoredVelocity = rb != null ? rb.linearVelocity : Vector2.zero,
            Knockback = kb,
            LockedPosition = obj.transform.position,
            RemainingTime = duration,
            IsVictim = isVictim,
            IsPlayer = isPlayer
        };

        if (isPlayer)
            GameEvents.TriggerForceFreezePlayer(true, !isVictim);

        if (anim != null) anim.speed = 0f;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
        }
        if (kb != null) kb.IsFrozenInHitstop = true;

        frozenEntities[obj] = state;
    }

    private static void RestoreEntityState(FrozenEntityState state)
    {
        if (state == null || state.Target == null) return;

        if (state.IsVictim)
            state.Target.transform.position = state.LockedPosition;

        if (state.Animator != null)
            state.Animator.speed = state.OriginalAnimSpeed;

        if (state.Rb != null)
            state.Rb.gravityScale = state.OriginalGravityScale;

        if (state.Knockback != null)
            state.Knockback.IsFrozenInHitstop = false;

        if (state.IsPlayer)
        {
            GameEvents.TriggerForceFreezePlayer(false, !state.IsVictim);
        }
        else if (state.Rb != null && !state.IsVictim && (state.Knockback == null || !state.Knockback.IsBeingKnocked))
        {
            state.Rb.linearVelocity = state.StoredVelocity;
        }
    }

    private void ReleaseAllFrozenEntities()
    {
        foreach (var state in frozenEntities.Values)
            RestoreEntityState(state);
        frozenEntities.Clear();
    }

    #endregion
    private void StopTime(float time = 0.2f) => StartCoroutine(StopTimeC(time));
    
    private IEnumerator StopTimeC(float time = 0.2f)
    {
        freezeCamera = true;
        yield return new WaitForSecondsRealtime(time);
        freezeCamera = false;
    }

    private void JumpScare(Image image, float duration, float fadeMult) => StartCoroutine(StartJumpScare(image, duration, fadeMult)); 

    private IEnumerator StartJumpScare(Image image, float duration, float fadeMult)
    {
        if (jsObjImage == null) yield break;
        jsObjImage.sprite = image == null ? jsImages[Random.Range(0, jsImages.Length)] : image.sprite;
        jsObjImage.color = new Color(1,1,1,1);
        jsObjImage.enabled = true;
        yield return new WaitForSeconds(duration);
        while (jsObjImage.color.a > 0)
        {
            jsObjImage.color -= new Color(0, 0, 0, Time.deltaTime * fadeMult);
            yield return new WaitForFixedUpdate();
        }
        jsObjImage.enabled = false;
    }

    public void StoreObject(GameObject obj) => storedObj = obj;
    
    /// <summary>
    ///  Applies a force to a stored object, or any object thats in the radius if otherwise.
    /// </summary>
    public void ApplyForce(Vector2 force, float radius = 0, GameObject obj = null, Transform area = null)
    {
        if (storedObj != null)
        {
            storedObj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
            storedObj = null;
        }
        else if (obj != null) obj.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
        else if (area != null)
        {
           var hit = Physics2D.CircleCast(area.position, radius, new Vector2(area.transform.right.x, area.transform.up.y), 0f, LayerMask.GetMask("FX", "EVFX", "PVFX"));
           if (hit.collider != null) hit.collider.gameObject.GetComponent<Rigidbody2D>().AddForce(force, ForceMode2D.Impulse);
        }
    }
    
    public IEnumerator FreezeCameraTime(float freezeTime)
    {
        freezeCamera = true;
        yield return new WaitForSecondsRealtime(freezeTime);
        freezeCamera = false;
    }
    
    public void ToggleFreezeCamera(bool freeze = true) => freezeCamera = freeze;

    public void RandomAttribute(float fidelity = 1, IDamageable target = null)
    {
        var rng = Random.Range(0, 2);
        float[] rng2 = { Random.Range(0, 15), Random.Range(0, 2), Random.Range(0, 10), Random.Range(0, 4)};
        switch (rng)
        {
            case 0:
                var dm = RawDamage(rng2[0] * fidelity, 2, IDamageable.ElementType.Normal, 1, true,
                    rng2[1] == 0, rng2[2], 0.1f);
                GameEvents.TriggerApplyDamage(target, dm, true);
                target?.Shock(rng2[3]);
                break;
            case 1:
                target?.Heal(rng2[0]);
                break;
        }
    }
    
    /// <summary>
    /// A Global method as an alternative to using coroutines! But we have to make sure it has no parameters unfortunately
    /// </summary>
    public void MethodInvoke(string method, float time = 1f, bool repeating = false, float interval = 0)
    {
        if (!repeating) Invoke(method, time);
        else InvokeRepeating(method, time, interval);
    }

    private static void ApplyDamage(IDamageable target, Damage damage, bool facingRight, string aggressor = "") => target?.Damage(damage, facingRight, aggressor);

    public Damage RawDamage(float damageAmount, int damageFactor = 0,
        IDamageable.ElementType elementType = IDamageable.ElementType.Normal, float elementFactor = 1F, bool isTrue = false,
        bool damageOverTime = false, float procDmg = 0F, float procInterval = 0F)
    {
        var damage = new Damage
        {
            damageAmount = damageAmount,
            damageFactor = damageFactor,
            elementType = elementType,
            damageOverTime = damageOverTime,
            procDmg = procDmg,
            procInterval = procInterval,
            elementFactor = elementFactor,
            isTrue = isTrue,
        };
        return damage;
    }
}
