using System.Collections.Generic;
using UnityEngine;

public class NPCDatabase : MonoBehaviour
{
    public static NPCDatabase Instance { get; private set; }
    private readonly Dictionary<string, NPC> npcDatabase = new Dictionary<string, NPC>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildDatabase();
    }

    private void BuildDatabase()
    {
        var allNpcs = Resources.LoadAll<NPC>("NPC");

        foreach (var npc in allNpcs)
        {
            if (!npcDatabase.ContainsKey(npc.name)) npcDatabase.Add(npc.name, npc);
            else Debug.LogWarning($"[NPCDatabase] Duplicate npc name found: {npc.name}.");
        }
    }
    
    public NPC GetNpcByName(string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return null;
        npcDatabase.TryGetValue(npcName, out var npc);
        return npc;
    }
}
