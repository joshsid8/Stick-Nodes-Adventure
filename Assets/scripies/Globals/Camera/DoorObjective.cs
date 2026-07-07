using System;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class DoorObjective : MonoBehaviour
{
    public bool isOpen;
    
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

    private void OnEnable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.RegisterDoor(gameObject);
    }

    private void OnDisable()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.UnregisterDoor(gameObject);
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (!isOpen) switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (collision.gameObject.CompareTag("Enemy"))
                {
                    if (enemies == null || enemies.Length != 1) enemies = new GameObject[1];
                    if (enemies[0] == null) enemies[0] = collision.gameObject;
                }
                break;
            case Objectives.FindKeys:
                if (collision.gameObject.CompareTag("Player")) playerDetected = true; else playerDetected = false;
                break;
            case Objectives.DefeatBoss: 
                if (collision.gameObject.CompareTag("Boss")) boss = collision.gameObject;
                break;
            default:
                print("Clueless.");
                break;
        }
    }

    public void InstantOpen()
    {
        isOpen = true;
        foreach (var door in doorObj)
        {
            if (door != null)
            {
                door.finishTime = 0;
                door.objectiveComplete = true;
            }
        }
    }

    private void Open()
    {
        if (isOpen) return;
        isOpen = true;
        foreach (var door in doorObj) if (door != null) door.objectiveComplete = true;
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.OnDoorStatusChanged(this, true);
    }

    private void Update()
    {
        if (isOpen) return;
        
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (enemies == null) Open();
                break;
            case Objectives.FindKeys:
                if (playerDetected)
                {
                    foreach (var key in keys) if (!InvManager.Instance.invIndex.HasAtLeast(key, 1)) 
                        return;
                    
                    foreach (var key in keys)
                    {
                        foreach (var item in InvManager.Instance.itemSlots)
                            if (item != null && item.itemScriptableObject == key) {item.UseItem(); break;}
                    }
                    Open();
                }
                break;
            case Objectives.DefeatBoss:
                if (boss != null)
                {
                    BossHealth bh = boss.GetComponent<BossHealth>();
                    if (bh != null && bh.isDead) Open();
                }
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