using UnityEngine;

namespace scripies.Core
{
    public interface IPlayerStatus
    {
        bool IsDead { get; }
        bool IsStunned { get; }
        Vector3 Position { get; }
        // Add other common properties that enemies or managers might need
    }
}
