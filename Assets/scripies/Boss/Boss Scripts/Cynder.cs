using System.Collections;
using UnityEngine;

public class Cynder : BossBase
{
    public float backflipForceY;
    public float backflipForceX;

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();
        Strafe();
        FlyDash();

        isInRange = Physics2D.OverlapCircle(transform.position, attackRange * 1.5f, ba.playerLayer);
        rb.linearVelocity = freezeMovement ? Vector2.zero : new Vector2(rb.linearVelocity.x, rb.linearVelocity.y);
        //dashDirection = (ba.playerTransform.position - transform.position).normalized;
        
        isFalling = rb.linearVelocity.y < 0;
        
        animator.SetBool("isFalling", isFalling);
        
        animator.speed = bh.phase2 ? 1.15f : 1;
    }

    private void BackFlip() => rb.linearVelocity = ba.isFacingRight ? new Vector2(-backflipForceX, backflipForceY) :
        new Vector2(backflipForceX, backflipForceY);

    public override void Gamble()
    {
        if (ba.directionToPlayer >= 8f || ba.directionToPlayer <= -8f)
        {
            var rng = Random.Range(0, 101);
            if (rng <= 40)
            {
                animator.SetTrigger("Homing");
                ba.ActionCooldown(3f);
            }
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(attackTransform.position, attackRange);
    }
}
