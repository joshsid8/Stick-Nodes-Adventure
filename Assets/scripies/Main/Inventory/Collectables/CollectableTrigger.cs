using System.Collections;
using UnityEngine;

public class CollectableTrigger : MonoBehaviour
{
    private Collectable collectable;
    
    private SpriteRenderer spriteRenderer;
    [SerializeField] private Color[] itemColours;

    private void Start()
    {
        collectable = TryGetComponent<Collectable>(out var collectableComponent)
            ? collectableComponent
            : throw new MissingComponentException($"Collectable component in {gameObject.name} not found!");
        
        spriteRenderer = GetComponent<SpriteRenderer>();
        InvokeRepeating(nameof(ColourUpdate), 0f, 0.1f);
    }

    private void ColourUpdate()
    {
        var colType = collectable.collectable;
        if (colType == null) return;
        // No! I am NNNOT going to turn this into a switch expression!!!
        
        if (colType is ConsumableSO) spriteRenderer.color = new Color(1f, 0.525f, 0.251f);
        else if (colType is AspectSO) spriteRenderer.color = new Color(0.961f, 0.4f, 0.078f);
        else if (colType is AbilitySO) spriteRenderer.color = new Color(0.259f, 0.969f, 1f);
        else if (colType is CurrencySO) spriteRenderer.color = new Color(0.259f, 1f, 0.714f);
        else if (colType is ItemSO) spriteRenderer.color = new Color(0.8f, 1, 0.52f);
        //else print($"{gameObject.name} has an unknown collectable type: {colType.GetType()}");
    }
    
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
