using System.Collections;
using UnityEngine;

public class PlayerPerformAbility : MonoBehaviour
{
    public Transform abilityPoint;
    public AbilityObjSO[] objects;
    private PlayerController pc;
    private PlayerAbility pa;
    public float abilityDamageMult;
    public float abilityPotency;
    public float abilityLifetimeMult = 1f;
    public int[] abilityAmountMult = {1, 1, 1};
    private int slot;
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

    public void Execute(int blast = 0, int abSlot = 0)
    {
        if (!isActivated)
        {
            isActivated = true;
            blastType = blast;
            slot = abSlot;
            switch (blast)
            {
                case 0:
                    if (objects[abSlot] != null && pa.energy >= objects[abSlot].abilityPrefab.GetComponent<Projectile>().energyRequired * Mathf.Round(objects[abSlot].amount * abilityAmountMult[abSlot]))
                    {
                        pa.isPerformingAbility = true;
                        pa.animator.Play(pc.isGrounded ? "Fire" : "AirFire");
                    }
                    break;
                case 1:
                    if (objects[abSlot] != null && pa.energy >= objects[abSlot].abilityPrefab.GetComponentInChildren<Pillar>().energyRequired)
                    {
                        pa.isPerformingAbility = true;
                        pa.animator.Play("Pillar");
                    }
                    break;
                case 2:
                    if (objects[abSlot] != null && pa.energy >= objects[abSlot].abilityPrefab.GetComponentInChildren<Beam>().energyRequired)
                    {
                        pa.isPerformingAbility = true;
                        pa.animator.Play("Beam");
                    }
                    break;
            }
        }
    }

    private IEnumerator Blast()
    {
        switch (blastType)
        {
            case 0:
                if (!unavailable && objects[slot] != null)
                {
                    if (objects[slot].abilityPrefab.GetComponent<Projectile>().isHoldable)
                    {
                        var currentAmount = objects[slot].amount;
                        while (pa.energy >= objects[slot].abilityPrefab.GetComponent<Projectile>().energyRequired /
                               Mathf.Round(objects[slot].amount * abilityAmountMult[slot]) && currentAmount > 0 && pa.isHolding)
                        {
                            var newProjectile = Instantiate(objects[slot].abilityPrefab, abilityPoint.position, Quaternion.identity);
                            var projectileScript = newProjectile.GetComponent<Projectile>();
                            if (projectileScript != null)
                            {
                                projectileScript.SetValues(1, GetComponent<PlayerController>().isFacingRight);
                                objects[slot].SetProperties(projectileScript);
                                pa.energy -= projectileScript.energyRequired;
                                pa.currentAbilityCooldown = projectileScript.cooldown * pa.abilityCooldownMult;
                                currentAmount--;
                            }
                            else Debug.LogError("where is bro's projectile");

                            yield return new WaitForSeconds(objects[0].spawnInterval);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < objects[slot].amount; i++)
                        {
                            var newProjectile = Instantiate(objects[slot].abilityPrefab, abilityPoint.position, 
                                Quaternion.identity);
                            var projectileScript = newProjectile.GetComponent<Projectile>();
                            if (projectileScript != null)
                            {
                                projectileScript.SetValues(1, GetComponent<PlayerController>().isFacingRight);
                                objects[slot].SetProperties(projectileScript);
                                pa.energy -= projectileScript.energyRequired;
                                pa.currentAbilityCooldown = projectileScript.cooldown * pa.abilityCooldownMult;
                            }
                            else Debug.LogError("where is bro's projectile");

                            yield return new WaitForSeconds(objects[slot].spawnInterval);
                        }
                    }
                }
                pa.isPerformingAbility = false;
                break;
            case 1:
                if (!unavailable && objects[slot] != null)
                {
                    var xVector = 0f;
                    for (var i = 0; i < Mathf.Round(objects[slot].amount * abilityAmountMult[slot]); i++)
                    {
                        if (i == 0) xVector = objects[slot].initSeparation;
                        var newPillar = Instantiate(objects[slot].abilityPrefab,
                            abilityPoint.position + new Vector3(xVector * transform.right.x, 0f), Quaternion.identity);
                        var pillarScript = newPillar.GetComponentInChildren<Pillar>();
                        
                        if (pillarScript != null)
                        {
                            pillarScript.SetValues(1, GetComponent<PlayerController>().isFacingRight);
                            objects[slot].SetProperties(pillarScript);
                            pa.currentAbilityCooldown = pillarScript.cooldown * pa.abilityCooldownMult;
                            pa.energy -= pillarScript.energyRequired;
                            if (!pillarScript.isHoldable) pa.isPerformingAbility = false;
                        }
                        else Debug.LogError("where is bro's pillar");
                        xVector += objects[slot].separation;
                        yield return new WaitForSeconds(objects[slot].spawnInterval);
                    }
                }
                else pa.isPerformingAbility = false;
                break;
            case 2:
                if (objects[slot] != null)
                {
                    var newBeam = Instantiate(objects[slot].abilityPrefab, abilityPoint.position + new Vector3(0, 0, 0f),
                        abilityPoint.rotation);
                    var beamScript = newBeam.GetComponentInChildren<Beam>();

                    if (beamScript != null)
                    {
                        beamScript.SetValues(1, GetComponent<PlayerController>().isFacingRight);
                        objects[slot].SetProperties(beamScript);
                        pa.currentAbilityCooldown = beamScript.cooldown * pa.abilityCooldownMult;
                        pa.energy -= beamScript.energyRequired;
                        if (!beamScript.isHoldable) pa.isPerformingAbility = false;
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