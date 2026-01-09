using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyFly : MonoBehaviour
{
    // Fly stuff
    public bool isChasing;
    [SerializeField] private float playerRadius;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float[] flightOffset = new float[3];
    [SerializeField] private Vector2 flyDirection;
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float smoothing = 0.15f;
    private bool isEasing;
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
            StartCoroutine(EaseOut());
            var orng = Random.Range(-8, 8);
            orng = orng < 3 && orng > 0 ? 4 : orng > -3 && orng < 0 ? -4 : orng;
            flightOffset[0] = orng;
            flightOffset[1] = Random.Range(5.0f, 8.0f);
            yield return new WaitForSeconds(1.5f);
            flightOffset[2] = Random.Range(-1, 1);
            yield return new WaitForSeconds(1.5f);
            flightOffset[2] = Random.Range(-1, 1);
        }
    }

    // Update is called once per frame
    private void FixedUpdate()
    {
        flyDirection = new Vector2(playerTransform.position.x + flightOffset[0], playerTransform.position.y + flightOffset[1]);
        var targetPosition = flyDirection + new Vector2(flightOffset[2], flightOffset[2]);
        var smoothedPosition = Vector2.Lerp(rb.position, targetPosition, smoothing);
        rb.linearVelocity = (smoothedPosition - rb.position) * moveSpeed;
    }

    private IEnumerator EaseOut()
    {
        isEasing = true;
        var previousMoveSpeed = moveSpeed;
        if (rb.linearVelocity.x == 0) moveSpeed = 0; 
        else moveSpeed = Math.Abs(rb.linearVelocity.x * 2);
        while (moveSpeed < previousMoveSpeed)
        {
            moveSpeed += Time.deltaTime * 4;
            yield return new WaitForFixedUpdate();
        }

        moveSpeed = previousMoveSpeed;
        isEasing = false;

    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(flyDirection + new Vector2(flightOffset[2], flightOffset[2]), playerRadius);
    }
}
