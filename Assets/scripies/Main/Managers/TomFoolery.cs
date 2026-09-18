using System;
using System.Collections;
using System.Linq;
using scripies.Core;
using scripies.Player;
using UnityEngine;
/// <summary>
/// This is Shenanigans' brother
/// If there is a function that's inconvenient to place, AS WELL as not being fit for Shenanigans (because of assembly references), add it here!
/// Keep in mind it that this derives from Main, unlike Shenanigans, that derives from Core,
/// Meaning scripts that arent in Main cant access this script... TODO: Could there be some way to explicitly give shenanigans access to this, and vice versa?
/// </summary>
public class TomFoolery : MonoBehaviour
{
    public static TomFoolery Instance {get; private set;}
    private Shenanigans shenanigans;
    private PlayerHealth player;

    private void OnEnable()
    {
        GameEvents.ToggleDoor += ToggleDoor;
        GameEvents.OnSpawnPointChanged += PlayerSpawnChange;
    }
    
    private void OnDisable()
    {
        GameEvents.ToggleDoor -= ToggleDoor;
        GameEvents.OnSpawnPointChanged -= PlayerSpawnChange;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        shenanigans = Shenanigans.Instance ?? FindAnyObjectByType<Shenanigans>();
        player = PlayerHealth.Instance ?? FindAnyObjectByType<PlayerHealth>();
    }
    
    public void ToggleDoor(string doorName, bool open) => StartCoroutine(ToggleDoorC(doorName, open));

    private static IEnumerator ToggleDoorC(string doorName, bool open)
    {
        GameObject door = null;

        while (door == null)
        {
            foreach (var am in FindObjectsByType<AccessManager>(FindObjectsSortMode.None))
            {
                if (!am.gameObject.name.Contains(doorName)) continue;
                door = am.gameObject;
                //print("found our instance");
                break;
            }
            yield return new WaitForSeconds(0.5f);
            yield return null;
        }
        yield return new WaitForSeconds(0.1f);
        if (!door.TryGetComponent<AccessManager>(out var accessManager)) yield break;
        //print($"toggling door {doorName}: {open}");
        switch (open)
        {
            case true:
                accessManager.OpenDoors();
                break;
            case false:
                accessManager.CloseDoors();
                break;
        }
    }

    private void PlayerSpawnChange(GameObject point, bool spawn)
    {
        if (point == null) return;
        if (spawn)
        {
            player.pc.currentSpawnPoint = point;
            foreach (var map in MapManager.Instances.Values.Where(map => map.activeMap))
            {
                map.currentSpawnPoint = point;
                break;
            }
        }
        player.pc.currentCheckPoint = point;
        GameEvents.TriggerPersistGameData();
    }
}
