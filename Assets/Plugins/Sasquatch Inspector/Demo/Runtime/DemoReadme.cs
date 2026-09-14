using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 1 — the scene's front door. Every other exhibit is a plain component whose Inspector has been
    /// designed with Sasquatch Inspector; this one explains that, and links out to the tools that build them.
    ///
    /// Note what ISN'T here: no attributes, no custom editor, no partial classes. The layout that renders this
    /// component lives in a layout asset, authored by dragging in the Layout Designer. That is the whole point
    /// of the package, so the demo scripts deliberately stay ordinary.
    /// </summary>
    public class DemoReadme : MonoBehaviour
    {
        [Tooltip("Shown as a checklist in the designed layout.")]
        public string[] tryThis =
        {
            "Select each object below and compare its Inspector to the plain C# behind it.",
            "Right-click any component header and choose Design Inspector Layout.",
            "Open the Showcase Sandbox to browse every widget on its own.",
        };

        [Tooltip("Flip me — the note below is wired to a condition that watches this box.")]
        public bool openedTheDesigner;

        // ---- Buttons -------------------------------------------------------------------------------------
        // Wired to Button nodes in the designed layout. Editor-only work stays behind UNITY_EDITOR so this
        // component still compiles into a player build (see the player-assembly guard test).

        // Opening the designer deliberately leaves `openedTheDesigner` ALONE. The designed layout labels that
        // box "Try this toggle" and hangs a condition off it, so it belongs to the reader — a button that
        // silently ticked it read as a bug, not as a feature.
        public void OpenLayoutDesigner()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Sasquatch Inspector/Layout Designer");
#endif
        }

        public void OpenShowcaseSandbox()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Sasquatch Inspector/Showcase Sandbox");
#endif
        }

        public void OpenWelcomeWindow()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExecuteMenuItem("Tools/Sasquatch Inspector/Welcome");
#endif
        }
    }
}
