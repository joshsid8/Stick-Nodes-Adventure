using System.Collections;
using UnityEngine;

public class EnemyShootBlast : MonoBehaviour
{
    public Transform abilityPoint;
    public float abilityDirection;
    private EnemyPatrol ep;
    private BossAttack ba;
    private bool unavailable;
    [SerializeField] private LayerMask terrainLayers;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ep = GetComponent<EnemyPatrol>();
        ba = GetComponent<BossAttack>();
    }

    private void Update() => unavailable = Physics2D.OverlapCircle(abilityPoint.position, 0.5f, terrainLayers);
 
    public IEnumerator PerformAbility(AbilityObjSO ability)
    {
        var xVector = 0f;
        for (var i = 0; i < ability.amount; i++)
        {
            if (!unavailable)
            {
                if (i == 0 && ability.abilityPrefab.GetComponent<Projectile>() == null) xVector = ability.initSeparation;
                var newAbility = Instantiate(ability.abilityPrefab, abilityPoint.position + new Vector3(xVector, 0f), Quaternion.identity);
                print($"spawned {newAbility.name} in {abilityPoint.position + new Vector3(xVector, 0f)} (thats {xVector} added to {abilityPoint.position})");
                if (newAbility.GetComponent<Projectile>() != null)
                {
                    var projectile = newAbility.GetComponent<Projectile>();
                    ability.SetProperties(projectile);
                    projectile.SetValues(2, ba != null ? ba.isFacingRight : ep.ec.isFacingRight);
                    projectile.direction = abilityDirection;
                }
                else if (newAbility.GetComponentInChildren<Pillar>() != null)
                {
                    var pillar = newAbility.GetComponentInChildren<Pillar>();
                    ability.SetProperties(pillar);
                    pillar.SetValues(2, ba != null ? ba.isFacingRight : ep.ec.isFacingRight);
                    pillar.direction = abilityDirection;
                }
                else if (newAbility.GetComponentInChildren<Beam>() != null)
                {
                    var beam = newAbility.GetComponentInChildren<Beam>();
                    ability.SetProperties(beam);
                    beam.SetValues(2, ba != null ? ba.isFacingRight : ep.ec.isFacingRight);
                    beam.direction = abilityDirection;
                }
                else Debug.LogError("where is bro's ability");
            }
            if (ability.abilityPrefab.GetComponent<Projectile>() == null)
            {
                print($"added {ability.separation} to {xVector} to make it {xVector + ability.separation}");
                xVector += ability.separation;
            }
            yield return new WaitForSeconds(ability.spawnInterval);
        }
    }
}
