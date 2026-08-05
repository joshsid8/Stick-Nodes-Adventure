using System.Collections;
using NUnit.Framework.Internal.Commands;
using scripies.Core;
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
    public int currentSlot;
    public int currentBlastType;
    
    protected IPlayerStatus pa;
    private IEnemyStatus es;

    private void Start()
    {
        if (casterType == CasterType.Player) pa = GetComponent<IPlayerStatus>();
        else es = GetComponent<IEnemyStatus>();
    }

    private void Update()
    {
        unavailable = Physics2D.OverlapCircle(new Vector2(abilityPoint.position.x, abilityPoint.position.y), 1f, terrainLayers);
        if (casterType == CasterType.Player)
            if (isActivated && pa.CurrentAbilityCooldown <= 0) isActivated = false;
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
            if (casterType == CasterType.Player && blastType == 0 && ability.isHoldable && !pa.IsHoldingAbility) break;
            if (!unavailable)
            {
                if (i == 0 && ability.shockwave) xOffset = ability.initSeparation;
                
                Spawn(i, blastType, xOffset, isFacingRight, casterFactor, ability);
                
                if (blastType != 0) xOffset += ability.separation;
                yield return new WaitForSeconds(ability.spawnInterval);
            }
            else if (i == 0 && casterType == CasterType.Player) GameEvents.TriggerPlayerPerformAbility(false);
        }
        if (casterType == CasterType.Player && blastType == 0) GameEvents.TriggerPlayerPerformAbility(false);
    }

    private void Spawn(int i, int blastType, float xOffset, bool isFacingRight, int casterFactor, AbilityObjSO ability)
    {
        Vector3[] spawnPos = {abilityPoint.position + transform.right * xOffset, abilityPoint.position - transform.right * xOffset};
        var spawnRot = blastType == 2 ? abilityPoint.rotation : Quaternion.identity;
        GameObject[] newObjs = {Instantiate(ability.abilityPrefab, spawnPos[0], spawnRot), Instantiate(ability.abilityPrefab, spawnPos[1], spawnRot)};
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
                        GameEvents.TriggerAddPlayerEnergy(-abScript.energyRequired / 2);
                        GameEvents.TriggerAddPlayerTime("Ability", abScript.cooldown);
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
                    GameEvents.AddPlayerEnergy(-abScript.energyRequired);
                    GameEvents.TriggerAddPlayerTime("Ability", abScript.cooldown);
                }
            }
        }
    }

    private bool GetFacingRight()
    {
        if (casterType == CasterType.Player) return pa.IsFacingRight;
        return es.IsFacingRight;
    }
}
