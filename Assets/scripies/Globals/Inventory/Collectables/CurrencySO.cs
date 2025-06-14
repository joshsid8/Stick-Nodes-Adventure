using UnityEngine;

[CreateAssetMenu(fileName = "CreditCollectable", menuName = "Collectable/Currency")]
public class CurrencySO : CollectableSOBase
{
    public float amount;
    public override void Collect(GameObject objectCollected)
    {
        PlayerManager.Instance.SetGlobalFloat("Money", amount);
    }
}
