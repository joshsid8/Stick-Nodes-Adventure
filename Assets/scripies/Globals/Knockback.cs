using System.Collections;
using UnityEngine;

public class Knockback : MonoBehaviour
{
    //jump yourself
    public float knockbackTime = 0.2f;
    public float hitDirectionForce = 10f;
    public float constForce = 5f;
    public float inputForce = 7.5f;
    public bool IsBeingKnocked {get; private set;}
    private Rigidbody2D rb;
    private Coroutine knockbackCoroutine;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    
    private IEnumerator KnockbackAction (Vector2 hitDirection, Vector2 constantForceDirection, float inputDirection)
    {
        IsBeingKnocked = true;
        
        Vector2 hitForce;
        Vector2 knockbackConstForce;
        Vector2 knockbackForce;
        Vector2 combinedForce;
        
        hitForce = hitDirection * hitDirectionForce;
        knockbackConstForce = constantForceDirection * constantForceDirection;
        
        float currentTime = 0f;
        while (currentTime < knockbackTime)
        {
            currentTime += Time.fixedDeltaTime;
            knockbackForce = hitForce + knockbackConstForce;
            if (inputDirection != 0)
            {
                combinedForce = knockbackForce + new Vector2(inputDirection * inputForce, 0f);
            }
            else
            {
                combinedForce = knockbackForce;
            }
            rb.linearVelocity = combinedForce;
            yield return new WaitForFixedUpdate();
        }
        
        IsBeingKnocked = false;
    }

    public void CallKnockback(Vector2 hitDirection, Vector2 constantForceDirection, float inputDirection)
    {
        knockbackCoroutine = StartCoroutine(KnockbackAction(hitDirection, constantForceDirection, inputDirection));
    }
}
