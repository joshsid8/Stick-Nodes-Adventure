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
    private bool forcePlayerTeleport;
    public Transform teleportTarget;
    
    [SerializeField] private bool isSceneTeleport;
    public SceneField sceneToTeleport;
    [SerializeField] private GameObject enterIndicator;
    [SerializeField] private bool inRange;
     
    [SerializeField] private AudioClip teleportSfx;
    
    public bool doesDamage;
    public float damage = 20;
    
    private void OnTriggerEnter2D(Collider2D collision)
    {
        switch (collision.tag)
        {
            case "Player" when !isSceneTeleport:
            {
                if (tagsToIgnore.Contains(collision.tag)) return;
                if (!forcePlayerTeleport) StartCoroutine(PlayerHealth.Instance.Spawn());
                else collision.transform.position = teleportTarget.position;
                if (doesDamage)
                {
                    collision.GetComponent<PlayerHealth>().playerHealth -= damage;
                    GameEvents.TriggerPlayerHealthChanged(damage);
                }
                break;
            }
            case "Player" when isSceneTeleport:
                inRange = true;
                enterIndicator.SetActive(inRange);
                StartCoroutine(PlayerByTeleporter());
                break;
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
            case "Terrain": case "Door": case "Untagged": case "Spawner": case "MainCamera": case "NPC": case "Section": case "camera":
                return;
            default:
                collision.transform.position = teleportTarget.position;
                break;
        }
        if (teleportSfx != null) AudioSource.PlayClipAtPoint(teleportSfx, transform.position);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        enterIndicator.SetActive(false);
        inRange = false;
    }

    private IEnumerator PlayerByTeleporter()
    {
        var teleported = false;
        while (inRange && !teleported)
        {   //TODO: Add an Interact button in the Editor, then remove the second condition
            if (Input.GetButtonDown("Interact") || Input.GetKeyDown(KeyCode.W))
            {
                GameHUD.Instance.ActivateShader(1f, 0.5f, true, 0.5f);
                SceneManager.LoadSceneAsync(sceneToTeleport.SceneName,LoadSceneMode.Additive);
                UnloadCurrentScenes();
                TeleportPlayerToScene();
                teleported = true;
            }
            yield return null;
        }
    }

    private static void UnloadCurrentScenes()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && !scene.name.Contains("Persistent")) 
                SceneManager.UnloadSceneAsync(scene.name);
        }
    }
    
    private void TeleportPlayerToScene()
    {
        var player = PlayerHealth.Instance.gameObject;
        var scene = SceneManager.GetSceneByName(sceneToTeleport.SceneName);
        GameObject teleportPoint = null;
        foreach (var obj in scene.GetRootGameObjects())
        {
            if (obj.TryGetComponent<MapManager>(out var mm)) continue;
            teleportPoint = mm.mainSpawnPoint;
            break;
        }
        if (teleportPoint == null) return;
        player.transform.position = teleportPoint.transform.position;
    }
}
