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
    public bool canTeleport;
    
    [SerializeField] private List<string> tagsToIgnore = new();
    [SerializeField] [Tooltip("Will teleport the player to said position instead of taking them to their most recent checkpoint")] 
    public Transform teleportTarget;
    [Space]
    [Header("Player Teleporting Properties")]
    [SerializeField] private bool forcePlayerTeleport;

    [SerializeField] private bool isTeledoor;
    [SerializeField] private bool isSceneTeleport;
    public SceneField sceneToTeleport;
    [SerializeField] private bool inRange;
    [SerializeField] private Vector2 teleporterSize;
    [SerializeField] private GameObject enterIndicator;
    [SerializeField] private string connectingDoor;
    [Space]
    [SerializeField] private AudioClip teleportSfx;
    private PlayerInput input;
    
    public bool doesDamage;
    public float damage = 20;

    private void Start()
    {
        input = PlayerHealth.Instance.pc.input ?? new PlayerInput();
        StartCoroutine(JustTeleported());
    }

    private void Update()
    {
        if (sceneToTeleport == null || !isSceneTeleport) return;
        inRange = Physics2D.OverlapBox(transform.position, teleporterSize, 0, LayerMask.GetMask("Player"));
        enterIndicator.SetActive(inRange);
        if (input.Player.Interact.IsPressed() && inRange && canTeleport && isTeledoor) StartCoroutine(TeleportPlayerToScene());
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (collision.tag)
        {
            case "Player" when !isSceneTeleport && !isTeledoor:
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
            case "Terrain": case "Door": case "Untagged": case "Spawner": case "MainCamera": case "NPC": case "Section": case "camera": case "Player" when isSceneTeleport || isTeledoor:
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
                if (mapManager == null) continue;
                mapManager.DisableSpawners();
                SceneManager.UnloadSceneAsync(scene.name);
            }
        }
    }
    
    private IEnumerator TeleportPlayerToScene()
    {
        if (!canTeleport) yield break;
        canTeleport = false;
        PlayerHealth.Instance.pc.canAct = false;
        GameHUD.Instance.SetShaderColour(Color.black, 0); 
        GameHUD.Instance.ActivateShader(0f, -2f);
        GameObject teleportPoint = null;
        var player = PlayerHealth.Instance.gameObject;
        yield return new WaitForSeconds(0.2f);
        if (isSceneTeleport)
        {
            UnloadCurrentScenes();
            
            var scene = SceneManager.GetSceneByName(sceneToTeleport.SceneName);
            if (!scene.isLoaded) SceneManager.LoadScene(sceneToTeleport.SceneName, LoadSceneMode.Additive);
            yield return new WaitForSeconds(0.3f);

            if (string.IsNullOrEmpty(connectingDoor))
            {
                foreach (var obj in scene.GetRootGameObjects())
                {
                    if (!obj.TryGetComponent<MapManager>(out var mm)) continue;
                    teleportPoint = mm.mainSpawnPoint;
                    break;
                }
            }
            else
            {
                var lookup = ObjectDatabase.Instance.GetTeleDoorByName(connectingDoor);
                if (lookup != null) teleportPoint = lookup;
                if (teleportPoint == null)
                {
                    Debug.LogWarning(
                        $"Failed finding using ObjectDatabase lookup. Trying again using FindObjectsByType.");
                    var allDoors = FindObjectsByType<DoorIdentifier>(FindObjectsSortMode.None);
                    foreach (var door in allDoors)
                    {
                        if (!door.doorNameID.Contains(connectingDoor)) continue;
                        teleportPoint = door.gameObject;
                        break;
                    }
                }
            }
        }
        else teleportPoint = teleportTarget.gameObject;
        yield return new WaitForSeconds(0.25f);
        if (teleportPoint == null)
        {
            GameHUD.Instance.ActivateShader(0.5f, 1f);
            GameEvents.TriggerPlaySound();
            PlayerHealth.Instance.pc.canAct = true;
            throw new NullReferenceException($"[Teleporter {gameObject.name}] Not a teleport point in sight from the looks of it...");
        }
        player.transform.position = teleportPoint.transform.position;
        yield return new WaitForSeconds(0.5f);
        if (input.Player.enabled) PlayerHealth.Instance.pc.canAct = true;
        StartCoroutine(JustTeleported());
        GameHUD.Instance.ActivateShader(0.5f, 2f);
    }

    private static IEnumerator JustTeleported()
    {
        var allTeleporters = FindObjectsByType<Teleporter>(FindObjectsSortMode.None);
        foreach (var teleporter in allTeleporters)
        {
            teleporter.canTeleport = false;
            yield return new WaitForSeconds(0.5f);
            teleporter.canTeleport = true;
        }
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.dodgerBlue;
        Gizmos.DrawWireCube(transform.position, teleporterSize);
    }
}
