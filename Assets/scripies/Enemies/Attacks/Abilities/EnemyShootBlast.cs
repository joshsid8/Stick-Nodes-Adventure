using System.Collections;
using UnityEngine;

public class EnemyShootBlast : MonoBehaviour
{
    public Transform abilityPoint;
    
    // Projectile
    private EnemyPatrol ep;
    public float projectileSpeed;
    
    // Beam and Pillar
    public float abilityLifetime;
    public float abilityProc;

    public int damageType;

    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ep = GetComponent<EnemyPatrol>();
    }
    public void ProjectileThrow(GameObject projectile)
    {
        GameObject newProjectile = Instantiate(projectile, abilityPoint.position, abilityPoint.rotation);
        EnemyFireProjectile efp = newProjectile.GetComponent<EnemyFireProjectile>();
        
        if (efp != null) efp.SetValues(ep.isFacingRight, projectileSpeed, Vector2.zero, false, damageType);
        else Debug.LogError("where is bro's projectile ability");
    }
    public void ShootBeam(GameObject beam)
    {
        GameObject newBeam = Instantiate(beam, abilityPoint.position, abilityPoint.rotation);
        EnemyBeam eb = newBeam.GetComponent<EnemyBeam>();
        
        if (eb != null) eb.SetValues(ep.isFacingRight, abilityLifetime, abilityProc, Vector2.zero, false, damageType);
        else Debug.LogError("where is bro's beam ability");
    }
    
    public void SummonPillar(GameObject pillar)
    {
        GameObject newBeam = Instantiate(pillar, abilityPoint.position, abilityPoint.rotation);
        EnemyFirePillar eb = newBeam.GetComponent<EnemyFirePillar>();
    }
}
