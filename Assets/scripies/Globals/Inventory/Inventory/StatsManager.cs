using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StatsManager : MonoBehaviour
{
    public Button statsButton;
    public GameObject statsGO;
    public bool isEnabled;
    
    public TMP_Text[] stats;
    public string[] statNames;
    public float[] statsValues;

    public TMP_Text statDesc;
    public string[] statDescriptions;
    
    // References to player components (assigned in Awake)
    private PlayerController pc;
    private PlayerHealth ph;
    private PlayerAbility pa;
    private PlayerDefense pd;
    private PlayerCombat pf;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        isEnabled = false;
        statsGO.SetActive(false);
        statsButton.onClick.AddListener(ToggleStats);
        pc = FindAnyObjectByType<PlayerController>();
        ph = FindAnyObjectByType<PlayerHealth>();
        pa = FindAnyObjectByType<PlayerAbility>();
        pd = FindAnyObjectByType<PlayerDefense>();
        pf = FindAnyObjectByType<PlayerCombat>();
    }

    private void Update()
    {
        if (Input.GetButtonDown("Inventory") && isEnabled) ToggleStats();
        SetStatText();
    }

    private void ToggleStats()
    {
        isEnabled = !isEnabled;
        statsGO.SetActive(isEnabled);
    }

    private void SetStatText()
    {
        for (int i = 0; i < stats.Length; i++)
        {
            //stats[i].text = statNames[i] + ": " + statsValues[i].ToString("F1");
            
            switch (statNames[i])
            {
                case "MaxHealth": stats[i].text = "Max Health: " + ph.playerMaxHealth.ToString("F0"); break;
                case "RegenerationMultiplier": stats[i].text = "Regeneration: " + ph.regenerationMultiplier.ToString("F1"); break;
                case "MovementSpeed": stats[i].text = "Walk SPD: " + pc.movementSpeed.ToString("F0"); break;
                case "JumpHeight": stats[i].text = "Jump Force: " + pc.jumpForce.ToString("F0"); break;
                case "StunMultiplier": stats[i].text = "Stun Multiplier: " + ph.stunMultiplier.ToString("F2"); break;
                case "MaxEnergy": stats[i].text = "Max Energy: " + pa.maxEnergy.ToString("F0"); break;
                case "RawDamageResistance": stats[i].text = "Protection: " + ph.rawDamageResistance.ToString("F1"); break;
                case "EnergyRefill": stats[i].text = "Energy: " + pa.energyRefill.ToString("F1"); break;
                case "DashSpeed": stats[i].text = "Dash Speed: " + pc.dashSpeed.ToString("F1"); break;
                case "DashCooldown": stats[i].text = "Dash CD: " + pc.dashCooldown.ToString("F1"); break;
                case "GroundDamageAmount": stats[i].text = "Ground Attack DMG: " + pf.groundDamageAmount.ToString("F0"); break;
                case "GroundAttackCooldown": stats[i].text = "Ground Attack SPD: " + pf.attackCooldown.ToString("F1"); break;
                case "AirDamageAmount": stats[i].text = "Air Attack DMG: " + pf.airDamageAmount.ToString("F0"); break;
                case "AirAttackCooldown": stats[i].text = "Air Attack CD: " + pf.airAttackCooldown.ToString("F1"); break;
                case "BlockDamageResistance": stats[i].text = "Block Resistance: " + pd.damageResistance.ToString("F1"); break;
                case "CounterTime": stats[i].text = "Counter Window: " + pd.counterTime.ToString("F1"); break;
                case "CounterDamage": stats[i].text = "Counter DMG: " + pd.counterDamage.ToString("F0"); break;
                case "PlayerAttackStun": stats[i].text = "Stun Multiplier: " + pf.stunTime.ToString("F2"); break;
                
                default: stats[i].text = ""; break;
            }
        }
    }
}
