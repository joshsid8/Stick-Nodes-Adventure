using System.Collections;
using UnityEngine;

public class FindFloor : MonoBehaviour
{
    // Detect Floor and sheeeee
    private RaycastHit2D[] hit;
    private bool foundSurface;
    [SerializeField] private LayerMask surfaceLayers;
    public Transform bottomPoint;
    [SerializeField] private float rayDistance;
    [SerializeField] private float raySpeed;
    [SerializeField] private MonoBehaviour scriptToWake;
    private bool toggle;
    [SerializeField] private LayerMask masks;
    
    // Update is called once per frame
    private void FixedUpdate()
    {
        hit = Physics2D.RaycastAll(bottomPoint.position, Vector2.down, rayDistance, surfaceLayers);
        foreach (var h in hit) print($"Found {h.collider.gameObject.name} with y position {h.collider.gameObject.transform.position.y}");
        foundSurface = hit != null;
        if (!foundSurface)
        {
            rayDistance += raySpeed;
        }
        else if (foundSurface && !toggle)
        {
            toggle = true;
            FoundSurface(hit[0].collider.gameObject);
        }
    }

    private void FoundSurface(GameObject surface)
    {
        var fahhh = bottomPoint.transform.position = new Vector3(gameObject.transform.position.x, surface.transform.position.y + surface.transform.lossyScale.y / 2, surface.transform.position.z);
        print($"position: {fahhh}. this is because {surface.name}'s y position is {surface.transform.position.y}, adding {surface.transform.lossyScale.y} to it.");
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
