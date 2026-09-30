using System;
using scripies.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Pillar : AbilityBase
{ 
    //PILLAR IS SO BARREN NOW!!!
    private void LateUpdate() => isFacingRightDirection = !GameObject.FindGameObjectWithTag("Player").GetComponent<PerformAbility>().pa.IsFacingRight;
    public override void SetValues(int factor, bool facingRight, GameObject caster = null, AbilityObjSO id = null)
    {
        typeFactor = factor;
        // isFacingRight is already updated by update
        if (caster != null) abilityCaster = caster;
        if (id != null) abilityId = id;
    }
    
    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
