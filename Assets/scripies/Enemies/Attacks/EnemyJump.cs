using System.Collections.Generic;
using UnityEngine;

public class EnemyJump : MonoBehaviour
{
    public bool jumpTogether;
    public bool myTurn;
    [SerializeField] private float turnTime = 5;
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
            if (ally.GetComponent<EnemyController>().idName == ec.idName && !allies.Contains(ally)) prePartners.Add(ally);
        partners = prePartners.ToArray();
    }
    // Update is called once per frame
    private void Update()
    {
        turnTimer -= Time.deltaTime;
        if (turnTimer <= 0)
        {
            myTurn = false;
            turnTimer = turnTime;
        }
        print($"{name}'s turn to fight player 1 on 1");
        ea.enabled = myTurn;
    }
    private void OnEnable() => UpdatePartners();
}
