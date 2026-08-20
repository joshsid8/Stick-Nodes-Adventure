using System;
using System.Collections;
using scripies.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace scripies.Enemy
{
    public class EnemyPatrol : MonoBehaviour
    {
        [Header("Patrol")] public bool isPatrolling = true;
        public float eyeSight = 10f;
        [Space] [Header("Walking")] public float walkSpeed = 5f;
        [SerializeField] private bool canWalk = true;
        private bool isWalking = true;
        [SerializeField] private float[] walkTime = { 2, 4f };
        private float currentWalkTime;
        [SerializeField] private float randomPause;
        [SerializeField] private float[] randomPauseChance = { 20f, 101f };

        [Space] [Header("Player Check")] [SerializeField]
        private bool hasFoundPlayer;

        public Transform playerCheck;
        public LayerMask playerLayer;

        [Space] [Header("Wall Check")] [SerializeField]
        private bool touchingWall;

        [SerializeField] private Transform wallCheck;
        [SerializeField] private LayerMask whatIsWall;
        [SerializeField] private float wallCheckRay = 2f;
        [Space] [Header("Ground Check")] public bool isGrounded;
        [SerializeField] private Transform edgeCheck;
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private float edgeCheckRay = 2f;
        
        [SerializeField] private AudioClip[] footstepClips;
        [SerializeField] private AudioClip foundPlayerClip;

        [HideInInspector] public Rigidbody2D rb;
        private Animator animator;
        public EnemyController ec;

        void Awake()
        {
            animator = GetComponent<Animator>();
            rb = GetComponent<Rigidbody2D>();
            ec = GetComponent<EnemyController>();
        }

        private void OnDisable()
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isPatrolling", false);
        }

        // Update is called once per frame
        void Update()
        {
            if (ec.vegetable) return;
            if (isPatrolling) Patrol();
            
            animator.SetBool("isPatrolling", isPatrolling);
            animator.SetBool("isWalking", isWalking);

            currentWalkTime -= Time.deltaTime;
            if (currentWalkTime <= 0) isWalking = true;
        }

        void Patrol()
        {
            if (isWalking && canWalk)
                rb.linearVelocity = new Vector2(ec.isFacingRight ? walkSpeed : -walkSpeed, rb.linearVelocity.y);

            randomPause -= Time.deltaTime;
            if (randomPause <= 0 && canWalk)
            {
                var chance = Random.Range(randomPauseChance[0], randomPauseChance[1]);
                var rng = Random.Range(0, 100);
                if (rng >= chance) StartCoroutine(Pause());
                randomPause = 2;
            }

            touchingWall = Physics2D.Raycast(wallCheck.position, Vector2.down * transform.localScale.y, wallCheckRay,
                whatIsWall);
            if (touchingWall) Flip();

            isGrounded = Physics2D.Raycast(edgeCheck.position, Vector2.down * transform.localScale.y, edgeCheckRay,
                groundLayer);
            if (!isGrounded && rb.linearVelocity.y == 0) Flip();

            hasFoundPlayer = Physics2D.Raycast(playerCheck.position,
                ec.isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x, eyeSight, playerLayer);
            if (hasFoundPlayer && isPatrolling) StartCoroutine(FoundPlayer());
        }

        public IEnumerator FoundPlayer()
        {
            rb.linearVelocity = Vector2.zero;
            isPatrolling = false;
            isWalking = false;
            GameEvents.TriggerAddPlayerCombat(20, false, "EnemySpotted");
            AudioSource.PlayClipAtPoint(foundPlayerClip, transform.position);
            yield return new WaitForSeconds(0.5f);
            ec?.ChangeState(EnemyController.EnemyState.Zoning);
        }

        private IEnumerator Pause()
        {
            isWalking = false;
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            if (canWalk) currentWalkTime = Random.Range(walkTime[0], walkTime[1]);
            yield return new WaitForSeconds(currentWalkTime / 2);
            var rng = Random.Range(0, 101);
            var chance = Random.Range(0, 101);
            if (rng >= chance) Flip();
            randomPause = 3;
        }
        
        private void PlaySound(AudioClip sfx) => GameEvents.TriggerPlaySound(sfx ?? footstepClips[Random.Range(0, footstepClips.Length)], true, gameObject);

        private void Flip() => ec.Flip();

        private void OnDrawGizmosSelected()
        {
            if (!GetComponent<EnemyAttack>()) return;
            Gizmos.color = Color.grey;
            Vector3 rayDirection = ec.isFacingRight ? Vector2.right : Vector2.left * transform.localScale.x * eyeSight;
            Gizmos.DrawLine(playerCheck.position, playerCheck.position + rayDirection);
            Gizmos.color = Color.red;
            Vector3 rayDirection2 = Vector2.down * transform.localScale.y * wallCheckRay;
            Gizmos.DrawLine(wallCheck.position, wallCheck.position + rayDirection2);
            Gizmos.color = Color.green;
            Vector3 rayDirection3 = Vector2.down * transform.localScale.y * edgeCheckRay;
            Gizmos.DrawLine(edgeCheck.position, edgeCheck.position + rayDirection3);
        }
    }
}