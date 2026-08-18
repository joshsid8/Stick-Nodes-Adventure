using System;
using UnityEngine;

namespace scripies.Dialogue
{
    [CreateAssetMenu(menuName = "Dialogue/Dialogue Object")]
    public class DialogueObject : ScriptableObject
    {
        [SerializeField] private string npcName;
        [SerializeField] [TextArea(4, 10)]
        [Tooltip("Keep in mind: # = slow down, * = speed up, |/_ = animations 1 to 3, ~ = stop animation")]
        private string[] dialogue;
        public DialogueThread[] dialogueThread;
        [SerializeField] private bool pendingChoice;
        [SerializeField] [TextArea(1, 2)] private string choiceQuestion;
        [SerializeField] private bool isAnimated;

        [Header("Responses")] public DialogueResponse yesResponse;
        public DialogueResponse noResponse;

        public string[] Dialogue => dialogue;
        public DialogueThread[] DialogueThread => dialogueThread;
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

        [Tooltip("The exact names of the float variables in GameManager to change (e.g., 'PlayerHealth')")]
        public string[] targetFloatVariables;

        public float[] newFloatValues;

        [Space(10)] [Tooltip("Check if this response should change a global boolean variable.")]
        public bool appliesBooleanChange;

        [Tooltip("The exact names of the boolean variables in GameManager to change (mostly from the Shenanigans/PlayerManager script) ")]
        public string[] targetBoolVariables;

        public bool[] newBoolValues;

        [Space(10)] [Header("Questing")] [Tooltip("Will this NPC assign a quest for the player?")]
        public bool willGiveQuest;

        public ScriptableObject questToOffer;

        public bool isQuestInteraction;
        public DialogueObject itemCheckFailureDialogue;
        public bool completeSpeakQuestOnClose;

        [Tooltip("If the player has already completed this quest, this dialogue will be shown instead")]
        public DialogueObject derivedDialogue;

        [Space(10)] [Header("Item Drops")] [Tooltip("How many of x items the speaker should give")]
        public bool canDropItem;

        public GameObject[] itemDrop;
        public CollectableSOBase[] itemType;

        [Header("Colouring & Font Changes")] [Tooltip("The colour of each text")]
        public bool canBeColoured;

        public Color[] colour = { Color.black };
        public int colourIndex;
        public bool canBeBold;
        public bool[] bold;

        [Header("NPC Control")] [Tooltip("Who is speaking?")]
        public string speaker;
        [Tooltip("Adds this dialogue to the current speaker. Ensure this is filled if this dialogue is quest based")]
        public DialogueObject alteredDialogue;

        [Tooltip("Anyone else needs to be affected by this dialogue?")]
        public string externalInstance;
        [Tooltip("Adds this dialogue to another npc. Ensure this is filled if the quest type SpeakTo/GiveTo")]
        public DialogueObject alteredExternalDialogue;

    }

    [Serializable]
    public class DialogueThread
    {
        // TODO: Implement!
        public string dialogue;
        public Color dialogueColour = Color.black;
        public bool isBold;
    }
}