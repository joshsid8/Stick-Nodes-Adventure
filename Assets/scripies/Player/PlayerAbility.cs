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
    public float damageMultiplier;
    public float abilityCooldownMult = 1f;
    
    public float originalEnergyRefill;
    public float energyRefill;
    public float modEnergyRefill;
    
    public bool canAbility;
    public float currentAbilityCooldown;
    public bool isPerformingAbility;

    public float clickTimer;

    [SerializeField] private int blastType;
    
    private PlayerPerformAbility ability;
    private PlayerController pc;
    private PlayerDefense pd;
    
    [HideInInspector] public Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SetValues();
        animator = GetComponent<Animator>();
        pc = GetComponent<PlayerController>();
        pd = GetComponent<PlayerDefense>();
        ability = GetComponent<PlayerPerformAbility>();
        maxEnergy = energy;
        originalEnergyRefill = energyRefill;
    }

    // Update is called once per frame
    void Update()
    {
        Fire();
        Energy();
        animator.SetBool("isPerformingAbility", isPerformingAbility);
        clickTimer += Time.deltaTime;
    }

    void SetValues()
    {
        if (playerStats != null)
        {
            maxEnergy = playerStats.MaxEnergy;
            energy = playerStats.MaxEnergy; // Start with full energy
            energyRefill = playerStats.EnergyRefill;
            modEnergyRefill = playerStats.ModEnergyRefill;
            damageMultiplier = playerStats.DamageMultiplier;
        }
        else
        {
            Debug.LogError("where is bros scriptable object");
            enabled = false;
        }
    }
    void Fire()
    {
        if (canAbility && !pc.isDashing && !pc.isWallSliding && !isPerformingAbility && !pd.isBlocking && pc.canAct)
        {
            canAbility = false;
            if (Input.GetButtonDown("Fire") && Input.GetAxisRaw("Vertical") >= 0)
            {
                clickTimer = 0;
                ability.Execute(0);
            }
            else if (Input.GetButton("Fire") && Input.GetAxisRaw("Vertical") < 0)
            {
                ability.Execute(1);
            }
            //else if (Input.GetButton("Fire") && Input.GetAxisRaw("Vertical") >= 0) ability.Execute(2);
        }


        if (!canAbility)
        {
            currentAbilityCooldown -= Time.deltaTime;
            if (currentAbilityCooldown <= 0) canAbility = true;
        }
    }

    private void Energy()
    {
        energy += Time.deltaTime * energyRefill;
        if (energy >= maxEnergy) energy = maxEnergy;
        else if (energy < 0) energy = 0;
        
        energyBar.fillAmount = energy / maxEnergy;
        energyText.text = energy == maxEnergy ? energy.ToString("F0") + " / " + maxEnergy.ToString("F0")
            : energy.ToString("F1") + " / " + maxEnergy.ToString("F0");

    }
}
