using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class DoorObjective : MonoBehaviour
{
    public ObjectiveDoor doorObj;
    [SerializeField] Objectives objective;

    public GameObject[] enemies;
    public GameObject boss;
    private enum Objectives
    {
        DefeatEnemy,
        FindKeys,
        DefeatBoss
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (collision.gameObject.CompareTag("Enemy"))
                {
                    enemies = new GameObject[1];
                    for (int i = 0; i < enemies.Length; i++) if (enemies[i] == null) enemies[i] = collision.gameObject;
                }
                break;
            case Objectives.FindKeys:
                print("ion know");
                break;
            case Objectives.DefeatBoss: 
                if (objective == Objectives.DefeatBoss)
                {
                    if (collision.gameObject.CompareTag("Boss")) boss = collision.gameObject;
                }
                break;
            default:
                print("Clueless.");
                break;
        }
    }

    private void Update()
    {
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (enemies == null)
                {
                    doorObj.objectiveComplete = true;
                }
                break;
            case Objectives.FindKeys:
                print("ion know");
                break;
            case Objectives.DefeatBoss:
                BossHealth bossHealth = boss.GetComponent<BossHealth>();
                if (bossHealth != null && bossHealth.isDead) doorObj.objectiveComplete = true;
                break;
            default:
                print("Just clueless.");
                break;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Dead"))
        {
            enemies = null;
        }
    }
}