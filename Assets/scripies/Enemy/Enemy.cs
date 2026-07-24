using UnityEngine;

namespace scripies.Enemy
{
    [CreateAssetMenu(fileName = "EnemyVariant", menuName = "Enemy Variant")]
    public class Enemy : ScriptableObject
    {
        public string enemyName;

        public enum EnemyType
        {
            Basic,
            MiniBoss,
            Boss
        }

        public EnemyType enemyType;
        public float damageMult = 1;
        public float healthMult = 1;
        public float speedMult = 1;

        public void SetProperties(EnemyAttack ea)
        {
            ea.damageAmount *= damageMult;
            ea.eh.enemyHealth *= healthMult;
            ea.em.dashSpeed *= speedMult;
            if (ea.ef != null) ea.ef.currentFlySpeed *= speedMult;
            ea.ep.walkSpeed *= speedMult;
        }
    }
}