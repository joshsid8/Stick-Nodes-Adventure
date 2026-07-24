using System.Collections.Generic;
using UnityEngine;

namespace scripies.Enemy
{
    public class EnemyJump : MonoBehaviour
    {
        [Header("Jump checks")] public bool isJumping;
        public bool jumpTogether;
        public bool myTurn;
        public float turnTime = 5;
        public float turnTimer;
        [SerializeField] private GameObject[] partners = new GameObject[1];
        private EnemyController ec;
        private EnemyAttack ea;

        public EnemyAttack.StyleMode prevState;

        private void Awake()
        {
            ec = GetComponent<EnemyController>();
            ea = GetComponent<EnemyAttack>();
        }
        //We will have to use ITarget in PlayerHealth for this to work
        /*private void UpdatePartners()
        {
            prevState = ea.styleMode;
            var allies = PlayerHealth.Instance.aggressiveEnemies;
            var prePartners = new List<GameObject>();
            foreach (var ally in allies)
                if (ally.GetComponent<EnemyController>().enemy.enemyName == ec.enemy.enemyName &&
                    !prePartners.Contains(ally))
                    prePartners.Add(ally);
            partners = prePartners.ToArray();
        }*/

        // Update is called once per frame
        private void Update()
        {
            if (isJumping)
            {
                if (myTurn && turnTimer > 0) turnTimer -= Time.deltaTime;

                if (turnTimer <= 0)
                {
                    myTurn = false;
                    turnTimer = turnTime;
                    ea.styleMode = EnemyAttack.StyleMode.Passive;
                    ec.ChangeState(EnemyController.EnemyState.Zoning);
                }
            }
        }
    }
}