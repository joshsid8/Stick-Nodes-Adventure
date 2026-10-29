using scripies.Core;
using UnityEngine;

public class Beam : AbilityBase
{
    public bool moveWithCaster = true;
    PerformAbility player;

    public override void SetValues(int factor, bool facingRight, GameObject caster = null, AbilityObjSO id = null)
    {
        base.SetValues(factor, facingRight, caster, id);
        if (id != null && id.setProperties)
            moveWithCaster = id.moveWithCaster;
    }

    protected override void Start()
    {
        base.Start();
        parentObject.transform.eulerAngles = new Vector3(0, 0, isFacingRightDirection ? direction : -direction + 180f);
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.GetComponent<PerformAbility>();
    }

    protected override void Update()
    {
        base.Update();
        if (pa != null && typeFactor == 1) GameEvents.TriggerAddPlayerTime("Ability", 0);
        if (isFacingRightDirection) transform.eulerAngles = Vector3.zero;
        else transform.eulerAngles = new Vector3(0, 180, 0);
        transform.localEulerAngles = new Vector3(0, 0, 0);
        
        if (moveWithCaster && typeFactor == 1 && player != null && player.abilityPoint != null)
            parentObject.transform.position = player.abilityPoint.position;
    }
    
    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

}
