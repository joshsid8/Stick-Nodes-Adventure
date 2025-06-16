using UnityEngine;

[System.Serializable]
public class DialogueResponse
{
    [SerializeField] private string responseText;
    [SerializeField] private DialogueObject dialogueObject; // The next dialogue to show

    [Header("Custom Global Variable Change")]
    [Tooltip("Check if this response should change a global float variable.")]
    public bool appliesFloatChange;
    [Tooltip("The exact name of the float variable in GameManager to change (e.g., 'PlayerMaxHealth', 'EnemyHealthMultiplier'). Case-sensitive.")]
    public string targetFloatVariableName;
    public float newFloatValue;

    [Space(10)] // Adds some space in the inspector
    [Tooltip("Check if this response should change a global boolean variable.")]
    public bool appliesBooleanChange;
    [Tooltip("The exact name of the boolean variable in GameManager to change (e.g., 'PlayerCanDoubleJump', 'PlayerHasDashAbility'). Case-sensitive.")]
    public string targetBooleanVariableName;
    public bool newBooleanValue;
    
    public string ResponseText => responseText;
    public DialogueObject DialogueObject => dialogueObject;
}