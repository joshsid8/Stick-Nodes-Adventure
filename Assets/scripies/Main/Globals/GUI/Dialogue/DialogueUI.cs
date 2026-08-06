using System;
using System.Collections;
using scripies.Core;
using UnityEngine;
using TMPro;

namespace scripies.Dialogue
{
    public class DialogueUI : GUIBase
    {
        public static DialogueUI Instance { get; private set; }
        public static event Action<NPC> OnDialogueClose;

        public GameObject dialogueBox;
        [SerializeField] private TMP_Text textLabel;
        [SerializeField] private TMP_Text npcNameLabel;
        private TypeWriterFX typeWriter;
        private DialogueResponseHandler handler;
        public GameObject playerBars;
        public bool isAnimated;
        public Animator animator;
        private NPC currentNpc;
        [SerializeField] private int textIndex;
        [SerializeField] private int currentColourIndex;
        private bool isBold;
        [SerializeField] private int currentBoldIndex;

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            base.Awake();

            typeWriter = GetComponent<TypeWriterFX>();
            handler = GetComponent<DialogueResponseHandler>();
            rt = GetComponent<RectTransform>();
            CloseDialogue();
        }

        private void OnEnable()
        {
            GameEvents.ShowDialogue += ShowDialogue;
            GameEvents.CloseDialogue += ForceCloseDialogue;
        }
        private void OnDisable()
        {
            GameEvents.ShowDialogue -= ShowDialogue;
            GameEvents.CloseDialogue -= ForceCloseDialogue;
        }

        private void Update() => textLabel.fontStyle = isBold ? FontStyles.Bold : FontStyles.Normal;

        public void ShowDialogue(ScriptableObject dO, NPC npc = null)
        {
            var dialogueObject = dO as DialogueObject;
            if (dialogueObject == null) return;
            if (npc != null) currentNpc = npc;
            CloseAll();
            playerBars.SetActive(false);
            isAnimated = dialogueObject.IsAnimated;
            dialogueBox.SetActive(true);
            gameObject.SetActive(true);
            GetActive(true);
            textIndex = dialogueObject.colourIndex;
            currentColourIndex = 0;
            currentBoldIndex = 0;
            QuestHandling(dialogueObject);

            StartCoroutine(StepThroughDialogue(dialogueObject));
        }

        private IEnumerator StepThroughDialogue(DialogueObject dialogueObject)
        {
            foreach (var dialogue in dialogueObject.Dialogue)
            {
                npcNameLabel.text = dialogueObject.NPCName;
                npcNameLabel.color = dialogueObject.nameColour;
                if (dialogueObject.canBeColoured && currentColourIndex <= textIndex)
                    textLabel.color = dialogueObject.colour[currentColourIndex];
                else if (!dialogueObject.canBeColoured) textLabel.color = Color.black;
                if (dialogueObject.canBeColoured) currentColourIndex++;
                if (dialogueObject.canBeBold) isBold = dialogueObject.bold[currentBoldIndex];
                else isBold = false;
                if (currentBoldIndex < textIndex || dialogueObject.canBeBold) currentBoldIndex++;
                // pretty expensive method, maybe refactor this
                animator = isAnimated ? GameObject.Find(currentNpc.npcName ?? dialogueObject.speaker
                    ?? dialogueObject.NPCName).GetComponent<Animator>() : null;
                yield return typeWriter.Run(dialogue, textLabel, npcNameLabel);
                yield return new WaitUntil(() => Input.GetButtonDown("Submit"));
            }

            if (dialogueObject.PendingChoice) StartCoroutine(ChoiceDialogue(dialogueObject));
            else
            {
                ApplyCustomEffect(dialogueObject);
                CloseDialogue(dialogueObject);
            }
        }

