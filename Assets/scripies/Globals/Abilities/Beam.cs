using UnityEngine;

public class Beam : AbilityBase
{
    protected override void Start()
    {
        base.Start();
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? direction : -direction + 180f);
    }

    protected override void Update()
    {
        base.Update();
        if (pa != null && typeFactor == 1) pa.currentAbilityCooldown = cooldown * pa.abilityCooldownMult;
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
    }
    
    public override void SetValues(int factor, bool facingRight)
    {
        base.SetValues(factor, facingRight);
        isFacingRightDirection = facingRight;
    }
    
    private void OnDestroy()
    {
        if (pa != null && typeFactor == 1) pa.isPerformingAbility = false;
    }

}
