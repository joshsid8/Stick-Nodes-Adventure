using scripies.Core;
using UnityEngine;
using TMPro;
public class GameHUD : MonoBehaviour
{
    // this will just be all/some of the necessary data shown on the screen
    
    private IPlayerStatus player;
    [SerializeField] private TMP_Text currencyText;

    private IEnemyStatus boss;
    [SerializeField] private TMP_Text bossTitleText;
    
    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>();
    }

    private void Update()
    {
        currencyText.text = "₦odeBucks: " + player.NodeBucks.ToString("F0");
    }
}
