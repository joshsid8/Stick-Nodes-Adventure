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
            stats[i].text = statNames[i] + ": " + statsValues[i].ToString("F1");
            if (statNames[i] == "MaxHealth") stats[i].text = "Max Health: " + ph.playerMaxHealth.ToString("F0");
            else if (statNames[i] == "RegenerationMultiplier") stats[i].text = "Health Regen SPD: " + ph.regenerationMultiplier.ToString("F1");
            else if (statNames[i] == "MovementSpeed") stats[i].text = "Walk SPD: " + pc.movementSpeed.ToString("F0");
            else if (statNames[i] == "JumpHeight") stats[i].text = "Jump Force: " + pc.jumpForce.ToString("F0");
            else if (statNames[i] == "StunMultiplier") stats[i].text = "Persistence: " + ph.stunMultiplier.ToString("F2");
            else if (statNames[i] == "MaxEnergy") stats[i].text = "Max Energy: " + pa.maxEnergy.ToString("F0");
            else if (statNames[i] == "RawDamageResistance") stats[i].text = "Protection: " + ph.rawDamageResistance.ToString("F1");
            else if (statNames[i] == "EnergyRefill") stats[i].text = "Energy Regen SPD: " + pa.energyRefill.ToString("F1");
            else if (statNames[i] == "DashSpeed") stats[i].text = "Dash Speed: " + pc.dashSpeed.ToString("F1");
            else if (statNames[i] == "DashCooldown") stats[i].text = "Dash CD: " + pc.dashCooldown.ToString("F1");
            else if (statNames[i] == "GroundDamageAmount") stats[i].text = "Ground Attack DMG: " + pf.groundDamageAmount.ToString("F0");
            else if (statNames[i] == "GroundAttackCooldown") stats[i].text = "Ground Attack SPD: " + pf.attackCooldown.ToString("F1");
            else if (statNames[i] == "AirDamageAmount") stats[i].text = "Air Attack DMG: " + pf.airDamageAmount.ToString("F0");
            else if (statNames[i] == "AirAttackCooldown") stats[i].text = "Air Attack CD: " + pf.airAttackCooldown.ToString("F1");
            else if (statNames[i] == "BlockDamageResistance") stats[i].text = "Block Resistance: " + pd.damageResistance.ToString("F1");
            else if (statNames[i] == "CounterTime") stats[i].text = "Counter Window: " + pd.counterTime.ToString("F1");
            else if (statNames[i] == "CounterDamage") stats[i].text = "Counter DMG: " + pd.counterDamage.ToString("F0");
            else if (statNames[i] == "PlayerAttackStun") stats[i].text = "Stun Multiplier: " + pf.stunTime.ToString("F2");
            else stats[i].text = "";
        }
    }
}
