using System.Threading.Tasks;
using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseButtonsTarget : ScriptableObject
    {
        public int gold = 100;
        public string lastAction = "(none yet)";

        public void SayHello()
        {
            lastAction = "Hello!";
        }

        public void ResetData()
        {
            gold = 0;
            lastAction = "Reset";
        }

        public void AddGold(int amount)
        {
            gold += amount;
            lastAction = $"+{amount} gold";
        }

        public async Task RunLongTaskAsync()
        {
            lastAction = "Working…";
            await Task.Delay(1500);
            lastAction = "Done!";
        }

        public void Save() => lastAction = "Saved";

        public void Celebrate() => lastAction = "Nice! 🎉";
    }
}
