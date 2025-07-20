using System;
using UnityEngine;
using System.Collections;
using Random = UnityEngine.Random;

public class Spawner : MonoBehaviour
{
    public GameObject objectToSpawn;
    public GameObject[] spawnPoints;
    
    public float spawnTime;
    public float spawnAmount;
    public bool spawnOnStart = true;
    public bool canSpawn = true;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        if (canSpawn)
        {
            if (spawnOnStart)
                Instantiate(objectToSpawn, spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position,
                    Quaternion.identity);
            for (int i = 0; i < spawnAmount; i++)
            {
                yield return new WaitForSeconds(spawnTime);
                Instantiate(objectToSpawn, spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position,
                    Quaternion.identity);
            }
        }
    }

    private IEnumerator Spawn()
    {
        if (canSpawn)
        {
            for (int i = 0; i < spawnAmount; i++)
            {
                yield return new WaitForSeconds(spawnTime);
                Instantiate(objectToSpawn, spawnPoints[Random.Range(0, spawnPoints.Length)].transform.position,
                    Quaternion.identity);
            }
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy")) canSpawn = false;
    }
    
    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Dead"))
        {
            canSpawn = true;
            StartCoroutine(Spawn());
        }
    }
}
