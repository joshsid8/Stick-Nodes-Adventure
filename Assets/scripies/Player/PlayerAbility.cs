using scripies.Core;
using TMPro;
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
    public int blastIndex;
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
        GameEvents.AddPlayerEnergy += AddEnergy;
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
                    Execute();
                else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Pillar>() != null)
                    Execute(1);
                else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Beam>() != null)
                    Execute(2);
            }
        
        if (!pc.isDashing && !pc.isWallSliding && pc.canAct)
            isHolding = Input.GetButton("Fire") ? isHolding = true : isHolding = false;
        
        if (currentAbilityCooldown > 0) currentAbilityCooldown -= Time.deltaTime;
        if (currentAbilityCooldown <= 0) canAbility = true;
        else canAbility = false;
    }
    
    private void Execute(int slot = 0)
    {
        if (ability == null) return;
        var abilitySo = ability.objects[slot];
        var prefabBase = abilitySo.abilityPrefab.GetComponent<AbilityBase>() ?? abilitySo.abilityPrefab.GetComponentInChildren<AbilityBase>();

        if (prefabBase == null)
        {
            Debug.LogError("Ability prefab missing AbilityBase component!");
            return;
        }

        var totalAmount = Mathf.Round(abilitySo.amount * ability.abilityAmountMult[slot]);
        var energyReq = abilitySo.energyRequired == 0 ? prefabBase.energyRequired : abilitySo.energyRequired;
        
        var hasEnoughEnergy = energy >= energyReq * totalAmount;
        if (hasEnoughEnergy && !ability.unavailable)
        {
            ability.isActivated = true;
            ability.currentBlastType = blastType;
            ability.currentSlot = slot;
            isPerformingAbility = true;

            var animName = blastType switch
            {
                0 => pc.isGrounded ? "Fire" : "AirFire",
                1 => "Pillar",
                2 => "Beam",
                _ => "Fire"
            };
            GameEvents.PlayerAction(animName);
        }
    }
    
    private void AddEnergy(float amount)
    {
        energy += amount;
        if (energy > maxEnergy) energy = maxEnergy;
    }
    
    private void Energy()
    {
        if (!isPerformingAbility)
        {
            if (PlayerHealth.Instance.inCombat) energy += Time.deltaTime * 2 * energyRefill;
            else energy += Time.deltaTime * 5 * energyRefill;
        }

        if (energy >= maxEnergy) energy = maxEnergy;
        else if (energy < 0) energy = 0;
        
        energyBar.fillAmount = energy / maxEnergy;
        energyText.text = energy == maxEnergy ? energy.ToString("F0") + " / " + maxEnergy.ToString("F0")
            : energy.ToString("F1") + " / " + maxEnergy.ToString("F0");
    }
}
