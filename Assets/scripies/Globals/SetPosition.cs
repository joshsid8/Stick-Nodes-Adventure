using System.Collections;
using UnityEngine;

public class SetPosition : MonoBehaviour
{
    // Detect Floor and sheeeee
    [SerializeField] private LayerMask surfaceLayers;
    public Transform bottomPoint;
    [SerializeField] private float rayDistance = 50f;
    [SerializeField] private MonoBehaviour scriptToWake;

    private RaycastHit2D hit;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        var initHit = Physics2D.Raycast(bottomPoint.position, Vector2.down, rayDistance, surfaceLayers);

        if (initHit.collider != null) FoundSurface(initHit.point);
        else CancelScript();
    }

    private void FoundSurface(Vector2 hitPoint)
    {
        bottomPoint.transform.position = new Vector3(transform.position.x, hitPoint.y, transform.position.z);
        hit = Physics2D.CircleCast(new Vector2(bottomPoint.position.x, transform.position.y), 0.2f, Vector2.down,
            0f, LayerMask.GetMask("Ground", "Platform", "Wall"));
        if (hit.collider == null) ActivateScript();
        else CancelScript();
    }

    private void ActivateScript()
    {
        if (scriptToWake != null) scriptToWake.enabled = true;
        enabled = false;
    }

    private void CancelScript()
    {
        //PlayerHealth.Instance.GetComponent<PlayerAbility>().isPerformingAbility = false;
        Destroy(bottomPoint.gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawLine(bottomPoint.position, bottomPoint.position + Vector3.down * rayDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.2f);
    }
}
