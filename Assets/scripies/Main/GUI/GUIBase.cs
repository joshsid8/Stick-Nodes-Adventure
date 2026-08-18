using System.Collections;
using scripies.Core;
using scripies.Player;
using UnityEngine;

public abstract class GUIBase : MonoBehaviour
{
    [Tooltip("get active?")]
    public bool isActive;
    [Tooltip("lets... get... AA-")]
    protected static int ActiveGUICount;
    protected PlayerController player;
    protected RectTransform rt;

    protected virtual void Awake() => ActiveGUICount = 0;
    
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        GetPlayer();
        if (player == null)
        {
            player = PlayerHealth.Instance.pc;
            if (player == null) throw new MissingReferenceException("BRUUHHH");
        }
    }
    private void Update()
    {
        if (ActiveGUICount < 0) ActiveGUICount = 0;
    }

    private void GetPlayer()
    {
        if (player == null) player = FindAnyObjectByType<PlayerController>();
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
    private void Enable()
    {
        isActive = true;
        ActiveGUICount++;
        UpdatePlayerState();
        OnActivated();
    }

    private void Disable()
    {
        isActive = false;
        ActiveGUICount = Mathf.Max(0, ActiveGUICount - 1);
        UpdatePlayerState();
        OnDeactivated();
    }
    
    /// <summary>
    ///  get actiiiiiiivvee-
    /// </summary>
    protected void GetActive(bool active)
    {
        if (active) Enable();
        else Disable();
    }
    private void UpdatePlayerState()
    {
        if (player == null) GetPlayer();
        player.canAct = ActiveGUICount <= 0;
    }
    
    private void OnActivated() => GameEvents.TriggerEnableGUI();

    private void OnDeactivated() => GameEvents.TriggerDisableGUI();
}
