using System;
using UnityEngine;
using UnityEngine.UI;

public class StatsManager : GUIBase
{
    public static StatsManager Instance { get; private set; }
    
    public StatInfo[] stats;

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
        if (closeShader == null || !isActive) return;
        CloseCurrentMenus();
        closeShader.onClick.AddListener(ToggleStats);
    }

    public void ClearStatShaders()
    {
        foreach (var stat in stats) 
            stat.ClearInfo();
    }
}
