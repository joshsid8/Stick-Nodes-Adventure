using scripies.Core;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }
    // this will just be all/some of the necessary data shown on the screen
    
    private IPlayerStatus player;
    [SerializeField] private TMP_Text currencyText;
    [SerializeField] private GameObject abilityArsenal;

    private IEnemyStatus boss;
    public Image bossHealthBar;
    [SerializeField] private Image bossHealthBackBar;
    public TMP_Text bossTitleText;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        ToggleBossHealth(false);
    }
    
    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<IPlayerStatus>();
        abilityArsenal.SetActive(true);
    }

    private void Update()
    {
        currencyText.text = "₦odeBucks: " + player.NodeBucks.ToString("F0");
        bossHealthBackBar.color = new Color(bossHealthBar.color.r * 0.4f, bossHealthBar.color.g * 0.4f, bossHealthBar.color.b * 0.4f, 1);
    }
    
    public void ToggleBossHealth(bool state)
    {
        bossHealthBar.GetComponent<RectTransform>().parent.gameObject.SetActive(state);
        bossTitleText.gameObject.SetActive(state);
    }
}
