using System;
using UnityEngine;

public class ObjectPersistence : MonoBehaviour
{
    public static ObjectPersistence instance;
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }
}
