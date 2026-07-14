using System;
using System.Collections;
using UnityEngine;
using TMPro;

public class DialogueUI : GUIBase
{
    public static DialogueUI Instance {get; private set;}
    
    public GameObject dialogueBox;
    [SerializeField] private TMP_Text textLabel;
    [SerializeField] private TMP_Text npcNameLabel;
    private TypeWriterFX typeWriter;
    private DialogueResponseHandler handler; // Already exists
    private GameObject playerBars;
    public bool isAnimated;
    public Animator animator;
    [SerializeField] int textIndex;
    [SerializeField] int currentColourIndex;
    private bool isBold;
    [SerializeField] int currentBoldIndex;
    
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
        playerBars = GameObject.Find("PlayerBars");
        CloseDialogue(null);
    }

    //private void Update() {textLabel.fontStyle = isBold ? FontStyles.Bold : FontStyles.Normal; Cluelessness.Instance.CluelessF(ActiveGUICount, "ActiveGUICount" ,1);}
    
    public void ShowDialogue(DialogueObject dialogueObject)
    {
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
        foreach (string dialogue in dialogueObject.Dialogue)
        {
            npcNameLabel.text = dialogueObject.NPCName;
            npcNameLabel.color = dialogueObject.nameColour;
            if (dialogueObject.canBeColoured && currentColourIndex <= textIndex) textLabel.color = dialogueObject.colour[currentColourIndex];
            else if (!dialogueObject.canBeColoured) textLabel.color = Color.black;
            if (dialogueObject.canBeColoured) currentColourIndex++;
            if (dialogueObject.canBeBold) isBold = dialogueObject.bold[currentBoldIndex];
            else isBold = false;
            if (currentBoldIndex < textIndex || dialogueObject.canBeBold) currentBoldIndex++;
            animator = isAnimated ? GameObject.Find(dialogueObject.speaker).GetComponent<Animator>() : null;
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
        if (dialogueObject.isQuestInteraction && dialogueObject.questToOffer.questType == Quest.QuestType.GiveTo)
        {
            if (InvManager.Instance == null || InvManager.Instance.invIndex == null) Debug.LogError("Quest Check: InvManager or its Index is not ready!");
            else
            {
                var needs = new (CollectableSOBase id, int count)[dialogueObject.questToOffer.objectsToInteract.Length];
                for (var i = 0; i < dialogueObject.questToOffer.objectsToInteract.Length; i++)
                    needs[i] = (dialogueObject.questToOffer.objectsToInteract[i], dialogueObject.questToOffer.numberOfObjects[i]);
                
                var hasItems = InvManager.Instance.invIndex.MeetsAllCounts(needs);
                if (!hasItems)
                {
                    if (dialogueObject.itemCheckFailureDialogue != null) 
                        StartCoroutine(StepThroughDialogue(dialogueObject.itemCheckFailureDialogue));
                    else
                    {
                        Debug.LogWarning("Player failed item check, but no failure dialogue is set.");
                        CloseDialogue(null);
                    }
                }
                else 
                {
                    var slots = QuestLogManager.Instance.questSlots;
                    foreach (var slot in slots)
                    {
                        if (slot != null && slot.questType == Quest.QuestType.GiveTo && slot.nTi == dialogueObject.derrivedDialogue)
                            slot.questCompleted = true;
                    }
                }
            }
        }
    }
    public void DecrementActiveGUICount() => ActiveGUICount--;
    private void CloseDialogue(DialogueObject dialogueObject)
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
        
        if (dialogueObject != null && dialogueObject.isQuestInteraction && dialogueObject.questToOffer.questType == Quest.QuestType.SpeakTo &&
            dialogueObject.completeSpeakQuestOnClose)
        {
            var slots = QuestLogManager.Instance.questSlots;
            for (var i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].questType == Quest.QuestType.SpeakTo && slots[i].nTi == dialogueObject) slots[i].questCompleted = true;
        }
        
        if (dialogueObject != null)
        {
            if (dialogueObject.alteredDialogue != null)
                GameObject.Find(dialogueObject.speaker).GetComponent<DialogueTrigger>().dialogue[0] = dialogueObject.alteredDialogue;
            
            if (dialogueObject.alteredExternalDialogue != null)
                GameObject.Find(dialogueObject.externalInstance).GetComponent<DialogueTrigger>().dialogue[0] =
                    dialogueObject.alteredExternalDialogue;
        }
    }

    private IEnumerator ChoiceDialogue(DialogueObject dialogueObject)
    {
        if (dialogueObject != null)
        {
            npcNameLabel.text = dialogueObject.NPCName;
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
            for (int i = 0; i < dialogueObject.targetFloatVariables.Length; i++)
                PlayerManager.Instance.SetGlobalFloat(dialogueObject.targetFloatVariables[i], dialogueObject.newFloatValues[i]);
        }

        if (dialogueObject.appliesBooleanChange)
        {
            for (int i = 0; i < dialogueObject.targetBoolVariables.Length; i++)
                PlayerManager.Instance.SetGlobalBool(dialogueObject.targetBoolVariables[i], dialogueObject.newBoolValues[i]);
        }

        if (dialogueObject.willGiveQuest) QuestLogManager.Instance.AssignQuest(dialogueObject.questToOffer);
        
        if (dialogueObject.canDropItem)
        {
            for (int i = 0; i < dialogueObject.itemDrop.Length; i++)
            {
                dialogueObject.itemDrop[i].GetComponent<Collectable>().collectable = dialogueObject.itemType[i];
                Instantiate(dialogueObject.itemDrop[i], player.transform.position, Quaternion.identity);
                //print($"Dropped {dialogueObject.itemDrop[i]}, with the type of {dialogueObject.itemType[i]}.");
            }
        }
    }
}
