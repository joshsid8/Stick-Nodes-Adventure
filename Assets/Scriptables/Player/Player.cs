using UnityEngine;

[CreateAssetMenu(fileName = "PlayerStats", menuName = "Player/PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("Vitality")]
    [Tooltip("The maximum health of the player.")]
    [SerializeField] private float maxHealth = 100f;
    public float MaxHealth => maxHealth;

    [Tooltip("How quickly does the player regenerate their health?")]
    [SerializeField] private float regenerationMultiplier = 5f;
    public float RegenerationMultiplier => regenerationMultiplier;
    [Tooltip("For when you are fighting!")]
    [SerializeField] private float modRegenerationMultiplier = 1f;
    public float ModRegenerationMultiplier => modRegenerationMultiplier;
    [Tooltip("Stun multiplier when the player gets stunned! (e.g., 0 for no stun)")]
    [SerializeField] private float stunMultiplier = 1f;
    public float StunMultiplier => stunMultiplier;

    [Tooltip("The multiplied amount of damage you take without blocking!")]
    [SerializeField] private float rawDamageResistance = 1f;
    public float RawDamageResistance => rawDamageResistance;
    
    [Tooltip("Protection after getting hurt!")]
    public float invincibleDuration = 1f;
    public float InvincibleDuration => invincibleDuration;

    [Header("Agility")]
    [Tooltip("The base movement speed of the player")]
    [SerializeField] private float movementSpeed = 10.0f;
    public float MovementSpeed => movementSpeed;
    
    [Tooltip("Height of a normal jump")]
    [SerializeField] private float jumpForce = 27f;
    public float JumpForce => jumpForce;
    
    [Tooltip("Multiplier for falling speed")]
    [SerializeField] private float fallMultiplier = 3f;
    public float FallMultiplier => fallMultiplier;
    
    [Tooltip("Multiplier for lower jumps")]
    [SerializeField] private float lowJumpMultiplier = 5f;
    public float LowJumpMultiplier => lowJumpMultiplier;

    [Tooltip("The apex speed the player can fall")] 
    [SerializeField] private float maxFallSpeed = 35f;
    public float MaxFallSpeed => maxFallSpeed;
    
    [Tooltip("The amount of wall jumps before landing")]
    [SerializeField] private float availableWalls;
    public float AvailableWalls => availableWalls;
        
    [Tooltip("Speed of the dash")]
    [SerializeField] private float dashSpeed = 25f;
    public float DashSpeed => dashSpeed;

    [Tooltip("Duration of the dash")]
    [SerializeField] private float startDashTime = 0.65f;
    public float StartDashTime => startDashTime;

    [Tooltip("Cooldown time after a dash")]
    [SerializeField] private float dashCooldown = 0.5f;
    public float DashCooldown => dashCooldown;

    [Tooltip("Energy cost for dashing")]
    [SerializeField] private float dashEnergyCost = 5f;
    public float DashEnergyCost => dashEnergyCost;

    [Tooltip("The speed of sliding on a wall")]
    [SerializeField] private float wallSlideSpeed = 1f;
    public float WallSlideSpeed => wallSlideSpeed;
    
    [Tooltip("The force of jumpping off a wall")]
    [SerializeField] private float wallJumpForce = 25f;
    public float WallJumpForce => wallJumpForce;
    
    [Header("Ability")]
    [Tooltip("maximum energy for bros abilities")]
    [SerializeField] private float maxEnergy = 50f;
    public float MaxEnergy => maxEnergy;

    [Tooltip("how quickly does bro regenerate bros energy")]
    [SerializeField] private float energyRefill = 5f;
    public float EnergyRefill => energyRefill;

    [Tooltip("how much worse bros energy regen gets when fighting")]
    [SerializeField] private float modEnergyRefill = 2f;
    public float ModEnergyRefill => modEnergyRefill;

    [Tooltip("the cost of bros ability")]
    [SerializeField] private float abilityEnergyCost = 10f;
    public float AbilityEnergyCost => abilityEnergyCost;

    [Tooltip("damage multiplier for bros ability")]
    [SerializeField] private float damageMultiplier = 1f;
    public float DamageMultiplier => damageMultiplier;
    
    [Tooltip("cooldown for bros ability")]
    [SerializeField] private float abilityCooldown = 1f;
    public float AbilityCooldown => abilityCooldown;

    [Header("Strength")]
    [Tooltip("Can dawg even m1??!")]
    [SerializeField] private bool m1Ability = true;
    public bool M1Ability => m1Ability;
    
    [Tooltip("damageeeeeeee")]
    [SerializeField] private float groundDamageAmount = 20f;
    public float GroundDamageAmount => groundDamageAmount;

    [Tooltip("why do we have to waaiiittt")]
    [SerializeField] private float groundAttackCooldown = 0.3f;
    public float GroundAttackCooldown => groundAttackCooldown;

    [Tooltip("YES ENERGY LETS GOOO")]
    [SerializeField] private float groundAttackEnergy = 5f;
    public float GroundAttackEnergy => groundAttackEnergy;

    [Tooltip("we are so cool when in the air, huh")]
    [SerializeField] private float airDamageAmount = 20f;
    public float AirDamageAmount => airDamageAmount;

    [Tooltip("ARE WE TAILS?! NO I DONT THINK SOOOOOOO")]
    [SerializeField] private float airAttackForce = 15f;
    public float AirAttackForce => airAttackForce;

    [Tooltip("AUUUUGH NOT AGAIN")]
    [SerializeField] private float airAttackCooldown = 0.15f;
    public float AirAttackCooldown => airAttackCooldown;

    [Tooltip("we gotta get at least 10 thousand")]
    [SerializeField] private float playerAttackStunTime = 0.2f;
    public float PlayerAttackStunTime => playerAttackStunTime;

    [Header("Defense")]
    
    [Tooltip("normal block dmg resistance")]
    [SerializeField] private float blockDamageResistance = 0.5f;
    public float BlockDamageResistance => blockDamageResistance;

    [Tooltip("counter check")]
    [SerializeField] private bool counterAbility = false;
    public bool CounterAbility => counterAbility;

    [Tooltip("time window for the counter")]
    [SerializeField] private float counterTime = 0.2f;
    public float CounterTime => counterTime;

    [Tooltip("damage resistance during counter window.")]
    [SerializeField] private float counterDamageResistance = 0.2f;
    public float CounterDamageResistance => counterDamageResistance;

    [Tooltip("damage dealt by the counter")]
    [SerializeField] private float counterDamage = 10;
    public float CounterDamage => counterDamage;

    [Tooltip("stun duration applied by the counter")]
    [SerializeField] private float counterStun = 1f;
    public float CounterStun => counterStun;
}
