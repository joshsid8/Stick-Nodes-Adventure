using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CollectableTrigger : MonoBehaviour
{
    private Collectable collectable;

    private void Awake() => collectable = GetComponent<Collectable>();

    public IEnumerator OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.collider.CompareTag("Player")) yield break;
        yield return new WaitForSeconds(0.1f);
        collectable.Collect(collision.gameObject);
        Destroy(gameObject);
    }
    public void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;
        collectable.Collect(collision.gameObject);
        Destroy(gameObject);
    }
}
