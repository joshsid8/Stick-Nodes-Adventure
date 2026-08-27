using UnityEngine;

[CreateAssetMenu(fileName = "Badge", menuName = "Badge")]
public class Badge : CollectableSOBase
{
    public override void Collect(GameObject objectCollected) => StatsManager.Instance.AddBadge(this);
}
