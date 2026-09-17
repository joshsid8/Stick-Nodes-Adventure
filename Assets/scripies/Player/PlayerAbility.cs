using System;
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
        
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        private void Start()
        {
            pc = GetComponent<PlayerController>();
            pd = GetComponent<PlayerDefense>();
            ability = GetComponent<PerformAbility>();

            maxEnergy = energy;
            GameEvents.AddPlayerEnergy += AddEnergy;
            GameEvents.PlayerPerformAbility += TogglePa;
            
            pc.input.Player.Ability.started += _ =>
            {
                if (ability.objects[blastIndex] == null || pc.isPerformingAction || !canAbility[blastIndex]) return;
                Execute(blastIndex);
                isHolding = true;
            };
            pc.input.Player.Ability.canceled += _ => isHolding = false;
            pc.input.Player.AbilitySwap.performed += _ => AbilitySwap();
        }

        private float backupTimer;
        // Update is called once per frame!!!
        private void Update()
        {
            if (pc.canAct && !pc.isDashing && !pc.isWallSliding) AbilityCooldowns();
            Energy();
            
            timeBetweenCast += Time.deltaTime;
            if (timeBetweenCast >= 1.5f) spamAmount = 0;
            if (isPerformingAbility) backupTimer += Time.deltaTime;
            else backupTimer = 0;
            if (backupTimer < 5f || !isPerformingAbility) return; // TODO: This was made as a makeshift fix to stop the player from only being able to walk. Instead of this, make an actual fix
            isPerformingAbility = false;
            isHolding = false;
            backupTimer = 0;
            Debug.LogError("You were likely frozen for too long. disabling isPerformingAbility");
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
            if (!hasEnoughEnergy || ability.unavailable) return;
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
                
            if (timeBetweenCast <= 1.5f) spamAmount++; else spamAmount = 0;
            timeBetweenCast = 0;
        }

        private void AbilitySwap()
        {
            if (isPerformingAbility) return;
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
        
        private void AbilityCooldowns()
        {
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
        
        private static int GetType(AbilityObjSO ab)
        {
            return ab.abilityPrefab.GetComponent<Projectile>() != null ? 0 :
                   ab.abilityPrefab.GetComponentInChildren<Pillar>() != null ? 1 :
                   ab.abilityPrefab.GetComponentInChildren<Beam>() != null ? 2 : 0;
        }

        private void AddEnergy(float amount)
        {
            energy += amount;
            if (energy > maxEnergy) energy = maxEnergy;
        }

        private void TogglePa(bool state)
        {
            isPerformingAbility = state;
            print($"toggling to {state}");
            if (!state) StopAllCoroutines(); // Stop any pending safety resets if we manually release
            else StartCoroutine(AbilitySafetyReset());
        }

        private IEnumerator AbilitySafetyReset()
        {
            yield return new WaitForSeconds(5f); // 5 second safety net
            if (!isPerformingAbility) yield break;
            Debug.LogWarning("Ability safety reset triggered!");
            isPerformingAbility = false;
            isHolding = false;
        }
        
        private void Energy()
        {
            if (!isPerformingAbility)
            {
                if (spamAmount >= 3) energy -= Time.deltaTime * 3f;
                else
                {
                    if (PlayerHealth.Instance.inCombat) energy += Time.deltaTime * 2 * energyRefill;
                    else energy += Time.deltaTime * 5 * energyRefill;
                }
            }

            if (energy >= maxEnergy) energy = maxEnergy;
            else if (energy < 0) energy = 0;

            energyBar.fillAmount = energy / maxEnergy;
        }
    }
}