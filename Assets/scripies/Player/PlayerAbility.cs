using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class PlayerAbility : MonoBehaviour
{
    public Image energyBar;
    public TMP_Text energyText;
    public float maxEnergy;
    public float energy;
    public float energyRefill;
    public float abilityCooldownMult = 1;
    
    public bool canAbility;
    public float currentAbilityCooldown;
    public bool isPerformingAbility;
    public bool isHolding;
    [SerializeField] private int maxBlastIndex;
    [SerializeField] private int blastIndex;
    [SerializeField] private int blastType;
    
    public PerformAbility ability;
    private PlayerController pc;
    private PlayerDefense pd;
    
    [HideInInspector] public Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        pc = GetComponent<PlayerController>();
        pd = GetComponent<PlayerDefense>();
        ability = GetComponent<PerformAbility>();
        maxEnergy = energy;
    }

    // Update is called once per frame
    void Update()
    {
        
        if (Input.GetButtonDown("FireSwap"))
        {
            blastIndex++;
            if (blastIndex > maxBlastIndex) blastIndex = 0;
        }
        
        Fire();
        Energy();
        animator.SetBool("isPerformingAbility", isPerformingAbility);
        animator.SetBool("isHolding", isHolding);
    }

    void Fire()
    {
        // Made it so that all 3 slots can be any type of ability
        if (canAbility && !pc.isDashing && !pc.isWallSliding && !isPerformingAbility && pc.canAct && !ability.isActivated)
            if (Input.GetButtonDown("Fire"))
            {
                if (ability.objects[blastIndex].abilityPrefab.GetComponent<Projectile>() != null)
                    ability.Execute(0, blastIndex);
                else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Pillar>() != null)
                    ability.Execute(1, blastIndex);
                else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Beam>() != null)
                    ability.Execute(2, blastIndex);
            }
        
        if (!pc.isDashing && !pc.isWallSliding && pc.canAct)
            isHolding = Input.GetButton("Fire") ? isHolding = true : isHolding = false;
        
        if (currentAbilityCooldown > 0) currentAbilityCooldown -= Time.deltaTime;
        if (currentAbilityCooldown <= 0) canAbility = true;
        else canAbility = false;
    }

    private void Energy()
    {
        if (!isPerformingAbility)
        {
            if (pc.inCombat) energy += Time.deltaTime * 2 * energyRefill;
            else energy += Time.deltaTime * 5 * energyRefill;
        }

        if (energy >= maxEnergy) energy = maxEnergy;
        else if (energy < 0) energy = 0;
        
        energyBar.fillAmount = energy / maxEnergy;
        energyText.text = energy == maxEnergy ? energy.ToString("F0") + " / " + maxEnergy.ToString("F0")
            : energy.ToString("F1") + " / " + maxEnergy.ToString("F0");
    }
}
