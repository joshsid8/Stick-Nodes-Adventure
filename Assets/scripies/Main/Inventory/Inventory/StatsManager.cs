using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StatsManager : GUIBase
{
    public static StatsManager Instance { get; private set; }
    
    private Dictionary<string, StatSO> statLookup;
    public StatInfo[] stats;
    public List<BadgeSO> badges = new();
    public List<BadgeSlot> badgeSlots = new();
    
    protected override void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        base.Awake();
        
        statLookup = new Dictionary<string, StatSO>();
        foreach(var s in stats) {
            if(s.stat != null) statLookup[s.stat.name] = s.stat;
        }
    }

    private void Start()
    {
        if (isActive) ToggleStats();
        ClearStatShaders();
        UpdateBadgeSlotList();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            UpdateBadgeSlotList();
    }

    private void Update()
    {
        //if (Input.GetButtonDown("Inventory") && isActive || Input.GetButtonDown("Cancel") && isActive) ToggleStats();
    }
    
    public string GetDisplayName(string rawName) 
    {
        if (statLookup.TryGetValue(rawName, out var stat))
            return stat.DisplayName;
        return rawName; // Fallback to raw name if not found
    }

    public void ToggleStats()
    {
        isActive = !isActive;
        rt.anchoredPosition = new Vector2(0, isActive ? 0 : 1200);
        GetActive(isActive);
        if (isActive) CloseCurrentMenus();
        UpdateBadgeSlotList();
    }

    public void ClearStatShaders()
    {
        foreach (var stat in stats) 
            stat.ClearInfo();
    }
    
    public void AddBadge(BadgeSO badgeSo)
    {
        if (badges.Contains(badgeSo)) return;
        badges.Add(badgeSo);
        UpdateBadgeSlotList();
    }
    public bool HasBadge(BadgeSO badgeSo) => badges.Contains(badgeSo);

    private void UpdateBadgeSlotList()
    {
        if (badges.Count == 0)
        {
            foreach (var slot in badgeSlots) slot.gameObject.SetActive(false);
            return;
        }
        for (var i = 0; i < badges.Count; i++)
        {
            badgeSlots[i].gameObject.SetActive(true);
            badgeSlots[i].SetBadge(badges[i]);
        }
    }
    
    public void Save(ref PlayerBadges data)
    {
        if (badges == null) return;            
        var badgesToSave = badges.ToArray() as ScriptableObject[]; //The hell do you mean a statically typed language construct???
        data.badges = badgesToSave;
    }

    public void Load(PlayerBadges data)
    {
        if (badges == null || data.badges.Length == 0) return;
        List<BadgeSO> badgesToLoad = new();
        foreach (var badge in data.badges) badgesToLoad.Add(badge as BadgeSO); 
        badges = new List<BadgeSO>(badgesToLoad);
        UpdateBadgeSlotList();
    }
}

[Serializable]
public struct PlayerBadges
{
    public ScriptableObject[] badges;
}