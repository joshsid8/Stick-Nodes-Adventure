using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatInfo : GUIBase, IPointerClickHandler
{
    [SerializeField] private TMP_Text statName;
    [SerializeField] private GameObject shader;
    [SerializeField] private TMP_Text statDescription;
    [SerializeField] public StatSO stat;
    
    public bool isClicked;
    
    private PlayerController pc;
    private PlayerHealth ph;
    private PlayerAbility pa;
    private PerformAbility pp;
    private PlayerDefense pd;
    private PlayerCombat pf;
    private StatsManager statsManager;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        statDescription.text = string.Empty;
        isClicked = false;
        pc = PlayerHealth.Instance.pc;
        ph = PlayerHealth.Instance;
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>();
        pp = PlayerHealth.Instance.GetComponent<PerformAbility>();
        pd = PlayerHealth.Instance.GetComponent<PlayerDefense>();
        pf = PlayerHealth.Instance.GetComponent<PlayerCombat>();
        statsManager = FindAnyObjectByType<StatsManager>();
        ClearInfo();
    }
    private void Update()
    {
        switch (stat.StatName)
        {
            case "MaxHealth": DisplayFloat("Max HP", ph.playerMaxHealth); break;
            case "RegenerationMultiplier": DisplayPercent("Health REG", ph.regenerationMultiplier); break;
            case "MovementSpeedMultiplier": DisplayFloat("Walk SPD", pc.movementSpeedMult); break;
            case "JumpForceMultiplier": DisplayFloat("Jump Height", pc.jumpForceMult); break;
            case "ShockMultiplier": DisplayFloat("Firmness", ph.shockMultiplier); break;
            case "RawDamageResistance": DisplayPercent("Toughness", ph.rawDamageResistance); break;
            
            case "DashSpeed": DisplayFloat("Dash SPD", pc.dashSpeed); break;
            case "DashCooldown": DisplayFloat("Dash CD", pc.dashCooldown); break;
            case "DashTime": DisplayFloat("Dash Duration", pc.dashTime); break;
            
            case "MaxEnergy": DisplayFloat("Max Energy", pa.maxEnergy); break;
            case "EnergyRefill": DisplayPercent("Energy REG", pa.energyRefill); break;
            case "AbilityCooldownMultiplier": DisplayPercent("Ability Efficiency", pa.abilityCooldownMult); break;
            case "AbilityDamageMultiplier": DisplayPercent("Ability Strength", pp.abilityDamageMult); break;
            case "AbilityPotency": DisplayPercent("Ability Potency", pp.abilityPotency); break;
            case "AbilityLifetimeMultiplier": DisplayPercent("Ability Longevity", pp.abilityLifetimeMult); break;
            
            case "AttackDamageMultiplier": DisplayPercent("ATK Strength", pf.attackDamageMult); break;
            case "AttackCooldownMultiplier": DisplayPercent("Ground ATK Efficiency", pf.attackCooldownMult); break;
            case "AttackPotency": DisplayPercent("Attack Potency", pf.attackPotency); break;
            
            case "BlockDamageResistance": DisplayPercent("Block Resistance", pd.blockDamageResistance); break;
            case "CounterTime": DisplayFloat("Counter Window", pd.counterTime); break;
            case "CounterDamage": DisplayFloat("Counter DMG", pd.counterDamage); break;
            case "CounterDamageResistance": DisplayPercent("Counter Resistance", pd.counterDamageResistance); break;
            
            default: statName.text = ""; Debug.LogWarning("Clueless."); break;
        }
        shader.SetActive(isClicked);
    }

    private void DisplayFloat(string title, float value)
    {
        statName.text = title + ": " + value.ToString("F1");
        // well that was eashy
    }

    private void DisplayPercent(string title, float value)
    { 
        var percent = value * 100 - 100;
        if (title != "BlockDamageResistance" && title != "RawDamageResistance" && title == "CounterDamageResistance")
        {
            if (value != 1) statName.text = title + ": " + percent.ToString("F0") + "%";
            else statName.text = $"{title}: 0%";
        }
        else
        {
            if (value != 1) statName.text = title + ": " + (-percent).ToString("F0") + "%";
            else statName.text = $"{title}: 0%";
        }

        // man im so smat >:D

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
