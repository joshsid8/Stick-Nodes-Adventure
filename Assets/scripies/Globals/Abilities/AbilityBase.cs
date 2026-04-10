using UnityEngine;
using UnityEngine.Serialization;

public abstract class AbilityBase : MonoBehaviour
{
    [Tooltip("This is for the whatever gets hurt. \n1: Enemies/Bosses\n2: Player\nDefault/0: neutral/all")]
    public int typeFactor;
    public int elementType;
    [FormerlySerializedAs("beamDamage")]
    [FormerlySerializedAs("projectileDamage")]
    [FormerlySerializedAs("pillarDamage")]
    public float damage;
    
    [FormerlySerializedAs("beamKnockbackForce")]
    [FormerlySerializedAs("projectileKnockbackForce")]
    public Vector2 knockbackForce;
    
    public bool isDirectional;
    
    [FormerlySerializedAs("beamStunTime")]
    [FormerlySerializedAs("projectileStunTime")]
    [FormerlySerializedAs("pillarStunTime")]
    public float stunTime;
    
    public float energyRequired;
    
    [FormerlySerializedAs("abilityCooldown")]
    public float cooldown;
    
    [FormerlySerializedAs("beamProc")]
    [FormerlySerializedAs("pillarProc")]
    public float proc;

    [FormerlySerializedAs("beamProcTimer")]
    [FormerlySerializedAs("pillarProcTimer")]
    public float procTimer;
    
    [FormerlySerializedAs("beamLifetime")]
    [FormerlySerializedAs("projectileLifeTime")]
    [FormerlySerializedAs("pillarLifeTime")]
    public float lifeTime;
    
    [FormerlySerializedAs("beamTimer")]
    [FormerlySerializedAs("pillarTimer")]
    public float lifeTimer;
    public bool isHoldable;
    
    public Transform playerTransform;
    public Animator animator;
    
    public GameObject parentObject;
    public Collider2D coll;
    public PlayerAbility pa;
    
    [FormerlySerializedAs("eruptAnim")]
    public AnimationClip anim;
    
    [FormerlySerializedAs("collideAnim")]
    [FormerlySerializedAs("finishAnim")]
    public AnimationClip endAnim;
}
