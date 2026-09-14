using System;
using UnityEngine;
using UnityEngine.Events;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseAdvancedTarget : ScriptableObject
    {
        public Color accent = new Color(0.30f, 0.55f, 0.95f);

        public Color background = new Color(0.15f, 0.15f, 0.18f);

        [SerializeReference] public IShowcaseEffect onHitEffect = new ShowcaseDamageEffect();

        public UnityEvent onTriggered = new UnityEvent();
    }

    public interface IShowcaseEffect { }

    [Serializable]
    public class ShowcaseDamageEffect : IShowcaseEffect
    {
        public int amount = 10;
        public string damageType = "Fire";
    }

    [Serializable]
    public class ShowcaseHealEffect : IShowcaseEffect
    {
        public int healAmount = 20;
        public bool overTime;
    }

    [Serializable]
    public class ShowcaseKnockbackEffect : IShowcaseEffect
    {
        public float force = 5f;
        public Vector3 direction = Vector3.forward;
    }
}
