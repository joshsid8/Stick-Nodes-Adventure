using System;
using System.Collections;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;

namespace scripies.Dialogue
{
    public class NPChat : MonoBehaviour, IDialogueTrigger
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
        public NPC npc;
        public List<DialogueObject> dialogue = new();
        [SerializeField] private LayerMask pLayer;

        [SerializeField] private bool canTurn;
        [SerializeField] private bool isFacingRight;
        private Transform playerTransform;

        [SerializeField] private bool moveCamToNpc;
        private float chatInterval = 0.2f;
        [SerializeField] private bool canHaveNullList;
        
        public bool isShopkeeper;
        private ShopKeeper shopKeeper;
        
        // Getters and Setters
        public bool Enabled { get => enabled; set => enabled = value; }
        public DialogueObject[] Dialogue => dialogue.ToArray();
        public bool IsForced { get => isForced; set => isForced = value; }
        public Transform ChatPoint => chatPoint;
        public float ChatRadius => chatRadius;
        
        private void Start()
        {
            var playerObject = PlayerHealth.Instance.gameObject;
            if (playerObject != null) playerTransform = playerObject.transform;
            if (isShopkeeper) shopKeeper = GetComponent<ShopKeeper>();
        }

        private void Update()
        {
            var inputDirection = Input.GetAxisRaw("Vertical");
            if (chatInterval <= 0) isInRange = Physics2D.OverlapCircle(chatPoint.position, chatRadius, pLayer);
            if (!DialogueUI.Instance.dialogueBox.activeInHierarchy && isTriggered) isTriggered = false;
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
                    if (dialogue == null) return;
                    // '^' means "from the end of a list datatype" WHICH IS SO COOL
                    var priorityDialogue = dialogue[^1];
                    if (priorityDialogue != null) DialogueUI.Instance.ShowDialogue(priorityDialogue, npc);
                    else if (isShopkeeper) shopKeeper.StartShop();
                    CameraEventManager.Instance.ResetCamera();
                    if (moveCamToNpc) CameraEventManager.Instance.ChangeTarget(gameObject);
                }
            }
            else if (indicator != null) indicator.SetActive(false);
        }
        
        public void AddDialogue(ScriptableObject dialogueObject, GameObject speaker = null)
        {
            if (speaker != gameObject) return;
            
            if (!dialogue.Contains(dialogueObject as DialogueObject)) dialogue.Add(dialogueObject as DialogueObject);
            else print($"Tried to add some existing dialogue ({dialogueObject.name}) to {gameObject.name}");
        }

        public void ClearDialogue() => dialogue.Clear();
        
        private void OnEnable()
        {
            GameEvents.OnToggleDialogueTrigger += ToggleDialogueTrigger;
            GameEvents.AddDialogue += AddDialogue;
            GameEvents.OnDialogueClose += RemoveSpokenDialogue;
        }
        private void OnDisable()
        {
            GameEvents.OnToggleDialogueTrigger -= ToggleDialogueTrigger;
            GameEvents.AddDialogue -= AddDialogue;
            GameEvents.OnDialogueClose -= RemoveSpokenDialogue;
        }
        
        private void ToggleDialogueTrigger(GameObject obj, bool state)
        {
            if (obj == gameObject) isActive = state;
        }

        private void RemoveSpokenDialogue(ScriptableObject dialogueToRemove)
        {
            var dto = dialogueToRemove as DialogueObject;
            
            if (dialogue.Count <= 1 && !canHaveNullList || !dialogue.Contains(dto)) return;
            StartCoroutine(RemoveDialogue(dto));
            chatInterval = 0.2f;
        }

        private IEnumerator RemoveDialogue(DialogueObject dto)
        {
            yield return new WaitForSeconds(0.01f);
            dialogue.RemoveAt(dialogue.IndexOf(dto));
            if (isShopkeeper) shopKeeper.StartShop(); 
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