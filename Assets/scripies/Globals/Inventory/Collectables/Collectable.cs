using System;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(CollectableTrigger))]
public class Collectable : MonoBehaviour
{
    public CollectableSOBase collectable;

    [SerializeField] private bool isInitial;
    [SerializeField] private bool onGround;
    [SerializeField] private float edgeCheckRay = 2f;
    [SerializeField] private float playerFinderHitbox = 2f;
    [SerializeField] private float flySpeed = 1f;
    private Rigidbody2D rb;
    private RaycastHit2D hit;
    public void Collect(GameObject objectCollected) => collectable.Collect(objectCollected);


    private void Start() => rb = GetComponent<Rigidbody2D>();

    private void Update()
    {
        name = collectable.itemName;
        onGround = Physics2D.Raycast(transform.position, Vector2.down * transform.localScale.y, edgeCheckRay, LayerMask.GetMask("Ground", "Platform"));
        if (rb.linearVelocity == Vector2.zero && onGround)
        {
            rb.gravityScale = 0;
            GetComponent<Collider2D>().isTrigger = true;
        }
        Vector2 flyDirection = (PlayerHealth.Instance.gameObject.transform.position - transform.position).normalized;
        hit = Physics2D.CircleCast(transform.position, playerFinderHitbox, Vector2.zero, 0f, LayerMask.GetMask("Player"));
        if (hit.collider != null && GetComponent<Collider2D>().isTrigger)
        {
            flySpeed += Time.fixedDeltaTime * 20;
            rb.linearVelocity = flyDirection * flySpeed;
        }
        else if (flySpeed > 0) flySpeed -= Time.fixedDeltaTime * 20;
        
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 rayDirection3 = Vector2.down * transform.localScale.y * edgeCheckRay;
        Gizmos.DrawLine(transform.position, transform.position + rayDirection3);
        
        Gizmos.DrawWireSphere(transform.position, playerFinderHitbox);
    }
}
