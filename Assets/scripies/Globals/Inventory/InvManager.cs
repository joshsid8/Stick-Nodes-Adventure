using UnityEngine;

public class InvManager : MonoBehaviour
{
    public GameObject inventoryMenu;
    private bool isActivated;
    PlayerController player;
    
    
    void Awake()
    {
        player = FindAnyObjectByType<PlayerController>();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetButtonDown("Inventory") && !isActivated)
        {
            inventoryMenu.SetActive(true);
            isActivated = true;
            player.canAct = false;
        }
        else if (Input.GetButtonDown("Inventory") && isActivated)
        {
            inventoryMenu.SetActive(false);
            isActivated = false;
            player.canAct = true;
        }
    }
}
