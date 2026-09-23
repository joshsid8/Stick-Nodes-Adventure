using UnityEngine;

namespace scripies.Enemy
{
    [CreateAssetMenu(fileName = "EnemyVariant", menuName = "Entities/Enemy Variant")]
    public class Enemy : Entity
    {
        public enum EnemyType
        {
            Basic,
            MiniBoss,
        }

        [EnumButtons] public EnemyType enemyType;
        public float damageMult = 1;
        public float healthMult = 1;
        public float speedMult = 1;
    }
}