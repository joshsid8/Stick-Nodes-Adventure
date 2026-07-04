using System;
using UnityEngine;

public abstract class GUIBase : MonoBehaviour
{
    [Tooltip("lets get active?")]
    public bool isActive;

    protected static int ActiveGUICount;
    protected PlayerController player;
    protected RectTransform rt;

    protected virtual void Awake()
    {
        GetPlayer();
    }

    private void Update()
    {
        if (ActiveGUICount < 0) ActiveGUICount = 0;
    }

    protected void GetPlayer()
    {
        if (player == null && PlayerHealth.Instance != null)
            player = PlayerHealth.Instance.GetComponent<PlayerController>();
    }

    protected virtual void Enable()
    {
        isActive = true;
        ActiveGUICount++;
        UpdatePlayerState();
        OnActivated();
    }

    protected void CloseAll()
    {
        foreach (var gui in FindObjectsByType<GUIBase>(FindObjectsSortMode.None)){
            if (gui.isActive)
            {
                if (gui.TryGetComponent<InvManager>(out var inv)) inv.ToggleInventory();
                else if (gui.TryGetComponent<QuestLogManager>(out var quest)) quest.ToggleQuestLog();
                else
                {
                    print($"Closing {gui.gameObject.name}");
                    gui.isActive = false;
                    gui.GetActive(false);
                }
                ActiveGUICount--;
            }
        }
    }

    protected void Disable()
    {
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
