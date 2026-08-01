namespace scripies.Core
{
    public interface IDialogueTrigger
    {
        bool Enabled { get; set; }
        Dialogue.DialogueObject[] Dialogue { get; }
        bool IsForced { get; set; }
        UnityEngine.Transform ChatPoint { get; }
        float ChatRadius { get; }

        void AddDialogue(UnityEngine.ScriptableObject dialogue);
    }
}
