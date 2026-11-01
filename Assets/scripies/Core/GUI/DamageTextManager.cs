using TMPro;
using UnityEngine;

namespace scripies.Core
{
    /// <summary>
    /// Listens for GameEvents.OnDamageText and spawns DamageText popups at hit locations.
    /// Can use an assigned prefab or automatically construct a world-space TextMeshPro popup on the fly.
    /// </summary>
    public class DamageTextManager : MonoBehaviour
    {
        public static DamageTextManager Instance { get; private set; }

        [Header("Prefab (Optional)")]
        [Tooltip("Optional prefab containing a DamageText component. If null, a TextMeshPro popup will be created dynamically.")]
        public GameObject damageTextPrefab;

        [Header("Motion & Lifetime")]
        public float floatSpeed = 1.6f;
        public float duration = 0.85f;

        [Header("Appearance (When creating dynamically)")]
        public float fontSize = 5.5f;
        public float critFontSize = 7.5f;
        public int sortingOrder = 500;
        public string sortingLayerName = "Default";
        public TMP_FontAsset customFont;

        [Header("Default Colors")]
        public Color normalDamageColor = Color.white;
        public Color criticalDamageColor = new Color(1f, 0.85f, 0.2f);
        public Color playerDamageColor = new Color(1f, 0.35f, 0.35f);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            GameEvents.OnDamageText += SpawnDamageText;
        }

        private void OnDisable()
        {
            GameEvents.OnDamageText -= SpawnDamageText;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Spawns a floating damage number at the given world position.
        /// </summary>
        public void SpawnDamageText(Vector3 position, float amount, Color color, bool isCritical)
        {
            if (amount <= 0.01f) return;

            var resolvedColor = color == default 
                ? (isCritical ? criticalDamageColor : normalDamageColor) 
                : color;

            if (damageTextPrefab != null)
            {
                var instance = Instantiate(damageTextPrefab, position, Quaternion.identity);
                var damageText = instance.GetComponent<DamageText>() ?? instance.AddComponent<DamageText>();
                damageText.Setup(amount, resolvedColor, isCritical, duration, floatSpeed);
            }
            else
            {
                // Dynamic fallback: create a world-space TextMeshPro object
                var go = new GameObject("DamageTextPopup");
                go.transform.position = position;

                var tmp = go.AddComponent<TextMeshPro>();
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = isCritical ? critFontSize : fontSize;
                tmp.fontStyle = FontStyles.Bold;
                tmp.sortingOrder = sortingOrder;
                if (!string.IsNullOrEmpty(sortingLayerName))
                    tmp.sortingLayerID = SortingLayer.NameToID(sortingLayerName);

                if (customFont != null) tmp.font = customFont;

                // Adjust rect bounds so text does not wrap or truncate
                var rt = tmp.rectTransform;
                rt.sizeDelta = new Vector2(5f, 2f);

                var damageText = go.AddComponent<DamageText>();
                damageText.Setup(amount, resolvedColor, isCritical, duration, floatSpeed);
            }
        }
    }
}
