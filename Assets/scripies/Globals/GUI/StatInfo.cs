using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatInfo : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text statName;
    [SerializeField] private GameObject shader;
    [SerializeField] private TMP_Text statDescription;
    [SerializeField] public StatSO stat;
    
    public bool isClicked;
    
    private PlayerController pc;
    private PlayerHealth ph;
    private PlayerAbility pa;
    private PlayerPerformAbility pp;
    private PlayerDefense pd;
    private PlayerCombat pf;
    private StatsManager statsManager;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        statDescription.text = string.Empty;
        isClicked = false;
        pc = FindAnyObjectByType<PlayerController>();
        ph = FindAnyObjectByType<PlayerHealth>();
        pa = FindAnyObjectByType<PlayerAbility>();
        pp = FindAnyObjectByType<PlayerPerformAbility>();
        pd = FindAnyObjectByType<PlayerDefense>();
        pf = FindAnyObjectByType<PlayerCombat>();
        statsManager = FindAnyObjectByType<StatsManager>();
        ClearInfo();
    }
    private void Update()
    {
        switch (stat.StatName)
        {
            case "MaxHealth": statName.text = "Max Health: " + ph.playerMaxHealth.ToString("F1"); break;
            case "RegenerationMultiplier": statName.text = "Health REG: " + ph.regenerationMultiplier.ToString("F1"); break;
            case "MovementSpeed": statName.text = "Walk SPD: " + pc.movementSpeed.ToString("F1"); break;
            case "JumpHeight": statName.text = "Jump Force: " + pc.jumpForce.ToString("F1"); break;
            case "StunMultiplier": statName.text = "Firmness: " + ph.stunMultiplier.ToString("F2"); break;
            case "RawDamageResistance": statName.text = "Vulnerability: " + ph.rawDamageResistance.ToString("F1"); break;
            
            case "DashSpeed": statName.text = "Dash Speed: " + pc.dashSpeed.ToString("F1"); break;
            case "DashCooldown": statName.text = "Dash CD: " + pc.dashCooldown.ToString("F1"); break;
            case "DashTime": statName.text = "Dash CD: " + pc.dashTime.ToString("F1"); break;
            case "DashEnergyCost": statName.text = "Dash Energy Cost: " + pc.dashEnergyCost.ToString("F1"); break;
            
            case "MaxEnergy": statName.text = "Max Energy: " + pa.maxEnergy.ToString("F1"); break;
            case "EnergyRefill": statName.text = "Energy REG: " + pa.energyRefill.ToString("F1"); break;
            case "AbilityCooldownMultiplier": statName.text = "Ability Efficiency: " + pa.abilityCooldownMult.ToString("F1"); break;
            case "AbilityDamageMultiplier": statName.text = "Ability Strength: " + pa.abilityDamageMult.ToString("F1"); break;
            case "AbilityPotency": statName.text = "Ability Potency: " + pp.abilityPotency.ToString("F1"); break;
            case "AbilityLifetimeMultiplier": statName.text = "Ability Longevity: " + pp.abilityLifetimeMult.ToString("F1"); break;
            
            case "GroundDamageAmount": statName.text = "Ground Attack DMG: " + pf.groundDamageAmount.ToString("F1"); break;
            case "GroundAttackCooldown": statName.text = "Ground Attack CD: " + pf.attackCooldown.ToString("F1"); break;
            case "AirDamageAmount": statName.text = "Air Attack DMG: " + pf.airDamageAmount.ToString("F1"); break;
            case "AirAttackCooldown": statName.text = "Air Attack CD: " + pf.airAttackCooldown.ToString("F1"); break;
            case "AttackKnockbackMultiplier": statName.text = "Melee Repulsion: " + pf.attackPotency.ToString("F1"); break;
            
            case "BlockDamageResistance": statName.text = "Block Resistance: " + pd.damageResistance.ToString("F1"); break;
            case "CounterTime": statName.text = "Counter Window: " + pd.counterTime.ToString("F1"); break;
            case "CounterDamage": statName.text = "Counter DMG: " + pd.counterDamage.ToString("F1"); break;
            case "PlayerAttackStun": statName.text = "Melee Shock: " + pf.stunTime.ToString("F2"); break;
            
            default: statName.text = ""; Debug.LogWarning("Clueless."); break;
        }
        shader.SetActive(isClicked);
    }

    public void DisplayInfo()
    {
        
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    private void OnLeftClick()
    {
        statsManager.ClearShaders();
        isClicked = true;
        statName.text = stat.StatName;
        statDescription.text = stat.StatDescription;
    }
    
    public void ClearInfo()
    {
        isClicked = false;
        statDescription.text = string.Empty;
    }
}
