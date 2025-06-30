using System;
using UnityEngine;

public class CollectableTrigger : MonoBehaviour
{
    private Collectable collectable;

    private void Awake()
    {
        collectable = GetComponent<Collectable>();
    }

    public void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.collider.tag == "Player")
        {
            collectable.Collect(collision.gameObject);
            Destroy(gameObject);
        }
    }
    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            collectable.Collect(collision.gameObject);
            Destroy(gameObject);
        }
    }
}
