using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 13 — the data shapes the other exhibits never reach.
    ///
    /// Exhibits 1–12 cover the common ground: floats, enums, colours, lists, object references. This one
    /// deliberately picks the four field shapes a normal inspector handles WORST, and shows what the package
    /// does with each:
    ///
    /// <list type="bullet">
    ///   <item><b>A dictionary.</b> Unity cannot serialize <c>Dictionary&lt;K,V&gt;</c> at all before 6.6.
    ///         <see cref="SerializedDictionary{TKey,TValue}"/> ships with this package, works on every
    ///         supported version, and renders as a real Key/Value table with duplicate-key warnings and full
    ///         value editors — a Color key column gets swatches, an enum key column gets a dropdown.</item>
    ///   <item><b>A polymorphic field.</b> A <c>[SerializeReference]</c> interface field normally draws as
    ///         nothing useful. Here it becomes a searchable picker of every concrete implementor, and the
    ///         chosen type's own fields draw inline underneath.</item>
    ///   <item><b>A string list.</b> A <c>List&lt;string&gt;</c> of short labels is a tag set, not a list —
    ///         so it can render as chips you type into and dismiss, instead of nine numbered rows.</item>
    ///   <item><b>A spring.</b> A <c>SpringConfig</c> has no meaningful default drawer. It gets a live
    ///         preview you can flick, because "duration 0.4, bounce 0.3" means nothing until you see it.</item>
    /// </list>
    ///
    /// <para>The panel also uses the two container shapes the rest of the scene never shows: a TAB strip
    /// across the top, and a GRID for the option bank.</para>
    /// </summary>
    public class DemoLoadout : MonoBehaviour
    {
        // ═══ Polymorphic ═══════════════════════════════════════════════════════════════════════════════════
        // A plain interface plus three implementors. Nothing is registered anywhere — the picker finds them
        // by scanning for concrete types assignable to the field, so adding a fourth ability is one new class.

        public interface IAbility
        {
            string Describe();
        }

        [Serializable]
        public class DashAbility : IAbility
        {
            [Range(1f, 20f)] public float distance = 6f;
            [Range(0f, 5f)] public float cooldown = 1.2f;
            public bool phasesThroughWalls;
            public string Describe() => $"Dash {distance}m";
        }

        [Serializable]
        public class HealAbility : IAbility
        {
            [Range(0f, 200f)] public float amount = 45f;
            [Range(0f, 10f)] public float overTimeSeconds = 3f;
            public Color glow = new Color(0.45f, 0.95f, 0.6f);
            public string Describe() => $"Heal {amount}";
        }

        [Serializable]
        public class SummonAbility : IAbility
        {
            public string creatureName = "Ember Wisp";
            [Range(1, 8)] public int count = 3;
            [Range(0f, 60f)] public float lifetime = 12f;
            public string Describe() => $"Summon {count}x {creatureName}";
        }

        [SerializeReference]
        [Tooltip("A [SerializeReference] interface field. Pick a concrete ability and its own fields appear inline.")]
        public IAbility primaryAbility = new DashAbility();

        [SerializeReference]
        [Tooltip("The same field type, holding a different concrete type — the value lives inline on THIS object, " +
                 "not in a separate asset.")]
        public IAbility secondaryAbility = new HealAbility();

        // ═══ Dictionaries ══════════════════════════════════════════════════════════════════════════════════

        [Tooltip("An enum-keyed dictionary. The key column is a dropdown, the value column a real float field.")]
        public SerializedDictionary<DemoWeapon.WeaponClass, float> classDamageMultiplier =
            new SerializedDictionary<DemoWeapon.WeaponClass, float>
            {
                { DemoWeapon.WeaponClass.Blade,   1.00f },
                { DemoWeapon.WeaponClass.Bow,     0.85f },
                { DemoWeapon.WeaponClass.Stave,   1.30f },
                { DemoWeapon.WeaponClass.Hammer,  1.55f },
                { DemoWeapon.WeaponClass.Bazooka, 2.40f },
            };

        [Tooltip("A string-keyed dictionary in the Minimal style — same table, no shell. Type a duplicate key " +
                 "and the row tells you before it costs you a bug.")]
        public SerializedDictionary<string, int> ammoPools =
            new SerializedDictionary<string, int>
            {
                { "light", 240 },
                { "heavy", 60 },
                { "shells", 24 },
                { "rockets", 6 },
            };

        // ═══ Tag chips ═════════════════════════════════════════════════════════════════════════════════════

        [Tooltip("A List<string> rendered as CHIPS. Type to add, click the x to remove — nine numbered rows " +
                 "would say the same thing in five times the space.")]
        public List<string> traits = new List<string> { "agile", "fire-resist", "two-handed", "noisy" };

        // ═══ Spring ════════════════════════════════════════════════════════════════════════════════════════

        [Tooltip("A SpringConfig. The widget draws a live preview you can flick — the only honest way to " +
                 "review a feel value.")]
        public SasquatchSpring.SpringConfig panelSpring = new SasquatchSpring.SpringConfig
        {
            Duration = 0.42f,
            Bounce = 0.35f,
        };

        // ═══ Option bank — the Grid container's subject ═════════════════════════════════════════════════════

        [Tooltip("Eight independent flags. Stacked they are eight rows; in a grid they are two.")]
        public bool allowSprint = true;
        public bool allowSlide;
        public bool allowWallRun = true;
        public bool allowDoubleJump;
        public bool friendlyFire;
        public bool respawnOnDeath = true;
        public bool dropLootOnDeath = true;
        public bool keepBuffsOnRespawn;

        // ═══ Identity — the second tab ═════════════════════════════════════════════════════════════════════

        public string loadoutName = "Ember Scout";

        [TextArea(2, 4)]
        public string notes = "Fast, fragile, and loud. Pairs badly with stealth objectives.";

        [Range(1, 60)] public int recommendedLevel = 12;
    }
}
