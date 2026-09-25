using System.Collections.Generic;
using scripies.Player;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatInfo : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] protected TMP_Text statName;
    [SerializeField] protected GameObject shader;
    [SerializeField] protected TMP_Text statDescription;
    [SerializeField] public StatSO stat;
    
    public bool isClicked;
    
    private PlayerController pc;
    private PlayerHealth ph;
    private PlayerAbility pa;
    private PerformAbility pp;
    private PlayerDefense pd;
    private PlayerCombat pf;
    protected StatsManager statsManager;
    
    // System.Func?! This is something I MUST learn!!!
    private System.Func<float> getValue;
    private bool isPercent;
    private bool isReversed;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        statDescription.text = string.Empty;
        isClicked = false;
        ph = PlayerHealth.Instance;
        pc = ph.pc ?? PlayerHealth.Instance.GetComponent<PlayerController>() ?? FindAnyObjectByType<PlayerController>();
        pa = PlayerHealth.Instance.GetComponent<PlayerAbility>();
        pp = PlayerHealth.Instance.GetComponent<PerformAbility>();
        pd = PlayerHealth.Instance.GetComponent<PlayerDefense>();
        pf = PlayerHealth.Instance.GetComponent<PlayerCombat>();
        statsManager = StatsManager.Instance;
        
        SetupStatMapping();
        ClearInfo();
    }

    private void SetupStatMapping()
    {
        if (stat == null)
        {
            gameObject.SetActive(false);
            return;
        }
        switch (stat.name)
        {
            case "MaxHealth": getValue = () => ph.maxHealth; break;
            case "RegenerationMultiplier": getValue = () => ph.regenerationMultiplier; isPercent = true; break;
            case "MovementSpeedMultiplier": getValue = () => pc.movementSpeedMult; break;
            case "JumpForceMultiplier": getValue = () => pc.jumpForceMult; break;
            case "ShockMultiplier": getValue = () => ph.shockMultiplier; isPercent = true; isReversed = true; break;
            case "RawDamageResistance": getValue = () => ph.rawDamageResistance; isPercent = true; isReversed = true; break;
            
            case "DashSpeed": getValue = () => pc.dashSpeed; break;
            case "DashCooldown": getValue = () => pc.dashCooldown; break;
            case "DashTime": getValue = () => pc.dashTime; break;
            
            case "MaxEnergy": getValue = () => pa.maxEnergy; break;
            case "EnergyRefill": getValue = () => pa.energyRefill; isPercent = true; break;
            case "AbilityCooldownMultiplier": getValue = () => pa.abilityCooldownMult; isPercent = true; break;
            case "AbilityDamageMultiplier": getValue = () => pp.abilityDamageMult; isPercent = true; break;
            case "AbilityPotency": getValue = () => pp.abilityPotency; isPercent = true; break;
            case "AbilityLifetimeMultiplier": getValue = () => pp.abilityLifetimeMult; isPercent = true; break;
            
            case "AttackDamageMultiplier": getValue = () => pf.attackDamageMult; isPercent = true; break;
            case "AttackCooldownMultiplier": getValue = () => pf.attackCooldownMult; isPercent = true; break;
            case "AttackPotency": getValue = () => pf.attackPotency; isPercent = true; break;
            
            case "BlockDamageResistance": getValue = () => pd.blockDamageResistance; isPercent = true; isReversed = true; break;
            case "CounterTime": getValue = () => pd.counterTime; break;
            case "CounterDamage": getValue = () => pd.counterDamage; break;
            case "CounterDamageResistance": getValue = () => pd.counterDamageResistance; isPercent = true; isReversed = true; break;
            
            default: getValue = null; break;
        }
    }
    
    protected virtual void Update()
    {
        if (getValue != null)
        {
            if (isPercent) DisplayPercent(stat.DisplayName, getValue(), isReversed);
            else DisplayFloat(stat.DisplayName, getValue());
        }
        else
        {
            statName.text = "";
        }
        
        shader.SetActive(isClicked);
    }

    private void DisplayFloat(string title, float value)
    {
        statName.text = title + ": " + value.ToString("F1");
        // well that was eashy
    }

    private void DisplayPercent(string title, float value, bool reverse = false)
    { 
        var percent = value * 100 - 100;
        if (!reverse)
        {
            if ((int)value != 1) statName.text = title + ": " + percent.ToString("F0") + "%";
            else statName.text = $"{title}: 0%";
        }
        else
        {
            if ((int)value != 1) statName.text = title + ": " + (-percent).ToString("F0") + "%";
            else statName.text = $"{title}: 0%";
        }

        // man im so smat >:D
        // are u tho?
        // YES! YES I AM!!!
    }
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    protected virtual void OnLeftClick()
    {
        if (getValue == null || stat == null) return;
        statsManager.ClearStatShaders();
        isClicked = true;
        statName.text = stat.name;
        statDescription.text = stat.StatDescription;
    }
    
    public virtual void ClearInfo()
    {
        isClicked = false;
        statDescription.text = string.Empty;
    }
}
