using UnityEngine;

namespace scripies.Core
{
    public interface IEnemyStatus
    {
        // Health
        bool IsDead { get; }
        bool IsStunned { get; }
        
        // Movement
        Vector3 Position { get; }
        bool IsGrounded { get; }
        bool IsFacingRight { get; }
        
    }
}
