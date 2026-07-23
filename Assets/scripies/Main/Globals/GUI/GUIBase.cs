using System;
using UnityEngine;

public abstract class GUIBase : MonoBehaviour
{
    [Tooltip("lets get active?")]
    public bool isActive;

    protected static int ActiveGUICount;
    protected PlayerController player;
    protected RectTransform rt;

    protected virtual void Awake() => GetPlayer();
    
    private void Update()
    {
        if (ActiveGUICount < 0) ActiveGUICount = 0;
    }

    private void GetPlayer()
    {
        if (player == null && PlayerHealth.Instance != null)
            player = PlayerHealth.Instance.GetComponent<PlayerController>();
    }

    private void Enable()
    {
        isActive = true;
        ActiveGUICount++;
        UpdatePlayerState();
        OnActivated();
    }

    public void CloseAll()
    {
        var allGuis = FindObjectsByType<GUIBase>(FindObjectsSortMode.None);
        foreach (var gui in allGuis){
            if (gui.isActive && gui != this)
            {
                if (gui.TryGetComponent<InvManager>(out var inv)) inv.ToggleInventory();
                else if (gui.TryGetComponent<QuestLogManager>(out var quest)) quest.ToggleQuestLog();
                else gui.GetActive(false);
            }
        }
    }

    private void Disable()
    {
        //if (!isActive) return;
        isActive = false;
        ActiveGUICount = Mathf.Max(0, ActiveGUICount - 1);
        UpdatePlayerState();
        OnDeactivated();
    }
    
    /// <summary>
    ///  lets get actiiiiiiivvee-
    /// </summary>
    protected void GetActive(bool active)
    {
        if (active) Enable();
        else Disable();
    }
    private void UpdatePlayerState()
    {
        if (player == null) GetPlayer();
        else player.canAct = ActiveGUICount <= 0;
    }
    
    protected virtual void OnActivated() { }

    protected virtual void OnDeactivated() { }
}
