using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A string field whose <c>{fieldName}</c> tokens resolve to live values wherever it is used — so the
    /// text you compose in the Rich Text Preview is the text your game actually draws.
    ///
    /// <code>
    /// public class Weapon : MonoBehaviour
    /// {
    ///     public float damage = 42f;
    ///     public DamageType element = DamageType.FireDamage;
    ///     public SasquatchString description;      // "Deals &lt;b&gt;{damage}&lt;/b&gt; {element}"
    /// }
    ///
    /// label.text = weapon.description;             // "Deals &lt;b&gt;42&lt;/b&gt; Fire Damage"
    /// label.SetText(weapon.description);           // same
    /// </code>
    ///
    /// Resolution lives in the TYPE, not in any one text system, so the implicit conversion covers
    /// TextMeshPro, uGUI Text, UI Toolkit, string concatenation and <c>Debug.Log</c> without a per-call-site
    /// helper. There is no component to add and no preprocessor to install.
    ///
    /// Declared as a struct (like <see cref="SceneReference"/>) so a field can never be null: a component
    /// created at runtime through <c>AddComponent</c> yields an empty template rather than a
    /// NullReferenceException at the first assignment.
    /// </summary>
    [Serializable]
    public struct SasquatchString
    {
        [SerializeField] private string template;

        // The object whose fields the tokens name — normally the very object that declares this field.
        // Written by the inspector when the template is edited (see SasquatchStringDrawer). Serialising it
        // is what makes prefabs work: Unity remaps references internal to an instantiated hierarchy, so
        // every spawned copy points at ITS OWN component and resolves its own values.
        [SerializeField] private UnityEngine.Object source;

        public SasquatchString(string template, UnityEngine.Object source = null)
        {
            this.template = template;
            this.source = source;
        }

        /// <summary>The raw text exactly as typed, tokens unresolved.</summary>
        public string Template => template ?? string.Empty;

        /// <summary>The object the tokens resolve against, or null if this template was never bound.</summary>
        public UnityEngine.Object Source => source;

        /// <summary>
        /// The resolved text. An unbound template (or an unknown token name) yields the literal text
        /// unchanged, which is exactly what a plain <c>string</c> field would have produced — never an
        /// exception and never an empty string.
        /// </summary>
        public string Value => SasquatchTokens.Resolve(template, source);

        public static implicit operator string(SasquatchString value) => value.Value;

        public override string ToString() => Value;

        /// <summary>
        /// Points this template at a different object. Only needed for instances built in code — anything
        /// authored in the inspector is already bound. Safe to call from <c>Awake</c>:
        /// <c>description.Bind(this);</c> mutates the field in place because it is a struct field, not a copy.
        /// </summary>
        public void Bind(UnityEngine.Object owner) => source = owner;

        /// <summary>Resolves against some other object without changing what this template is bound to.</summary>
        public string ResolveAgainst(object other) => SasquatchTokens.Resolve(template, other);
    }
}
