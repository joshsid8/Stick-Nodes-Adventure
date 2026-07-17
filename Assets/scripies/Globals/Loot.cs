using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class Loot : MonoBehaviour
{
    [Header("Loot")]
    public GameObject moneyLoot;
    public CurrencySO currencyType;
    public int minMoneyAmount;
    public int maxMoneyAmount;
    
    public GameObject[] itemLoot;
    
    public CollectableSOBase[] possibleItems;
    public float[] itemChance;
    
    private bool dropping = true;
    [Space]
    [Header("Popping (Objects getting moved a certain way)")]
    [SerializeField] private float minPopX;
    [SerializeField] private float maxPopX;
    
    [SerializeField] private float minPopY;
    [SerializeField] private float maxPopY;

    public void DropLoot()
    {
        if (dropping)
        {
            dropping = false;
            var targetScene = gameObject.scene;
            
            for (var i = 0; i < possibleItems.Length; i++)
            {
                var iRng = Random.Range(0.00f, 101.00f);
                if (iRng <= itemChance[i])
                {
                    var item = Instantiate(itemLoot[i], transform.position, transform.rotation);
                    item.GetComponent<Collectable>().collectable = possibleItems[i];
                    item.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(Random.Range(minPopX, maxPopX), Random.Range(minPopY, maxPopY));
                    SceneManager.MoveGameObjectToScene(item, targetScene);
                }
            }

            var rng = Random.Range(minMoneyAmount, maxMoneyAmount);
            for (var i = 0; i < rng; i++)
            {
                var money = Instantiate(moneyLoot, transform.position, transform.rotation);
                money.GetComponent<Collectable>().collectable = currencyType;
                money.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(Random.Range(minPopX, maxPopX), Random.Range(minPopY, maxPopY));
                SceneManager.MoveGameObjectToScene(money, targetScene);
            }
        }
    }
}
