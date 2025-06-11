using System.Collections;
using UnityEngine;

public class EnemyProjectileThrow : MonoBehaviour
{
    private EnemyFireBall pj;
    public Transform projectilePoint;
    public GameObject projectile;
    private EnemyPatrol ep;
    public float projectileSpeed;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pj = GetComponent<EnemyFireBall>();
        ep = GetComponent<EnemyPatrol>();
    }
    public void ProjectileThrow()
    {
        GameObject newProjectile = Instantiate(projectile, projectilePoint.position, projectilePoint.rotation);
        EnemyFireBall fireballScript = newProjectile.GetComponent<EnemyFireBall>();
        if (fireballScript != null)
        {
            fireballScript.SetValues(ep.isFacingRight, projectileSpeed, Vector2.zero, false);
        }
        else
        {
            Debug.LogError("where is bro's ability");
        }
    }


}
