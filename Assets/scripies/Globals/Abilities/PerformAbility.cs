using System;
using System.Collections;
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

    private PlayerController pc;
    private PlayerAbility pa;
    private EnemyPatrol ep;
    private BossAttack ba;

    [HideInInspector] public bool isActivated;
    [HideInInspector] public bool unavailable;

    private int currentSlot;
    private int currentBlastType;

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
        unavailable = Physics2D.OverlapCircle(abilityPoint.position, 0.5f, terrainLayers);
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
        var energyReq = prefabBase.energyRequired;
        
        var hasEnoughEnergy = false;
        if (blastType == 0) hasEnoughEnergy = pa.energy >= energyReq * totalAmount;
        else hasEnoughEnergy = pa.energy >= energyReq;

        if (hasEnoughEnergy)
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
        var xOffset = 0f;

        for (var i = 0; i < amount; i++)
        {
            if (casterType == CasterType.Player && blastType == 0 && ability.isHoldable && !pa.isHolding) break;

            if (!unavailable)
            {
                if (i == 0 && blastType == 1) xOffset = ability.initSeparation;
                var spawnPos = abilityPoint.position + transform.right * xOffset;
                var spawnRot = blastType == 2 ? abilityPoint.rotation : Quaternion.identity;

                var newObj = Instantiate(ability.abilityPrefab, spawnPos, spawnRot);
                // the ??, i assume its an 'if null. do this'. learn what that is!!!
                var abScript = newObj.GetComponent<AbilityBase>() ?? newObj.GetComponentInChildren<AbilityBase>();

                if (abScript != null)
                {
                    ability.SetProperties(abScript);
                    abScript.SetValues(casterFactor, isFacingRight);
                    
                    if (casterType == CasterType.Enemy) abScript.direction = abilityDirection;

                    if (casterType == CasterType.Player)
                    {
                        pa.energy -= abScript.energyRequired;
                        pa.currentAbilityCooldown = abScript.cooldown * pa.abilityCooldownMult;
                        
                        if (blastType != 0 && !abScript.isHoldable) pa.isPerformingAbility = false;
                    }
                }
                else Debug.LogError($"Prefab {ability.abilityPrefab.name} has no AbilityBase >:(");
            }

            if (blastType != 0) xOffset += ability.separation;
            yield return new WaitForSeconds(ability.spawnInterval);
        }
        if (casterType == CasterType.Player) pa.isPerformingAbility = false;
    }

    private bool GetFacingRight()
    {
        if (casterType == CasterType.Player) return pc.isFacingRight;
        if (ba != null) return ba.isFacingRight;
        if (ep != null) return ep.isFacingRight;
        return true;
    }
}
