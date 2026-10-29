using System;
using scripies.Core;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class Loot : MonoBehaviour
{
    private GameObject persistentItem;
    [Header("Loot")]
    public GameObject moneyLoot;
    public CurrencySO currencyType;
    public int minMoneyAmount;
    public int maxMoneyAmount;
    
    public LootItem[] lootItems = new LootItem[1];
    
    private bool dropping = true;
    [Space]
    [Header("Popping (Objects getting moved a certain way)")]
    [SerializeField] private float minPopX;
    [SerializeField] private float maxPopX;
    
    [SerializeField] private float minPopY;
    [SerializeField] private float maxPopY;
    
    private void Awake()
    {
        persistentItem = GameObject.Find("persistentItem");
        if (persistentItem == null && InvManager.Instance != null)
            persistentItem = InvManager.Instance.persistentItem;
    }
    private void OnEnable() => GameEvents.DropLoot += DropLoot;
    private void OnDisable() => GameEvents.DropLoot -= DropLoot;

    private void DropLoot(GameObject obj)
    {
        if (obj != gameObject) return;
        if (dropping)
        {
            dropping = false;
            var targetScene = gameObject.scene;
            if (persistentItem == null)
            {
                persistentItem = GameObject.Find("persistentItem");
                if (persistentItem == null && InvManager.Instance != null)
                    persistentItem = InvManager.Instance.persistentItem;
            }
            
            foreach (var lootItem in lootItems)
            {
                if (lootItem == null || lootItem.item == null) continue;
                var iRng = Random.Range(0.00f, 100.00f);
                if (iRng <= lootItem.chance)
                {
                    var prefabToSpawn = lootItem.obj != null ? lootItem.obj : persistentItem;
                    if (prefabToSpawn == null) continue;
                    var item = Instantiate(prefabToSpawn, transform.position, transform.rotation);
                    SceneManager.MoveGameObjectToScene(item, targetScene);
                    item.GetComponent<Collectable>().collectable = lootItem.item;
                    item.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(Random.Range(minPopX, maxPopX), Random.Range(minPopY, maxPopY));
                }
            }

            if (moneyLoot != null && currencyType != null)
            {
                var rng = Random.Range(minMoneyAmount, maxMoneyAmount);
                for (var i = 0; i < rng; i++)
                {
                    var money = Instantiate(moneyLoot, transform.position, transform.rotation);
                    SceneManager.MoveGameObjectToScene(money, targetScene);
                    money.GetComponent<Collectable>().collectable = currencyType;
                    money.GetComponent<Rigidbody2D>().linearVelocity = new Vector2(Random.Range(minPopX, maxPopX), Random.Range(minPopY, maxPopY));
                }
            }
        }
    }
    
    [Serializable]
    public class LootItem
    {
        public GameObject obj;
        public CollectableSOBase item;
        public int amount = 1;
        public float chance = 100;
    }
}
