using UnityEngine;

public class EnemyActiveDefense : MonoBehaviour
{
    // This is a very simple script, allowing the enemy to attack once the player is close enough.
    
    private EnemyAttack ea;
    private bool inRange;
    
    private void Start()
    {
        ea = GetComponent<EnemyAttack>();
    }
    
    private void Update()
    {
        inRange = Physics2D.OverlapCircle(ea.attackTransform.position, ea.attackRange * 1.25f, LayerMask.GetMask("Player"));
        if (ea.canAttack && !ea.isAttacking && inRange)
        {
            GetComponent<Animator>().Play(ea.evasiveMoves[Random.Range(0, ea.evasiveMoves.Length)]);
            print("Clueless.");
        }
    }
}