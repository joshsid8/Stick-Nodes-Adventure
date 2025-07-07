using UnityEngine;

public class PlayerFireProjectile : MonoBehaviour
{
    public Transform projectilePoint;
    public GameObject projectile;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityDamage;
    public float abilityStunTime;
    public Vector2 abilityKnockbackForce;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pa = GetComponent<PlayerAbility>();
        pc = GetComponent<PlayerController>(); // Gets the PlayerController from the Player GameObject
    }

    public void Execute()
    {
        GameObject newProjectile = Instantiate(projectile, projectilePoint.position, Quaternion.identity);
        PlayerFireBall fireballScript = newProjectile.GetComponent<PlayerFireBall>();
        if (fireballScript != null)
        {
            fireballScript.SetValues(pc.isFacingRight, abilityDamage * pa.damageMultiplier, abilityStunTime, abilityKnockbackForce);
        }
        else
        {
            Debug.LogError("where is bro's ability");
        }
    }
}