 using System;
using UnityEngine;

public class ObjectiveDoor : MonoBehaviour
{
    public bool objectiveComplete;
    public float finishTime;

    [SerializeField] private AudioClip openSfx;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() => objectiveComplete = false;
    
    // Update is called once per frame
    void Update()
    {
        if (objectiveComplete && MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
        {
            mm.SaveSceneData();
            if (openSfx != null) AudioSource.PlayClipAtPoint(openSfx, transform.position);
            Destroy(gameObject, finishTime);
        }
    }

}
