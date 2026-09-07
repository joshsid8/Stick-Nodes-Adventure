using UnityEngine;

[CreateAssetMenu(fileName = "Badge", menuName = "Badge")]
public class BadgeSO : CollectableSOBase
{
    public override void Collect(GameObject objectCollected) => StatsManager.Instance.AddBadge(this);
}
