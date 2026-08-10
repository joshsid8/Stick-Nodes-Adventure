using scripies.Core;
using UnityEngine;
using UnityEngine.UI;

namespace scripies.Player
{
    public class PlayerAbility : MonoBehaviour
    {
        public Image energyBar;
        public float maxEnergy;
        public float energy;
        public float energyRefill;
        public float abilityCooldownMult = 1;

        public bool[] canAbility = new bool[3];
        public float[] currentAbilityCooldowns = new float[3];
        public float[] maxAbilityCooldowns = new float[3];
        public bool isPerformingAbility;
        public bool isHolding;
        [SerializeField] private int maxBlastIndex;
        public int blastIndex;
        [SerializeField] private int blastType;

        public PerformAbility ability;
        private PlayerController pc;
        private PlayerDefense pd;

        [HideInInspector] public Animator animator;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            animator = GetComponent<Animator>();
            pc = GetComponent<PlayerController>();
            pd = GetComponent<PlayerDefense>();
            ability = GetComponent<PerformAbility>();
            maxEnergy = energy;
            GameEvents.AddPlayerEnergy += AddEnergy;
            GameEvents.PlayerPerformAbility += TogglePa;
        }

        // Update is called once per frame
        void Update()
        {

            if (Input.GetButtonDown("FireSwap"))
            {
                blastIndex++;
                if (blastIndex > maxBlastIndex || blastIndex == maxBlastIndex && ability.objects[maxBlastIndex] == null || blastIndex == 1 && ability.objects[1] == null) 
                    blastIndex = 0;
            }

            Fire();
            Energy();
            animator.SetBool("isPerformingAbility", isPerformingAbility);
            animator.SetBool("isHolding", isHolding);
        }

        void Fire()
        {
            // Made it so that all 3 slots can be any type of ability
            if (!pc.isDashing && !pc.isWallSliding && !pc.isWallJumping && !isPerformingAbility && pc.canAct && !ability.isActivated && canAbility[blastIndex])
            {
                if (Input.GetButtonDown("Fire"))
                {
                    if (ability.objects[blastIndex].abilityPrefab.GetComponent<Projectile>() != null) 
                        Execute();
                    else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Pillar>() != null)
                        Execute(1);
                    else if (ability.objects[blastIndex].abilityPrefab.GetComponentInChildren<Beam>() != null)
                        Execute(2);
                }
            }
            if (!pc.isDashing && !pc.isWallSliding && pc.canAct)
                isHolding = Input.GetButton("Fire") ? isHolding = true : isHolding = false;

            for (var i = 0; i < canAbility.Length; i++)
            {
                if (currentAbilityCooldowns[i] < maxAbilityCooldowns[i]) currentAbilityCooldowns[i] += Time.deltaTime;
                if (currentAbilityCooldowns[i] >= maxAbilityCooldowns[i])
                {
                    canAbility[i] = true;
                    currentAbilityCooldowns[i] = maxAbilityCooldowns[i];
                }
                else canAbility[i] = false;
            }
        }

        private void Execute(int slot = 0)
        {
            if (ability == null) return;
            blastType = GetType(ability.objects[slot]);
            var abilitySo = ability.objects[slot];
            var prefabBase = abilitySo.abilityPrefab.GetComponent<AbilityBase>() ?? abilitySo.abilityPrefab.GetComponentInChildren<AbilityBase>();

            if (prefabBase == null)
            {
                Debug.LogError("Ability prefab missing AbilityBase component!");
                return;
            }

            var totalAmount = Mathf.Round(abilitySo.amount * ability.abilityAmountMult[slot]);
            var energyReq = abilitySo.energyRequired == 0 ? prefabBase.energyRequired : abilitySo.energyRequired;

            maxAbilityCooldowns[slot] = prefabBase.cooldown;
            
            var hasEnoughEnergy = energy >= energyReq * totalAmount;
            if (hasEnoughEnergy && !ability.unavailable)
            {
                ability.isActivated = true;
                ability.currentBlastType = blastType;
                ability.currentSlot = slot;
                isPerformingAbility = true;

                var animName = blastType switch
                {
                    0 => pc.isGrounded ? "Fire" : "AirFire",
                    1 => "Pillar",
                    2 => "Beam",
                    _ => "Fire"
                };
                GameEvents.TriggerPlayerAction(animName, true);
            }
        }

        int GetType(AbilityObjSO ab)
        {
            if (ab.abilityPrefab.GetComponent<Projectile>() != null) return 0;
            if (ab.abilityPrefab.GetComponentInChildren<Pillar>() != null) return 1;
            if (ab.abilityPrefab.GetComponentInChildren<Beam>() != null) return 2;
            return 0;
        }

        private void AddEnergy(float amount)
        {
            energy += amount;
            if (energy > maxEnergy) energy = maxEnergy;
        }

        private void TogglePa(bool state) => isPerformingAbility = state;

        private void Energy()
        {
            if (!isPerformingAbility)
            {
                if (PlayerHealth.Instance.inCombat) energy += Time.deltaTime * 2 * energyRefill;
                else energy += Time.deltaTime * 5 * energyRefill;
            }

            if (energy >= maxEnergy) energy = maxEnergy;
            else if (energy < 0) energy = 0;

            energyBar.fillAmount = energy / maxEnergy;
        }
    }
}