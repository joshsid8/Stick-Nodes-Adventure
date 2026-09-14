using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal class ShowcaseValidationTarget : ScriptableObject
    {
        public string playerName = "";

        public int level = 120;

        public int teamSize = 3;

        public bool enableAdvanced = false;

        public float advancedTuning = 0.5f;

        public string advancedNote = "Flip the toggle to edit me.";

        public bool ValidateTeamSize() => teamSize % 2 == 0;
    }
}
