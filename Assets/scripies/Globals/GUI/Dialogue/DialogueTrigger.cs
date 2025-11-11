using System;
using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{

    public float chatRadius;
    public Transform chatPoint;
    [SerializeField] private bool isInRange;
    public bool isTriggered;
    public bool isForced;
    public bool willTravel;
    
    public GameObject indicator;
    public DialogueObject dialogue;
    [SerializeField] private LayerMask pLayer;

    [SerializeField] private bool canTurn;
    [SerializeField] private bool isFacingRight;
    private Transform playerTransform;
    
    private void Awake()
    {
        GameObject playerObject = PlayerHealth.Instance.gameObject;
        if (playerObject != null) playerTransform = playerObject.transform;
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
                if (dialogue != null) DialogueUI.Instance.ShowDialogue(dialogue);
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
