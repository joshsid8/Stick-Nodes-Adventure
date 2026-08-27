using UnityEngine;

namespace scripies.Core
{
    public interface IEnemyJump
    {
        bool MyTurn { get; set; }
        float TurnTime { get; }
        void SetState(string state, float? duration = null);
        void SetPrevStyle();
    }
}
