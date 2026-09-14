 using System;
 using System.Collections;
 using scripies.Core;
 using UnityEngine;

public class DoorObject : MonoBehaviour
{
    private AccessManager manager;
    public float openTime;
    [SerializeField] private AudioClip openSfx;

    private void Start()
    {
        manager = GetComponentInParent<AccessManager>();
        if (manager.isOpen) StartCoroutine(Open());
    }

    public IEnumerator Open()
    {
        if (openSfx != null) GameEvents.TriggerPlaySound(openSfx, gameObject);
        if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm)) mm.SaveSceneData();
        yield return new WaitForSeconds(openTime);
        gameObject.SetActive(false);
    }
}
