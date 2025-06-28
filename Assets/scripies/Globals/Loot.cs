using UnityEngine;

public class Loot : MonoBehaviour
{
    public GameObject moneyLoot;
    public CurrencySO currencyType;
    public int minMoneyAmount;
    public int maxMoneyAmount;
    
    public GameObject[] itemLoot;
    public GameObject[] aspectLoot;
    
    public ItemSO[] possibleItems;
    public AspectSO[] possibleAspects;
    public float[] itemChance;
    public float[] aspectChance;
    
    private bool dropping = true;
    
    public void DropLoot()
    {
        if (dropping)
        {
            dropping = false;
            for (int i = 0; i < possibleItems.Length; i++)
            {
                float iRng = Random.Range(0.00f, 100.00f);
                if (iRng <= itemChance[i])
                {
                    GameObject item = Instantiate(itemLoot[i], transform.position, transform.rotation);
                    item.GetComponent<Collectable>().collectable = possibleItems[i];
                    item.GetComponent<Rigidbody2D>().linearVelocity =
                        new Vector2(Random.Range(-5f, 5f), Random.Range(-5f, 5f));
                }
            }

            for (int i = 0; i < possibleAspects.Length; i++)
            {
                float aRng = Random.Range(0.00f, 100.00f);
                if (aRng <= aspectChance[i])
                {
                    GameObject aspect = Instantiate(aspectLoot[i], transform.position, transform.rotation);
                    aspect.GetComponent<Collectable>().collectable = possibleAspects[i];
                    aspect.GetComponent<Rigidbody2D>().linearVelocity =
                        new Vector2(Random.Range(-5f, 5f), Random.Range(-5f, 5f));
                }
            }

            int rng = Random.Range(minMoneyAmount, maxMoneyAmount);
            for (int i = 0; i < rng; i++)
            {
                GameObject money = Instantiate(moneyLoot, transform.position, transform.rotation);
                money.GetComponent<Collectable>().collectable = currencyType;
                money.GetComponent<Rigidbody2D>().linearVelocity =
                    new Vector2(Random.Range(-5f, 5f), Random.Range(-5f, 5f));
            }
        }
    }
}
