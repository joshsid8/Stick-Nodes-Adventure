using System.Collections.Generic;
using UnityEngine;

public class EnemyJump : MonoBehaviour
{
    public bool isJumping;
    public bool jumpTogether;
    public bool myTurn;
    public float turnTime = 5;
    public float turnTimer;
    [SerializeField] private GameObject[] partners = new GameObject[1];
    private EnemyController ec;
    private EnemyAttack ea;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ec = GetComponent<EnemyController>();
        ea = GetComponent<EnemyAttack>();
    }
    private void UpdatePartners()
    {
        var allies = PlayerHealth.Instance.aggressiveEnemies;
        var prePartners = new List<GameObject>();
        foreach (var ally in allies)
            if (ally.GetComponent<EnemyController>().idName == ec.idName && !prePartners.Contains(ally)) prePartners.Add(ally);
        partners = prePartners.ToArray();
    }
    // Update is called once per frame
    private void Update()
    {
        if (isJumping)
        {
            if (myTurn && turnTimer > 0)
            {
                turnTimer -= Time.deltaTime;
                print($"{gameObject.name}'s turn");
            }

            if (turnTimer <= 0)
            {
                myTurn = false;
                turnTimer = turnTime;
                ec.ChangeState(EnemyController.EnemyState.Zoning);
            }
        }
    }
    private void OnEnable() => UpdatePartners();
}
