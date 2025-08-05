using System.Collections;
using UnityEngine;

public class FindFloor : MonoBehaviour
{
    // Detect Floor and sheeeee
    private RaycastHit2D hit;
    private bool foundSurface;
    [SerializeField] private LayerMask surfaceLayers;
    [SerializeField] private Transform bottomPoint;
    [SerializeField] private float rayDistance;
    [SerializeField] private float raySpeed;
    [SerializeField] private MonoBehaviour scriptToWake;
    private bool toggle;
    [SerializeField] private LayerMask masks;
    
    // Update is called once per frame
    private void FixedUpdate()
    {
        hit = Physics2D.Raycast(bottomPoint.position, Vector2.down, rayDistance, surfaceLayers);
        foundSurface = hit.collider != null;
        if (!foundSurface)
        {
            rayDistance += raySpeed;
        }
        else if (foundSurface && !toggle)
        {
            toggle = true;
            FoundSurface();
        }
    }

    private void FoundSurface()
    {
        gameObject.AddComponent<BoxCollider2D>();
        gameObject.GetComponent<BoxCollider2D>().offset = new Vector2(0, transform.position.y - bottomPoint.position.y - 2f);
        gameObject.GetComponent<BoxCollider2D>().size = new Vector2(0.5f, 2.5f);
        gameObject.GetComponent<BoxCollider2D>().excludeLayers = masks;
        gameObject.transform.position -= new Vector3(0, rayDistance, 0);
        scriptToWake.enabled = true;

        enabled = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Vector3 rayDirection = Vector2.up * rayDistance;
        Gizmos.DrawLine(bottomPoint.position, bottomPoint.position - rayDirection);
    }
}
