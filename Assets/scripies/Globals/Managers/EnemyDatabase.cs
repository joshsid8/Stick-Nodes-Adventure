using System.Collections.Generic;
using UnityEngine;

public class EnemyDatabase : MonoBehaviour
{
    public static EnemyDatabase Instance { get; private set; }
    
    private Dictionary<string, Enemy> enemyDatabase = new Dictionary<string, Enemy>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildDatabase();
    }

    private void BuildDatabase()
    {
        var allEnemies = Resources.LoadAll<Enemy>("Enemy");

        foreach (var enemy in allEnemies)
        {
            if (!enemyDatabase.ContainsKey(enemy.name))
            {
                enemyDatabase.Add(enemy.name, enemy);
            }
            else
            {
                Debug.LogWarning($"[EnemyDatabase] Duplicate enemy name found: {enemy.name}.");
            }
        }
    }
    
    public Enemy GetEnemyByName(string enemyName)
    {
        if (string.IsNullOrEmpty(enemyName)) return null;
        enemyDatabase.TryGetValue(enemyName, out var enemy);
        return enemy;
    }
}
