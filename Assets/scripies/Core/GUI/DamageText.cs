using TMPro;
using UnityEngine;

namespace scripies.Core
{
    /// <summary>
    /// Simple floating damage number.
    /// Moves upward, fades out, and destroys itself after duration.
    /// </summary>
    public class DamageText : MonoBehaviour
    {
        [Header("Components")]
        public TMP_Text textMesh;

        [Header("Movement & Fade")]
        public float moveSpeed = 1.5f;
        public float duration = 0.8f;

        private float elapsed;
        private Color baseColor = Color.white;
        private Vector3 moveDirection = Vector3.up;
        private Vector3 initialScale = Vector3.one;

        private void Awake()
        {
            if (textMesh == null) textMesh = GetComponent<TMP_Text>();
            initialScale = transform.localScale;
        }

        public void Setup(float damage, Color color, bool isCritical = false, float customDuration = 0.8f, float customSpeed = 1.5f)
        {
            if (textMesh == null) textMesh = GetComponent<TMP_Text>();

            duration = customDuration;
            moveSpeed = customSpeed;
            baseColor = color == default ? Color.white : color;

            // Display whole number if integer-ish, otherwise 1 decimal place
            var isWholeNumber = Mathf.Approximately(damage, Mathf.Round(damage));
            textMesh.text = isWholeNumber ? Mathf.RoundToInt(damage).ToString() : damage.ToString("F1");
            textMesh.color = baseColor;

            // Slight random horizontal drift so numbers don't stack directly on top of each other
            var horizontalDrift = Random.Range(-0.35f, 0.35f);
            moveDirection = new Vector3(horizontalDrift, 1f, 0f).normalized;

            // Critical hit styling (e.g., larger scale)
            if (isCritical)
            {
                initialScale *= 1.35f;
                transform.localScale = initialScale;
            }

            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            // Float upward smoothly
            transform.position += moveDirection * (moveSpeed * Time.deltaTime);

            // Fade out alpha towards the end of its lifetime
            var normalizedTime = Mathf.Clamp01(elapsed / duration);
            var alpha = Mathf.Lerp(1f, 0f, normalizedTime);

            if (textMesh != null)
            {
                textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            }

            // Simple punch-down scale effect (starts slightly bigger, settles)
            if (normalizedTime < 0.2f)
            {
                var punch = Mathf.Lerp(1.2f, 1f, normalizedTime / 0.2f);
                transform.localScale = initialScale * punch;
            }

            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
