using System;
using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{

    public float chatRadius;
    public Transform chatPoint;
    [SerializeField] private bool isInRange;
    [SerializeField] private bool isTriggered;
    [SerializeField] private bool isForced;
    
    public GameObject indicator;
    [SerializeField] DialogueObject[] dialogue;
    [SerializeField] private int dialogueNo;
    [SerializeField] private int dialogueNoAmount;
    [SerializeField] private LayerMask pLayer;
    [SerializeField] private GameObject dialogueBox;

    [SerializeField] private bool canTurn;
    [SerializeField] private bool isFacingRight;
    private Transform playerTransform;
    
    
    private DialogueUI dialogueUI;
    
    private void Start()
    {
        dialogueUI = FindAnyObjectByType<DialogueUI>();
        
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
        }
    }

    void Update()
    {
        float inputDirection = Input.GetAxisRaw("Vertical");
        
        isInRange = Physics2D.OverlapCircle(chatPoint.position, chatRadius, pLayer);
        if (isInRange && !isTriggered)
        {
            if (canTurn) FacePlayer();
            indicator.SetActive(true);
            if (!isForced && inputDirection > 0 && !isTriggered || isForced && !isTriggered)
            {
                isTriggered = true;
                isForced = false;
                RollThroughDialogue(dialogueNo);
            }
        }
        else
        {
            indicator.SetActive(false);
        }

        if (!dialogueBox.gameObject.activeInHierarchy && isTriggered) isTriggered = false;
        //if (isTriggered) indicator.SetActive(false);
    }

    private void RollThroughDialogue(int dialogueNumber)
    {
        dialogueUI.ShowDialogue(dialogue[dialogueNumber]);
        if (dialogueBox.gameObject.activeInHierarchy && isTriggered && dialogueNoAmount > 0)
        {
            dialogueNoAmount--;
            dialogueNo++;
            print("incremented");
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(chatPoint.position, chatRadius);
    }
    private void FacePlayer()
    {
        float directionToPlayer = playerTransform.position.x - transform.position.x;

        if (directionToPlayer > 0 && !isFacingRight)
        {
            Flip();
        }
        else if (directionToPlayer < 0 && isFacingRight)
        {
            Flip();
        }
    }

    private void Flip()
    {
        isFacingRight = !isFacingRight;
        transform.Rotate(0.0f, 180.0f, 0.0f);
    }
}
