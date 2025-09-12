using UnityEngine;

public class PlayerPerformAbility : MonoBehaviour
{
    public Transform abilityPoint;
    public GameObject[] objects;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityDamageMult;
    public float abilityPotency;

    public bool isDirectional;
    public float abilityDirection;
    public float abilityLifetimeMult = 1f;
    [SerializeField] private float xVector;

    public int abilityAmount;
    private int blastType;
    [HideInInspector] public bool isActivated;

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
        if (isActivated && pa.currentAbilityCooldown <= 0) isActivated = false;
    }

    public void Execute(int blast)
    {
        if (!isActivated)
        {
            isActivated = true;
            blastType = blast;
            switch (blast)
            {
                case 0:
                    if (objects[0] != null && pa.energy >= objects[0].GetComponent<PlayerShootBlast>().energyRequired)
                        if (pc.isGrounded) pa.animator.Play("Fire");
                        else pa.animator.Play("AirFire");
                    break;
                case 1:
                    if (objects[1] != null && pa.energy >= objects[1].GetComponent<PlayerSummonPillar>().energyRequired)
                        if (pc.isGrounded) pa.animator.Play("Fire");
                        else pa.animator.Play("AirFire");
                    break;
                case 2:
                    if (objects[2] != null &&
                        pa.energy >= objects[2].GetComponentInChildren<PlayerShootBeam>().energyRequired)
                        if (pc.isGrounded) pa.animator.Play("Fire");
                        else pa.animator.Play("AirFire");
                    break;
                case 3:
                    print("*insert reinforcement here*");
                    break;
                default:
                    blastType = Random.Range(0, 3);
                    if (pc.isGrounded) pa.animator.Play("Fire");
                    else pa.animator.Play("AirFire");
                    break;
            }
        }
    }

    private void Blast()
    {
        switch (blastType)
        {
            case 0:
                if (!unavailable && objects[0] != null)
                {
                    GameObject newProjectile = Instantiate(objects[0], abilityPoint.position, Quaternion.identity);
                    PlayerShootBlast projectileScript = newProjectile.GetComponent<PlayerShootBlast>();
                    if (projectileScript != null)
                    {
                        projectileScript.SetValues(pc.isFacingRight, abilityDamageMult, abilityPotency,
                            abilityPotency);
                        pa.energy -= projectileScript.energyRequired;
                        pa.currentAbilityCooldown = projectileScript.abilityCooldown * pa.abilityCooldownMult;
                    }
                    else Debug.LogError("where is bro's projectile");
                }
                break;
            case 1:
                if (!unavailable && objects[1] != null)
                {
                    float preXVector = xVector;
                    for (int i = 0; i < abilityAmount; i++)
                    {
                        GameObject newPillar = Instantiate(objects[1],
                            abilityPoint.position + new Vector3(xVector * transform.right.x, 0f, 0f),
                            Quaternion.identity);
                        PlayerSummonPillar pillarScript = newPillar.GetComponent<PlayerSummonPillar>();

                        if (pillarScript != null)
                        {
                            pillarScript.SetValues(abilityLifetimeMult,
                                abilityDamageMult / abilityAmount, abilityPotency);
                            pa.energy -= pillarScript.energyRequired / abilityAmount;
                            pa.currentAbilityCooldown = pillarScript.abilityCooldown * pa.abilityCooldownMult;
                        }
                        else Debug.LogError("where is bro's pillar");
                        xVector += 1.5f;
                    }

                    xVector = preXVector;
                }
                break;
            case 2:
                if (objects[2] != null)
                {
                    GameObject newBeam = Instantiate(objects[2], abilityPoint.position + new Vector3(0, 0, 0f),
                        abilityPoint.rotation);
                    PlayerShootBeam beamScript = newBeam.GetComponentInChildren<PlayerShootBeam>();

                    if (beamScript != null)
                    {
                        beamScript.SetValues(GetComponent<PlayerController>().isFacingRight, abilityLifetimeMult,abilityPotency ,abilityDirection, isDirectional);
                        pa.energy -= beamScript.energyRequired;
                        pa.currentAbilityCooldown = beamScript.abilityCooldown * pa.abilityCooldownMult;
                    }
                    else Debug.LogError("where is bro's beam");
                }
                break;

        }
    }
}