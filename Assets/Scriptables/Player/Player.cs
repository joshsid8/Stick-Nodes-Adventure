using UnityEngine;

[CreateAssetMenu(fileName = "PlayerStats", menuName = "Player/PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("Vitality")]
    [Tooltip("The maximum health of the player!")]
    [SerializeField] private float maxHealth = 100f;
    public float MaxHealth => maxHealth;

    [Tooltip("The multiplier for regenerating health!")]
    [SerializeField] private float regenerationMultiplier = 1f;
    public float RegenerationMultiplier => regenerationMultiplier;
    
    [Tooltip("Stun multiplier when the player gets stunned! (e.g., 0 for no stun)")]
    [SerializeField] private float shockMultiplier = 1f;
    public float ShockMultiplier => shockMultiplier;

    [Tooltip("The multiplied amount of damage you take without blocking!")]
    [SerializeField] private float rawDamageResistance = 1f;
    public float RawDamageResistance => rawDamageResistance;

    [Header("Agility")]
    [Tooltip("The movement speed multiplayer of the player")]
    [SerializeField] private float movementSpeedMultiplier = 1f;
    public float MovementSpeedMultiplier => movementSpeedMultiplier;
    
    [Tooltip("Height multiplier of a normal jump")]
    [SerializeField] private float jumpForceMultiplier = 1f;
    public float JumpForceMultiplier => jumpForceMultiplier;
    
    [Tooltip("The amount of wall jumps before landing")]
    [SerializeField] private float availableWalls;
    public float AvailableWalls => availableWalls;
        
    [Tooltip("Speed of the dash")]
    [SerializeField] private float dashSpeed = 25f;
    public float DashSpeed => dashSpeed;

    [Tooltip("Duration of the dash")]
    [SerializeField] private float dashTime = 0.65f;
    public float DashTime => dashTime;

    [Tooltip("Cooldown time after a dash")]
    [SerializeField] private float dashCooldown = 0.5f;
    public float DashCooldown => dashCooldown;

    [Tooltip("Energy cost for dashing")]
    [SerializeField] private float dashEnergyCost = 5f;
    public float DashEnergyCost => dashEnergyCost;
    
    [Header("Ability")]
    [Tooltip("maximum energy for bros abilities")]
    [SerializeField] private float maxEnergy = 50f;
    public float MaxEnergy => maxEnergy;

    [Tooltip("how quickly does bro regenerate bros energy")]
    [SerializeField] private float energyRefillMultiplier = 1f;
    public float EnergyRefillMultiplier => energyRefillMultiplier;

    [Tooltip("damage multiplier for bros ability")]
    [SerializeField] private float abilityDamageMultiplier = 1f;
    public float AbilityDamageMultiplier => abilityDamageMultiplier;
    
    [Tooltip("cooldown multiplier for bros ability")]
    [SerializeField] private float abilityCooldownMultiplier = 1f;
    public float AbilityCooldownMultiplier => abilityCooldownMultiplier;
    
    [Tooltip("knockback and stun multiplier for bros ability")]
    [SerializeField] private float abilityPotency = 1f;
    public float AbilityPotency => abilityPotency;

    [Tooltip("lifetime multiplier for bros ability")]
    [SerializeField] private float abilityLifetimeMultiplier = 1f;
    public float AbilityLifetimeMultiplier => abilityLifetimeMultiplier;

    [Header("Strength")]
    [Tooltip("Can dawg even m1??!")]
    [SerializeField] private bool m1Ability = true;
    public bool M1Ability => m1Ability;

    [Tooltip("ARE WE TAILS?! NO I DONT THINK SOOOOOOO")]
    [SerializeField] private float airAttackForce = 20f;
    public float AirAttackForce => airAttackForce;
    
    [Tooltip("for da damage!!!")]
    [SerializeField] private float attackDamageMultiplier = 1f;
    public float AttackDamageMultiplier => attackDamageMultiplier;

    [Tooltip("are you sure?")] 
    [SerializeField] private float attackPotency = 1f;
    public float AttackPotency => attackPotency;
    
    [Tooltip("pretty sure")]
    [SerializeField] private float attackCooldownMultiplier = 1f;
    public float AttackCooldownMultiplier => attackCooldownMultiplier;

    [Header("Defense")]
    [Tooltip("normal block dmg resistance")]
    [SerializeField] private float blockDamageResistance = 0.5f;
    public float BlockDamageResistance => blockDamageResistance;

    [Tooltip("counter check")]
    [SerializeField] private bool counterAbility;
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
    
}
