using UnityEngine;

namespace scripies.Core
{
    public interface IDialogueTrigger
    {
        bool Enabled { get; set; }
        Dialogue.DialogueObject[] Dialogue { get; }
        bool IsForced { get; set; }
        UnityEngine.Transform ChatPoint { get; }
        float ChatRadius { get; }

        void AddDialogue(ScriptableObject dialogue, GameObject speaker = null);
        void RemoveDialogue(ScriptableObject dialogue, GameObject speaker = null);
        void ClearDialogue();
    }
}
