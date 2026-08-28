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
    protected static List<GUIBase> ActiveGuis = new();
    protected PlayerController player;
    protected RectTransform rt;
    private GUIBase[] allGuis;
    
    protected virtual void Awake()
    {
        ActiveGUICount = 0;
        rt = GetComponent<RectTransform>();
        allGuis = FindObjectsByType<GUIBase>(FindObjectsSortMode.None);
    }
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(0.5f);
        GetPlayer();
        if (player != null) yield break;
        player = PlayerHealth.Instance.pc ?? FindAnyObjectByType<PlayerController>();
        if (player == null) throw new MissingReferenceException("BRUUHHH");
    }
    private void Update()
    {
        //Cluelessness.Instance.CluelessF(ActiveGUICount, "ActiveGUICount");
        if (ActiveGUICount < 0) ActiveGUICount = 0;
        foreach (var gui in allGuis)
        {
            if (gui.isActive) break;
            if (gui != allGuis[^1] || gui.isActive || ActiveGUICount <= 0) return;
            Debug.LogError($"All gui's are inactive, but the count was {ActiveGUICount}. Resetting Count. You may be frozen, mb");
            ActiveGUICount = 0;
        }
    }

    private void GetPlayer() => player = PlayerHealth.Instance.pc ?? FindAnyObjectByType<PlayerController>();

    public void CloseAll()
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

    public void CloseCurrentMenus()
    {
        var allGuis = FindObjectsByType<GUIBase>(FindObjectsSortMode.None);
        foreach (var gui in allGuis)
        {
            if (!gui.isActive || gui == this) return;
            
            if (gui.TryGetComponent<InvManager>(out var inv)) inv.ToggleInventory();
            else if (gui.TryGetComponent<QuestLogManager>(out var quest)) quest.ToggleQuestLog();
            else if (gui.TryGetComponent<StatsManager>(out var stats)) stats.ToggleStats();
            else if (gui.TryGetComponent<ShopManager>(out var shop)) shop.ToggleShop(false);
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
            //default: topMostGui.GetActive(false); break;
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
    ///  get actiiiiiiivvee-
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
        //print($"Player can act: {player.canAct} | ActiveGUICount: {ActiveGUICount}");
    }
    
    private void OnActivated() => GameEvents.TriggerEnableGUI();

    private void OnDeactivated() => GameEvents.TriggerDisableGUI();
}
