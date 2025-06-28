using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class TypeWriterFX : MonoBehaviour
{
    public float originalTypingSpeed = 0.05f;
    public float typingSpeed = 0.05f;
    public float punctuationPause = 0.2f;
    public float textSpeedMultiplier = 2;
    public int actionFactor;
    private bool skip;
    
    private bool isFacingRight;
    public Coroutine Run(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
    {
        return StartCoroutine(TypeText(textToType, textLabel, npcLabel));
    }

    private void Update()
    {
        if (Input.GetButtonDown("Submit") || Input.GetMouseButtonDown(0))
        {
            skip = true;
        }
        if (GetComponent<DialogueUI>().isAnimated)
        {
            switch (actionFactor)
            {
                case 1:
                    GetComponent<DialogueUI>().animator.SetBool("Action1", true);
                    GetComponent<DialogueUI>().animator.SetBool("Action2", false);
                    GetComponent<DialogueUI>().animator.SetBool("Action3", false);
                    break;
                case 2:
                    GetComponent<DialogueUI>().animator.SetBool("Action2", true);
                    GetComponent<DialogueUI>().animator.SetBool("Action1", false);
                    GetComponent<DialogueUI>().animator.SetBool("Action3", false);
                    break;
                case 3:
                    GetComponent<DialogueUI>().animator.SetBool("Action3", true);
                    GetComponent<DialogueUI>().animator.SetBool("Action1", false);
                    GetComponent<DialogueUI>().animator.SetBool("Action2", false);
                    break;
                default:
                    GetComponent<DialogueUI>().animator.SetBool("Action1", false);
                    GetComponent<DialogueUI>().animator.SetBool("Action2", false);
                    GetComponent<DialogueUI>().animator.SetBool("Action3", false);
                    break;
            }
        }
    }

    private IEnumerator TypeText(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
    {
        typingSpeed = originalTypingSpeed;
        textLabel.text = string.Empty;
        actionFactor = 0;
        skip = false;
        
        for (int i = 0; i < textToType.Length; i++)
        {
            if (textToType[i] == '#')
            {
                i++;
                typingSpeed /= textSpeedMultiplier;
            }
            else if (textToType[i] == '*')
            {
                i++;
                typingSpeed *= textSpeedMultiplier;
            }

            if (GetComponent<DialogueUI>().isAnimated)
            {
                // man i love switch statements!
                switch (textToType[i])
                {
                    case '|':
                        i++;
                        actionFactor = 1;
                        break;
                    case '/':
                        i++;
                        actionFactor = 2;
                        break;
                    case '_':
                        i++;
                        actionFactor = 3;
                        break;
                    case '~':
                        i++;
                        actionFactor = 0;
                        break;
                }
            }
            
            textLabel.text += textToType[i];
            
            if (textToType[i] == '.' || textToType[i] == ',' || textToType[i] == '?' || textToType[i] == '!' ||
                textToType[i] == ':' || textToType[i] == ';')
            {
                yield return new WaitForSeconds(punctuationPause); // Pause for the punctuation duration
            }
            else
            {
                yield return new WaitForSeconds(typingSpeed);
            }

            if (skip && i >= 1)
            {
                for (int l = 0; l < textToType.Length; l++)
                {
                    if (textToType[l] == '*' || textToType[l] == '_' || textToType[l] == '|' || textToType[l] == '#')
                    {
                        Debug.Log("skipped a character: " + textToType[l]);
                    }
                }
                textLabel.text = textToType;
                yield return new WaitUntil(() => Input.GetButtonDown("Submit") || Input.GetMouseButtonDown(0));
                i = textToType.Length;
            }

        }
    }
}
