using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace scripies.Enemy
{
    public class EnemyFly : MonoBehaviour
    {
        [Header("Fly checks")] public bool isChasing;
        public bool isTargeting;
        [SerializeField] private float baseFlySpeed = 10f;
        [HideInInspector] public float currentFlySpeed = 10f;
        [SerializeField] private float flyAcceleration = 10f;

        [Space] [Header("Fly movement")] [SerializeField]
        private float[] flightOffset = new float[3];

        [SerializeField] private Vector2 flyDirection;
        [SerializeField] private float smoothing = 0.15f;
        public Vector2 targetPositions;
        [SerializeField] private float[] timer = new float[2];
        public float[] gravities = { 8, 0 };

        [Space] [Header("Player refs")] [SerializeField]
        private float playerRadius;

        [SerializeField] private Transform playerTransform;

        // Components
        private EnemyAttack ea;
        private Rigidbody2D rb;

        private void Awake()
        {
            ea = GetComponent<EnemyAttack>();
            rb = GetComponent<Rigidbody2D>();
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void OnEnable()
        {
            currentFlySpeed = baseFlySpeed;
            playerTransform = GameObject.FindGameObjectWithTag("Player").transform;
            rb.gravityScale = gravities[1];
            ea.animator.SetBool("isFlying", true);
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

        public void CeaseFlying()
        {
            ea.isFlying = false;
            isChasing = false;
            isTargeting = false;
            rb.gravityScale = gravities[0];
            ea.animator.SetBool("isFlying", false);
            enabled = false;
        }

        private void Update()
        {
            timer[0] -= Time.deltaTime;
            timer[1] -= Time.deltaTime;

            if (timer[0] <= 0)
            {

                if (rb.linearVelocity.x == 0) currentFlySpeed = 0;
                else currentFlySpeed = Math.Abs(rb.linearVelocity.x * 2);

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

            if (currentFlySpeed < baseFlySpeed) currentFlySpeed += Time.deltaTime * flyAcceleration;
            else currentFlySpeed = baseFlySpeed;
        }

        private void FixedUpdate()
        {
            if (isChasing && !isTargeting)
            {
                flyDirection = new Vector2(playerTransform.position.x + flightOffset[0],
                    playerTransform.position.y + flightOffset[1]);
                var target = flyDirection + new Vector2(flightOffset[2], flightOffset[2]);
                var smoothedPosition = Vector2.Lerp(rb.position, target, smoothing);
                var force = (smoothedPosition - rb.position) * currentFlySpeed;
                rb.AddForce(force);
            }
            else if (isTargeting)
            {
                flyDirection = new Vector2(playerTransform.position.x + targetPositions.x,
                    playerTransform.position.y + targetPositions.y);
                var smoothedPosition = Vector2.Lerp(rb.position, flyDirection, smoothing);
                var force = (smoothedPosition - rb.position) * currentFlySpeed;
                rb.AddForce(force);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(flyDirection + new Vector2(flightOffset[2], flightOffset[2]), playerRadius);
        }
    }
}