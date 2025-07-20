using System;
using System.Collections;
using UnityEngine;
using TMPro;
using Unity.VisualScripting;

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
    InvManager invManager;
    [SerializeField] GameObject inventory;
    [SerializeField] int textIndex;
    [SerializeField] int currentColourIndex;
    private bool isBold;
    [SerializeField] int currentBoldIndex;
    
    private void Start()
    {
        typeWriter = GetComponent<TypeWriterFX>();
        handler = GetComponent<DialogueResponseHandler>();
        player = GameObject.Find("Player").GetComponent<PlayerController>();
        playerBars = GameObject.Find("PlayerBars");
        invManager = GetComponent<InvManager>();
        CloseDialogue();
    }

    private void Update()
    {
        textLabel.fontStyle = isBold ? FontStyles.Bold : FontStyles.Normal;
    }


    public void ShowDialogue(DialogueObject dialogueObject)
    {
        playerBars.SetActive(false);
        player.canAct = false;
        player.isFrozen = true;
        player.isFrozen = false;
        player.jumpForce = 0f;
        isAnimated = dialogueObject.IsAnimated;
        dialogueBox.SetActive(true);
        invManager.enabled = false;
        inventory.SetActive(false);
        textIndex = dialogueObject.colourIndex;
        currentColourIndex = 0;
        currentBoldIndex = 0;
        StartCoroutine(StepThroughDialogue(dialogueObject));
    }

    private IEnumerator StepThroughDialogue(DialogueObject dialogueObject)
    {
        foreach (string dialogue in dialogueObject.Dialogue)
        {
            npcNameLabel.text = dialogueObject.NPCName;
            if (dialogueObject.canBeColoured && currentColourIndex <= textIndex) textLabel.color = dialogueObject.colour[currentColourIndex];
            else if (!dialogueObject.canBeColoured) textLabel.color = Color.black;
            if (dialogueObject.canBeColoured) currentColourIndex++;
            if (dialogueObject.canBeBold) isBold = dialogueObject.bold[currentBoldIndex];
            else isBold = false;
            if (currentBoldIndex < textIndex || dialogueObject.canBeBold) currentBoldIndex++;
            animator = isAnimated ? GameObject.Find(npcNameLabel.text).GetComponent<Animator>() : null;
            yield return typeWriter.Run(dialogue, textLabel, npcNameLabel);
            yield return new WaitUntil(() => Input.GetButtonDown("Submit"));
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
        invManager.enabled = true;
        textLabel.color = Color.black;
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
