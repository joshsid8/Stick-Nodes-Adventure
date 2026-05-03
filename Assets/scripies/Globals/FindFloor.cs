using System.Collections;
using UnityEngine;

public class FindFloor : MonoBehaviour
{
    // Detect Floor and sheeeee
    [SerializeField] private LayerMask surfaceLayers;
    public Transform bottomPoint;
    [SerializeField] private float rayDistance = 50f;
    [SerializeField] private MonoBehaviour scriptToWake;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        var hit = Physics2D.Raycast(bottomPoint.position, Vector2.down, rayDistance, surfaceLayers);

        if (hit.collider != null) FoundSurface(hit.point);
        else enabled = false;
    }

    private void FoundSurface(Vector2 hitPoint)
    {
        bottomPoint.transform.position = new Vector3(transform.position.x, hitPoint.y, transform.position.z);
        if (scriptToWake != null) scriptToWake.enabled = true;
        enabled = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawLine(bottomPoint.position, bottomPoint.position + Vector3.down * rayDistance);
    }
}
