using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
public class QuestLogManager : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private string questTitle;
    [TextArea(5, 10)] [SerializeField] private string questDescription;

    [SerializeField] private TMP_Text titleObject;
    [SerializeField] private TMP_Text descriptionObject;
    
    [SerializeField] private TMP_Text infoTitle;
    [SerializeField] private TMP_Text infoDescription;

    private void Start()
    {
        titleObject.text = questTitle;
        descriptionObject.text = questDescription;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) OnLeftClick();
    }

    private void OnLeftClick()
    {
        infoTitle.text = questTitle;
        infoDescription.text = questDescription;
    }
}
