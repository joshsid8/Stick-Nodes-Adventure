using scripies.Core;
using UnityEngine;

[CreateAssetMenu(fileName = "Badge", menuName = "Collectable/Badge")]
public class BadgeSO : CollectableSOBase
{
    public override void Collect() => GameEvents.OnBadgeCollect?.Invoke(this);
    public override string SetDescription()
    {
        return itemDescription;
    }
}
