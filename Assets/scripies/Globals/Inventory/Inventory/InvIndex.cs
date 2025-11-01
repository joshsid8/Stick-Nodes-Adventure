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
        Debug.Log($"{id.name} has been removed");
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
}