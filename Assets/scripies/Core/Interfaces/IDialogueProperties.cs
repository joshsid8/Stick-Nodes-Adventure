using UnityEngine;

namespace scripies.Core
{
    public interface IDialogueProperties
    {
        bool Enabled { get; set; }
        Dialogue.DialogueObject[] Dialogue { get; }
        bool IsForced { get; set; }
        Transform ChatPoint { get; }
        float ChatRadius { get; }
        NPC Speaker { get; }

        void AddDialogue(ScriptableObject dialogue, GameObject speaker = null);
        void RemoveDialogue(ScriptableObject dialogue, GameObject speaker = null);
        void ClearDialogue();
    }
}
