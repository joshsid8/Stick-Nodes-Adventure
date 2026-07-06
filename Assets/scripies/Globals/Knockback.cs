using System;
using System.Collections;
using UnityEngine;

public class Knockback : MonoBehaviour
{
    // jump yourself
    public float knockbackTime = 0.1f;
    public float hitDirectionForce = 1f;
    public bool IsBeingKnocked {get; private set;}
    private Rigidbody2D rb;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private IEnumerator KnockbackAction(Vector2 hitDirection, float inputDirection = 0)
    {
        if (hitDirection.x == 0 && hitDirection.y != 0) rb.linearVelocity = new Vector2(0, hitDirection.y);
        else if (hitDirection != Vector2.zero)
        {
            IsBeingKnocked = true;
            Vector2 hitForce;
            Vector2 knockbackForce;
            Vector2 combinedForce;

            hitForce = hitDirection * hitDirectionForce;
            
            var currentTime = 0f;
            while (currentTime < knockbackTime)
            {
                currentTime += Time.fixedDeltaTime;
                knockbackForce = hitForce;
                if (inputDirection != 0) combinedForce = knockbackForce + new Vector2(inputDirection, 0f);
                else combinedForce = knockbackForce;
                
                combinedForce.x /= currentTime;
                if (combinedForce.y == 0) rb.linearVelocity = new Vector2(combinedForce.x, rb.linearVelocity.y);
                else if (combinedForce.x == 0) rb.linearVelocity = new Vector2(rb.linearVelocity.x, combinedForce.y);
                else rb.linearVelocity = combinedForce;
                yield return new WaitForFixedUpdate();
            }

            IsBeingKnocked = false;
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        }

    }

    public void CallKnockback(Vector2 hitDirection, float inputDirection)
    {
        StartCoroutine(KnockbackAction(hitDirection, inputDirection));
    }
}
