using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Showcase
{
    // A deliberately LARGE list (120 items) so the Showcase can demonstrate pagination, search, and
    // clickable-header sorting at scale. The contents are generated DETERMINISTICALLY (a seeded xorshift,
    // never Random) so the demo + its auto-generated C# source snippet are identical every time it opens.
    internal class ShowcaseInventoryTarget : ScriptableObject
    {
        public enum Rarity { Common, Uncommon, Rare, Epic, Legendary }

        [Serializable]
        public class InventoryItem
        {
            public string name = "Item";
            public int damage;
            public Rarity rarity = Rarity.Common;
            public int value;
            public float weight;
        }

        public List<InventoryItem> items = Generate();

        private static List<InventoryItem> Generate()
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
                // xorshift32 — deterministic pseudo-random, no UnityEngine.Random/System.Random.
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
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
