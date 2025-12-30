using System.Collections;
using UnityEngine;
public class EnemyFly : MonoBehaviour
{
    // Fly stuff
    public bool isChasing;
    [SerializeField] private float playerRadius;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float[] flightOffset = new float[2];
    [SerializeField] private Vector2 flyDirection;
    [SerializeField] private float moveSpeed = 5f;
    
    // Components
    private EnemyAttack ea;
    private Rigidbody2D rb;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        ea = GetComponent<EnemyAttack>();
        rb = GetComponent<Rigidbody2D>();
        
        playerTransform = PlayerHealth.Instance.gameObject.transform;
        while (isChasing)
        {
            yield return new WaitForSeconds(3f);
            flightOffset[0] = Random.Range(-10, 10);
            flightOffset[1] = Random.Range(5, 10);
        }
    }

    // Update is called once per frame


    private void Update()
    {flyDirection = new Vector2(playerTransform.position.x + flightOffset[0], playerTransform.position.y + flightOffset[1]);
        // Calculate the distance between the flyer and the offset point
        var distance = Vector2.Distance(transform.position, flyDirection);
        
        // Let the flyer fly slowly to the offset point
        var direction = (flyDirection - rb.position).normalized;
        rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, direction, moveSpeed * Time.fixedDeltaTime);
        
        print($"from {name} - {distance}px away from the player");
        // Also let there be a random changing offset thereof that the flyer should follow
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(flyDirection, playerRadius);
    }
}