        private void QuestHandling(DialogueObject dialogueObject)
        {
            if (dialogueObject.isQuestInteraction && dialogueObject.questToOffer != null)
            {
                var quest = dialogueObject.questToOffer as QuestSO;
                if (InvManager.Instance == null || InvManager.Instance.invIndex == null || quest == null)
                    Debug.LogError("Quest Check: InvManager or its Index is not ready... or the quest is null.");
                else
                {
                    var needs = new (CollectableSOBase id, int count)[quest.objectsToInteract.Length];
                    for (var i = 0; i < quest.objectsToInteract.Length; i++)
                        needs[i] = (quest.objectsToInteract[i], quest.numberOfObjects[i]);
                    var hasItems = InvManager.Instance.invIndex.MeetsAllCounts(needs);
                    if (!hasItems)
                    {
                        if (dialogueObject.itemCheckFailureDialogue != null)
                            StartCoroutine(StepThroughDialogue(dialogueObject.itemCheckFailureDialogue));
                        else
                        {
                            Debug.LogWarning("Player failed item check, but no failure dialogue is set.");
                            CloseDialogue();
                        }
                    }
                    else
                    {
                        var slots = QuestLogManager.Instance.questSlots;
                        foreach (var slot in slots)
                        {
                            if (slot != null && slot.questType == Quest.QuestType.GiveTo && slot.nTi == currentNpc)
                                slot.questCompleted = true;
                        }
                    }
                }
            }
        }

        public void DecrementActiveGUICount() => ActiveGUICount--;
        
        private void ForceCloseDialogue() => CloseDialogue();
        
        private void CloseDialogue(DialogueObject dialogueObject = null)
        {
            OnDialogueClose?.Invoke(currentNpc);
            if (dialogueObject != null)
            {
                if (dialogueObject.alteredDialogue != null)
                    GameObject.Find(currentNpc.npcName ?? dialogueObject.speaker ?? dialogueObject.NPCName)
                        .GetComponent<DialogueTrigger>().AddDialogue(dialogueObject.alteredDialogue);

                if (dialogueObject.alteredExternalDialogue != null) StartCoroutine(ChangeDialogues(dialogueObject));
            }
            GameEvents.TriggerDialogueClose(dialogueObject);
            ClearData();
        }

        private IEnumerator ChangeDialogues(DialogueObject dialogueObject)
        {
            GameObject externalInstance = null;
            
            while (externalInstance == null)
            {
                print("waiting for our instance");
                externalInstance = GameObject.Find(dialogueObject.externalInstance);
                yield return null;
            }
            print("found our instance, changing their dialogue");
            externalInstance.GetComponent<DialogueTrigger>().AddDialogue(dialogueObject.alteredExternalDialogue);
        }

        private void ClearData()
        {
            if (isAnimated)
            {
                isAnimated = false;
                animator = null;
            }

            typeWriter.actionFactor = 0;
            dialogueBox.SetActive(false);
            GetActive(false);
            textLabel.text = String.Empty;
            playerBars.SetActive(true);
            textLabel.color = Color.black;
            npcNameLabel.color = Color.black;
            currentNpc = null;
        }

        private IEnumerator ChoiceDialogue(DialogueObject dialogueObject = null)
        {
            if (dialogueObject != null)
            {
                npcNameLabel.text = dialogueObject.NPCName ?? dialogueObject.speaker ?? currentNpc.npcName ?? "HUH?! WHERE IS EVERYTHING?!";
                yield return typeWriter.Run(dialogueObject.ChoiceQuestion, textLabel, npcNameLabel);
                handler.ShowResponses(dialogueObject);
            }
            else CloseDialogue(dialogueObject);
        }

        private void ApplyCustomEffect(DialogueObject dialogueObject)
        {
            if (PlayerManager.Instance == null)
            {
                Debug.LogError("GAAAAAHHHHHHH");
                return;
            }

            if (dialogueObject.appliesFloatChange)
            {
                for (var i = 0; i < dialogueObject.targetFloatVariables.Length; i++)
                    PlayerManager.Instance.SetGlobalFloat(dialogueObject.targetFloatVariables[i],
                        dialogueObject.newFloatValues[i]);
            }

            if (dialogueObject.appliesBooleanChange)
            {
                for (var i = 0; i < dialogueObject.targetBoolVariables.Length; i++)
                {
                    PlayerManager.Instance.SetGlobalBool(dialogueObject.targetBoolVariables[i],
                        dialogueObject.newBoolValues[i]);
                }
            }

            if (dialogueObject.willGiveQuest) QuestLogManager.Instance.AssignQuest(dialogueObject.questToOffer as QuestSO);

            if (dialogueObject.canDropItem)
            {
                for (var i = 0; i < dialogueObject.itemDrop.Length; i++)
                {
                    dialogueObject.itemDrop[i].GetComponent<Collectable>().collectable = dialogueObject.itemType[i];
                    Instantiate(dialogueObject.itemDrop[i], player.transform.position, Quaternion.identity);
                    //print($"Dropped {dialogueObject.itemDrop[i]}, with the type of {dialogueObject.itemType[i]}.");
                }
            }
        }
    }
}