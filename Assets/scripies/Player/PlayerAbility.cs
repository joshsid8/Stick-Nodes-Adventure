using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerAbility : MonoBehaviour
{
    public Image energyBar;
    public TMP_Text energyText;
    public float maxEnergy;
    public float energy;
    public float energyRefill;
    public float abilityCooldownMult = 1;
    
    public bool canAbility;
    public float currentAbilityCooldown;
    public bool isPerformingAbility;
    
    [SerializeField] private int blastType;
    
    private PlayerPerformAbility ability;
    private PlayerController pc;
    private PlayerDefense pd;
    
    [HideInInspector] public Animator animator;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        pc = GetComponent<PlayerController>();
        pd = GetComponent<PlayerDefense>();
        ability = GetComponent<PlayerPerformAbility>();
        maxEnergy = energy;
    }

    // Update is called once per frame
    void Update()
    {
        Fire();
        Energy();
        animator.SetBool("isPerformingAbility", isPerformingAbility);
    }

    void Fire()
    {
        if (canAbility && !pc.isDashing && !pc.isWallSliding && !isPerformingAbility && pc.canAct && !ability.isActivated)
        {
            if (Input.GetButtonDown("Fire") && Input.GetAxisRaw("Vertical") == 0) ability.Execute(0);
            else if (Input.GetButton("Fire") && Input.GetAxisRaw("Vertical") < 0) ability.Execute(1);
            else if (Input.GetButton("Fire") && Input.GetAxisRaw("Vertical") > 0) ability.Execute(2);
            else if (Input.GetButton("Fire") && pd.isBlocking) ability.Execute(3);
        }
        
        if (currentAbilityCooldown > 0) currentAbilityCooldown -= Time.deltaTime;
        if (currentAbilityCooldown <= 0) canAbility = true;
        else canAbility = false;
    }

    private void Energy()
    {
        if (!pc.inCombat) energy += Time.deltaTime * 5 * energyRefill; else energy += Time.deltaTime * 2 * energyRefill;
        if (energy >= maxEnergy) energy = maxEnergy;
        else if (energy < 0) energy = 0;
        
        energyBar.fillAmount = energy / maxEnergy;
        energyText.text = energy == maxEnergy ? energy.ToString("F0") + " / " + maxEnergy.ToString("F0")
            : energy.ToString("F1") + " / " + maxEnergy.ToString("F0");

    }
}
