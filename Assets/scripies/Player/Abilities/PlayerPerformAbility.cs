using UnityEngine;

public class PlayerPerformAbility : MonoBehaviour
{
    public Transform abilityPoint;
    public GameObject[] objects;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityDamage;
    public float abilityStunTime;
    public Vector2 abilityKnockbackForce; 
    public bool isDirectional;
    public Vector2 abilityDirection;
    public float abilityProc;
    public float abilityLifetime;
    private float xVector;
    public int blastType;
    
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pa = GetComponent<PlayerAbility>();
        pc = GetComponent<PlayerController>(); // Gets the PlayerController from the Player GameObject
    }

    public void Execute()
    {
        switch (blastType)
        {
            case 0:
                GameObject newProjectile = Instantiate(objects[0], abilityPoint.position, Quaternion.identity);
                PlayerShootBlast fireballScript = newProjectile.GetComponent<PlayerShootBlast>();
                if (fireballScript != null)
                {
                    fireballScript.SetValues(pc.isFacingRight, abilityDamage * pa.damageMultiplier, abilityStunTime,
                        abilityKnockbackForce);
                }
                else Debug.LogError("where is bro's projectile");
                break;
            case 1:
                GameObject newPillar = Instantiate(objects[1], abilityPoint.position + new Vector3(xVector, -1f, 0f), Quaternion.identity);
                PlayerFirePillar pillarScript = newPillar.GetComponent<PlayerFirePillar>();
        
                if (pillarScript != null) pillarScript.SetValues(abilityLifetime, abilityDamage * pa.damageMultiplier, abilityProc, abilityKnockbackForce);
                else Debug.LogError("where is bro's pillar"); 
                break;
            case 2:
                GameObject newBeam = Instantiate(objects[2], abilityPoint.position + new Vector3(xVector, 0, 0f), Quaternion.identity);
                PlayerFireBeam beamScript = newBeam.GetComponent<PlayerFireBeam>();
        
                if (beamScript != null) beamScript.SetValues(GetComponent<PlayerController>().isFacingRight, abilityLifetime, abilityProc, abilityDirection, isDirectional);
                else Debug.LogError("where is bro's beam"); 
                break;
            default:
                Debug.LogError($"blast type is nun - '{blastType}'.");
                GetComponent<Animator>().SetTrigger("Damaged");
                break;
        }
    }
}