 using System;
 using System.Collections;
 using scripies.Core;
 using UnityEngine;

public class DoorObject : MonoBehaviour
{
    private AccessManager manager;
    public float openTime;
    [SerializeField] private AudioClip openSfx;
    [SerializeField] private AudioClip closeSfx;

    private void Start()
    {
        manager = GetComponentInParent<AccessManager>();
        if (manager.isOpen) StartCoroutine(Open());
    }

    public IEnumerator Open()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm)) mm.SaveSceneData();
        yield return new WaitForSeconds(openTime);
        if (openSfx != null) GameEvents.TriggerPlaySound(openSfx, gameObject);
        gameObject.SetActive(false);
    }
    
    public void Close()
    {
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm)) mm.SaveSceneData();
        if (closeSfx != null) GameEvents.TriggerPlaySound(closeSfx, gameObject);
        gameObject.SetActive(true);
    }
}
