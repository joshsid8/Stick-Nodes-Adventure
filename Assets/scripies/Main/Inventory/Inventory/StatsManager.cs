using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StatsManager : GUIBase
{
    public static StatsManager Instance { get; private set; }
    
    public StatInfo[] stats;
    public List<Badge> badges = new();
    public List<BadgeSlot> badgeSlots = new();
    protected override void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        base.Awake();
    }

    private void Start()
    {
        if (isActive) ToggleStats();
        ClearStatShaders();
        UpdateBadgeSlotList();
    }

    private void Update()
    {
        if (Input.GetButtonDown("Inventory") && isActive || Input.GetButtonDown("Cancel") && isActive) ToggleStats();
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
    
    public void AddBadge(Badge badge)
    {
        if (badges.Contains(badge)) return;
        badges.Add(badge);
        UpdateBadgeSlotList();
    }
    public bool HasBadge(Badge badge) => badges.Contains(badge);

    private void UpdateBadgeSlotList()
    {
        for (var i = 0; i < badges.Count; i++)
        {
            badgeSlots[i].gameObject.SetActive(true);
            badgeSlots[i].SetBadge(badges[i]);
        }
    }
}
