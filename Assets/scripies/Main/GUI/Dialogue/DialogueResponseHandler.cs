using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace scripies.Dialogue
{
    public class DialogueResponseHandler : MonoBehaviour
    {
        [SerializeField] private RectTransform responseBox;
        [SerializeField] private GameObject responseButtonsParent;

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
            dialogueUI.DecrementActiveGUICount();
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

            dialogueUI.ShowDialogue(response.DialogueObject);
        }
    }
}