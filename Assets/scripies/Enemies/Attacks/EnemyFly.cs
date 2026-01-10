using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyFly : MonoBehaviour
{
    // Fly stuff
    public bool isChasing;
    public bool isTargeting;
    public float moveSpeed = 10f;
    private float previousMoveSpeed;
    
    [SerializeField] private float[] flightOffset = new float[3];
    [SerializeField] private Vector2 flyDirection;
    [SerializeField] private float smoothing = 0.15f;
    public Vector2 targetPositions;
    [SerializeField] private float[] timer = new float[2];
    
    [SerializeField] private float playerRadius;
    [SerializeField] private Transform playerTransform;
    // Components
    private EnemyAttack ea;
    private Rigidbody2D rb;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        ea = GetComponent<EnemyAttack>();
        rb = GetComponent<Rigidbody2D>();
        
        previousMoveSpeed = moveSpeed;
        playerTransform = PlayerHealth.Instance.gameObject.transform;
        /*while (isChasing)
        {
            StartCoroutine(EaseIn());
            var orng = Random.Range(-8, 8);
            orng = orng < 3 && orng > 0 ? 4 : orng > -3 && orng < 0 ? -4 : orng;
            flightOffset[0] = orng;
            flightOffset[1] = Random.Range(5.0f, 8.0f);
            yield return new WaitForSeconds(1.5f);
            flightOffset[2] = Random.Range(-1, 1);
            yield return new WaitForSeconds(1.5f);
            flightOffset[2] = Random.Range(-1, 1);
        }*/
    }

    private void Update()
    {
        timer[0] -= Time.deltaTime;
        timer[1] -= Time.deltaTime;
        
        if (timer[0] <= 0)
        {
            
            if (rb.linearVelocity.x == 0) moveSpeed = 0; 
            else moveSpeed = Math.Abs(rb.linearVelocity.x * 2);
            
            var orng = Random.Range(-8, 8);
            orng = orng < 3 && orng > 0 ? 4 : orng > -3 && orng < 0 ? -4 : orng;
            flightOffset[0] = orng;
            flightOffset[1] = Random.Range(3.0f, 7.0f);
            timer[0] = 3;
        }

        if (timer[1] <= 0)
        {
            flightOffset[2] = Random.Range(-1, 1);
            timer[1] = 1.5f;
        }
        
        if (moveSpeed < previousMoveSpeed) moveSpeed += Time.deltaTime * 6.5f;
        else moveSpeed = previousMoveSpeed;
    }

    private void FixedUpdate()
    {
        if (isChasing && !isTargeting)
        {
            flyDirection = new Vector2(playerTransform.position.x + flightOffset[0], 
                playerTransform.position.y + flightOffset[1]);
            var target = flyDirection + new Vector2(flightOffset[2], flightOffset[2]);
            var smoothedPosition = Vector2.Lerp(rb.position, target, smoothing);
            rb.linearVelocity = (smoothedPosition - rb.position) * moveSpeed;
        }
        else if (isTargeting)
        {
            flyDirection = new Vector2(playerTransform.position.x + targetPositions.x, playerTransform.position.y + targetPositions.y);
            var smoothedPosition = Vector2.Lerp(rb.position, flyDirection, smoothing);
            rb.linearVelocity = (smoothedPosition - rb.position) * moveSpeed;
        }
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(flyDirection + new Vector2(flightOffset[2], flightOffset[2]), playerRadius);
    }
}
