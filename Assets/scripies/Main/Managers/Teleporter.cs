using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using scripies.Enemy;
using scripies.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Teleporter : MonoBehaviour
{
    [SerializeField] private List<string> tagsToIgnore = new();
    [SerializeField] [Tooltip("Will teleport the player to said position instead of taking them to their most recent checkpoint")] 
    public Transform teleportTarget;
    [Space]
    [Header("Player Teleporting Properties")]
    [SerializeField] private bool forcePlayerTeleport;
    [SerializeField] private bool isSceneTeleport;
    public SceneField sceneToTeleport;
    [SerializeField] private bool inRange;
    [SerializeField] private Vector2 teleporterSize;
    [SerializeField] private GameObject enterIndicator;
    [SerializeField] private string connectingDoor;
    [Space]
    [SerializeField] private AudioClip teleportSfx;
    
    public bool doesDamage;
    public float damage = 20;

    private void Update()
    {
        if (sceneToTeleport == null || !isSceneTeleport) return;
        inRange = Physics2D.OverlapBox(transform.position, teleporterSize, 0, LayerMask.GetMask("Player"));
        enterIndicator.SetActive(inRange);
    }

    private void OnEnable()
    {
        PlayerHealth.Instance.pc.input.Player.Interact.performed += _ =>
        {
            if (inRange) StartCoroutine(TeleportPlayerToScene());
        };
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (collision.tag)
        {
            case "Player" when !isSceneTeleport:
            {
                if (tagsToIgnore.Contains(collision.tag)) return;
                if (!forcePlayerTeleport) StartCoroutine(PlayerHealth.Instance.Spawn());
                else /*collision.GetComponent<Rigidbody2D>().MovePosition(teleportTarget.position);*/ collision.transform.position = teleportTarget.position;
                if (doesDamage)
                {
                    collision.GetComponent<PlayerHealth>().playerHealth -= damage;
                    GameEvents.TriggerPlayerHealthChanged(damage);
                }
                break;
            }
            case "Enemy":
            case "Boss":
            {
                if (tagsToIgnore.Contains(collision.tag)) return;
                collision.transform.position = teleportTarget.position;
                if (doesDamage)
                {
                    var e = collision.GetComponent<EnemyHealth>();
                    var b = collision.GetComponent<BossHealth>();
                    if (e != null) collision.GetComponent<EnemyHealth>().enemyHealth -= damage;
                    if (b != null) collision.GetComponent<BossHealth>().bossHealth -= damage;
                }
                break;
            }
            case "Terrain": case "Door": case "Untagged": case "Spawner": case "MainCamera": case "NPC": case "Section": case "camera": case "Player" when isSceneTeleport:
                return;
            default:
                collision.transform.position = teleportTarget.position;
                break;
        }
        if (teleportSfx != null) AudioSource.PlayClipAtPoint(teleportSfx, transform.position);
    }

    private void UnloadCurrentScenes()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.name != gameObject.scene.name && scene.name != sceneToTeleport.SceneName && scene.name != "Persistent Objects")
            {
                MapManager.Instances.TryGetValue(scene.name, out var mapManager);
                mapManager?.DisableSpawners();
                SceneManager.UnloadSceneAsync(scene.name);
            }
        }
    }
    
    private IEnumerator TeleportPlayerToScene()
    {
        GameHUD.Instance.ActivateShader(1f, 15f, true, 1f);
        yield return new WaitForSeconds(0.5f);
        if (!SceneManager.GetSceneByName(sceneToTeleport.SceneName).isLoaded) SceneManager.LoadSceneAsync(sceneToTeleport.SceneName, LoadSceneMode.Additive);
        UnloadCurrentScenes();
        
        var player = PlayerHealth.Instance.gameObject;
        var scene = SceneManager.GetSceneByName(sceneToTeleport.SceneName);
       
        GameObject teleportPoint = null;
        if (string.IsNullOrEmpty(connectingDoor))
        {
            foreach (var obj in scene.GetRootGameObjects())
            {
                if (obj.TryGetComponent<MapManager>(out var mm))
                {
                    teleportPoint = mm.mainSpawnPoint;
                    break;
                }
            }
        }
        else
        {
            var allDoors = FindObjectsByType<DoorIdentifier>(FindObjectsSortMode.None);
            foreach (var door in allDoors)
            {
                if (door.doorNameID.Contains(connectingDoor))
                {
                    teleportPoint = door.gameObject;
                    break;
                }
            }
        }
        yield return new WaitForSeconds(0.5f);
        if (teleportPoint == null) throw new NullReferenceException($"[Teleporter {gameObject.name}] Not a teleport point in sight from the looks of it...");
        player.transform.position = teleportPoint.transform.position;
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.dodgerBlue;
        Gizmos.DrawWireCube(transform.position, teleporterSize);
    }
}
