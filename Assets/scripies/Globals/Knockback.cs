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
        
        // Calculate the starting force vector
        Vector2 startForce = hitDirection.normalized * hitDirectionForce;
        float elapsed = 0f;

        while (elapsed < knockbackTime)
        {
            elapsed += Time.fixedDeltaTime;
            float normalizedTime = elapsed / knockbackTime;
            
            // Smoothly reduce velocity from startForce to zero
            // We keep the current Y velocity if you want gravity to still affect the player during knockback
            Vector2 currentKnockback = Vector2.Lerp(startForce, Vector2.zero, normalizedTime);
            
            rb.linearVelocity = new Vector2(currentKnockback.x, currentKnockback.y != 0 ? currentKnockback.y : rb.linearVelocity.y);
            
            yield return new WaitForFixedUpdate();
        }

        IsBeingKnocked = false;
        // Optionally reset horizontal velocity to stop sliding at the end
        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
    }

    public void CallKnockback(Vector2 hitDirection, float unusedInput = 0)
    {
        StopAllCoroutines(); 
        StartCoroutine(KnockbackAction(hitDirection));
    }
}
