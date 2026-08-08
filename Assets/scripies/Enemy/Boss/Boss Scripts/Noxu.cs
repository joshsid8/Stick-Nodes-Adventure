using Unity.VisualScripting;
using UnityEngine;

public class Noxu : BossBase
{
    // Update is called once per frame
    protected override void Update()
    {
        base.Update();
        rb.linearVelocity = freezeMovement ? Vector2.zero : new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
    }

    private void ReplenishShield(float shieldHp) => bh.shieldHealth = shieldHp;

    public override void Gamble()
    {
        if (storedObjects.Count == 0 || storedObjects[0] == null || storedObjects[0].IsDestroyed()) return;
        var rng = Random.Range(0, 3);
        animator.SetInteger("factor", rng);
        animator.SetTrigger("Boulder");
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
