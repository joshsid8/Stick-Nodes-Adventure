using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A theme asset: a base skin (which of the dark/light widget palettes the rendered inspectors
    /// use) plus optional style-token → <see cref="LayoutStyle"/> mappings for nodes that carry a
    /// StyleToken. Assign one in Project Settings → Sasquatch Inspector; leave the setting empty to follow
    /// the editor skin automatically (DefaultDarkTheme / DefaultLightTheme).
    /// </summary>
    [CreateAssetMenu(menuName = "Sasquatch Inspector/Theme", fileName = "SasquatchTheme")]
    public class SasquatchThemeAsset : ScriptableObject
    {
        /// <summary>Maps one style-token name to the <see cref="LayoutStyle"/> it resolves to.</summary>
        [Serializable]
        public struct TokenMapping
        {
            public string token;
            public LayoutStyle style;
        }

        [SerializeField] private List<TokenMapping> themes = new();

        // Dark default = every pre-existing theme asset (serialized without this field) keeps its
        // historical dark rendering.
        [SerializeField]
        [Tooltip("Which widget palette the rendered inspectors use with this theme: the dark skin or the light skin. Token styles above apply on top.")]
        private SasquatchThemeSkin baseSkin = SasquatchThemeSkin.Dark;

        public List<TokenMapping> Themes => themes;

        /// <summary>The widget palette (dark/light) rendered inspectors use while this theme is active.</summary>
        public SasquatchThemeSkin BaseSkin
        {
            get => baseSkin;
            set => baseSkin = value;
        }

        /// <summary>Looks up the <see cref="LayoutStyle"/> mapped to <paramref name="token"/>; returns false when the token isn't in the theme.</summary>
        public bool TryGetStyle(string token, out LayoutStyle style)
        {
            if (!string.IsNullOrEmpty(token) && themes != null)
            {
                foreach (var mapping in themes)
                {
                    if (mapping.token == token)
                    {
                        style = mapping.style;
                        return true;
                    }
                }
            }
            style = default;
            return false;
        }
    }
}
