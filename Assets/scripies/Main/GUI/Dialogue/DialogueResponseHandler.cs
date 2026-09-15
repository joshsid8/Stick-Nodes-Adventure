using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace scripies.Dialogue
{
    public class DialogueResponseHandler : MonoBehaviour
    {
        public RectTransform responseBox;
        [SerializeField] private GameObject responseButtonsParent;

        public Button yesButton;
        public Button noButton;
        public TMP_Text yesButtonText;
        public TMP_Text noButtonText;

        private DialogueManager dialogueManager;

        private void Awake()
        {
            dialogueManager = GetComponent<DialogueManager>();
            responseBox.gameObject.SetActive(false);
            responseButtonsParent.gameObject.SetActive(false);
        }

        public void ShowResponses(DialogueObject dialogueObject)
        {
            dialogueManager.DecrementActiveGUICount();
            responseButtonsParent.gameObject.SetActive(true);
            responseBox.gameObject.SetActive(true);

            yesButtonText.text = dialogueObject.YesResponse.ResponseText;
            noButtonText.text = dialogueObject.NoResponse.ResponseText;

            yesButton.onClick.RemoveAllListeners();
            noButton.onClick.RemoveAllListeners();

            yesButton.onClick.AddListener(() => OnPickedResponse(dialogueObject.YesResponse));
            noButton.onClick.AddListener(() => OnPickedResponse(dialogueObject.NoResponse));
        }

        private void OnPickedResponse(DialogueResponse response)
        {
            responseButtonsParent.gameObject.SetActive(false);
            responseBox.gameObject.SetActive(false);

            dialogueManager.ShowDialogue(response.DialogueObject);
        }
    }
}