using System.Collections;
using UnityEngine;

public class Noxu : BossBase
{
    // Update is called once per frame
    private void Update()
    {
        Strafe();
        if (freezeMovement) rb.linearVelocity = Vector2.zero;
        else rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }

    private void ReplenishShield(float shieldHp) => bh.shieldHealth = shieldHp;

    public override void Gamble()
    {
        var rng = Random.Range(0, 3);
        animator.SetInteger("factor", rng);
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
