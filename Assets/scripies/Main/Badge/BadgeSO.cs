using UnityEngine;

[CreateAssetMenu(fileName = "Badge", menuName = "Collectable/Badge")]
public class BadgeSO : CollectableSOBase
{
    public override void Collect(GameObject objectCollected) => StatsManager.Instance.AddBadge(this);
}
