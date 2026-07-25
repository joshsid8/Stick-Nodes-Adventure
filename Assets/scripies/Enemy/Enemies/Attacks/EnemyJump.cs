using System.Collections.Generic;
using scripies.Core;
using UnityEngine;

namespace scripies.Enemy
{
    public class EnemyJump : MonoBehaviour, IEnemyJump
    {
        [Header("Jump checks")] public bool isJumping;
        public bool jumpTogether;
        public bool myTurn;
        public bool MyTurn { get => myTurn; set => myTurn = value; }
        public float turnTime = 5;
        public float TurnTime => turnTime;
        public float turnTimer;
        [SerializeField] private GameObject[] partners = new GameObject[1];
        private EnemyController ec;
        private EnemyAttack ea;

        public EnemyAttack.StyleMode prevState = EnemyAttack.StyleMode.Passive;

        private void Awake()
        {
            ec = GetComponent<EnemyController>();
            ea = GetComponent<EnemyAttack>();
        }
        public void SetState(string state, float? duration = null)
        {
            if (state == "Zoning")
            {
                if (ec != null) ec.ChangeState(EnemyController.EnemyState.Zoning);
                if (duration.HasValue)
                {
                    var zone = GetComponent<EnemyZone>();
                    if (zone != null) zone.currentZonePeriod = duration.Value;
                }
            }
            else if (state == "Attacking")
            {
                if (ec != null) ec.ChangeState(EnemyController.EnemyState.Attacking);
            }
        }

        public void SetAttackStyleFromJump()
        {
            if (ea != null) ea.styleMode = prevState;
        }

        // Update is called once per frame
        private void Update()
        {
            if (!isJumping) prevState = ea != null ? ea.styleMode : EnemyAttack.StyleMode.Passive;
            else
            {
                if (myTurn && turnTimer > 0) turnTimer -= Time.deltaTime;

                if (turnTimer <= 0)
                {
                    myTurn = false;
                    turnTimer = turnTime;
                    if (ea != null) ea.styleMode = EnemyAttack.StyleMode.Passive;
                    if (ec != null) ec.ChangeState(EnemyController.EnemyState.Zoning);
                }
            }
        }
    }
}