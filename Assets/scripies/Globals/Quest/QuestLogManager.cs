using System;
using UnityEngine;
using TMPro;
public class QuestLogManager : MonoBehaviour
{
    public static QuestLogManager Instance {get; private set;}

    public Quest[] questSlots;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddQuest(Quest quest)
    {
        
    }

}
