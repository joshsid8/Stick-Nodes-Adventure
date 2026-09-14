using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// A robust, runtime-safe wrapper for referencing Unity Scenes.
    /// In the editor, it provides a nice SceneAsset picker. At runtime, it provides
    /// direct access to the scene path, GUID, and name for loading scenes.
    /// </summary>
    [Serializable]
    public struct SceneReference : ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset sceneAsset;
#endif

        [SerializeField] private string scenePath;
        [SerializeField] private string sceneGuid;

        /// <summary>
        /// The asset path of the scene (e.g. "Assets/Scenes/Gameplay.unity").
        /// </summary>
        public string ScenePath => scenePath;

        /// <summary>
        /// The stable GUID of the scene asset.
        /// </summary>
        public string SceneGuid => sceneGuid;

        /// <summary>
        /// The name of the scene (without file extension).
        /// </summary>
        public string SceneName => System.IO.Path.GetFileNameWithoutExtension(scenePath);

        /// <summary>
        /// The scene's index in Build Settings, or -1 if it isn't in Build Settings. Runtime-safe — use with
        /// <c>SceneManager.LoadScene(int)</c>. (No implicit int conversion: it would clash with the implicit
        /// string conversion below and make <c>LoadScene(sceneReference)</c> ambiguous.)
        /// </summary>
        public int BuildIndex => UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(scenePath);

        /// <summary>
        /// Implicit conversion to string so a SceneReference can be passed directly
        /// to SceneManager.LoadScene(sceneReference).
        /// </summary>
        public static implicit operator string(SceneReference sceneReference) => sceneReference.ScenePath;

        /// <summary>Editor-only: syncs the cached scene path + GUID from the picked SceneAsset before serialization.</summary>
        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            // Sync the path and GUID from the sceneAsset if we are on the main thread in editor.
            // OnBeforeSerialize can run on background threads during building or domain reloading,
            // where calling AssetDatabase throws exceptions/warnings.
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == 1 && 
                !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (sceneAsset != null)
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(sceneAsset);
                    if (!string.IsNullOrEmpty(path) && path != scenePath)
                    {
                        scenePath = path;
                        sceneGuid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
                    }
                }
                else if (!string.IsNullOrEmpty(scenePath))
                {
                    scenePath = "";
                    sceneGuid = "";
                }
            }
#endif
        }

        /// <summary>No-op by design — AssetDatabase can't be called during deserialization (see the note below).</summary>
        public void OnAfterDeserialize()
        {
            // Intentionally empty.
            //
            // We must NOT call AssetDatabase here. OnAfterDeserialize runs inside Unity's
            // serialization pipeline, where AssetDatabase.GUIDToAssetPath (and friends) throw:
            //   "GUIDToAssetPath_Internal is not allowed to be called during serialization".
            // That exception fired every time a scene reference was cleared in the inspector
            // (sceneAsset == null while sceneGuid still held the old value).
            //
            // Deferring the call (EditorApplication.delayCall) is not an option either: this is a
            // struct, so a deferred lambda cannot mutate 'this' back onto the serialized instance.
            //
            // The editor-only sceneAsset link is serialized natively by Unity, so it survives on
            // its own. If that link is ever lost while sceneGuid persists, SceneReferenceDrawer
            // re-resolves it from the GUID — a context where AssetDatabase is safe to call.
        }
    }
}
