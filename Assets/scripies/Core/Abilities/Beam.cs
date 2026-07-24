using scripies.Core;
using UnityEngine;

public class Beam : AbilityBase
{
    private bool moveWithCaster = true;
    PerformAbility player;
    protected override void Start()
    {
        base.Start();
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? direction : -direction + 180f);
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<PerformAbility>();
    }

    protected override void Update()
    {
        base.Update();
        if (pa != null && typeFactor == 1) GameEvents.TriggerAddPlayerTime("Ability", cooldown);
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        
        if (moveWithCaster && typeFactor == 1) parentObject.transform.position = player.abilityPoint.position;
    }
    
    public override void SetValues(int factor, bool facingRight)
    {
        base.SetValues(factor, facingRight);
        isFacingRightDirection = facingRight;
    }
    
    private void OnDestroy()
    {
        if (pa != null && typeFactor == 1) GameEvents.TriggerPlayerPerformAbility(false);
    }

}
