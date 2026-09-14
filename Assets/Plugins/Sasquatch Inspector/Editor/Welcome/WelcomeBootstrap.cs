using System;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Shows the Welcome popup once on first install (and again after a MAJOR update), gated by
    /// <see cref="WelcomeState"/>. Modeled on <see cref="LayoutPresetBootstrap"/> and
    /// <see cref="EditorSpringFX"/>'s run-once migration: the real work is deferred off the domain reload via
    /// <see cref="EditorApplication.delayCall"/>, and it never pops during the test runner, batch mode, or a
    /// player build.
    /// </summary>
    [InitializeOnLoad]
    internal static class WelcomeBootstrap
    {
        static WelcomeBootstrap()
        {
            // Defer until the editor has settled (AssetDatabase ready, not mid domain-reload import).
            EditorApplication.delayCall += MaybeShow;
        }

        private static void MaybeShow()
        {
            // Never interrupt automation — CI / -batchmode, the test runner, or a player build.
            if (Application.isBatchMode) return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") >= 0) return;
            if (BuildPipeline.isBuildingPlayer) return;

            // Right after a reload the compile/import pipeline may still be busy — re-defer a frame.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += MaybeShow;
                return;
            }

            if (!WelcomeState.ShouldAutoShow()) return;
            SasquatchWelcomeWindow.ShowWelcome(auto: true);
        }
    }
}
