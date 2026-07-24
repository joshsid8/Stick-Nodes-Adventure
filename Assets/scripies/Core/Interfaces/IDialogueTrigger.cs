using scripies.Dialogue;
using UnityEngine;

public interface IDialogueTrigger {
    bool Enabled { get; set; }
    DialogueObject[] Dialogue { get; }
    bool IsForced { get; set; }
    Transform ChatPoint { get; }
    float ChatRadius { get; }
    
    void AddDialogue(ScriptableObject dialogue);
}
