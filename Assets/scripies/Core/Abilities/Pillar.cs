using scripies.Core;
using UnityEngine;

public class Pillar : AbilityBase
{ 
    //PILLAR IS SO BARREN NOW!!!
    
    [SerializeField] private bool shockwave;

    public override void SetValues(int factor, bool facingRight)
    {
        typeFactor = factor;
        isFacingRightDirection = GameObject.FindGameObjectWithTag("Player").GetComponent<PerformAbility>().pa.IsFacingRight;
    }

    private void OnDestroy()
    {
        if (pa != null && typeFactor == 1) GameEvents.TriggerPlayerPerformAbility(false);
    }
    
}
