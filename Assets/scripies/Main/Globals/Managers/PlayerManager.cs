using UnityEngine;
using System.Collections.Generic;
using scripies.Player;

public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    // Reference to your base player stats (PlayerStats ScriptableObject)
    // This holds the "clean" default values for all player attributes.
    public PlayerStats basePlayerStats; 

    // References to player components (assigned in Awake)
    private PlayerController pc;
    private PlayerHealth ph;
    private PlayerAbility pa;
    private PlayerCurrency pm;
    private PlayerDefense pd;
    private PlayerCombat pf;
    private PerformAbility pp;
    
    private Dictionary<string, float> currentFloatStats = new Dictionary<string, float>();
    private Dictionary<string, bool> currentBooleanStats = new Dictionary<string, bool>();
    
    private List<AspectSO> activeAspects = new List<AspectSO>();
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        pc = FindAnyObjectByType<PlayerController>();
        ph = FindAnyObjectByType<PlayerHealth>();
        pa = FindAnyObjectByType<PlayerAbility>();
        pp = FindAnyObjectByType<PerformAbility>();
        pm = FindAnyObjectByType<PlayerCurrency>();
        pd = FindAnyObjectByType<PlayerDefense>();
        pf = FindAnyObjectByType<PlayerCombat>();

        if (pc == null) Debug.LogWarning("PlayerManager: PlayerController not found.");
        if (ph == null) Debug.LogWarning("PlayerManager: PlayerHealth not found.");
        if (pa == null) Debug.LogWarning("PlayerManager: PlayerAbility not found.");
        if (pp == null) Debug.LogWarning("PlayerManager: PerformAbility not found.");
        if (pm == null) Debug.LogWarning("PlayerManager: PlayerCurrency not found.");
        if (pd == null) Debug.LogWarning("PlayerManager: PlayerDefense not found.");
        if (pf == null) Debug.LogWarning("PlayerManager: PlayerCombat not found.");

        if (basePlayerStats != null) InitializeBaseStats();
        else Debug.LogError("PlayerManager: basePlayerStats ScriptableObject is not assigned! Player stats will not be correctly initialized.");
    }

    private void InitializeBaseStats()
    {
        currentFloatStats["MaxHealth"] = basePlayerStats.MaxHealth;
        currentFloatStats["RegenerationMultiplier"] = basePlayerStats.RegenerationMultiplier;
        currentFloatStats["ShockMultiplier"] = basePlayerStats.ShockMultiplier;
        currentFloatStats["RawDamageResistance"] = basePlayerStats.RawDamageResistance;
        
        currentFloatStats["MovementSpeedMultiplier"] = basePlayerStats.MovementSpeedMultiplier;
        currentFloatStats["JumpForceMultiplier"] = basePlayerStats.JumpForceMultiplier;
        currentFloatStats["DashSpeed"] = basePlayerStats.DashSpeed;
        currentFloatStats["DashTime"] = basePlayerStats.DashTime;
        currentFloatStats["DashCooldown"] = basePlayerStats.DashCooldown;
        currentFloatStats["DashEnergyCost"] = basePlayerStats.DashEnergyCost;

        currentFloatStats["MaxEnergy"] = basePlayerStats.MaxEnergy;
        currentFloatStats["EnergyRefill"] = basePlayerStats.EnergyRefillMultiplier;
        currentFloatStats["AbilityCooldownMultiplier"] = basePlayerStats.AbilityCooldownMultiplier;
        currentFloatStats["AbilityPotency"] = basePlayerStats.AbilityPotency;
        currentFloatStats["AbilityLifetimeMultiplier"] = basePlayerStats.AbilityLifetimeMultiplier;
        currentFloatStats["AbilityDamageMultiplier"] = basePlayerStats.AbilityDamageMultiplier;
        
        //currentFloatStats["AirAttackForce"] = basePlayerStats.AirAttackForce;
        currentFloatStats["AttackDamageMultiplier"] = basePlayerStats.AttackDamageMultiplier;
        currentFloatStats["AttackPotency"] = basePlayerStats.AttackPotency;
        currentFloatStats["AttackCooldownMultiplier"] = basePlayerStats.AttackCooldownMultiplier;
        
        currentFloatStats["BlockDamageResistance"] = basePlayerStats.BlockDamageResistance;
        currentFloatStats["CounterTime"] = basePlayerStats.CounterTime;
        currentFloatStats["CounterDamageResistance"] = basePlayerStats.CounterDamageResistance;
        currentFloatStats["CounterDamage"] = basePlayerStats.CounterDamage;
        //currentFloatStats["AvailableWalls"] = basePlayerStats.AvailableWalls;

        currentBooleanStats["CounterAbility"] = basePlayerStats.CounterAbility;
        currentBooleanStats["M1Ability"] = basePlayerStats.M1Ability;

        ApplyAllStatsToComponents();
    }


    public float GetCurrentFloat(string variableName)
    {
        if (currentFloatStats.ContainsKey(variableName)) return currentFloatStats[variableName];
        Debug.LogWarning($"PlayerManager: Attempted to get unknown float variable '{variableName}'. Returning 0.");
        return 0f;
    }

    public bool GetCurrentBool(string variableName)
    {
        if (currentBooleanStats.ContainsKey(variableName)) return currentBooleanStats[variableName];
        Debug.LogWarning($"PlayerManager: Attempted to get unknown bool variable '{variableName}'. Returning false.");
        return false;
    }

    public void ApplyAspectModifiers(AspectSO aspect)
    {
        if (!activeAspects.Contains(aspect))
        {
            activeAspects.Add(aspect);
            RecalculateAllStats();
        }
        else Debug.LogWarning($"PlayerManager: Attempted to apply already active aspect: {aspect.name}.");
    }

    public void RemoveAspectModifiers(AspectSO aspect)
    {
        if (activeAspects.Contains(aspect))
        {
            activeAspects.Remove(aspect);
            RecalculateAllStats();
        }
        else Debug.LogWarning($"PlayerManager: Attempted to remove inactive aspect: {aspect.name}.");
    }

    private void RecalculateAllStats()
    {
        InitializeBaseStats();

        foreach (var aspect in activeAspects)
        {
            for (var i = 0; i < aspect.floatStatsName.Length; i++)
            {
                var statName = aspect.floatStatsName[i];
                var statValue = aspect.floatStats[i];

                if (currentFloatStats.ContainsKey(statName)) 
                    currentFloatStats[statName] += currentFloatStats[statName] * statValue;
                else 
                    Debug.LogWarning($"PlayerManager: Aspect '{aspect.name}' trying to modify unknown stat: {statName}");
                
            }
            // For boolean stats, you might need a "OR" logic (if any aspect enables it, it's enabled)
            // Example:
            // if (aspect.boolStatsName.Length > 0) {
            //     for (int i = 0; i < aspect.boolStatsName.Length; i++) {
            //         string boolStatName = aspect.boolStatsName[i];
            //         _currentBooleanStats[boolStatName] = _currentBooleanStats[boolStatName] || aspect.boolStatsValue[i];
            //     }
            // }
        }

        ApplyAllStatsToComponents();
    }

    private void ApplyAllStatsToComponents()
    {
        if (ph != null) //health
        {
            ph.playerMaxHealth = GetCurrentFloat("MaxHealth");
            ph.regenerationMultiplier = GetCurrentFloat("RegenerationMultiplier");
            ph.shockMultiplier = GetCurrentFloat("ShockMultiplier");
            ph.rawDamageResistance = GetCurrentFloat("RawDamageResistance");
        }
        if (pc != null) //movement
        {
            pc.movementSpeedMult = GetCurrentFloat("MovementSpeedMultiplier");
            pc.jumpForceMult = GetCurrentFloat("JumpForceMultiplier");
            pc.dashSpeed = GetCurrentFloat("DashSpeed");
            pc.dashTime = GetCurrentFloat("DashTime");
            pc.dashCooldown = GetCurrentFloat("DashCooldown");
            pc.dashEnergyCost = GetCurrentFloat("DashEnergyCost");
        }
        if (pa != null && pp != null) //ability
        {
            pa.maxEnergy = GetCurrentFloat("MaxEnergy");
            pa.energyRefill = GetCurrentFloat("EnergyRefill");
            pa.abilityCooldownMult = GetCurrentFloat("AbilityCooldownMultiplier");
            pp.abilityDamageMult = GetCurrentFloat("AbilityDamageMultiplier");
            pp.abilityLifetimeMult = GetCurrentFloat("AbilityLifetimeMultiplier");
            pp.abilityPotency =  GetCurrentFloat("AbilityPotency");
        }
        if (pd != null) //defense
        {
            pd.blockDamageResistance = GetCurrentFloat("BlockDamageResistance");
            pd.counterTime = GetCurrentFloat("CounterTime");
            pd.counterDamageResistance = GetCurrentFloat("CounterDamageResistance");
            pd.counterDamage = GetCurrentFloat("CounterDamage");
        }
        if (pf != null) //combat
        {
            pf.attackDamageMult = GetCurrentFloat("AttackDamageMultiplier");
            pf.attackCooldownMult = GetCurrentFloat("AttackCooldownMultiplier");
            //pf.airAttackForce = GetCurrentFloat("AirAttackForce");
            pf.attackPotency = GetCurrentFloat("AttackPotency");
        }
    }

    public void SetGlobalFloat(string variableName, float value)
    {
        switch (variableName)
        {
            case "PlayerMaxHealth":
                ph.playerMaxHealth = value;
                break;
            case "PlayerHealth":
                ph.playerHealth = value;
                ph.playerHealth = Mathf.Clamp(ph.playerHealth, 0, ph.playerMaxHealth);
                break;
            case "PlayerRegen":
                if (ph != null) ph.regenerationMultiplier = value;
                break;
            case "ShockMultiplier":
                if (ph != null) ph.shockMultiplier = value;
                break;
            case "PlayerMaxEnergy":
                if (pa != null) pa.maxEnergy = value;
                break;
            case "PlayerEnergy":
                if (pa != null) pa.energy = value;
                break;
            case "PlayerEnergyRefill":
                if (pa != null) pa.energyRefill = value;
                break;
            case "PlayerMovementSpeed":
                if (pc != null) pc.movementSpeed = value;
                break;
            case "PlayerJumpForce":
                if (pc != null) pc.jumpForce = value;
                break;
            case "DamageResistance":
                if (pd != null) pd.blockDamageResistance = value;
                break;
            case "CounterWindow":
                if (pd != null) pd.counterTime = value;
                break;
            case "CombatTimer":
                if (pc != null) ph.currentCombatTimer = value;
                break;
            case "Money":
                if (pm != null) pm.nodeBucks += value;
                break;
            default:
                if (!string.IsNullOrEmpty(variableName)) Debug.LogWarning($"dawg ion know what a '{variableName}' is bruh");
                else Debug.LogWarning("BRO THERE IS NOTHING HERE TO CHAAANGE >:(");
                break;
        }
    }
    public void SetGlobalBool(string variableName, bool value)
    {
        switch (variableName)
        {
            case "PlayerCanDoubleJump":
                pc.canDoubleJump = value;
                break;
            case "PlayerCanAct":
                if (pc != null)
                {
                    pc.canAct = value;
                    Debug.Log($"GameManager: PlayerCanAct set to {pc.canAct}");
                }
                else Debug.LogWarning($"PlayerController not found to set PlayerCanAct.");
                break;
            case "BossAttack":
                GameObject boss = GameObject.FindGameObjectWithTag("Boss");
                boss.GetComponent<BossAttack>().isAttacking = value;
                break;
            // To be added later
            default:
                if (!string.IsNullOrEmpty(variableName)) Debug.LogWarning($"zlawg whats a '{variableName}'??");
                else Debug.LogWarning("BRO THERE IS NOTHING HERE TO CHAAANGE >:(");
                break;
        }
    }
    public void AlterGlobalFloat(string variableName, float value)
    {
        switch (variableName)
        {
            case "PlayerHealth":
                ph.playerHealth += value;
                ph.playerHealth = Mathf.Clamp(ph.playerHealth, 0, ph.playerMaxHealth);
                break;
            case "PlayerEnergy":
                if (pa != null) pa.energy += value;
                break;
            case "CounterWindow":
                if (pd != null) pd.counterTime += value;
                break;
            case "CombatTimer":
                if (pc != null) ph.currentCombatTimer += value;
                break;
            case "Money":
                if (pm != null) pm.nodeBucks += value;
                break;
            default:
                if (!string.IsNullOrEmpty(variableName)) Debug.LogWarning($"dawg ion know what a '{variableName}' is bruh");
                else Debug.LogWarning("BRO THERE IS NOTHING HERE TO CHAAANGE >:(");
                break;
        }
    }

    public void Save(ref PlayerSaveStats data)
    {
        data.playerMaxHealth = ph.playerMaxHealth;
        data.playerHealth = ph.playerHealth;
        data.regenerationMult = ph.regenerationMultiplier;
        data.shockMultiplier = ph.shockMultiplier;
        data.rawDamageResistance = ph.rawDamageResistance;

        data.movementSpeedMult = pc.movementSpeedMult;
        data.jumpForceMult = pc.jumpForceMult;
        data.dashSpeed = pc.dashSpeed;
        data.dashTime = pc.dashTime;
        data.dashCooldown = pc.dashCooldown;
        data.dashEnergyCost = pc.dashEnergyCost;

        data.playerMaxEnergy = pa.maxEnergy;
        data.playerEnergy = pa.energy;
        data.playerEnergyRefill = pa.energyRefill;
        data.abilityCooldownMult = pa.abilityCooldownMult;
        data.abilityDamageMult = pp.abilityDamageMult;
        data.abilityLifetimeMult = pp.abilityLifetimeMult;
        data.abilityPotency = pp.abilityPotency;

        data.blockDamageResistance = pd.blockDamageResistance;
        data.combatTime = ph.currentCombatTimer;
        data.counterDamage = pd.counterDamage;
        data.counterDamageResistance = pd.counterDamageResistance;
    }

    public void Load(PlayerSaveStats data)
    {
        pd.counterDamageResistance = data.counterDamageResistance;
        pd.counterDamage = data.counterDamage;
        ph.currentCombatTimer = data.combatTime;
        pd.blockDamageResistance = data.blockDamageResistance;

        pp.abilityPotency = data.abilityPotency;
        pp.abilityLifetimeMult = data.abilityLifetimeMult;
        pp.abilityDamageMult = data.abilityDamageMult;
        pa.abilityCooldownMult = data.abilityCooldownMult;
        pa.energyRefill = data.playerEnergyRefill;
        pa.energy = data.playerEnergy;
        pa.maxEnergy = data.playerMaxEnergy;

        pc.dashEnergyCost = data.dashEnergyCost;
        pc.dashCooldown = data.dashCooldown;
        pc.dashTime = data.dashTime;
        pc.dashSpeed = data.dashSpeed;
        pc.jumpForceMult = data.jumpForceMult;
        pc.movementSpeedMult = data.movementSpeedMult;

        ph.rawDamageResistance = data.rawDamageResistance;
        ph.shockMultiplier = data.shockMultiplier;
        ph.regenerationMultiplier = data.regenerationMult;
        ph.playerHealth = data.playerHealth;
        ph.playerMaxHealth = data.playerMaxHealth;
    }
    
}
[System.Serializable]
public struct PlayerSaveStats
{
    public float playerMaxHealth;
    public float playerHealth;
    public float regenerationMult;
    public float shockMultiplier;
    public float rawDamageResistance;
    
    public float movementSpeedMult;
    public float jumpForceMult;
    public float dashSpeed;
    public float dashTime;
    public float dashCooldown;
    public float dashEnergyCost;
    
    public float playerMaxEnergy;
    public float playerEnergy;
    public float playerEnergyRefill;
    public float abilityCooldownMult;
    public float abilityDamageMult;
    public float abilityLifetimeMult;
    public float abilityPotency;
    
    public float blockDamageResistance;
    public float combatTime;
    public float counterDamage;
    public float counterDamageResistance;
    
}