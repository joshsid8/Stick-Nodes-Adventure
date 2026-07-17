using System;
using System.Collections.Generic;
using UnityEngine;

public class DialogueTrigger : MonoBehaviour
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
                if (dialogue != null) DialogueUI.Instance.ShowDialogue(dialogue[0], npc);
            }
        }
        else indicator.SetActive(false);

        if (!DialogueUI.Instance.dialogueBox.activeInHierarchy && isTriggered) isTriggered = false;
    }

    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(chatPoint.position, chatRadius);
    }
    private void FacePlayer()
    {
        float directionToPlayer = playerTransform.position.x - transform.position.x;

        if (directionToPlayer > 0 && !isFacingRight) Flip();
        else if (directionToPlayer < 0 && isFacingRight) Flip();
    }

    private void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
}
