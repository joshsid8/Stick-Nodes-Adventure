using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class AccessManager : MonoBehaviour
{
    private enum Objectives { DefeatEnemy, FindKeys, DefeatBoss, UnlockBadge }
    public bool isOpen;
    
    public List<DoorObject> doorObjs = new();
    [EnumButtons] [SerializeField] private Objectives objective;

    public List<GameObject> enemies = new();
    public GameObject boss;
    public CollectableSOBase[] keys;
    public BadgeSO badgeSo;
    [SerializeField] private bool checkOnTrigger = true;
    private bool playerDetected;

    private void Awake()
    {
        if (doorObjs.Count == 0) doorObjs = GetComponentsInChildren<DoorObject>().ToList(); // SINCE WHEN DID TOLIST EXIST?! MY LIIIIIIFFFFEEE!!!
        playerDetected = !checkOnTrigger;
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
    
    public void InstantOpen()
    {
        isOpen = true;
        foreach (var door in doorObjs)
        {
            if (door != null)
            {
                door.openTime = 0;
                StartCoroutine(door.Open());
                if (door.gameObject.activeInHierarchy) door.gameObject.SetActive(false);
            }
            else print("Door is null?!");
        }
    }

    [ContextMenu("Open Doors")]
    private void OpenDoors()
    {
        if (isOpen) return;
        isOpen = true;
        foreach (var door in doorObjs) if (door != null)
                StartCoroutine(door.Open());
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
            mm.OnDoorStatusChanged(this, true);
    }

    private void Update()
    {
        if (isOpen) return;
        
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (enemies.Count == 0) OpenDoors();
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
                                item.DecrementItem(); 
                                break;
                            }
                    }
                    OpenDoors();
                }
                break;
            case Objectives.UnlockBadge:
                if (StatsManager.Instance.HasBadge(badgeSo) && playerDetected) OpenDoors();
                break;
            case Objectives.DefeatBoss:
                if (boss != null)
                {
                    var bh = boss.GetComponent<BossHealth>();
                    if (bh != null && bh.isDead) OpenDoors();
                }
                break;
            default:
                print("Just clueless.");
                break;
        }
    }
    
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && checkOnTrigger) playerDetected = true;
        
        if (isOpen) return;
        switch (objective)
        {
            case Objectives.DefeatEnemy:
                if (collision.gameObject.CompareTag("Enemy"))
                {
                    if (enemies.Count < 1) enemies.Add(collision.gameObject);
                    if (enemies[0] == null) enemies[0] = collision.gameObject;
                }
                // This ensures that the doors open the moment the enemy dies, rather than when they get destroyed
                else if (collision.gameObject.layer == LayerMask.NameToLayer("EDead") && enemies.Contains(collision.gameObject))
                    enemies.Remove(collision.gameObject);
                break;
            case Objectives.FindKeys:
            case Objectives.UnlockBadge:
                playerDetected = collision.gameObject.CompareTag("Player");
                break;
            case Objectives.DefeatBoss:
                if (collision.gameObject.CompareTag("Boss")) boss = collision.gameObject;
                break;
            default:
                print($"Clueless: {objective}");
                break;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("EDead") && objective == Objectives.DefeatEnemy)
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
        else if (collision.gameObject.CompareTag("Player") && checkOnTrigger) playerDetected = false;
        else if (collision.gameObject.CompareTag("Boss"))
            boss.transform.position =
                new Vector2(GetComponent<Collider2D>().bounds.center.x, boss.transform.position.y);
    }
}