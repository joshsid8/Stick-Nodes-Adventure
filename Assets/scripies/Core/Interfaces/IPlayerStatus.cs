using UnityEngine;

namespace scripies.Core
{
    public interface IPlayerStatus
    {
        // Health
        bool IsDead { get; }
        bool IsStunned { get; }
        float CurrentHealth { get; }
        
        // Movement
        Vector3 Position { get; }
        bool IsGrounded { get; }
        bool IsPerformingAction { get; }
        bool IsFacingRight { get; }
        
        // Ability
        bool IsHoldingAbility { get; }
        bool IsPerformingAbility { get; }
        float CurrentEnergy { get; }
        float CurrentAbilityCooldown { get; }
        
        // Defense
        bool IsCountering { get; }
    }
}
