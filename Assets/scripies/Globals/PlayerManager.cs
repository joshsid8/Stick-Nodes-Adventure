using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class PlayerManager : MonoBehaviour
{
    // Singleton instance
    public static PlayerManager Instance { get; private set; }

    // the bools
    private bool playerCanDoubleJump;

    // big stuff
    private PlayerController playerController;
    private PlayerHealth playerHealth;
    private PlayerAbility playerAbility;
    private PlayerCurrency playerCurrency;
    private PlayerDefense playerDefense;
    private PlayerCombat playerCombat;

    // original values
    [HideInInspector] public float oMoveSpeed;
    [HideInInspector] public float oDashSpeed;
    [HideInInspector] public float oDashStartTime;
    [HideInInspector] public float oDashCooldown;
    [HideInInspector] public float oDashEnergyCost;

    [HideInInspector] public float oJumpForce;
    [HideInInspector] public float oFallMultiplier;
    [HideInInspector] public float oLowJumpMultiplier;

    [HideInInspector] public float oWallSlideSpeed;
    [HideInInspector] public int oWallsLeft;
    [HideInInspector] public float oWallJumpForce;

    [HideInInspector] public float oMaxHealth;
    [HideInInspector] public float oMaxHealthRegen;
    [HideInInspector] public float oStunMultiplier;
    [HideInInspector] public float oInvincibleWindow;

    [HideInInspector] public float oDamageResistance;
    [HideInInspector] public float oCounterWindow;
    [HideInInspector] public float oCounterDamage;
    [HideInInspector] public float oCounterStun;
    [HideInInspector] public float oCounterResistance;
    
    [HideInInspector] public float oGroundAttackDamage;
    [HideInInspector] public float oGroundDamageCooldown;
    [HideInInspector] public float oAirAttackDamage;
    [HideInInspector] public float oAirDamageCooldown;
    [HideInInspector] public float oAttackEnergy;
    [HideInInspector] public float oAirAttackForce;

    [HideInInspector] public float oMaxEnergy;
    [HideInInspector] public float oAbilityEnergyRegen;
    [HideInInspector] public float oAbilityCooldown;



    private void Awake()
    {
        // Implement Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        playerController = FindAnyObjectByType<PlayerController>();
        playerHealth = FindAnyObjectByType<PlayerHealth>();
        playerAbility = FindAnyObjectByType<PlayerAbility>();
        playerCurrency = FindAnyObjectByType<PlayerCurrency>();
        playerDefense = FindAnyObjectByType<PlayerDefense>();
        playerCombat =  FindAnyObjectByType<PlayerCombat>();

        OriginalValues();

        if (playerController != null)
        {
            playerCanDoubleJump = playerController.canDoubleJump;
        }
    }

    private void OriginalValues()
    {
        oMoveSpeed = playerController.originalMovementSpeed;
        oDashSpeed = playerController.dashSpeed;
        oDashStartTime = playerController.startDashTime;
        oDashCooldown =  playerController.dashCooldown;
        oDashEnergyCost = playerController.dashEnergyCost;

        oJumpForce =  playerController.originalJumpForce;
        oFallMultiplier =  playerController.fallMultiplier;
        oLowJumpMultiplier = playerController.lowJumpMultiplier;

        oWallSlideSpeed = playerController.wallSlideSpeed;
        oWallJumpForce = playerController.wallJumpForce;

        oMaxHealth = playerHealth.playerMaxHealth;
        oMaxHealthRegen = playerHealth.originalRegenerationMultiplier;
        oStunMultiplier = playerHealth.originalStunMultiplier;
        oInvincibleWindow = playerHealth.invincibleDuration;

        oDamageResistance = playerDefense.damageResistance;
        oCounterWindow = playerDefense.counterTime;
        oCounterDamage = playerDefense.counterDamage;
        oCounterStun = playerDefense.counterStun;
        oCounterResistance = playerDefense.counterDamageResistance;

        oGroundAttackDamage = playerCombat.damageAmount;
        oGroundDamageCooldown = playerCombat.attackCooldown;
        oAirAttackDamage = playerCombat.airAttackCooldown;
        oAirDamageCooldown = playerCombat.airAttackCooldown;
        oAttackEnergy = playerCombat.attackEnergy;
        oAirAttackForce = playerCombat.airAttackForce;

        oMaxEnergy = playerAbility.maxEnergy;
        oAbilityEnergyRegen = playerAbility.originalEnergyRefill;
        oAbilityCooldown = playerAbility.abilityCooldown;
    }

// Public Methods
    public void SetGlobalFloat(string variableName, float value)
    {
        switch (variableName)
        {
            case "PlayerMaxHealth":
                SetPlayerMaxHealth(value);
                break;
            case "PlayerHealth":
                playerHealth.playerHealth = value;
                playerHealth.playerHealth = Mathf.Clamp(playerHealth.playerHealth, 0, playerHealth.playerMaxHealth);
                break;
            case "PlayerRegen":
                if (playerHealth != null) playerHealth.regenerationMultiplier = value;
                break;
            case "StunMultiplier":
                if (playerHealth != null) playerHealth.stunMultiplier = value;
                break;
            case "PlayerMaxEnergy":
                if (playerAbility != null) playerAbility.maxEnergy = value;
                break;
            case "PlayerEnergy":
                if (playerAbility != null) playerAbility.energy = value;
                break;
            case "PlayerEnergyRefil":
                if (playerAbility != null) playerAbility.energyRefill = value;
                break;
            case "PlayerMovementSpeed":
                if (playerController != null) playerController.movementSpeed = value;
                break;
            case "PlayerJumpForce":
                if (playerController != null) playerController.jumpForce = value;
                break;
            case "DamageResistance":
                if (playerDefense != null) playerDefense.damageResistance = value;
                break;
            case "CounterWindow":
                if (playerDefense != null) playerDefense.counterTime = value;
                break;
            case "CombatTimer":
                if (playerController != null) playerController.currentCombatTimer = value;
                break;
            case "Money":
                if (playerCurrency != null) playerCurrency.nodeBucks = value;
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
                EnablePlayerDoubleJump(value);
                break;
            case "PlayerCanAct":
                if (playerController != null)
                {
                    playerController.canAct = value;
                    Debug.Log($"GameManager: PlayerCanAct set to {playerController.canAct}");
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

    private void EnablePlayerDoubleJump(bool enable)
    {
        playerCanDoubleJump = enable;
        if (playerController != null)
        {
            playerController.canDoubleJump = enable;
            Debug.Log($"GameManager: Player Double Jump: {(enable ? "Enabled" : "Disabled")}");
        }
    }
    
    private void SetPlayerMaxHealth(float newMax)
    {
        playerHealth.playerMaxHealth = newMax;
        if (playerHealth.playerHealth > playerHealth.playerMaxHealth) // Adjust current health if max decreases
        {
            playerHealth.playerHealth = playerHealth.playerMaxHealth;
        }
    }
    
        public void AlterGlobalFloat(string variableName, float value)
    {
        switch (variableName)
        {
            case "PlayerMaxHealth":
                playerHealth.playerMaxHealth += value;
                break;
            case "PlayerHealth":
                playerHealth.playerHealth += value;
                playerHealth.playerHealth = Mathf.Clamp(playerHealth.playerHealth, 0, playerHealth.playerMaxHealth);
                break;
            case "PlayerRegen":
                if (playerHealth != null) playerHealth.regenerationMultiplier += value;
                break;
            case "StunMultiplier":
                if (playerHealth != null) playerHealth.stunMultiplier += value;
                break;
            case "PlayerMaxEnergy":
                if (playerAbility != null) playerAbility.maxEnergy += value;
                break;
            case "PlayerEnergy":
                if (playerAbility != null) playerAbility.energy += value;
                break;
            case "PlayerEnergyRefil":
                if (playerAbility != null) playerAbility.energyRefill += value;
                break;
            case "PlayerMovementSpeed":
                if (playerController != null) playerController.movementSpeed += value;
                break;
            case "PlayerJumpForce":
                if (playerController != null) playerController.jumpForce += value;
                break;
            case "DamageResistance":
                if (playerDefense != null) playerDefense.damageResistance += value;
                break;
            case "CounterWindow":
                if (playerDefense != null) playerDefense.counterTime += value;
                break;
            case "CombatTimer":
                if (playerController != null) playerController.currentCombatTimer += value;
                break;
            case "Money":
                if (playerCurrency != null) playerCurrency.nodeBucks += value;
                break;
            default:
                if (!string.IsNullOrEmpty(variableName)) Debug.LogWarning($"dawg ion know what a '{variableName}' is bruh");
                else Debug.LogWarning("BRO THERE IS NOTHING HERE TO CHAAANGE >:(");
                break;
        }
    }
    
    public void SetOriginalFloat(string variableName)
    {
        switch (variableName)
        {
            case "PlayerMaxHealth":
                playerHealth.playerMaxHealth = oMaxHealth;
                break;
            case "PlayerRegen":
                if (playerHealth != null) playerHealth.regenerationMultiplier = oMaxHealthRegen;
                break;
            case "StunMultiplier":
                if (playerHealth != null) playerHealth.stunMultiplier = oStunMultiplier;
                break;
            case "PlayerMaxEnergy":
                if (playerAbility != null) playerAbility.maxEnergy = oMaxEnergy;
                break;
            case "PlayerEnergyRefill":
                if (playerAbility != null) playerAbility.energyRefill = oAbilityEnergyRegen;
                break;
            case "PlayerMovementSpeed":
                if (playerController != null) playerController.movementSpeed = oMoveSpeed;
                break;
            case "PlayerJumpForce":
                if (playerController != null) playerController.jumpForce = oJumpForce;
                break;
            case "DamageResistance":
                if (playerDefense != null) playerDefense.damageResistance = oDamageResistance;
                break;
            case "CounterWindow":
                if (playerDefense != null) playerDefense.counterTime = oCounterWindow;
                break;
            default:
                if (!string.IsNullOrEmpty(variableName)) Debug.LogWarning($"dawg ion know what a '{variableName}' is bruh");
                else Debug.LogWarning("BRO THERE IS NOTHING HERE TO CHAAANGE >:(");
                break;
        }
    }
    
}
