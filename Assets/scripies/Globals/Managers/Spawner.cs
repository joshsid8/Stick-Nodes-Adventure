using System;
using UnityEngine;
using System.Collections;
using Random = UnityEngine.Random;

public class Spawner : MonoBehaviour
{
    [SerializeField] private EnemySpawn[] eSpawn;
    
    public GameObject[] spawnPoints;
    public float spawnTime;
    public bool spawnOnStart = true;
    public bool canSpawn = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        if (canSpawn)
        {
            foreach (var spawn in eSpawn)
            {
                for (var i = 0; i < spawn.spawnAmount; i++)
                {
                    if (spawnOnStart)
                        Instantiate(spawn.objectToSpawn, spawnPoints[0].transform.position, Quaternion.identity);
                    yield return new WaitForSeconds(spawnTime);
                }
            }
        }
    }

    private IEnumerator Spawn()
    {
        if (canSpawn)
        {
            foreach (var spawn in eSpawn)
            {
                for (var i = 0; i < spawn.spawnAmount; i++)
                {
                    yield return new WaitForSeconds(spawnTime);
                    Instantiate(spawn.objectToSpawn, spawnPoints[Random.Range(0,
                        spawnPoints.Length)].transform.position, Quaternion.identity);
                }
            }
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy")) canSpawn = false;
    }
    
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.gameObject.layer == LayerMask.NameToLayer("EDead") || collision.CompareTag("Enemy") && isActiveAndEnabled)
        {
            canSpawn = true;
            StartCoroutine(Spawn());
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        foreach (var spawn in spawnPoints)
        {
            Gizmos.DrawWireSphere(spawn.transform.position, 1f);
        }
    }

    [Serializable]
    public class EnemySpawn
    {
        public GameObject objectToSpawn;
        public float spawnAmount;
    }
}