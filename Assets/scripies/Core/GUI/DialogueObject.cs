using System;
using System.Collections.Generic;
using UnityEngine;

namespace scripies.Dialogue
{
    [CreateAssetMenu(menuName = "Dialogue/Dialogue Object")]
    public class DialogueObject : ScriptableObject
    {
        [SerializeField] private string npcName;
        [SerializeField]
        public DialogueThread[] dialogueThread =
        {
            new()
            {
                thread = "Huh?",
                dialogueColour = Color.black,
                isBold = false,
                useCameraEvents = false,
                disablePreviousEvents = false,
                cameraDistance = 10,
                zoomSpeed = 1,
                cameraTarget = ""
            }
        };
        [SerializeField] private bool pendingChoice;
        [SerializeField] private bool isAnimated;

        [Header("Responses")] public DialogueResponse yesResponse;
        public DialogueResponse noResponse;
        
        public DialogueThread[] DialogueThread => dialogueThread;
        public string NPCName => npcName;
        public Color nameColour = Color.black;
        public bool PendingChoice => pendingChoice;

        public DialogueResponse YesResponse => yesResponse;
        public DialogueResponse NoResponse => noResponse;

        public bool IsAnimated => isAnimated;
        
        [Header("Custom Global Variable Change (once the Dialogue closes)")]
        public List<ApplyBool> applyBools = new();
        public List<ApplyFloat> applyFloats = new();

        [Space(10)] [Header("Questing")]
        public bool willGiveQuest;
        public ScriptableObject questToOffer;
        [Tooltip("If the player has a GiveTo quest, but doesnt have the right items, this dialogue will be shown instead")]
        public DialogueObject itemCheckFailureDialogue;
        
        [Header("Item Drops & Camera Events")]
        public CollectableSOBase[] itemType;

        [Header("Colouring & Font Changes")]
        public bool canBeColoured;
        public bool canBeBold;

        [Header("NPC Control")]
        public bool openShopOnClose;
        
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
        [Tooltip("Keep in mind: # = slow down, * = speed up, |/_ = animations 1 to 3, ~ = stop animation")]
        [TextArea(4, 10)]
        public string thread;
        public Color dialogueColour = Color.black;
        public bool isBold;

        public bool useCameraEvents;
        public bool disablePreviousEvents;
        
        public float cameraDistance;
        public float zoomSpeed;
        
        public string cameraTarget;
        public enum LookupType {RawName, Tag, Section, Spawner}
        [EnumButtons] public LookupType cameraLookupType;
    }
    [Serializable]
    public class ApplyFloat
    {
        [Tooltip("(e.g., 'PlayerHealth')")]
        public string floatName;
        public float value;
        public bool additive;
    }
    
    [Serializable]
    public class ApplyBool
    {
        [Tooltip("(e.g., 'CanWallJump')")]
        public string boolName;
        public bool value;
    }
}