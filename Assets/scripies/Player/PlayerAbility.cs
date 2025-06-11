using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerAbility : MonoBehaviour
{
    public PlayerStats playerStats;
    public Image energyBar;
    public TMP_Text energyText;
    public float maxEnergy;
    public float energy;
    public float energyCost;
    public float damageMultiplier;
    
    public float originalEnergyRefill;
    public float energyRefill;
    public float modEnergyRefill;
    
    public bool canAbility;
    private float currentAbilityCooldown;
    public float abilityCooldown;
    [SerializeField] public bool isPerformingAbility;
    
    public MonoBehaviour ability;
    private PlayerController pc;
    private PlayerDefense pd;
    
    Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SetValues();
        animator = GetComponent<Animator>();
        pc = GetComponent<PlayerController>();
        pd = GetComponent<PlayerDefense>();
        maxEnergy = energy;
        originalEnergyRefill = energyRefill;
    }

    // Update is called once per frame
    void Update()
    {
        Fire();
        Energy();
    }

    void SetValues()
    {
        if (playerStats != null)
        {
            maxEnergy = playerStats.MaxEnergy;
            energy = playerStats.MaxEnergy; // Start with full energy
            energyRefill = playerStats.EnergyRefill;
            modEnergyRefill = playerStats.ModEnergyRefill;
            energyCost = playerStats.AbilityEnergyCost;
            abilityCooldown = playerStats.AbilityCooldown;
            damageMultiplier = playerStats.DamageMultiplier;
        }
        else
        {
            Debug.LogError("where is bros scriptable object");
            enabled = false;
            return;
        }
    }
    void Fire()
    {
        if (Input.GetButtonDown("Fire") && canAbility && !pc.isDashing && !pc.isWallSliding && !pd.isBlocking && pc.isGrounded && energy > energyCost)
        {
            canAbility = false;
            pc.rb.linearVelocity = Vector2.zero;
            animator.SetTrigger("Fire");
            pc.movementSpeed = 2f;
            currentAbilityCooldown = abilityCooldown;
        }

        if (!canAbility)
        {
            currentAbilityCooldown -= Time.deltaTime;
            if (currentAbilityCooldown <= 0)
            {
                pc.movementSpeed = pc.originalMovementSpeed;
                canAbility = true;
            }
        }
    }

    public IEnumerator FireAbility()
    {
        energy -= energyCost;
        ability.Invoke("Execute", 0f);
        yield return new WaitForSeconds(0.2f);
        pc.movementSpeed = pc.originalMovementSpeed;
    }

    private void Energy()
    {
        energy += Time.deltaTime * energyRefill;
        if (energy >= maxEnergy) energy = maxEnergy;
        else if (energy < 0) energy = 0;
        
        energyBar.fillAmount = energy / maxEnergy;
        energyText.text = energy == maxEnergy ? energy.ToString("F0") + " / " + maxEnergy.ToString("F0") : energy.ToString("F1") + " / " + maxEnergy.ToString("F0");

    }
}
