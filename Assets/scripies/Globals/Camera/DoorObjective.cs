using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class DoorObjective : MonoBehaviour
{
    public ObjectiveDoor[] doorObj;
    [SerializeField] Objectives objective;

    public GameObject[] enemies;
    public GameObject boss;
    public CollectableSOBase[] keys;
    private bool playerDetected;
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
                if (collision.gameObject.CompareTag("Player")) playerDetected = true; else playerDetected = false;
                break;
            case Objectives.DefeatBoss: 
                if (objective == Objectives.DefeatBoss) 
                    if (collision.gameObject.CompareTag("Boss")) boss = collision.gameObject;
                break;
            default:
                print("Clueless.");
                break;
        }
    }

    public void CompleteObjective()
    {
        foreach (var door in doorObj) door.objectiveComplete = true;
    }

    private void Update()
    {
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (enemies == null) CompleteObjective();
                break;
            case Objectives.FindKeys:
                if (playerDetected)
                {
                    foreach (var key in keys) 
                        if (!InvManager.Instance.Index.HasAtLeast(key, 1)) return;
                    CompleteObjective();
                    foreach (var key in keys)
                    {
                        foreach (var item in InvManager.Instance.itemSlots)
                            if (item != null && item.itemScriptableObject == key) {item.UseItem(); return;}
                    }
                }
                break;
            case Objectives.DefeatBoss:
                BossHealth bossHealth = boss.GetComponent<BossHealth>();
                if (bossHealth != null && bossHealth.isDead) CompleteObjective();
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
        else if (collision.gameObject.CompareTag("Player")) playerDetected = false;
    }
}