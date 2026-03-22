using System;
using UnityEngine;
using System.Collections.Generic;
public class Shenanigans : MonoBehaviour
{
    // This script is meant to manage literally every small function that occurs in this game, similar to the player manager.
    // If it cant be managed anywhere else without it being inconvenient, add it here!
     
    public static Shenanigans Instance {get; private set;}

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
