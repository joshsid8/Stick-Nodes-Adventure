using System.Collections.Generic;
using SasquatchSpring;
using SasquatchInspector.Editor;
using SasquatchInspector.Runtime;
using UnityEngine;

namespace SasquatchInspector.Showcase
{
    internal enum ItemRarity { Common, Rare, Epic, Legendary }

    // Backs the "Bonuses" showcase page: a SceneReference field (rendered via SceneReferenceDrawer), a SpringConfig
    // shown with the Spring widget, a SerializedDictionary or two (rendered via SerializedDictionaryDrawer), plus a
    // button that opens the bonus Spring Physics Sandbox. Lives in the Editor assembly, so it can call the editor
    // window directly.
    //
    // ItemRarity shows that dictionary KEYS are not limited to strings; rarityColors shows that dictionary
    // VALUES get their full editor, hence real colour swatches. ammo and element are the two fields the
    // SasquatchString template's {tokens} name — edit either and the preview moves.
    //
    // nativeRarityColors is Unity's OWN dictionary, for the Dictionary Table display type to draw.
    // [SerializeField] is not optional there even on a public field - Unity's dictionary backend demands it
    // (analyzer UAC1015), the one place it breaks its own "public fields serialize" rule - so the declaration
    // is worth showing exactly as a buyer must write it. It is gated whole on UNITY_6000_6_OR_NEWER: on
    // 6.0-6.5 the field cannot serialize at all, and an ungated one would greet a buyer opening the vendor's
    // own showcase with the "Unity can't serialize this" conversion card.
    //
    // Kept off the fields themselves: a comment directly above a member is lifted verbatim into the
    // sandbox's "C# Source" pane (ShowcaseSource.ExtendBackward), which shows bare declarations. That is why
    // the nativeRarityColors note above lives here and not on the field - it leaked 12 comment lines into the
    // pane (6 lines x the 2 pages that render the field) until TestShowcaseSourceSnippetsCarryNoComments
    // caught it on 6.6, the only stream where the gated field compiles at all.
    internal class ShowcaseBonusesTarget : ScriptableObject
    {
        public SceneReference targetScene;

        public SpringConfig spring = SpringConfig.Bouncy;

        public SerializedDictionary<string, int> lootWeights = new SerializedDictionary<string, int>
        {
            { "Common", 60 },
            { "Rare", 25 },
            { "Epic", 12 },
            { "Legendary", 3 },
        };

        public SerializedDictionary<ItemRarity, Color> rarityColors = new SerializedDictionary<ItemRarity, Color>
        {
            { ItemRarity.Common,    new Color(0.75f, 0.78f, 0.80f) },
            { ItemRarity.Rare,      new Color(0.30f, 0.55f, 0.95f) },
            { ItemRarity.Epic,      new Color(0.65f, 0.35f, 0.90f) },
            { ItemRarity.Legendary, new Color(0.95f, 0.70f, 0.20f) },
        };

#if UNITY_6000_6_OR_NEWER
        [SerializeField] public Dictionary<ItemRarity, Color> nativeRarityColors = new Dictionary<ItemRarity, Color>
        {
            { ItemRarity.Common,    new Color(0.75f, 0.78f, 0.80f) },
            { ItemRarity.Rare,      new Color(0.30f, 0.55f, 0.95f) },
            { ItemRarity.Epic,      new Color(0.65f, 0.35f, 0.90f) },
            { ItemRarity.Legendary, new Color(0.95f, 0.70f, 0.20f) },
        };
#endif

        public SerializedDictionary<string, int> styleTable = new SerializedDictionary<string, int>
        {
            { "Health", 100 },
            { "Stamina", 75 },
            { "Mana", 40 },
        };

        public SerializedDictionary<string, int> styleMinimal = new SerializedDictionary<string, int>
        {
            { "Health", 100 },
            { "Stamina", 75 },
            { "Mana", 40 },
        };

        public SerializedDictionary<string, float> styleLedger = new SerializedDictionary<string, float>
        {
            { "Forest", 0.45f },
            { "Cavern", 0.30f },
            { "Summit", 0.15f },
            { "Boss Arena", 0.10f },
        };

        public SerializedDictionary<ItemRarity, float> styleMapping = new SerializedDictionary<ItemRarity, float>
        {
            { ItemRarity.Common, 0.60f },
            { ItemRarity.Rare, 0.25f },
            { ItemRarity.Epic, 0.12f },
            { ItemRarity.Legendary, 0.03f },
        };

        public int ammo = 12;

        public string element = "Fire";

        public SasquatchString description =
            new SasquatchString("Deals <b><color=#FF8844>{ammo}</color></b> {element} damage.");

        // A template authored in the inspector binds itself; one built in a FIELD INITIALIZER cannot pass
        // `this`, so bind it here. Struct field, mutated in place — see SasquatchString.Bind.
        private void OnEnable() => description.Bind(this);

        public void OpenSpringSandbox() => SpringSandboxWindow.Open();
    }
}
