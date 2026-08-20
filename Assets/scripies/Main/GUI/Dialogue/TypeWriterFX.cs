using System;
using System.Collections;
using TMPro;
using UnityEngine;
using System.Text.RegularExpressions;

namespace scripies.Dialogue
{
    public class TypeWriterFX : MonoBehaviour
    {
        public float originalTypingSpeed = 0.05f;
        public float typingSpeed = 0.05f;
        public float punctuationPause = 0.2f;
        public float textSpeedMultiplier = 2;
        public int actionFactor;
        private bool skip;

        private readonly char[] speedControlChars = { '#', '*' };
        private readonly char[] actionControlChars = { '|', '/', '_', '~' };
        
        [SerializeField] private AudioClip[] typingSfx;

        public Coroutine Run(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
        {
            return StartCoroutine(TypeText(textToType, textLabel, npcLabel));
        }

        private void Update()
        {
            if (Input.GetButtonDown("Submit") || Input.GetMouseButtonDown(0)) 
                skip = true;
            
            var dialogueUI = GetComponent<DialogueUI>();
            
            if (dialogueUI == null || !dialogueUI.isAnimated) return;
            switch (actionFactor)
            {
                case 1:
                    dialogueUI.animator.SetBool("Action1", true);
                    dialogueUI.animator.SetBool("Action2", false);
                    dialogueUI.animator.SetBool("Action3", false);
                    break;
                case 2:
                    dialogueUI.animator.SetBool("Action2", true);
                    dialogueUI.animator.SetBool("Action1", false);
                    dialogueUI.animator.SetBool("Action3", false);
                    break;
                case 3:
                    dialogueUI.animator.SetBool("Action3", true);
                    dialogueUI.animator.SetBool("Action1", false);
                    dialogueUI.animator.SetBool("Action2", false);
                    break;
                default:
                    dialogueUI.animator.SetBool("Action1", false);
                    dialogueUI.animator.SetBool("Action2", false);
                    dialogueUI.animator.SetBool("Action3", false);
                    break;
            }
        }

        private IEnumerator TypeText(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
        {
            typingSpeed = originalTypingSpeed;
            textLabel.text = string.Empty;
            actionFactor = 0;
            skip = false;

            var cleanText = RemoveControlCharacters(textToType);

            for (var i = 0; i < textToType.Length; i++)
            {
                if (Array.IndexOf(speedControlChars, textToType[i]) != -1)
                {
                    if (i + 1 < textToType.Length)
                    {
                        if (textToType[i] == '#') typingSpeed /= textSpeedMultiplier;
                        else if (textToType[i] == '*') typingSpeed *= textSpeedMultiplier;
                        i++;
                    }
                    else Debug.LogWarning($"TypeWriterFX: Speed control character '{textToType[i]}' at end of string. Ignoring.");
                }

                var dialogueUI = GetComponent<DialogueUI>();
                if (dialogueUI != null && dialogueUI.isAnimated)
                {
                    if (Array.IndexOf(actionControlChars, textToType[i]) != -1)
                    {
                        if (i + 1 < textToType.Length)
                        {
                            switch (textToType[i])
                            {
                                case '|': actionFactor = 1; break;
                                case '/': actionFactor = 2; break;
                                case '_': actionFactor = 3; break;
                                case '~': actionFactor = 0; break;
                            }

                            i++;
                        }
                        else Debug.LogWarning($"TypeWriterFX: Action control character '{textToType[i]}' at end of string. Ignoring.");
                    }
                }

                if (i < textToType.Length) textLabel.text += textToType[i];
                
                if (i < textToType.Length && (textToType[i] == '.' || textToType[i] == ',' || textToType[i] == '?' || textToType[i] == '!' || textToType[i] == ':' || textToType[i] == ';')) 
                    yield return new WaitForSeconds(punctuationPause);
                else
                    yield return new WaitForSeconds(typingSpeed);
                

                if (skip)
                {
                    textLabel.text = cleanText;
                    yield return new WaitUntil(() => Input.GetButtonDown("Submit"));
                    break;
                }
            }

            textLabel.text = cleanText;
        }

        private string RemoveControlCharacters(string originalText)
        {
            // Combine all control characters into a single regex pattern
            // Escaping special regex characters like *, /, |, _
            var pattern = "[" + Regex.Escape(new string(speedControlChars)) +
                             Regex.Escape(new string(actionControlChars)) + "]";

            var result = "";
            for (var i = 0; i < originalText.Length; i++)
            {
                var currentChar = originalText[i];
                if (Array.IndexOf(speedControlChars, currentChar) != -1 ||
                    Array.IndexOf(actionControlChars, currentChar) != -1)
                {
                    // If it's a control character, skip it and the next character
                    //i++; // Skip the character that follows the control character too
                }
                else result += currentChar;
            }

            return result;

            // Alternative using Regex (if patterns are simpler, e.g., just removing the control char itself)
            // string pattern = @"[#*|/_~]"; // Matches any of your control characters
            // return Regex.Replace(originalText, pattern, "");
        }
    }
}