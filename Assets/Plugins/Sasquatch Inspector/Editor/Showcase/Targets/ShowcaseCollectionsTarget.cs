using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // · waypoints has six entries because a hairline, a zebra stripe and a timeline rail all need more than
    //   three rows before the pattern reads as a pattern.
    // · statBlocks is filled by the Showcase WINDOW, not here: In-Line Editor Rows needs real
    //   UnityEngine.Object elements, and a ScriptableObject cannot safely CreateInstance from a field
    //   initializer.
    // · onHitEffects shares its interface and three implementors with ShowcaseAdvancedTarget.cs.
    //
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations.
    internal class ShowcaseCollectionsTarget : ScriptableObject
    {
        public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

        [Serializable]
        public class LootEntry
        {
            public string itemName = "Item";
            public int weight = 10;
            public Rarity rarity = Rarity.Common;
            public int stars = 3;
        }

        public List<LootEntry> lootTable = new List<LootEntry>
        {
            new LootEntry { itemName = "Health Potion", weight = 40, rarity = Rarity.Common,    stars = 1 },
            new LootEntry { itemName = "Iron Sword",    weight = 25, rarity = Rarity.Uncommon, stars = 3 },
            new LootEntry { itemName = "Dragon Scale",  weight = 5,  rarity = Rarity.Legendary, stars = 5 },
        };

        public List<string> tags = new List<string> { "boss", "fire", "elite" };

        public List<string> waypoints = new List<string>
        {
            "Spawn", "Gate", "Courtyard", "Vault", "Rooftop", "Extract"
        };

        public List<ShowcaseStatBlock> statBlocks = new List<ShowcaseStatBlock>();

        [SerializeReference] public List<IShowcaseEffect> onHitEffects = new List<IShowcaseEffect>
        {
            new ShowcaseDamageEffect(),
            new ShowcaseKnockbackEffect(),
        };

        [Serializable]
        public class InventoryItem
        {
            public string name = "Item";
            public int damage;
            public Rarity rarity = Rarity.Common;
            public int value;
            public float weight;
        }

        public List<InventoryItem> items = MakeInventory();

        private static List<InventoryItem> MakeInventory()
        {
            var kinds = new[]
            {
                "Sword", "Axe", "Bow", "Dagger", "Staff", "Shield",
                "Helm", "Potion", "Ring", "Amulet", "Gloves", "Boots"
            };
            var list = new List<InventoryItem>(120);
            uint s = 2463534242u; // fixed seed -> identical list every open
            for (int i = 0; i < 120; i++)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5; // xorshift32 (deterministic)
                list.Add(new InventoryItem
                {
                    name = $"{kinds[i % kinds.Length]} {i:000}",
                    damage = (int)(s % 100),
                    rarity = (Rarity)(i % 5),
                    value = (int)(s % 1000) + 1,
                    weight = ((s % 50) + 1) / 10f
                });
            }
            return list;
        }
    }
}
