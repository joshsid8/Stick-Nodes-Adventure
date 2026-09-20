using System;
using scripies.Enemy;
using UnityEngine;

public class EnemyDomain : MonoBehaviour
{
    [Header("Aggro")] 
    public bool enemyInBounds;
    public Collider2D domainCollider;
    
    public PlayerBounds playerBounds = new();

    private EnemyController ec;
    private Rigidbody2D rb;
    public ParkourFeatures parkourFeatures;
    public enum ReturnType
    {
        Parkour, Flying, Teleporting
    }
    [EnumButtons] public ReturnType returnMethod = ReturnType.Parkour;
    [Tooltip("For Teleporting/Flying Only")] public string returnAnim;

    private void Start()
    {
        ec = GetComponent<EnemyController>();
        rb = GetComponent<Rigidbody2D>();
        playerBounds.playerCollider = GameObject.FindGameObjectWithTag("Player").GetComponent<Collider2D>();
    }

    private void Update()
    {
        enemyInBounds = domainCollider.OverlapPoint(transform.position);
        playerBounds.playerInBounds = domainCollider.OverlapPoint(playerBounds.playerCollider.transform.position);
        
        if (playerBounds.playerInBounds) playerBounds.aggroTimer -= Mathf.Clamp(Time.deltaTime, 0, playerBounds.aggroPeriod);
        else playerBounds.aggroTimer = playerBounds.aggroPeriod;

        if (!(playerBounds.aggroTimer <= 0)) return;
        if (!enemyInBounds) ec.LostPlayer();
        else ReturnToDomain();
    }

    private void ReturnToDomain()
    {
        print($"{gameObject.name} has lost the player, returning to domain {domainCollider.gameObject.name} [WIP]");
        ec.LostPlayer(true);
        
        // If the domain been deleted, find a neighbouring domain to settle in
            // Maybe if the domain returns, they can find a way to travel back home
        
        // Go to said point using a special a mobility move declared
            // Flying: AI pathfinding (either make myself of find a script online)
            // Teleporting: simply teleport them with the target of the domain
            // Parkour, a whole new system, similar to the player, where they try to run to their domain, avoiding any obstacles by jumping and dashing
                
        // Switch to patrolling mode as normal
    }
    
    [Serializable]
    public class PlayerBounds
    {
        public bool playerInBounds;
        public Collider2D playerCollider;
        public float aggroPeriod = 5f;
        public float aggroTimer;
    }
    
    [Serializable]
    public struct ParkourFeatures
    {
        public bool running;
        public float speed;
    }
}
