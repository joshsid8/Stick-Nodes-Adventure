using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
public class QuestLogManager : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private string questTitle;
    [TextArea(5, 10)] [SerializeField] private string questDescription;
    [SerializeField] private Quest.QuestType questType;

    [SerializeField] private TMP_Text titleObject;
    [SerializeField] private TMP_Text descriptionObject;
    [SerializeField] private TMP_Text objectiveText;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;

    private Quest quest;

    private void Start()
    {
        UpdateUI();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) UpdateUI();
    }

    private void UpdateUI()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
        switch (questType)
        {
            case Quest.QuestType.DefeatEnemy:
                objectiveText.text = $"Defeat {quest.enemiesToDefeat.Length}/{quest.noOfEnemiesToDefeat} enemies";
                break;
            case Quest.QuestType.HaveInInventory:
                objectiveText.text = $"Obtain {quest.objectsToFind.Length}";
                break;
        }
    }
}
