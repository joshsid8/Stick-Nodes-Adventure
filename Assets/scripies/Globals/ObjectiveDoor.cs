 using System;
using UnityEngine;

public class ObjectiveDoor : MonoBehaviour
{
    public bool objectiveComplete;
    public float finishTime;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        objectiveComplete = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (objectiveComplete && MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm))
        {
            mm.SaveSceneData();
            Destroy(gameObject, finishTime);
        }
    }

}
