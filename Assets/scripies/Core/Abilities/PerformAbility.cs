using System;
using System.Collections;
using NUnit.Framework.Internal.Commands;
using scripies.Core;
using Unity.VisualScripting;
using UnityEngine;

public class PerformAbility : MonoBehaviour
{
    public enum CasterType { Player, Enemy, None }
    public CasterType casterType = CasterType.None;

    public Transform abilityPoint;
    public LayerMask terrainLayers;
    [Tooltip("Ability Objects to spawn. Only the Player uses this (unless something else needs to hold its abilities)")]
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
    
    public IPlayerStatus pa;
    public IEnemyStatus es;

    private void Start()
    {
        if (casterType == CasterType.Player) pa = GetComponent<IPlayerStatus>();
        else es = GetComponent<IEnemyStatus>();
    }

    private void Update()
    {
        //TODO: Either remove or improve this flag. its not good
        unavailable = Physics2D.OverlapCircle(new Vector2(abilityPoint.position.x, abilityPoint.position.y), 1f, terrainLayers);
        
        if (casterType != CasterType.Player) return;
        if (isActivated && pa.CurrentAbilityCooldown[currentSlot] >= pa.MaxAbilityCooldown[currentSlot])
            isActivated = false;
    }
    
    public void Perform(AbilityObjSO ability) => StartCoroutine(SpawnRoutine(ability, ability.GetAbility() switch {Projectile => 0, Pillar => 1, Beam => 2, _ => -1}));
    
    // For the player only
    public void Blast()
    {
        if (casterType == CasterType.Player) StartCoroutine(SpawnRoutine(objects[currentSlot], objects[currentSlot].GetAbility() switch {Projectile => 0, Pillar => 1, Beam => 2, _ => -1})); 
    }

    private IEnumerator SpawnRoutine(AbilityObjSO ability, int blastType)
    {
        if (ability == null) yield break;
        var abilityScript = ability.abilityPrefab.GetComponent<AbilityBase>() ?? ability.abilityPrefab.GetComponentInChildren<AbilityBase>();
        var isHoldable = ability.setProperties ? ability.isHoldable : abilityScript.isHoldable;
        var isFacingRight = GetFacingRight();
        var casterFactor = casterType switch { CasterType.Player => 1, CasterType.Enemy => 2, _ => 0 };
        var amount = casterType == CasterType.Player ? (int)Mathf.Round(ability.amount * abilityAmountMult[currentSlot]) : ability.amount;
        var xOffset = blastType == 1 ? ability.initSeparation : 0f;
        
        for (var i = 0; i < amount; i++)
        {
            if (unavailable) continue;
            
            if (i == 0 && ability.shockwave) xOffset = ability.initSeparation;
            
            if (i < amount - 1) StartCoroutine(Spawn(blastType, xOffset, isFacingRight, casterFactor, ability, i == amount));
            else if (i == amount - 1 || blastType == 0 && casterType == CasterType.Player && isHoldable && !pa.IsHoldingAbility)
            {
                StartCoroutine(Spawn(blastType, xOffset, isFacingRight, casterFactor, ability, true));
                break;
            }
            if (i == amount - 1 || casterType == CasterType.Player && ability.isHoldable && !pa.IsHoldingAbility)
            {
                if (blastType == 0) GameEvents.TriggerPlayerPerformAbility(false);
                break;
            }
            if (blastType != 0) xOffset += ability.separation;
            yield return new WaitForSeconds(ability.spawnInterval);
        }
        if (casterType == CasterType.Player && !isHoldable) GameEvents.TriggerPlayerPerformAbility(false);
    }

    private IEnumerator Spawn(int blastType, float xOffset, bool isFacingRight, int casterFactor, AbilityObjSO ability, bool lastObject = false)
    {
        Vector3[] spawnPos = {abilityPoint.position + transform.right * xOffset, abilityPoint.position - transform.right * xOffset};
        var spawnRot = blastType == 2 ? abilityPoint.rotation : Quaternion.identity;
        GameObject[] newObjs = {Instantiate(ability.abilityPrefab, spawnPos[0], spawnRot), Instantiate(ability.abilityPrefab, spawnPos[1], spawnRot)};
        switch (ability.shockwave)
        {
            case true:
            {
                foreach (var obj in newObjs)
                {
                    var abScript = obj.GetComponent<AbilityBase>() ?? obj.GetComponentInChildren<AbilityBase>();

                    if (abScript == null) continue;
                    ability.SetProperties(abScript);
                    abScript.SetValues(casterFactor, isFacingRight, gameObject);

                    switch (casterType)
                    {
                        case CasterType.None:
                        case CasterType.Enemy:
                            abScript.direction = abilityDirection;
                            break;
                        case CasterType.Player:
                            GameEvents.TriggerAddPlayerEnergy(-abScript.energyRequired / 2);
                            GameEvents.TriggerAddPlayerTime("Ability", 0);
                            if (lastObject)
                            {
                                yield return new WaitUntil(() => newObjs[0].IsDestroyed() || abScript.lifeTimer <= 0);
                                GameEvents.TriggerPlayerPerformAbility(false);
                            }
                            break;
                        default:
                            Debug.LogError($"How did you choose {casterType} as a caster type???");
                            break;
                    }
                }
                break;
            }
            case false:
            {
                Destroy(newObjs[1]);
                var abScript = newObjs[0].GetComponent<AbilityBase>() ?? newObjs[0].GetComponentInChildren<AbilityBase>();
                if (abScript == null) yield break;
                ability.SetProperties(abScript);
                abScript.SetValues(casterFactor, isFacingRight, gameObject);

                switch (casterType)
                {
                    case CasterType.None:
                    case CasterType.Enemy:
                        abScript.direction = abilityDirection;
                        break;
                    case CasterType.Player:
                    {
                        GameEvents.AddPlayerEnergy(-abScript.energyRequired);
                        GameEvents.TriggerAddPlayerTime("Ability", 0);
                        if (lastObject && abScript is not Projectile)
                        {
                            GameEvents.TriggerPlayerPerformAbility(true);
                            while (!newObjs[0].IsDestroyed()) yield return null;
                            GameEvents.TriggerPlayerPerformAbility(false);
                        }
                        else GameEvents.TriggerPlayerPerformAbility(!lastObject);
                        break;
                    }
                    default:
                        Debug.LogError($"How did you choose {casterType} as a caster type???");
                        break;
                }
                while (!newObjs[0].IsDestroyed() && abScript is Beam)
                {
                    if (!ability.moveWithCaster) newObjs[0].transform.position = Vector2.MoveTowards(newObjs[0].transform.position, abilityPoint.position, Time.deltaTime * 10f);
                    else newObjs[0].transform.position = abilityPoint.position;
                    yield return null;
                }
                break;
            }
        }
    }
    
    private bool GetFacingRight()
    {
        //return casterType == CasterType.Player ? pa.IsFacingRight : es.IsFacingRight;
        return casterType switch
        {
            CasterType.Player => pa.IsFacingRight,
            CasterType.Enemy => es.IsFacingRight,
            _ => transform.rotation.y == 0
        };
    }
}
