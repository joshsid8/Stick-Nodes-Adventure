using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    // Singleton instance
    public static PlayerManager Instance { get; private set; }

    // the bools
    private bool playerCanDoubleJump;
    private bool playerHasDashAbility;
    
    // big stuff
    private PlayerController playerController;
    private PlayerHealth playerHealth;
    private PlayerAbility playerAbility;
    private PlayerCurrency playerCurrency;
    private PlayerDefense playerDefense;
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
        
        if (playerController != null)
        {
            playerCanDoubleJump = playerController.canDoubleJump;
            playerHasDashAbility = playerController.dashAbility;
        }
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
                playerHealth.playerHealth += value;
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
            case "CombatTimer":
                if (playerController != null) playerController.currentCombatTimer = value;
                break;
            case "Money":
                if (playerCurrency != null) playerCurrency.nodeBucks += value;
                break;
            default:
                Debug.LogWarning($"dawg ion know what a '{variableName}' is bruh");
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
            case "PlayerHasDashAbility":
                EnablePlayerDashAbility(value);
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
                Debug.LogWarning($"GameManager: Unknown bool variable '{variableName}' requested.");
                break;
        }
    }

    // --- Helper Methods for specific changes (optional, but good for complex logic) ---

    private void EnablePlayerDoubleJump(bool enable)
    {
        playerCanDoubleJump = enable;
        if (playerController != null)
        {
            playerController.canDoubleJump = enable;
            Debug.Log($"GameManager: Player Double Jump: {(enable ? "Enabled" : "Disabled")}");
        }
    }

    private void EnablePlayerDashAbility(bool enable)
    {
        playerHasDashAbility = enable;
        if (playerController != null)
        {
            playerController.dashAbility = enable;
            Debug.Log($"GameManager: Player Dash Ability: {(enable ? "Enabled" : "Disabled")}");
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
    
}
