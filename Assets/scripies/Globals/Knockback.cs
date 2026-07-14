using System;
using System.Collections;
using UnityEngine;

public class Knockback : MonoBehaviour
{
    // jump yourself
    public float knockbackTime = 0.2f;
    public float hitDirectionForce = 20f;
    public bool IsBeingKnocked {get; private set;}
    private Rigidbody2D rb;
    
    private void Awake() => rb = GetComponent<Rigidbody2D>();

    private IEnumerator KnockbackAction(Vector2 hitDirection)
    {
        if (hitDirection == Vector2.zero) yield break;

        IsBeingKnocked = true;
        
        var startForce = hitDirection.normalized * hitDirectionForce;
        var elapsed = 0f;

        while (elapsed < knockbackTime)
        {
            elapsed += Time.fixedDeltaTime;
            var normalizedTime = elapsed / knockbackTime;
            var currentKnockback = Vector2.Lerp(startForce, Vector2.zero, normalizedTime);
            
            rb.linearVelocity = new Vector2(currentKnockback.x, currentKnockback.y != 0 ? currentKnockback.y : rb.linearVelocity.y);
            yield return new WaitForFixedUpdate();
        }

        IsBeingKnocked = false;
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }

    public void CallKnockback(Vector2 hitDirection)
    {
        StopAllCoroutines(); 
        StartCoroutine(KnockbackAction(hitDirection));
    }
}
