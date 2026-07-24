using UnityEngine;

namespace scripies.Core
{
    public interface ITarget
    {
        Transform Transform { get; }
        bool IsActive { get; }
        int Priority { get; }
    }
}
