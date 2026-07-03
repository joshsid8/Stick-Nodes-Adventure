using UnityEngine;

public abstract class GUIBase : MonoBehaviour
{
    //become the base >:(
    [Tooltip("Lets get active?")] public bool isActive;
    private PlayerController player;
    
    private void Awake()
    {
        player = PlayerHealth.Instance.GetComponent<PlayerController>() ?? throw new System.NullReferenceException("eyh");
    }
    public void Activation(bool active = false)
    {
        player.canAct = !active;
    }
}
