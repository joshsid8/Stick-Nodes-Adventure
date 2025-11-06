using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class InvIndex
{
    private readonly Dictionary<CollectableSOBase, int> counts;

    public InvIndex()
    {
        counts = new Dictionary<CollectableSOBase, int>();
    }
    public void AddItem(CollectableSOBase id, int amount = 1)
    {
        if (id == null) throw new ArgumentException("id is null/empty. KILL YOURSELF!");
        if (amount <= 0) return;

        if (counts.TryGetValue(id, out var current)) counts[id] = current + amount;
        else counts[id] = amount;
    }

    public void RemoveItem(CollectableSOBase id, int amount = 1)
    {
        if (id == null) throw new ArgumentException("id is null/empty");
        if (amount <= 0) return;
        if (!counts.TryGetValue(id, out var current)) return;
        var newCount = current - amount;
        if (newCount > 0) counts[id] = newCount;
        else counts.Remove(id);
    }

    public int GetCount(CollectableSOBase id) => counts.TryGetValue(id, out var c) ? c : 0;

    public bool HasAtLeast(CollectableSOBase id, int count) => GetCount(id) >= count;

    public bool MeetsAllCounts(IEnumerable<(CollectableSOBase id, int count)> reqs) => reqs != null && reqs.All(r => HasAtLeast(r.id, r.count));

    public void Save(ref PlayerSaveIndex data)
    {
        data.items.Clear();
        foreach (var item in counts)
        {
            if (item.Key != null) 
                data.items.Add(new PlayerSaveIndex.SerializableItem { itemID = item.Key.name, count = item.Value });
        }
    }

    public void Load(PlayerSaveIndex data)
    {
        counts.Clear();
        if (data.items == null) return;
        foreach (var item in data.items)
        {
            var so = ItemDatabase.Instance.GetItemByName(item.itemID);
            if (so != null) AddItem(so, item.count);
            else Debug.LogWarning($"[InvIndex.Load] Failed to find item with ID: {item.itemID}");
        }
    }
}

[Serializable]
public struct PlayerSaveIndex
{
    [Serializable]
    public struct SerializableItem
    {
        public string itemID;
        public int count;
    }

    public List<SerializableItem> items;

    public static PlayerSaveIndex CreateNew()
    {
        return new PlayerSaveIndex
        {
            items = new List<SerializableItem>()
        };
    }
}