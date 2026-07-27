using System;
using System.Collections.Generic;
using scripies.Core;
using scripies.Player;
using UnityEngine;

namespace scripies.Dialogue
{
    public class DialogueTrigger : MonoBehaviour, IDialogueTrigger
    {
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
        public List<DialogueObject> dialogue = new List<DialogueObject>();
        [SerializeField] private LayerMask pLayer;

        [SerializeField] private bool canTurn;
        [SerializeField] private bool isFacingRight;
        private Transform playerTransform;
        
        // Getters and Setters
        public bool Enabled { get; set; }
        public DialogueObject[] Dialogue { get; private set; }
        public bool IsForced { get; set; }
        public Transform ChatPoint { get; private set; }
        public float ChatRadius { get; private set;}
        
        private void Awake()
        {
            var playerObject = PlayerHealth.Instance.gameObject;
            if (playerObject != null) playerTransform = playerObject.transform;
        }

        private void Update()
        {
            var inputDirection = Input.GetAxisRaw("Vertical");

            isInRange = Physics2D.OverlapCircle(chatPoint.position, chatRadius, pLayer);
            if (isInRange && !isTriggered)
            {
                if (canTurn) FacePlayer();
                indicator.SetActive(true);
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
            else indicator.SetActive(false);

            if (!DialogueUI.Instance.dialogueBox.activeInHierarchy && isTriggered) isTriggered = false;
            
            // interface refs
            IsForced = isForced;
            ChatPoint = chatPoint;
            ChatRadius = chatRadius;
        }
        
        public void AddDialogue(ScriptableObject dialogueObject) => dialogue.Add(dialogueObject as DialogueObject);
        
        private void OnEnable() => GameEvents.OnToggleDialogueTrigger += ToggleDialogueTrigger;
        private void OnDisable() => GameEvents.OnToggleDialogueTrigger -= ToggleDialogueTrigger;
        
        private void ToggleDialogueTrigger(GameObject obj, bool state)
        {
            if (obj == gameObject) enabled = state;
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