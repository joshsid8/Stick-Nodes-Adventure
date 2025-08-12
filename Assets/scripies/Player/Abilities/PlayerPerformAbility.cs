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
    public float abilityDirection;
    public float abilityProcMultiplier = 1f;
    public float abilityLifetimeMultiplier = 1f;
    [SerializeField] private float xVector;
    public int blastType;
    public int abilityAmount;

    [HideInInspector] public bool unavailable;
    [SerializeField] private LayerMask terrainLayers;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        pa = GetComponent<PlayerAbility>();
        pc = GetComponent<PlayerController>(); // Gets the PlayerController from the Player GameObject
    }

    private void Update()
    {
        unavailable = Physics2D.OverlapCircle(abilityPoint.position, 0.5f, terrainLayers);
    }

    public void Execute()
    {
        switch (blastType)
        {
            case 0:
                if (!unavailable)
                {
                    GameObject newProjectile = Instantiate(objects[0], abilityPoint.position, Quaternion.identity);
                    PlayerShootBlast fireballScript = newProjectile.GetComponent<PlayerShootBlast>();
                    if (fireballScript != null)
                    {
                        fireballScript.SetValues(pc.isFacingRight, abilityDamage * pa.damageMultiplier, abilityStunTime,
                            abilityKnockbackForce);
                    }
                    else Debug.LogError("where is bro's projectile");
                }
                break;
            case 1:
                if (!unavailable)
                {
                    float preXVector = xVector;
                    for (int i = 0; i < abilityAmount; i++)
                    {
                        GameObject newPillar = Instantiate(objects[1],
                            abilityPoint.position + new Vector3(xVector * transform.right.x, 0f, 0f),
                            Quaternion.identity);
                        PlayerFirePillar pillarScript = newPillar.GetComponent<PlayerFirePillar>();

                        if (pillarScript != null)
                            pillarScript.SetValues(abilityLifetimeMultiplier, abilityDamage * pa.damageMultiplier,
                                abilityProcMultiplier, abilityKnockbackForce);
                        else Debug.LogError("where is bro's pillar");
                        xVector += 1.5f;
                    }
                    xVector = preXVector;
                }

                break;
            case 2:
                GameObject newBeam = Instantiate(objects[2], abilityPoint.position + new Vector3(0, 0, 0f), abilityPoint.rotation);
                PlayerFireBeam beamScript = newBeam.GetComponentInChildren<PlayerFireBeam>();
        
                if (beamScript != null) beamScript.SetValues(GetComponent<PlayerController>().isFacingRight, abilityLifetimeMultiplier, abilityProcMultiplier, abilityDirection, isDirectional);
                else Debug.LogError("where is bro's beam"); 
                break;
            default:
                Debug.LogError($"blast type is nun - '{blastType}'. dw i gotchu");
                GetComponent<Animator>().SetTrigger("Damaged");
                blastType = Random.Range(0, 3);
                break;
        }
    }
}