using UnityEngine;

namespace scripies.Dialogue
{
    [System.Serializable]
    public class DialogueResponse
    {
        [SerializeField] private string responseText;
        [SerializeField] private DialogueObject dialogueObject; // The next dialogue to show
        public string ResponseText => responseText;
        public DialogueObject DialogueObject => dialogueObject;
    }
}