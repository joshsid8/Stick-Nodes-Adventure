using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using scripies.Core;
using scripies.Player;

// Majorly Edited By Junie
namespace scripies.Dialogue
{
    public class TypeWriterFX : MonoBehaviour
    {
        public class TextSegment
        {
            public string text;
            public float speed;
            public int action;
            public bool isPunctuation;
        }

        public float originalTypingSpeed = 0.05f;
        public float typingSpeed = 0.05f;
        public float punctuationPause = 0.2f;
        public float textSpeedMultiplier = 2;
        public int actionFactor;
        private bool skip;

        private readonly char[] speedControlChars = { '#', '*' };
        private readonly char[] actionControlChars = { '|', '/', '_', '~' };
        
        [SerializeField] private AudioClip[] typingSfx;
        
        private PlayerInput input;

        private void Start()
        {
            input = PlayerHealth.Instance.pc.input;
        }

        public Coroutine Run(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
        {
            return StartCoroutine(TypeText(textToType, textLabel, npcLabel));
        }

        public void ApplyAnimation(int action)
        {
            var dialogueUI = GetComponent<DialogueManager>();
            if (dialogueUI == null || !dialogueUI.isAnimated || dialogueUI.animator == null) return;

            dialogueUI.animator.SetBool("Action1", action == 1);
            dialogueUI.animator.SetBool("Action2", action == 2);
            dialogueUI.animator.SetBool("Action3", action == 3);
            
            if (action != 0) return;
            dialogueUI.animator.SetBool("Action1", false);
            dialogueUI.animator.SetBool("Action2", false);
            dialogueUI.animator.SetBool("Action3", false);
        }

        private List<TextSegment> ParseSegments(string rawText)
        {
            var segments = new List<TextSegment>();
            var currentSpeed = originalTypingSpeed;
            var currentAction = 0;
            var currentText = new System.Text.StringBuilder();

            for (var i = 0; i < rawText.Length; i++)
            {
                char c = rawText[i];

                // Check for control characters
                if (Array.IndexOf(speedControlChars, c) != -1 || Array.IndexOf(actionControlChars, c) != -1)
                {
                    // Flush current text to a segment if it's not empty
                    if (currentText.Length > 0)
                    {
                        segments.Add(new TextSegment { text = currentText.ToString(), speed = currentSpeed, action = currentAction });
                        currentText.Clear();
                    }

                    if (Array.IndexOf(speedControlChars, c) != -1)
                    {
                        if (c == '#') currentSpeed /= textSpeedMultiplier;
                        else if (c == '*') currentSpeed *= textSpeedMultiplier;
                    }
                    else if (Array.IndexOf(actionControlChars, c) != -1)
                    {
                        switch (c)
                        {
                            case '|': currentAction = 1; break;
                            case '/': currentAction = 2; break;
                            case '_': currentAction = 3; break;
                            case '~': currentAction = 0; break;
                        }
                    }
                    continue;
                }

                // Check for punctuation pauses
                if (c == '.' || c == ',' || c == '?' || c == '!' || c == ':' || c == ';')
                {
                    // Flush current text
                    if (currentText.Length > 0)
                    {
                        segments.Add(new TextSegment { text = currentText.ToString(), speed = currentSpeed, action = currentAction });
                        currentText.Clear();
                    }

                    // Add punctuation as its own segment
                    segments.Add(new TextSegment { text = c.ToString(), speed = punctuationPause, action = currentAction, isPunctuation = true });
                }
                else
                {
                    currentText.Append(c);
                }
            }

            if (currentText.Length > 0)
            {
                segments.Add(new TextSegment { text = currentText.ToString(), speed = currentSpeed, action = currentAction });
            }

            return segments;
        }

        private IEnumerator TypeText(string textToType, TMP_Text textLabel, TMP_Text npcLabel)
        {
            var segments = ParseSegments(textToType);
            var cleanText = RemoveControlCharacters(textToType);

            textLabel.text = cleanText;
            textLabel.maxVisibleCharacters = 0;
            skip = false;

            var visibleCharacters = 0;

            foreach (var segment in segments)
            {
                actionFactor = segment.action;
                ApplyAnimation(actionFactor);
                typingSpeed = segment.speed;

                for (var i = 0; i < segment.text.Length; i++)
                {
                    visibleCharacters++;
                    textLabel.maxVisibleCharacters = visibleCharacters;

                    if (skip) break;

                    yield return new WaitForSeconds(typingSpeed);
                }

                if (skip) break;
            }

            textLabel.maxVisibleCharacters = cleanText.Length;

            if (skip)
            {
                yield return new WaitUntil(() => input.UI.Submit.WasPressedThisFrame());
            }
        }

        private void Update()
        {
            if (input.UI.Submit.WasPressedThisFrame())
                skip = true;
        }

        private string RemoveControlCharacters(string originalText)
        {
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < originalText.Length; i++)
            {
                var currentChar = originalText[i];
                if (Array.IndexOf(speedControlChars, currentChar) == -1 &&
                    Array.IndexOf(actionControlChars, currentChar) == -1)
                {
                    result.Append(currentChar);
                }
            }
            return result.ToString();
        }
    }
}