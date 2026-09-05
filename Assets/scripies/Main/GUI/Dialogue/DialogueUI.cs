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
        public GameObject gameHUD;
        public bool isAnimated;
        public Animator animator;
        private NPC currentNpc;
        private bool isBold;
        
        [SerializeField] private AudioClip[] dialoguePings;
        private GameObject persistentItem;

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
            if (isActive) CloseDialogue();
            
            persistentItem = GameObject.Find("persistentItem");
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
            if (dialogueObject == null)
            {
                Debug.LogError("There isnt a dialogue object to show!");
                CloseDialogue();
                return;
            }
            if (npc != null) currentNpc = npc;
            CloseAll();
            gameHUD.SetActive(false);
            isAnimated = dialogueObject.IsAnimated;
            dialogueBox.SetActive(true);
            gameObject.SetActive(true);
            GetActive(true);

            QuestHandling(dialogueObject);
            StartCoroutine(StepThroughDialogue(dialogueObject));
        }

        private IEnumerator StepThroughDialogue(DialogueObject dialogueObject)
        {
            npcNameLabel.text = dialogueObject.NPCName;
            npcNameLabel.color = dialogueObject.nameColour;
            animator = isAnimated ? GameObject.Find(currentNpc.npcName ?? dialogueObject.speaker ?? dialogueObject.NPCName).GetComponent<Animator>() : null;
            if (dialoguePings.Length > 0) GameEvents.TriggerPlaySound(dialoguePings?[0], false);
            if (!dialogueObject.canBeColoured) textLabel.color = Color.black;
            if (!dialogueObject.canBeBold) isBold = false;
            
            foreach (var dialogue in dialogueObject.DialogueThread)
            {
                npcNameLabel.text = dialogueObject.NPCName;
                npcNameLabel.color = dialogueObject.nameColour;

                if (dialogueObject.canBeColoured) textLabel.color = dialogue.dialogueColour;
                if (dialogueObject.canBeBold) isBold = dialogue.isBold;

                if (dialogue.useCameraEvents)
                {
                    CameraEventManager.Instance.ZoomCamera(dialogue.cameraDistance, dialogue.zoomSpeed);
                    GameObject target = null;
                    switch (dialogue.cameraLookupType)
                    {
                        case DialogueThread.LookupType.RawName:
                            target = GameObject.Find(dialogue.cameraTarget ?? dialogue.cameraTarget + " (Clone)");
                            break;
                        case DialogueThread.LookupType.Tag:
                            target = GameObject.FindGameObjectWithTag(dialogue.cameraTarget);
                            break;
                        case DialogueThread.LookupType.Section:
                            var allSects = GameObject.FindGameObjectsWithTag("Section");
                            foreach (var sect in allSects)
                            {
                                if (sect.name.Contains(dialogue.cameraTarget))
                                {
                                    target = sect;
                                    break;
                                }
                            }
                            break;
                        case DialogueThread.LookupType.Spawner:
                            var spawners = GameObject.FindGameObjectsWithTag("Spawner");
                            foreach (var spawner in spawners)
                            {
                                if (spawner.name.Contains(dialogue.cameraTarget))
                                {
                                    target = spawner;
                                    break;
                                }
                            }
                            break;
                        default:
                            throw new ArgumentException($"[DialogueUI Camera Event] How did u even make the type as {dialogue.cameraLookupType}??");
                    }
                    if (target == null) throw new NullReferenceException($"[DialogueUI Camera Event] We couldnt find {dialogue.cameraTarget} nowhere!");
                    CameraEventManager.Instance.ChangeTarget(target);
                }
                else if (dialogue.disablePreviousEvents) CameraEventManager.Instance.ResetCamera();
                
                yield return typeWriter.Run(dialogue.thread, textLabel, npcNameLabel);
                if (dialogueObject.PendingChoice)
                {
                    var lastDialogue = dialogueObject.DialogueThread[^1];
                    if (dialogue != lastDialogue) yield break; 
                    StartCoroutine(ChoiceDialogue(dialogueObject));
                    yield return new WaitUntil(() => !handler.responseBox.gameObject.activeInHierarchy);
                }
                else yield return new WaitUntil(() => Input.GetButtonDown("Submit"));
            }
            ApplyCustomEffect(dialogueObject);
            CloseDialogue(dialogueObject);
        }

        private void QuestHandling(DialogueObject dialogueObject)
        {
            if (dialogueObject.questToOffer == null) return;
            
            var quest = dialogueObject.questToOffer as QuestSO;
            if (InvManager.Instance == null || InvManager.Instance.invIndex == null || quest == null)
                Debug.LogError("[Quest Check] InvManager or its Index is not ready... or the quest is null.");
            else
            {
                switch (quest.questType)
                {
                    case Quest.QuestType.GiveTo:
                        var needs = new (CollectableSOBase id, int count)[quest.objectsToInteract.Length];
                        for (var i = 0; i < quest.objectsToInteract.Length; i++) needs[i] = (quest.objectsToInteract[i], quest.numberOfObjects[i]);
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
                                if (slot != null && slot.nTi == currentNpc)
                                {
                                    slot.questCompleted = true;
                                    //StartCoroutine(StepThroughDialogue(dialogueObject));
                                    // Where dialogueObject is the quest completion dialogue
                                }
                            }
                        }
                        break;
                }
            }
        }

        public void DecrementActiveGUICount() => ActiveGUICount--;
        
        private void ForceCloseDialogue()
        {
            if (isActive) CloseDialogue();
        }
        
        private void CloseDialogue(DialogueObject dialogueObject = null)
        {
            OnDialogueClose?.Invoke(currentNpc);
            if (dialogueObject != null)
            {
                if (dialogueObject.openShopOnClose)
                {
                    var instance = GameObject.Find(currentNpc.npcName ?? dialogueObject.speaker ?? dialogueObject.NPCName);
                    if (instance != null && instance.TryGetComponent(out ShopKeeper shopKeeper)) 
                        shopKeeper.StartShop();
                }

                if (dialogueObject.alteredDialogue != null)
                {
                    var instance = GameObject.Find(currentNpc.npcName ?? dialogueObject.speaker ?? dialogueObject.NPCName);
                    if (instance != null) GameEvents.TriggerAddDialogue(dialogueObject.alteredDialogue, instance);
                }
                if (dialogueObject.alteredExternalDialogue != null) StartCoroutine(ChangeDialogues(dialogueObject));

                CameraEventManager.Instance.ResetCamera();
            }
            GameEvents.TriggerDialogueClose(dialogueObject);
            //GameEvents.TriggerPlaySound(dialoguePings[1], false);
            ClearData();
        }

        private static IEnumerator ChangeDialogues(DialogueObject dialogueObject)
        {
            GameObject externalInstance = null;
            
            while (externalInstance == null)
            {
                externalInstance = GameObject.Find(dialogueObject.externalInstance);
                yield return null;
            }
            //print("found our instance");
            yield return new WaitForSeconds(1);
            GameEvents.TriggerAddDialogue(dialogueObject.alteredExternalDialogue, externalInstance);
        }

        private void ClearData()
        {
            if (isAnimated)
            {
                isAnimated = false;
                animator = null;
            }

            typeWriter.actionFactor = 0;
            textLabel.text = string.Empty;
            gameHUD.SetActive(true);
            textLabel.color = Color.black;
            npcNameLabel.color = Color.black;
            currentNpc = null;
            dialogueBox.SetActive(false);
            GetActive(false);
        }

        private IEnumerator ChoiceDialogue(DialogueObject dialogueObject = null)
        {
            if (dialogueObject != null)
            {
                //npcNameLabel.text = dialogueObject.NPCName ?? dialogueObject.speaker ?? currentNpc.npcName ?? "HUH?! WHERE IS EVERYTHING?!";
                //yield return typeWriter.Run(dialogueObject.DialogueThread[^1].thread, textLabel, npcNameLabel);
                handler.ShowResponses(dialogueObject);
            }
            else CloseDialogue(dialogueObject);
            yield return null;
        }

        private void ApplyCustomEffect(DialogueObject dialogueObject)
        {
            if (PlayerManager.Instance == null)
            {
                Debug.LogError("GAAAAAHHHHHHH");
                return;
            }

            if (dialogueObject.applyFloats.Count > 0)
            {
                /*for (var i = 0; i < dialogueObject.targetFloatVariables.Length; i++)
                    PlayerManager.Instance.SetGlobalFloat(dialogueObject.targetFloatVariables[i],
                        dialogueObject.newFloatValues[i]);*/
                
                foreach (var floatToChange in dialogueObject.applyFloats) 
                    PlayerManager.Instance.SetGlobalFloat(floatToChange.floatName, floatToChange.value, floatToChange.additive);
            }

            if (dialogueObject.applyBools.Count > 0)
            {
                /*for (var i = 0; i < dialogueObject.targetBoolVariables.Length; i++)
                {
                    PlayerManager.Instance.SetGlobalBool(dialogueObject.targetBoolVariables[i],
                        dialogueObject.newBoolValues[i]);
                }*/
                
                foreach (var boolToChange in dialogueObject.applyBools) 
                    PlayerManager.Instance.SetGlobalBool(boolToChange.boolName, boolToChange.value);
            }

            if (dialogueObject.willGiveQuest) QuestLogManager.Instance.AssignQuest(dialogueObject.questToOffer as QuestSO);

            if (dialogueObject.itemType.Length > 0)
            {
                foreach (var type in dialogueObject.itemType)
                {
                    var item = Instantiate(persistentItem, player.transform.position, Quaternion.identity);
                    item.GetComponent<Collectable>().collectable = type;
                    //print($"Dropped {dialogueObject.itemDrop[i]}, with the type of {dialogueObject.itemType[i]}.");
                }
            }
        }
    }
}