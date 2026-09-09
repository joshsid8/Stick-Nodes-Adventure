using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Currency")]
public class CurrencySO : CollectableSOBase
{
    public float maxItem;
    public float minItem;
    public override void Collect(GameObject objectCollected)
    {
        var rng = Random.Range(minItem, maxItem);
        PlayerManager.Instance.AlterGlobalFloat("Money", rng);
    }
}
