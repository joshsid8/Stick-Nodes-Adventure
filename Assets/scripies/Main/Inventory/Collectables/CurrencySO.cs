using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Currency")]
public class CurrencySO : CollectableSOBase
{
    public float maxItem;
    public float minItem;
    public override void Collect()
    {
        var rng = Random.Range(minItem, maxItem);
        PlayerManager.Instance.AlterGlobalFloat("Money", rng);
    }

    public override string SetDescription()
    {
        return itemDescription;
    }
}
