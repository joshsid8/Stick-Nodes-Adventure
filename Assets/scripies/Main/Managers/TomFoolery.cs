using System;
using System.Collections;
using scripies.Core;
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

    private void OnEnable()
    {
        GameEvents.ToggleDoor += ToggleDoor;
    }
    
    private void OnDisable()
    {
        GameEvents.ToggleDoor -= ToggleDoor;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        shenanigans = Shenanigans.Instance;
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
}
