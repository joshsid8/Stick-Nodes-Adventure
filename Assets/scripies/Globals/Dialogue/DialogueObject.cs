using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Dialogue Object")]
public class DialogueObject : ScriptableObject
{
    [SerializeField] [TextArea] private string npcName;
    [SerializeField] [TextArea] private string[] dialogue;
    [SerializeField] private bool pendingChoice;
    [SerializeField] [TextArea] private string choiceQuestion;
    [SerializeField] private bool isAnimated;

    public DialogueResponse yesResponse;
    public DialogueResponse noResponse;

    public string[] Dialogue => dialogue;
    public string NPCName => npcName;
    public bool PendingChoice => pendingChoice;
    public string ChoiceQuestion => choiceQuestion;
    
    public DialogueResponse YesResponse => yesResponse;
    public DialogueResponse NoResponse => noResponse;
    public bool IsAnimated => isAnimated;
    
    [Header("Custom Global Variable Change (Without a response)")]
    [Tooltip("Check if this response should change a global float variable.")]
    public bool appliesFloatChange;
    [Tooltip("The exact name of the float variable in GameManager to change (e.g., 'PlayerMaxHealth'). Case-sensitive.")]
    public string targetFloatVariableName;
    public float newFloatValue;

    [Space(10)] // Adds some space in the inspector
    [Tooltip("Check if this response should change a global boolean variable.")]
    public bool appliesBooleanChange;
    [Tooltip("The exact name of the boolean variable in GameManager to change (e.g., 'PlayerCanDoubleJump', 'PlayerHasDashAbility'). Case-sensitive.")]
    public string targetBooleanVariableName;
    public bool newBooleanValue;
}