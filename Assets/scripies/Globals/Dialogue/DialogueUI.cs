using System;
using System.Collections;
using UnityEngine;
using TMPro;

public class DialogueUI : MonoBehaviour
{
    [SerializeField] private GameObject dialogueBox;
    [SerializeField] private TMP_Text textLabel;
    [SerializeField] private TMP_Text npcNameLabel;
    private TypeWriterFX typeWriter;
    private DialogueResponseHandler handler; // Already exists
    private GameObject playerBars;
    public bool isAnimated;
    private PlayerController player;
    [HideInInspector] public Animator animator;
    
    private void Start()
    {
        typeWriter = GetComponent<TypeWriterFX>();
        handler = GetComponent<DialogueResponseHandler>();
        player = GameObject.Find("Player").GetComponent<PlayerController>();
        playerBars = GameObject.Find("PlayerBars");
        CloseDialogue();
    }

    public void ShowDialogue(DialogueObject dialogueObject)
    {
        playerBars.SetActive(false);
        player.canAct = false;
        player.jumpForce = 0f;
        isAnimated = dialogueObject.IsAnimated;
        dialogueBox.SetActive(true);
        StartCoroutine(StepThroughDialogue(dialogueObject));
    }

    private IEnumerator StepThroughDialogue(DialogueObject dialogueObject)
    {
        foreach (string dialogue in dialogueObject.Dialogue)
        {
            npcNameLabel.text = dialogueObject.NPCName;
            animator = isAnimated ? GameObject.Find(npcNameLabel.text).GetComponent<Animator>() : null;
            yield return typeWriter.Run(dialogue, textLabel, npcNameLabel);
            yield return new WaitUntil(() => Input.GetButtonDown("Submit") || Input.GetMouseButton(0));
        }

        if (dialogueObject.PendingChoice)
        {
            StartCoroutine(ChoiceDialogue(dialogueObject));
        }
        else
        {
            ApplyCustomEffect(dialogueObject);
            CloseDialogue();
        }
    }

    private void CloseDialogue()
    {
        if (isAnimated)
        {
            isAnimated = false;
            animator = null;
        }
        typeWriter.actionFactor = 0;
        dialogueBox.SetActive(false);
        textLabel.text = String.Empty;
        if (player != null)
        {
            player.canAct = true;
            player.jumpForce = player.originalJumpForce;
        }
        playerBars.SetActive(true);
    }

    private IEnumerator ChoiceDialogue(DialogueObject dialogueObject)
    {
        if (dialogueObject != null)
        {
            npcNameLabel.text = dialogueObject.NPCName;
            yield return typeWriter.Run(dialogueObject.ChoiceQuestion, textLabel, npcNameLabel);
            handler.ShowResponses(dialogueObject);
        }
        else CloseDialogue();
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
            PlayerManager.Instance.SetGlobalFloat(dialogueObject.targetFloatVariableName, dialogueObject.newFloatValue);
        }

        if (dialogueObject.appliesBooleanChange)
        {
            PlayerManager.Instance.SetGlobalBool(dialogueObject.targetBooleanVariableName, dialogueObject.newBooleanValue);
        }
    }
}
