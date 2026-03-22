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
    
    public void Collect(GameObject objectCollected)
    {
        collectable.Collect(objectCollected);
    }
    
    private void Update()
    {
        onGround = Physics2D.Raycast(transform.position, Vector2.down * transform.localScale.y, edgeCheckRay, LayerMask.GetMask("Ground", "Platform"));
        if (GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero && onGround)
        {
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            GetComponent<Collider2D>().isTrigger = true;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 rayDirection3 = Vector2.down * transform.localScale.y * edgeCheckRay;
        Gizmos.DrawLine(transform.position, transform.position + rayDirection3);
    }
}
