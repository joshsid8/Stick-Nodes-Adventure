using System;
using System.Collections;
using scripies.Player;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(CollectableTrigger))]
public class Collectable : MonoBehaviour
{
    public CollectableSOBase collectable;

    public bool isStray = true;
    private bool gracePeriod;
    public bool pickable;
    [SerializeField] private bool onGround;
    [SerializeField] private float edgeCheckRay = 2f;
    public float playerFinderHitbox = 2f;
    [SerializeField] private float flySpeed = 1f;
    private Rigidbody2D rb;
    private RaycastHit2D hit;
    public void Collect(GameObject objectCollected)
    {
        if (collectable == null)
            throw new NullReferenceException($"item {gameObject.name} has no collectable. If it were initialised with one, it may mean its data wasnt loaded correctly.");
        collectable.Collect(objectCollected);
    }

    private IEnumerator Start()
    {
        rb = GetComponent<Rigidbody2D>();
        yield return new WaitForSeconds(1f);
        gracePeriod = true;
        pickable = true;
    }

    private void Update()
    {
        if (collectable != null) name = collectable.itemName; // else print($"aint no collectable for {name}");
        onGround = Physics2D.Raycast(transform.position, Vector2.down * transform.localScale.y, edgeCheckRay, LayerMask.GetMask("Ground", "Platform"));
        if (!pickable) return;
        if (onGround && gracePeriod)
        {
            rb.gravityScale = 0;
            GetComponent<Collider2D>().isTrigger = true;
        }
        Vector2 flyDirection = (PlayerHealth.Instance.gameObject.transform.position - transform.position).normalized;
        hit = Physics2D.CircleCast(transform.position, playerFinderHitbox, Vector2.zero, 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null && GetComponent<Collider2D>().isTrigger && isStray)
        {
            flySpeed += Time.fixedDeltaTime * 20;
            rb.linearVelocity = flyDirection * flySpeed;
        }
        else if (flySpeed > 0) flySpeed -= Time.fixedDeltaTime * 20;
        
        if (onGround && hit.collider == null || !isStray && onGround) rb.linearVelocity = Vector2.zero;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 rayDirection3 = Vector2.down * transform.localScale.y * edgeCheckRay;
        Gizmos.DrawLine(transform.position, transform.position + rayDirection3);
        Gizmos.DrawWireSphere(transform.position, playerFinderHitbox);
    }
}
