using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;

namespace scripies.Dialogue
{
    public class DialogueTrigger : MonoBehaviour, IDialogueTrigger
    {
        public bool isActive = true;
        public float chatRadius;
        public Transform chatPoint;
        [SerializeField] private bool isInRange;

        [Tooltip("This is turned on when the dialogue ui has been activated")]
        public bool isTriggered;

        [Tooltip("Usually for bosses only: the dialogue will activate without the player pressing the talk button")]
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
        }

        private void Update()
        {
            var inputDirection = Input.GetAxisRaw("Vertical");
            
            if (!isActive) return;
            isInRange = Physics2D.OverlapCircle(chatPoint.position, chatRadius, pLayer);
            if (isInRange && !isTriggered)
            {
                if (canTurn) FacePlayer();
                if (indicator != null) indicator.SetActive(true);
                if (!isForced && inputDirection > 0 && !isTriggered || isForced && !isTriggered)
                {
                    isTriggered = true;
                    isForced = false;
                    if (dialogue != null)
                    {
                        // '^' means "from the end of a list datatype" WHICH IS SO COOL
                        var priorityDialogue = dialogue[^1];
                        DialogueUI.Instance.ShowDialogue(priorityDialogue, npc);
                        if (dialogue.Count > 1) dialogue.RemoveAt(dialogue.IndexOf(priorityDialogue));
                    }
                }
            }
            else if (indicator != null) indicator.SetActive(false);

            if (!DialogueUI.Instance.dialogueBox.activeInHierarchy && isTriggered) isTriggered = false;
        }
        
        public void AddDialogue(ScriptableObject dialogueObject, GameObject speaker = null)
        {
            if (speaker != gameObject) return;
            
            if (!dialogue.Contains(dialogueObject as DialogueObject)) dialogue.Add(dialogueObject as DialogueObject);
            else Debug.LogWarning($"Tried to add some existing dialogue ({dialogueObject.name}) to {gameObject.name}");
        }

        public void ClearDialogue() => dialogue.Clear();
        
        private void OnEnable()
        {
            GameEvents.OnToggleDialogueTrigger += ToggleDialogueTrigger;
            GameEvents.AddDialogue += AddDialogue;
        }
        private void OnDisable()
        {
            GameEvents.OnToggleDialogueTrigger -= ToggleDialogueTrigger;
            GameEvents.AddDialogue -= AddDialogue;
        }
        
        private void ToggleDialogueTrigger(GameObject obj, bool state)
        {
            if (obj == gameObject) isActive = state;
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