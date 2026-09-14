using System;
using System.Collections.Generic;
using SasquatchInspector.Runtime;
using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 4 — collections, which is where stock Unity hurts most and therefore where the package has the
    /// most to prove. A list of ten slots is a wall of nested foldouts by default; the designed layout turns it
    /// into a table with real columns, reorder grips and row separators.
    /// </summary>
    public class DemoInventory : MonoBehaviour
    { 

        [Serializable]
        public class Slot
        {
            public string label = "Empty";
            // Rarity rather than an icon: a table column of tiny sprite slots reads as clutter, and an enum
            // column can be badged, tinted and sorted. Reuses DemoWeapon's scale so the two exhibits agree.
            public DemoWeapon.Rarity rarity = DemoWeapon.Rarity.Common;
            [Range(0, 99)] public int quantity = 1;
            [Range(0f, 1f)] public float condition = 1f;
        }

        [Range(0, 200)] public int carryWeight = 64;
        [Range(0, 200)] public int carryCapacity = 120;

        [Tooltip("A larger example inventory filled with randomized item values.")]
        public List<Slot> randomInventory = new List<Slot>
        {
            new Slot { label = "Dragon Scale",       rarity = DemoWeapon.Rarity.Legendary, quantity = 3,  condition = 0.95f },
            new Slot { label = "Iron Ore",           rarity = DemoWeapon.Rarity.Common,    quantity = 45, condition = 1.00f },
            new Slot { label = "Shadow Cloak",       rarity = DemoWeapon.Rarity.Epic,      quantity = 1,  condition = 0.72f },
            new Slot { label = "Healing Salve",      rarity = DemoWeapon.Rarity.Common,    quantity = 15, condition = 0.85f },
            new Slot { label = "Phoenix Feather",    rarity = DemoWeapon.Rarity.Legendary, quantity = 2,  condition = 1.00f },
            new Slot { label = "Rusty Dagger",       rarity = DemoWeapon.Rarity.Common,    quantity = 1,  condition = 0.18f },
            new Slot { label = "Sunfire Pendant",    rarity = DemoWeapon.Rarity.Rare,      quantity = 1,  condition = 0.90f },
            new Slot { label = "Mana Elixir",        rarity = DemoWeapon.Rarity.Rare,      quantity = 8,  condition = 1.00f },
            new Slot { label = "Goblin Tooth",       rarity = DemoWeapon.Rarity.Common,    quantity = 34, condition = 0.40f },
            new Slot { label = "Ancient Tome",       rarity = DemoWeapon.Rarity.Epic,      quantity = 1,  condition = 0.64f },
            new Slot { label = "Wooden Shield",      rarity = DemoWeapon.Rarity.Common,    quantity = 2,  condition = 0.35f },
            new Slot { label = "Mithril Ingot",      rarity = DemoWeapon.Rarity.Rare,      quantity = 12, condition = 1.00f },
            new Slot { label = "Ring of Evade",      rarity = DemoWeapon.Rarity.Epic,      quantity = 1,  condition = 0.88f },
            new Slot { label = "Lightning Scroll",   rarity = DemoWeapon.Rarity.Rare,      quantity = 5,  condition = 0.75f },
            new Slot { label = "Crystal Shard",      rarity = DemoWeapon.Rarity.Common,    quantity = 19, condition = 0.92f },
            new Slot { label = "Venom Vial",         rarity = DemoWeapon.Rarity.Common,    quantity = 6,  condition = 0.50f },
            new Slot { label = "Golden Apple",       rarity = DemoWeapon.Rarity.Legendary, quantity = 4,  condition = 1.00f },
            new Slot { label = "Heavy Warhammer",    rarity = DemoWeapon.Rarity.Rare,      quantity = 1,  condition = 0.45f },
            new Slot { label = "Obsidian Key",       rarity = DemoWeapon.Rarity.Epic,      quantity = 1,  condition = 1.00f },
            new Slot { label = "Silver Coin",        rarity = DemoWeapon.Rarity.Common,    quantity = 88, condition = 0.99f },
            new Slot { label = "Demon Horn",         rarity = DemoWeapon.Rarity.Rare,      quantity = 7,  condition = 0.60f },
            new Slot { label = "Boots of Haste",     rarity = DemoWeapon.Rarity.Rare,      quantity = 1,  condition = 0.78f },
            new Slot { label = "Spider Silk",        rarity = DemoWeapon.Rarity.Common,    quantity = 23, condition = 0.30f },
            new Slot { label = "Runed Greatsword",   rarity = DemoWeapon.Rarity.Legendary, quantity = 1,  condition = 0.91f },
            new Slot { label = "Spellbook of Frost", rarity = DemoWeapon.Rarity.Epic,      quantity = 1,  condition = 0.82f },
        };

        [Tooltip("A second, simpler collection — shows a different display mode on the same inspector.")]
        public List<string> recentPickups = new List<string> { "Ration", "Bent Nail", "Ember Flask" };

        [Tooltip("A SerializedDictionary — a Key/Value table with duplicate-key warnings, drag-to-reorder and " +
                 "real value editors. Works on every supported Unity version, unlike a plain Dictionary<,>.")]
        // Deliberately the BONUS type rather than a native Dictionary<K,V>: a native one only serializes from
        // Unity 6.6, so on the 6.0-6.5 floor this demo scene would show the "Unity can't serialize this" card
        // instead of an exhibit. The bonus type needs no version gate and reaches the very same table.
        //
        // Rarity keys and Color values are the point: the KEY column is an enum dropdown and the VALUE column
        // renders real colour swatches, which is what "values get their full Sasquatch field" means.
        public SerializedDictionary<DemoWeapon.Rarity, Color> rarityTint =
            new SerializedDictionary<DemoWeapon.Rarity, Color>
            {
                { DemoWeapon.Rarity.Common,    new Color(0.75f, 0.78f, 0.80f) },
                { DemoWeapon.Rarity.Rare,      new Color(0.30f, 0.55f, 0.95f) },
                { DemoWeapon.Rarity.Epic,      new Color(0.65f, 0.35f, 0.90f) },
                { DemoWeapon.Rarity.Legendary, new Color(0.95f, 0.70f, 0.20f) },
            };
    }
}
