using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;
using UnityEngine.UI;

public abstract class GUIBase : MonoBehaviour
{
    [Tooltip("get active?")]
    public bool isActive;
    [Tooltip("lets... get... AA-")]
    protected static int ActiveGUICount;
    private static List<GUIBase> ActiveGuis = new();
    protected PlayerController player;
    protected RectTransform rt;
    private GUIBase[] allGuis;
    
    protected PlayerInput input;
    
    protected virtual void Awake()
    {
        ActiveGUICount = 0;
        ActiveGuis.Clear();
        rt = GetComponent<RectTransform>();
        allGuis = FindObjectsByType<GUIBase>(FindObjectsSortMode.None);
    }
    
    private void Start()
    {
        input = PlayerHealth.Instance.pc.input;
        input.UI.CloseTopMost.performed += _ => CloseTopMost();
        UpdatePlayerState();
        if (player != null) return;
        player = PlayerHealth.Instance.pc ?? FindAnyObjectByType<PlayerController>();
        if (player == null) throw new MissingReferenceException("BRUUHHH");
    }
    private void Update()
    {
        if (ActiveGUICount < 0) ActiveGUICount = 0;
        foreach (var gui in allGuis)
        {
            if (gui.isActive) break;
            if (gui != allGuis[^1] || gui.isActive || ActiveGUICount <= 0) return;
            Debug.LogError($"All guis are inactive, but the count was {ActiveGUICount}. \nResetting Count. You may be frozen mb");
            ActiveGUICount = 0;
        }
    }

    private void GetPlayer() => player = PlayerHealth.Instance.pc ?? FindAnyObjectByType<PlayerController>();

    protected void CloseAll()
    {
        foreach (var gui in allGuis)
        {
            if (!gui.isActive || gui == this) return;
            
            if (gui.TryGetComponent<InvManager>(out var inv)) inv.ToggleInventory();
            else if (gui.TryGetComponent<QuestLogManager>(out var quest)) quest.ToggleQuestLog();
            else if (gui.TryGetComponent<StatsManager>(out var stats)) stats.ToggleStats();
            else if (gui.TryGetComponent<ShopManager>(out var shop)) shop.ToggleShop(false);
            else gui.GetActive(false);
        }
    }

    protected void CloseCurrentMenus()
    {
        foreach (var gui in allGuis)
        {
            if (!gui.isActive || gui == this) continue;
            
            if (gui is InvManager inv) inv.ToggleInventory();
            else if (gui is QuestLogManager quest) quest.ToggleQuestLog();
            else if (gui is StatsManager stats) stats.ToggleStats();
            else if (gui is ShopManager shop) shop.ToggleShop(false);
        }
    }

    public void CloseTopMost()
    {
        if (ActiveGUICount == 0 || ActiveGuis.Count == 0) return;
        var topMostGui = ActiveGuis[^1];
        switch (topMostGui)
        {
            case InvManager inv: inv.ToggleInventory(); break;
            case QuestLogManager quest: quest.ToggleQuestLog(); break;
            case StatsManager stats: stats.ToggleStats(); break;
            case ShopManager shop: shop.ToggleShop(false); break;
        }
    }
    
    private void Enable()
    {
        isActive = true;
        ActiveGUICount++;
        ActiveGuis.Add(this);
        OnActivated();
    }

    private void Disable()
    {
        isActive = false;
        ActiveGUICount = Mathf.Max(0, ActiveGUICount - 1);
        ActiveGuis.Remove(this);
        OnDeactivated();
    }
    
    /// <summary>
    /// get actiiiiiiivvee-
    /// </summary>
    protected void GetActive(bool active)
    {
        if (active) Enable();
        else Disable();
        UpdatePlayerState();
    }
    private void UpdatePlayerState()
    {
        if (player == null) GetPlayer();
        player.canAct = ActiveGUICount <= 0;
        if (ActiveGUICount > 0)
        {
            input ??= PlayerHealth.Instance.pc.input;
            input.UI.Enable();
            input.Player.Disable();
        }
        else
        {
            input ??= PlayerHealth.Instance.pc.input;
            input.UI.Disable();
            input.Player.Enable();
        }
    }
    
    private static void OnActivated() => GameEvents.TriggerEnableGUI();
    private static void OnDeactivated() => GameEvents.TriggerDisableGUI();
}
