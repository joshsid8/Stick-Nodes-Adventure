using System;
using System.Collections;
using UnityEngine;

public class Knockback : MonoBehaviour
{
    public bool IsBeingKnocked {get; private set;}
    private Rigidbody2D rb;
    
    private void Awake() => rb = GetComponent<Rigidbody2D>();

    private IEnumerator KnockbackAction(Vector2 hitDirection, float force, float time)
    {    
        if (hitDirection == Vector2.zero) yield break;

        IsBeingKnocked = true;
        
        var startForce = hitDirection.normalized * force;
        var elapsed = 0f;

        while (elapsed < time)
        {
            elapsed += Time.fixedDeltaTime;
            var normalizedTime = elapsed / time;
            var currentKnockback = Vector2.Lerp(startForce, Vector2.zero, normalizedTime);
            
            rb.linearVelocity = new Vector2(currentKnockback.x, currentKnockback.y != 0 ? currentKnockback.y : rb.linearVelocity.y);
            yield return new WaitForFixedUpdate();
        }

        IsBeingKnocked = false;
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }

    public void CallKnockback(Vector2 hitDirection, float force = 20f, float time = 0.2f)
    {
        StopAllCoroutines();
        StartCoroutine(KnockbackAction(hitDirection, force, time));
    }
}
