using System.Collections;
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

        [SerializeField] private float timeBetweenCast;
        [SerializeField] private float spamPeriod;
        [SerializeField] private int spamAmount;

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
            if (Input.GetButtonDown("FireSwap") && !isPerformingAbility)
            {
                blastIndex++;
                if (blastIndex >= 3) blastIndex = 0;
                
                // If the slot is empty, keep skipping until we find one or return to 0
                var start = blastIndex;
                while (ability.objects[blastIndex] == null)
                {
                    blastIndex++;
                    if (blastIndex >= 3) blastIndex = 0;
                    if (blastIndex == start) break;
                }
            }

            if (pc.canAct && !pc.isDashing && !pc.isWallSliding) Fire();
            Energy();
            timeBetweenCast += Time.deltaTime;
            
            animator.SetBool("isPerformingAbility", isPerformingAbility);
            animator.SetBool("isHolding", isHolding);
            
            if (timeBetweenCast >= 1.5f) spamAmount = 0;
        }

        private void Fire()
        {
            // Made it so that all 3 slots can be any type of ability
            if (!pc.isWallJumping && !isPerformingAbility && !ability.isActivated && canAbility[blastIndex])
            {
                if (Input.GetButtonDown("Fire") && ability.objects[blastIndex] != null) 
                    Execute(blastIndex);
            }
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
                
                if (timeBetweenCast <= 1.5f)
                {
                    spamAmount++;
                    if (spamAmount == 3) StartCoroutine(AntiSpam());
                }
                timeBetweenCast = 0;
            }
        }

        private static int GetType(AbilityObjSO ab)
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

        private IEnumerator AntiSpam()
        {
            //TODO: Think about if when spamming cooldown should be nerfed or energy should decrement
            //var prevCooldownMult = abilityCooldownMult;
            
            while (spamAmount >= 3)
            {
                spamPeriod += Time.deltaTime;
                /*if (spamPeriod % spamPeriod > 0.5f && abilityCooldownMult < 3)
                {
                    spamPeriod = 0;
                    abilityCooldownMult += 0.2f;
                    print($"nerfing cooldown to {abilityCooldownMult}");
                }*/
                yield return null;
            } 
            spamPeriod = 0;
            //abilityCooldownMult = prevCooldownMult;
        }
        private void Energy()
        {
            if (!isPerformingAbility)
            {
                if (PlayerHealth.Instance.inCombat) energy += Time.deltaTime * 2 * energyRefill;
                else if (spamPeriod > 0) energy -= Time.deltaTime;
                else energy += Time.deltaTime * 5 * energyRefill;
            }

            if (energy >= maxEnergy) energy = maxEnergy;
            else if (energy < 0) energy = 0;

            energyBar.fillAmount = energy / maxEnergy;
        }
    }
}