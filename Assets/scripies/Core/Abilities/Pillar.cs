using scripies.Core;
using UnityEngine;

public class Pillar : AbilityBase
{ 
    //PILLAR IS SO BARREN NOW!!!
    
    [SerializeField] private bool shockwave;

    private void OnDestroy()
    {
        if (pa != null && typeFactor == 1) GameEvents.TriggerPlayerPerformAbility(false);
    }
    
}
