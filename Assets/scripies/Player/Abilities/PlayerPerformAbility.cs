using System.Collections;
using UnityEngine;

public class PlayerPerformAbility : MonoBehaviour
{
    public Transform abilityPoint;
    public GameObject[] objects;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityDamageMult;
    public float abilityPotency;
    public float abilityDirection;
    public float abilityLifetimeMult = 1f;
    [SerializeField] private float xVector;

    public int[] abilityAmount = {1, 1, 1};
    private int blastType;
    [HideInInspector] public bool isActivated;
    //[HideInInspector] public bool isReinforcing;

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
        //pa.animator.SetBool("isReinforcing", isReinforcing);
        //Reinforce();
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
                    if (objects[0] != null && pa.energy >= objects[0].GetComponent<Projectile>().energyRequired * abilityAmount[0])
                    {
                        pa.isPerformingAbility = true;
                        abilityAmount[0] = objects[0].GetComponent<Projectile>().projectileAmount;
                        pa.animator.Play(pc.isGrounded ? "Fire" : "AirFire");
                    }
                    break;
                case 1:
                    if (objects[1] != null && pa.energy >= objects[1].GetComponentInChildren<Pillar>().energyRequired)
                    {
                        pa.isPerformingAbility = true;
                        pa.animator.Play(pc.isGrounded ? "Pillar" : "AirFire");
                    }
                    break;
                case 2:
                    if (objects[2] != null && pa.energy >= objects[2].GetComponentInChildren<Beam>().energyRequired)
                    {
                        pa.isPerformingAbility = true;
                        pa.animator.Play(pc.isGrounded ? "Beam" : "AirFire");
                    }
                    break;
                /*case 3:
                    if (objects[3] !=null &&
                        pa.energy >= objects[3].GetComponent<PlayerSummonPillar>().energyRequired)
                        isReinforcing = true;
                    break;*/
            }
        }
    }

    private IEnumerator Blast()
    {
        switch (blastType)
        {
            case 0:
                if (!unavailable && objects[0] != null)
                {
                    for (var i = 0; i < abilityAmount[0]; i++)
                    {
                        var newProjectile = Instantiate(objects[0], abilityPoint.position, Quaternion.identity);
                        var projectileScript = newProjectile.GetComponent<Projectile>();
                        if (projectileScript != null)
                        {
                            projectileScript.SetValues(1, GetComponent<PlayerController>().isFacingRight, 0,false
                                ,abilityDirection);
                            pa.energy -= projectileScript.energyRequired;
                            pa.currentAbilityCooldown = projectileScript.abilityCooldown * pa.abilityCooldownMult;
                            pa.isPerformingAbility = false;
                        }
                        else Debug.LogError("where is bro's projectile");

                        yield return new WaitForSeconds(0.1f);
                    }
                }
                else pa.isPerformingAbility = false;
                break;
            case 1:
                if (!unavailable && objects[1] != null)
                {
                    float preXVector = xVector;
                    for (int i = 0; i < abilityAmount[1]; i++)
                    {
                        var newPillar = Instantiate(objects[1],
                            abilityPoint.position + new Vector3(xVector * transform.right.x, 0f, 0f),
                            Quaternion.identity);
                        var pillarScript = newPillar.GetComponentInChildren<Pillar>();

                        if (pillarScript != null)
                        {
                            pillarScript.SetValues(abilityLifetimeMult,
                                abilityDamageMult / abilityAmount[1], abilityPotency);
                            pa.energy -= pillarScript.energyRequired / abilityAmount[1];
                            pa.currentAbilityCooldown = pillarScript.abilityCooldown * pa.abilityCooldownMult;
                        }
                        else Debug.LogError("where is bro's pillar");
                        xVector += 1.5f;
                        yield return new WaitForSeconds(0.1f);
                    }
                    pa.isPerformingAbility = false;
                    xVector = preXVector;
                }
                else pa.isPerformingAbility = false;
                break;
            case 2:
                if (objects[2] != null)
                {
                    var newBeam = Instantiate(objects[2], abilityPoint.position + new Vector3(0, 0, 0f),
                        abilityPoint.rotation);
                    var beamScript = newBeam.GetComponentInChildren<Beam>();

                    if (beamScript != null)
                    {
                        beamScript.SetValues(GetComponent<PlayerController>().isFacingRight, abilityDirection, 1, abilityDamageMult,abilityLifetimeMult, abilityPotency);
                        if (!beamScript.isHoldable)
                        {
                            pa.currentAbilityCooldown = beamScript.abilityCooldown * pa.abilityCooldownMult;
                            pa.energy -= beamScript.energyRequired;
                            pa.isPerformingAbility = false;
                        }
                    }
                    else Debug.LogError("where is bro's beam");
                }
                else pa.isPerformingAbility = false;
                break;
        }
    }

    /*private void Reinforce()
    {
        if (isReinforcing)
        {
            pc.rb.linearVelocity = new Vector2(0, pc.rb.linearVelocity.y);
            if (pc.canMove) pc.canMove = false;
            pc.jumpForce = 0f;
            PlayerHealth.Instance.shockMultiplier = 0f;
            pa.energyRefill = 0f;

            if (Input.GetButtonUp("Fire") || !pc.canAct || pc.isWallSliding || pc.isDashing || PlayerHealth.Instance.isDead)
            {
                isReinforcing = false;
                pc.canMove = true;
                PlayerHealth.Instance.shockMultiplier = PlayerHealth.Instance.originalShockMultiplier;
                pc.jumpForce = pc.originalJumpForce;
            }
        }
    }*/
}