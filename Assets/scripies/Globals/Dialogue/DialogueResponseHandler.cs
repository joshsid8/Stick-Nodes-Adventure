using UnityEngine;
using TMPro;
using UnityEngine.UI; // Required for Button

public class DialogueResponseHandler : MonoBehaviour
{
    [SerializeField] private RectTransform responseBox;
    [SerializeField] private GameObject responseButtonsParent; // A parent GameObject for your buttons (e.g., a Horizontal Layout Group)

    // NEW: References to the actual Yes/No buttons in your UI
    public Button yesButton;
    public Button noButton;
    public TMP_Text yesButtonText;
    public TMP_Text noButtonText;

    private DialogueUI dialogueUI;

    private void Awake()
    {
        dialogueUI = GetComponent<DialogueUI>();
        responseBox.gameObject.SetActive(false);
        responseButtonsParent.gameObject.SetActive(false);
    }

    public void ShowResponses(DialogueObject dialogueObject)
    {
        responseButtonsParent.gameObject.SetActive(true);
        responseBox.gameObject.SetActive(true);

        // Set button texts
        yesButtonText.text = dialogueObject.YesResponse.ResponseText;
        noButtonText.text = dialogueObject.NoResponse.ResponseText;

        // Clear previous listeners to prevent multiple calls
        yesButton.onClick.RemoveAllListeners();
        noButton.onClick.RemoveAllListeners();

        // Add new listeners
        yesButton.onClick.AddListener(() => OnPickedResponse(dialogueObject.YesResponse));
        noButton.onClick.AddListener(() => OnPickedResponse(dialogueObject.NoResponse));
    }

    private void OnPickedResponse(DialogueResponse response)
    {
        ApplyCustomEffect(response);
        responseButtonsParent.gameObject.SetActive(false);
        responseBox.gameObject.SetActive(false);
        
        dialogueUI.ShowDialogue(response.DialogueObject);
    }
    private void ApplyCustomEffect(DialogueResponse response)
    {
        if (GameManager.Instance == null)
        {
            Debug.LogError("WHERE IS THE GAME MANAGERERERERWHGWH");
            return;
        }

        if (response.appliesFloatChange)
        {
            GameManager.Instance.SetGlobalFloat(response.targetFloatVariableName, response.newFloatValue);
        }

        if (response.appliesBooleanChange)
        {
            GameManager.Instance.SetGlobalBool(response.targetBooleanVariableName, response.newBooleanValue);
        }
    }
}
