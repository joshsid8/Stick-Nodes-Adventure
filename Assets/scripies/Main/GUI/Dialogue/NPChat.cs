using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;

namespace scripies.Dialogue
{
    public class NPChat : MonoBehaviour, IDialogueProperties
    {
        public bool isActive = true;
        public float chatRadius;
        public Transform chatPoint;
        [SerializeField] private bool isInRange;

        [Tooltip("This is turned on when the dialogue ui has been activated")]
        public bool isTriggered;

        [Tooltip("Usually for bosses only: the dialogue will activate without the player pressing the interact button")]
        public bool isForced;

        [Tooltip("Will this npc move somewhere else at a certain point?")]
        public bool willTravel;

        public GameObject indicator;
        [HideInInspector] public NPC npc;
        public List<DialogueObject> dialogue = new();
        [SerializeField] private LayerMask pLayer;

        [SerializeField] private bool canTurn;
        [SerializeField] private bool isFacingRight;
        private Transform playerTransform;

        [SerializeField] private bool moveCamToNpc;
        private float chatInterval = 0.5f;
        [SerializeField] private bool canHaveNullList;
        public List<DialogueObject> spokenDialogue = new();
        
        public bool isShopkeeper;
        private ShopKeeper shopKeeper;
        
        // Getters and Setters
        public bool Enabled { get => enabled; set => enabled = value; }
        public DialogueObject[] Dialogue => dialogue.ToArray();
        public bool IsForced { get => isForced; set => isForced = value; }
        public Transform ChatPoint => chatPoint;
        public float ChatRadius => chatRadius;
        public Entity Speaker => npc;
        
        private void Start()
        {
            npc = GetComponent<Identifier>().entity as NPC;
            var playerObject = PlayerHealth.Instance.gameObject;
            if (playerObject != null) playerTransform = playerObject.transform;
            if (isShopkeeper) shopKeeper = GetComponent<ShopKeeper>();
            
            if (dialogue.Count == 0 && !canHaveNullList) throw new ArgumentNullException($"[NPChat] WWHHHAT?! {gameObject.name} HAS NO DIALOGUE!!! HOWWW");
        }

        private void LateUpdate()
        {
            var inputDirection = Input.GetAxisRaw("Vertical");
            if (chatInterval <= 0) isInRange = Physics2D.OverlapCircle(chatPoint.position, chatRadius, pLayer);
            if (!DialogueManager.Instance.dialogueBox.activeInHierarchy && isTriggered) isTriggered = false;
            chatInterval -= Time.deltaTime;
            
            if (!isActive) return;
            if (isInRange && !isTriggered)
            {
                if (canTurn) FacePlayer();
                if (indicator != null) indicator.SetActive(true);
                if (!isForced && inputDirection > 0 && !isTriggered || isForced && !isTriggered)
                {
                    isTriggered = true;
                    isForced = false;
                    // '^' means "from the end of a list datatype" WHICH IS SO COOL
                    if (dialogue.Count >= 1)
                    {
                        var priorityDialogue = dialogue[^1];
                        if (priorityDialogue != null)
                        {
                            DialogueManager.Instance.ShowDialogue(priorityDialogue, npc);
                            if (!spokenDialogue.Contains(priorityDialogue) && !priorityDialogue.respeakable) spokenDialogue.Add(priorityDialogue);
                            if (priorityDialogue.PendingChoice) dialogue.Remove(priorityDialogue);
                        }
                    }
                    else if (isShopkeeper && !ShopManager.Instance.isActive)
                    {
                        shopKeeper.StartShop();
                    }
                    if (moveCamToNpc && DialogueManager.Instance.isActive) CameraEventManager.Instance.ChangeTarget(gameObject);
                    CameraEventManager.Instance.ResetCamera();
                }
            }
            else if (indicator != null) indicator.SetActive(false);
        }
        
        public void AddDialogue(ScriptableObject dialogueObject, GameObject speaker = null)
        {
            if (speaker != gameObject || spokenDialogue.Contains(dialogueObject as DialogueObject)) return;
            if (!dialogue.Contains(dialogueObject as DialogueObject)) dialogue.Add(dialogueObject as DialogueObject);
            //else print($"Tried to add some existing dialogue ({dialogueObject.name}) to {gameObject.name}");
            StartCoroutine(SaveGame());
        }

        private IEnumerator SaveGame(float time = 0.2f)
        {
            yield return new WaitForSeconds(time);
            if (MapManager.Instances.TryGetValue(gameObject.scene.name, out var mm)) mm.UpdateLists();
        }
        
        public void ClearDialogue() => dialogue.Clear();
        
        private void OnEnable()
        {
            GameEvents.OnToggleDialogueTrigger += ToggleDialogueTrigger;
            GameEvents.AddDialogue += AddDialogue;
            GameEvents.RemoveDialogue += RemoveDialogue;
            GameEvents.OnDialogueClose += RemoveSpokenDialogue;
        }
        private void OnDisable()
        {
            GameEvents.OnToggleDialogueTrigger -= ToggleDialogueTrigger;
            GameEvents.AddDialogue -= AddDialogue;
            GameEvents.RemoveDialogue -= RemoveDialogue;
            GameEvents.OnDialogueClose -= RemoveSpokenDialogue;
        }
        
        private void ToggleDialogueTrigger(GameObject obj, bool state)
        {
            if (obj == gameObject) isActive = state;
        }

        public void RemoveDialogue(ScriptableObject dialogueObject, GameObject speaker = null)
        {
            if (speaker != gameObject || dialogueObject == null) return;
            if (!dialogue.Contains(dialogueObject as DialogueObject) || dialogue.Count <= 1 && !canHaveNullList) return;
            StartCoroutine(RemoveDialogue(dialogueObject as DialogueObject));
            chatInterval = 0.5f;
        }
        
        private void RemoveSpokenDialogue(ScriptableObject dialogueToRemove)
        {
            if (dialogueToRemove == null) return;
            var dto = dialogueToRemove as DialogueObject;
            if (dto == null) return;
            if (!spokenDialogue.Contains(dto) && dialogue.Contains(dto) && !dto.respeakable) spokenDialogue.Add(dto);
            if (dialogue.Count <= 1 && !canHaveNullList || !dialogue.Contains(dto)) return;
            StartCoroutine(RemoveDialogue(dto));
            chatInterval = 0.5f;
        }

        private IEnumerator RemoveDialogue(DialogueObject dto)
        {
            yield return new WaitForSeconds(0.01f);
            dialogue.RemoveAt(dialogue.IndexOf(dto));
            StartCoroutine(SaveGame());
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(chatPoint.position, chatRadius);
        }

        private void FacePlayer()
        {
            var directionToPlayer = playerTransform.position.x - transform.position.x;
            if (directionToPlayer > 0 && !isFacingRight || directionToPlayer < 0 && isFacingRight) Flip();
        }

        private void Flip()
        {
            isFacingRight = !isFacingRight;
            transform.Rotate(0.0f, 180.0f, 0.0f);
        }
    }
}