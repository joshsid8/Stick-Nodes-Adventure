using UnityEngine;

public class EnemyShootBlast : MonoBehaviour
{
    public Transform abilityPoint;
    
    // Projectile
    private EnemyPatrol ep;
    public float projectileSpeed;
    
    // Beam and Pillar
    public float damageMult;
    public float lifetimeMult;
    public float potency;
    public float abilityDirection;
    public int damageType;

    private bool unavailable;
    [SerializeField] private LayerMask terrainLayers;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start() => ep = GetComponent<EnemyPatrol>();

    private void Update() => unavailable = Physics2D.OverlapCircle(abilityPoint.position, 0.5f, terrainLayers);
 
    public void PerformAbility(AbilityObjSO ability)
    {
        if (!unavailable)
        {
            var newAbility = Instantiate(ability.abilityPrefab, abilityPoint.position, abilityPoint.rotation);
            var script = newAbility.GetComponent<AbilityBase>();
            if (script != null)
            {
                ability.SetProperties(script);
                script.SetValues(2, ep.isFacingRight);
            }
        }
    }
    
    public void ProjectileThrow(GameObject projectile)
    {
        if (!unavailable)
        {
            var newProjectile = Instantiate(projectile, abilityPoint.position, abilityPoint.rotation);
            var efp = newProjectile.GetComponent<EnemyFireProjectile>();

            if (efp != null) efp.SetValues(ep.isFacingRight, projectileSpeed, Vector2.zero, false, damageType);
            else Debug.LogError("where is bro's projectile ability");
        }
    }

    
    public void SummonPillar(GameObject pillar)
    {
        if (!unavailable)
        {
            var newBeam = Instantiate(pillar, abilityPoint.position, abilityPoint.rotation);
            var eb = newBeam.GetComponent<EnemyPillar>();
        }
    }

    public void ShootBeam(GameObject beam)
    {
        if (!unavailable)
        {
            var newBeam = Instantiate(beam, abilityPoint.position, abilityPoint.rotation);
            var eb = newBeam.GetComponentInChildren<Beam>();

            if (eb != null) eb.SetValues(2, ep.isFacingRight);
            else Debug.LogError("where is bro's beam ability");
        }
    }
}
