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
    public enum TeleportType {Scene, InterScene, PreviousDoor}
    public bool canTeleport;
    
    [SerializeField] private List<string> tagsToIgnore = new();
    [SerializeField] [Tooltip("Will teleport the player to said position instead of taking them to their most recent checkpoint")] 
    public Transform teleportTarget;
    [Space]
    [Header("Player Teleporting Properties")]
    [SerializeField] private bool forcePlayerTeleport;
    [SerializeField] private bool isTeledoor;
    [EnumButtons] public TeleportType teleportType;
    private TeleDoor thisTeleDoor;
    [Space]
    [Tooltip("Can be for both Scene and InterScene Teleporting")]
    [SerializeField] private TeleDoor targetTeleDoor;
    [Space]
    [Tooltip("For InterScene Teleporting IF targetTeleDoor is not set")]
    [SerializeField] private SceneField sceneToTeleport;
    [Space]
    [Tooltip("This will be automaticaly filled in, but we can initialise the field if the type is PreviousDoor")]
    public TeleDoor previousTeleDoor;
    [Space]
    [SerializeField] private bool inRange;
    [SerializeField] private Vector2 teleporterSize;
    [SerializeField] private GameObject enterIndicator;
    [Space]
    [SerializeField] private AudioClip teleportSfx;
    private PlayerInput input;
    
    public bool doesDamage;
    public float damage = 20;

    private void Start()
    {
        if (isTeledoor) thisTeleDoor = GetComponentInChildren<DoorIdentifier>().teleDoorID;
        StartCoroutine(TeleportInterval(0.5f));
        input = PlayerHealth.Instance.pc.input ?? new PlayerInput();
    }

    private void Update()
    {
        if (!isTeledoor || forcePlayerTeleport || !canTeleport) return;
        inRange = Physics2D.OverlapBox(transform.position, teleporterSize, 0, LayerMask.GetMask("Player"));
        if (enterIndicator != null) enterIndicator.SetActive(inRange);
        if (input.Player.Interact.WasPressedThisFrame() && inRange) StartCoroutine(ActivateTeleDoor());
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (collision.tag)
        {
            case "Player" when !isTeledoor:
            {
                if (tagsToIgnore.Contains(collision.tag)) return;
                if (!forcePlayerTeleport) StartCoroutine(PlayerHealth.Instance.Spawn());
                else /*collision.GetComponent<Rigidbody2D>().MovePosition(teleportTarget.position);*/ collision.transform.position = teleportTarget.position;
                if (doesDamage)
                {
                    collision.GetComponent<PlayerHealth>().health -= damage;
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
            case "Terrain": case "Door": case "Untagged": case "Spawner": case "MainCamera": case "NPC": case "Section": case "camera": case "Player" when isTeledoor:
                return;
            default:
                collision.transform.position = teleportTarget.position;
                break;
        }
        if (teleportSfx != null) GameEvents.TriggerPlaySound(teleportSfx, gameObject);
    }

    private void UnloadCurrentScenes(string sceneToIgnore = "")
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var currentMap) && currentMap != null)
        {
            currentMap.SaveSceneData();
        }
        GameEvents.TriggerSaveNpcData();

        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.name == gameObject.scene.name || scene.name == sceneToIgnore || scene.name == "Persistent Objects") continue;
            MapManager.Instances.TryGetValue(scene.name, out var mapManager);
            if (mapManager == null) continue;
            mapManager.SaveSceneData();
            mapManager.DisableSpawners();
            SceneManager.UnloadSceneAsync(scene.name);
        }
    }
    
    private IEnumerator ActivateTeleDoor()
    {
        if (!canTeleport) yield break;
        canTeleport = false;
        PlayerHealth.Instance.pc.canAct = false;
        GameHUD.Instance.SetShaderColour(Color.black, 0); 
        GameHUD.Instance.ActivateShader(0f, -2f);
        StartCoroutine(TeleportInterval(2.5f));
        GameObject teleportPoint = null;
        var player = PlayerHealth.Instance.gameObject;
        yield return new WaitForSeconds(0.2f);

        var targetSceneName = teleportType switch
        {
            TeleportType.InterScene => targetTeleDoor != null ? targetTeleDoor.sceneToLoad.SceneName : sceneToTeleport.SceneName,
            TeleportType.PreviousDoor => previousTeleDoor != null ? previousTeleDoor.sceneToLoad.SceneName : "",
            _ => ""
        };
        if (targetSceneName == gameObject.scene.name) Debug.LogError($"[Teleporter {gameObject.name}] Target scene name is the same as the current scene!");
        UnloadCurrentScenes(targetSceneName);
        
        switch (teleportType)
        {
            case TeleportType.InterScene:
            {
                if (string.IsNullOrEmpty(targetSceneName))
                {
                    Debug.LogError($"[Teleporter {gameObject.name}] Target scene name is empty for InterScene teleport!");
                    goto FailureCheck;
                }

                var scene = SceneManager.GetSceneByName(targetSceneName);
                if (!scene.isLoaded)
                {
                    var stl = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
                    if (stl == null) goto FailureCheck; 
                    if (!stl.isDone) yield return new WaitUntil(() => stl.isDone);
                }

                yield return new WaitUntil(() => ObjectDatabase.Instance.loaded);

                if (targetTeleDoor == null)
                {
                    scene = SceneManager.GetSceneByName(targetSceneName);
                    foreach (var obj in scene.GetRootGameObjects())
                    {
                        if (!obj.TryGetComponent<MapManager>(out var mm)) continue;
                        teleportPoint = mm.mainSpawnPoint;
                        break;
                    }
                }
                else
                {
                    yield return new WaitForSeconds(0.1f);
                    teleportPoint = ObjectDatabase.Instance.GetTeleDoorObjByID(targetTeleDoor);
                    if (teleportPoint == null) Debug.LogError($"[Teleporter {gameObject.name}] Failed finding {targetTeleDoor.name} using ObjectDatabase lookup");
                }
                break;
            }
            case TeleportType.PreviousDoor:
            {
                if (previousTeleDoor == null)
                {
                    Debug.LogError($"[Teleporter {gameObject.name}] previousTeleDoor is null for PreviousDoor teleport!");
                    goto FailureCheck;
                }
                if (string.IsNullOrEmpty(targetSceneName))
                {
                    Debug.LogError($"[Teleporter {gameObject.name}] Target scene name is empty for PreviousDoor teleport!");
                    goto FailureCheck;
                }

                var scene = SceneManager.GetSceneByName(targetSceneName);
                if (!scene.isLoaded)
                {
                    var stl = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
                    if (stl == null) goto FailureCheck;
                    if (!stl.isDone) yield return new WaitUntil(() => stl.isDone);
                }
                
                yield return new WaitUntil(() => ObjectDatabase.Instance.loaded);
                yield return new WaitForSeconds(0.1f); // Extra buffer for objects to settle

                teleportPoint = ObjectDatabase.Instance.GetTeleDoorObjByID(previousTeleDoor);
                break;
            }
            case TeleportType.Scene:
            default:
                if (targetTeleDoor == null)
                {
                    teleportPoint = teleportTarget.gameObject;
                    break;
                }
                teleportPoint = ObjectDatabase.Instance.GetTeleDoorObjByID(targetTeleDoor);
                if (teleportPoint == null)
                {
                    Debug.LogWarning($"[Teleporter {gameObject.name}] Failed finding using ObjectDatabase lookup. Trying again using FindObjectsByType.");
                    var allDoors = FindObjectsByType<DoorIdentifier>(FindObjectsSortMode.None);
                    foreach (var door in allDoors)
                    {
                        if (door.teleDoorID != targetTeleDoor) continue;
                        teleportPoint = door.gameObject;
                        break;
                    }
                }
                break;
        }
        var parentDoor = teleportPoint?.GetComponentInParent<Teleporter>();
        if (parentDoor != null) parentDoor.previousTeleDoor = thisTeleDoor;
        yield return new WaitForSeconds(0.25f);
        FailureCheck:
        if (teleportPoint == null)
        {
            GameHUD.Instance.ActivateShader(0.5f, 1f);
            GameEvents.TriggerPlaySound();
            PlayerHealth.Instance.pc.canAct = true;
            Debug.LogError($"[Teleporter {gameObject.name}] Not a teleport point in sight from the looks of it...");
            yield break;
        }
        player.transform.position = teleportPoint.transform.position;
        yield return new WaitForSeconds(0.5f);
        if (input.Player.enabled) PlayerHealth.Instance.pc.canAct = true;
        GameHUD.Instance.ActivateShader(0.5f, 2f);
    }

    private static IEnumerator TeleportInterval(float interval = 2f)
    {
        var allTeleporters = FindObjectsByType<Teleporter>(FindObjectsSortMode.None);
        foreach (var teleporter in allTeleporters)
        {
            if (teleporter == null) continue;
            teleporter.canTeleport = false;
        }
        yield return new WaitForSeconds(interval);
        foreach (var teleporter in allTeleporters)
        {
            if (teleporter == null) continue;
            teleporter.canTeleport = true;
        }
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = teleportType switch {TeleportType.Scene => Color.chartreuse, TeleportType.InterScene => Color.deepSkyBlue, TeleportType.PreviousDoor => Color.orangeRed, _ => Color.white};
        Gizmos.DrawWireCube(transform.position, teleporterSize);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = teleportType switch {TeleportType.Scene => Color.chartreuse, TeleportType.InterScene => Color.deepSkyBlue, TeleportType.PreviousDoor => Color.orangeRed, _ => Color.white};
        if (teleportType == TeleportType.Scene && teleportTarget != null) Gizmos.DrawLine(transform.position, teleportTarget.position);
    }
}
