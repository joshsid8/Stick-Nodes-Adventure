using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class DoorObjective : MonoBehaviour
{
    public bool isOpen;
    
    public ObjectiveDoor[] doorObj;
    [SerializeField] Objectives objective;

    public List<GameObject> enemies = new();
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
                    if (enemies.Count < 1) enemies.Add(collision.gameObject);
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
                            if (item != null && item.itemScriptableObject == key)
                            {
                                item.UseItem(); 
                                break;
                            }
                    }
                    Open();
                }
                break;
            case Objectives.DefeatBoss:
                if (boss != null)
                {
                    var bh = boss.GetComponent<BossHealth>();
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

        if (collision.gameObject.CompareTag("Dead") && objective == Objectives.DefeatEnemy)
        {
            foreach (var enemy in enemies)
            {
                if (collision.gameObject == enemy)
                {
                    enemies.Remove(enemy);
                    if (enemies.Count == 0) enemies = null;
                    break;
                }
                print($"{enemy} is NNNNNOT the same as {collision.gameObject}");
            }
        }
        else if (collision.gameObject.CompareTag("Player")) playerDetected = false;
        else if (collision.gameObject.CompareTag("Boss"))
            boss.transform.position =
                new Vector2(GetComponent<Collider2D>().bounds.center.x, boss.transform.position.y);
    }
}