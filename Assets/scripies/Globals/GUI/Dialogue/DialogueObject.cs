using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Dialogue Object")]
public class DialogueObject : ScriptableObject
{
    [SerializeField] private string npcName;
    [SerializeField] [TextArea(4, 10)] private string[] dialogue;
    [SerializeField] private bool pendingChoice;
    [SerializeField] [TextArea(1, 2)] private string choiceQuestion;
    [SerializeField] private bool isAnimated;

    public DialogueResponse yesResponse;
    public DialogueResponse noResponse;

    public string[] Dialogue => dialogue;
    public string NPCName => npcName;
    public Color nameColour = Color.black;
    public bool PendingChoice => pendingChoice;
    public string ChoiceQuestion => choiceQuestion;
    
    public DialogueResponse YesResponse => yesResponse;
    public DialogueResponse NoResponse => noResponse;
    public bool IsAnimated => isAnimated;
    
    [Header("Custom Global Variable Change (once the Dialogue closes)")] 
    [Tooltip("Check if this response should change a global float variable.")]
    public bool appliesFloatChange;
    [Tooltip("The exact name of the float variable in GameManager to change (e.g., 'PlayerHealth')")]
    public string targetFloatVariableName;
    public float newFloatValue;

    public string[] targetFloatVariables;
    public float[] newFloatValues;
    
    [Space(10)] [Tooltip("Check if this response should change a global boolean variable.")]
    public bool appliesBooleanChange;
    [Tooltip("The exact name of the boolean variable in GameManager to change  (mostly from the Shenanigans script) ")]
    public string targetBooleanVariableName;
    public bool newBooleanValue;

    public string[] targetBoolVariables;
    public bool[] newBoolValues;

    [Space(10)] [Header("Questing")] [Tooltip("Will this NPC assign a quest for the player?")]
    public bool willGiveQuest;

    public QuestSO questToOffer;

    [Space(10)] [Header("Item Drops")] [Tooltip("How many of x items the speaker should give")]
    public bool canDropItem;
    public GameObject[] itemDrop;
    public CollectableSOBase[] itemType;

    [Header("Colouring & Font Changes")] [Tooltip("The colour of each text")]
    public bool canBeColoured;
    public Color[] colour;
    public int colourIndex;
    public bool canBeBold;
    public bool[] bold;

    [Header("NPC Control")] [Tooltip("Who is speaking?")] public string speaker;
    public DialogueObject alteredDialogue;
    
    [Tooltip("Anything else needs to be affected by this dialogue?")] public string externalInstance;
    public DialogueObject alteredExternalDialogue;
}