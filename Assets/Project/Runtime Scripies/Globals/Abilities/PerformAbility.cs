using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class PerformAbility : MonoBehaviour
{
    public enum CasterType { Player, Enemy }
    public CasterType casterType;

    public Transform abilityPoint;
    public LayerMask terrainLayers;
    public AbilityObjSO[] objects;

    [Header("Player Properties")]
    public float abilityDamageMult = 1f;
    public float abilityPotency = 1f;
    public float abilityLifetimeMult = 1f;
    public int[] abilityAmountMult = { 1, 1, 1 };

    [Header("Enemy Properties")]
    public float abilityDirection;
    
    [HideInInspector] public bool isActivated;
    [HideInInspector] public bool unavailable;
    private int currentSlot;
    private int currentBlastType;
    
    private PlayerController pc;
    private PlayerAbility pa;
    private EnemyPatrol ep;
    private BossAttack ba;

    private void Start()
    {
        if (casterType == CasterType.Player)
        {
            pa = GetComponent<PlayerAbility>();
            pc = GetComponent<PlayerController>();
        }
        else
        {
            ep = GetComponent<EnemyPatrol>();
            ba = GetComponent<BossAttack>();
        }
    }

    private void Update()
    {
        unavailable = Physics2D.OverlapCircle(new Vector2(abilityPoint.position.x, abilityPoint.position.y), 1f, terrainLayers);
        if (casterType == CasterType.Player)
            if (isActivated && pa.currentAbilityCooldown <= 0) isActivated = false;
    }

    public void Execute(int blastType = 0, int slot = 0)
    {
        if (casterType != CasterType.Player || isActivated) return;

        var ability = objects[slot];
        if (ability == null) return;
        
        var prefabBase = ability.abilityPrefab.GetComponent<AbilityBase>() ?? ability.abilityPrefab.GetComponentInChildren<AbilityBase>();

        if (prefabBase == null)
        {
            Debug.LogError("Ability prefab missing AbilityBase component!");
            return;
        }

        var totalAmount = Mathf.Round(ability.amount * abilityAmountMult[slot]);
        var energyReq = ability.energyRequired == 0 ? prefabBase.energyRequired : ability.energyRequired;
        
        var hasEnoughEnergy = pa.energy >= energyReq * totalAmount;
        if (hasEnoughEnergy && !unavailable)
        {
            isActivated = true;
            currentBlastType = blastType;
            currentSlot = slot;
            pa.isPerformingAbility = true;

            var animName = blastType switch
            {
                0 => pc.isGrounded ? "Fire" : "AirFire",
                1 => "Pillar",
                2 => "Beam",
                _ => "Fire"
            };
            pa.animator.Play(animName);
        }
    }
    
    public void Perform(AbilityObjSO ability) => StartCoroutine(SpawnRoutine(ability, casterType == CasterType.Player ? currentBlastType : -1));

    public void Blast()
    {
        if (casterType == CasterType.Player) StartCoroutine(SpawnRoutine(objects[currentSlot], currentBlastType)); 
    }

    private IEnumerator SpawnRoutine(AbilityObjSO ability, int blastType)
    {
        if (ability == null) yield break;

        var isFacingRight = GetFacingRight();
        var casterFactor = casterType == CasterType.Player ? 1 : 2;
        var amount = casterType == CasterType.Player ? (int)Mathf.Round(ability.amount * abilityAmountMult[currentSlot]) : ability.amount;
        var xOffset = blastType == 1 ? ability.initSeparation : 0f;

        for (var i = 0; i < amount; i++)
        {
            if (casterType == CasterType.Player && blastType == 0 && ability.isHoldable && !pa.isHolding) break;
            if (!unavailable && ability.shockwave && i != 0 || !unavailable && !ability.shockwave)
            {
                Spawn(i, blastType, xOffset, isFacingRight, casterFactor, ability);

                if (blastType != 0) xOffset += ability.separation;
                if (!unavailable && ability.shockwave && i != 0 || !unavailable && !ability.shockwave)
                    yield return new WaitForSeconds(ability.spawnInterval);
            }
            else if (i == 0)
            {
                pa.isPerformingAbility = false;
                yield break;
            }
        }
        if (casterType == CasterType.Player && blastType == 0) pa.isPerformingAbility = false;
    }

    private void Spawn(int i, int blastType, float xOffset, bool isFacingRight, int casterFactor, AbilityObjSO ability)
    {
        Vector3[] spawnPos = {abilityPoint.position + transform.right * xOffset, abilityPoint.position - transform.right * xOffset};
        var spawnRot = blastType == 2 ? abilityPoint.rotation : Quaternion.identity;
        GameObject[] newObjs = {Instantiate(ability.abilityPrefab, spawnPos[0], spawnRot), Instantiate(ability.abilityPrefab, spawnPos[1], spawnRot)};
        if (newObjs[0].IsDestroyed() || newObjs[1].IsDestroyed())
        {
            pa.isPerformingAbility = false;
            return;
        }
        if (ability.shockwave)
        {
            foreach (var obj in newObjs)
            {
                var abScript = obj.GetComponent<AbilityBase>() ?? obj.GetComponentInChildren<AbilityBase>();

                if (abScript != null)
                {
                    ability.SetProperties(abScript);
                    abScript.SetValues(casterFactor, isFacingRight);

                    if (casterType == CasterType.Enemy) abScript.direction = abilityDirection;

                    if (casterType == CasterType.Player)
                    {
                        pa.energy -= abScript.energyRequired / 2;
                        pa.currentAbilityCooldown = abScript.cooldown * pa.abilityCooldownMult;
                    }
                }
            }
        }
        else if (!ability.shockwave)
        {
            Destroy(newObjs[1]);
            var abScript = newObjs[0].GetComponent<AbilityBase>() ?? newObjs[0].GetComponentInChildren<AbilityBase>();

            if (abScript != null)
            {
                ability.SetProperties(abScript);
                abScript.SetValues(casterFactor, isFacingRight);

                if (casterType == CasterType.Enemy) abScript.direction = abilityDirection;

                if (casterType == CasterType.Player)
                {
                    pa.energy -= abScript.energyRequired;
                    pa.currentAbilityCooldown = abScript.cooldown * pa.abilityCooldownMult;
                }
            }
        }
    }

    private bool GetFacingRight()
    {
        if (casterType == CasterType.Player) return pc.isFacingRight;
        if (ba != null) return ba.isFacingRight;
        if (ep != null) return ep.ec.isFacingRight;
        return true;
    }
}
