// Assets/Editor/AnimPathRemapper.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

public class AnimPathRemapper : EditorWindow
{
    private List<Object> animAssets = new List<Object>();
    private GameObject targetRoot;          // optional: root to search hierarchy for correct paths
    private bool autoFixMissingOnly = true; // only rewrite curves whose path is currently broken
    private Vector2 scroll;
    private string status = "";

    [MenuItem("Tools/Animation/Anim Path Remapper")]
    public static void ShowWindow() => GetWindow<AnimPathRemapper>("Anim Path Remapper");

    private void OnGUI()
    {
        GUILayout.Label("Animation Path Remapper", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag .anim files into the list below.\n" +
            "Optionally assign a GameObject (the animated prefab/scene object) so the tool can resolve correct paths.\n" +
            "Curves pointing at missing components/properties will be rewritten when a unique match is found.",
            MessageType.Info);

        targetRoot = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetRoot, typeof(GameObject), true);
        autoFixMissingOnly = EditorGUILayout.Toggle("Only fix missing", autoFixMissingOnly);

        // Asset list
        EditorGUILayout.LabelField("Animation Clips:");
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(100));
        for (int i = animAssets.Count - 1; i >= 0; i--)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                animAssets[i] = EditorGUILayout.ObjectField(animAssets[i], typeof(Object), false);
                if (GUILayout.Button("X", GUILayout.Width(20)))
                    animAssets.RemoveAt(i);
            }
        }
        EditorGUILayout.EndScrollView();

        var drop = DragAndDrop.objectReferences;
        foreach (var o in drop)
            if (o is AnimationClip && !animAssets.Contains(o))
                animAssets.Add(o);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add from Selection"))
                foreach (var o in Selection.objects)
                    if (o is AnimationClip && !animAssets.Contains(o))
                        animAssets.Add(o);
            if (GUILayout.Button("Clear"))
                animAssets.Clear();
        }

        EditorGUILayout.Space();
        GUI.enabled = animAssets.Count > 0;
        if (GUILayout.Button("Scan / Report Missing Paths", GUILayout.Height(30)))
            Scan(false);
        if (GUILayout.Button("Auto-Fix Unique Matches", GUILayout.Height(30)))
            Scan(true);
        GUI.enabled = true;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(status);
    }

    private void Scan(bool apply)
    {
        int fixedCount = 0, missingCount = 0;
        var report = new System.Text.StringBuilder();

        foreach (var obj in animAssets)
        {
            if (!(obj is AnimationClip clip)) continue;
            string clipName = AssetDatabase.GetAssetPath(clip);

            SerializedObject so = new SerializedObject(clip);
            SerializedProperty curves = so.FindProperty("m_EditorCurves");
            SerializedProperty ecurves = so.FindProperty("m_EulerEditorCurves");

            foreach (SerializedProperty set in new[] { curves, ecurves })
            {
                for (int i = 0; i < set.arraySize; i++)
                {
                    SerializedProperty c = set.GetArrayElementAtIndex(i);
                    string path = c.FindPropertyRelative("path").stringValue;
                    string attr = c.FindPropertyRelative("attribute").stringValue;
                    string xfer = c.FindPropertyRelative("xPath").stringValue; // type/class id

                    bool broken = IsBroken(path, attr, xfer);
                    if (!broken) continue;
                    missingCount++;

                    string newPath = ResolvePath(path, attr, xfer);
                    if (string.IsNullOrEmpty(newPath))
                    {
                        report.AppendLine($"[UNRESOLVED] {clipName} -> '{path}.{attr}'");
                        continue;
                    }

                    if (apply)
                    {
                        c.FindPropertyRelative("path").stringValue = newPath;
                        fixedCount++;
                        report.AppendLine($"[FIXED] {clipName}: '{path}' -> '{newPath}'");
                    }
                    else
                    {
                        report.AppendLine($"[MISSING] {clipName} -> '{path}.{attr}' | suggested: '{newPath}'");
                    }
                }
            }

            if (apply)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(clip);
            }
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            status = $"Done. Fixed {fixedCount} of {missingCount} missing curves.";
        }
        else
        {
            status = $"Found {missingCount} missing curves. See Console for details.";
        }

        Debug.Log(report.ToString());
    }

    // Does this curve reference something that no longer exists?
    private bool IsBroken(string path, string attr, string xfer)
    {
        if (targetRoot == null) return true; // can't verify without a target
        Transform t = FindTransformByPath(targetRoot.transform, path);
        if (t == null) return true;

        // Check component type via xPath (format like "UnityEngine.Behaviour/PPosition")
        string typeName = xfer.Contains("/") ? xfer.Substring(xfer.IndexOf('/') + 1) : xfer;
        Component comp = FindComponent(t, typeName);
        if (comp == null) return true;

        // Check property exists
        return !PropertyExists(comp, attr);
    }

    private string ResolvePath(string oldPath, string attr, string xfer)
    {
        if (targetRoot == null) return null;

        // Strategy: find any Transform/component in hierarchy whose name matches the
        // last segment of old path AND has the right component + property.
        string leaf = oldPath.Contains("/") ? oldPath.Substring(oldPath.LastIndexOf('/') + 1) : oldPath;
        string typeName = xfer.Contains("/") ? xfer.Substring(xfer.IndexOf('/') + 1) : xfer;

        var candidates = new List<string>();
        CollectMatches(targetRoot.transform, leaf, typeName, attr, ref candidates);

        return candidates.Count == 1 ? candidates[0] : null; // only auto-fix if unambiguous
    }

    private void CollectMatches(Transform t, string leaf, string typeName, string attr, ref List<string> outPaths)
    {
        if (t.name == leaf)
        {
            Component comp = FindComponent(t, typeName);
            if (comp != null && PropertyExists(comp, attr))
                outPaths.Add(GetHierarchyPath(targetRoot.transform, t));
        }
        foreach (Transform child in t)
            CollectMatches(child, leaf, typeName, attr, ref outPaths);
    }

    // --- Helpers ---
    private Transform FindTransformByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        string[] parts = path.Split('/');
        Transform cur = root;
        foreach (var p in parts)
        {
            if (cur == null) return null;
            cur = cur.Find(p);
        }
        return cur;
    }

    private string GetHierarchyPath(Transform root, Transform t)
    {
        string s = t.name;
        while (t.parent != null && t.parent != root)
        {
            t = t.parent;
            s = t.name + "/" + s;
        }
        return s;
    }

    private Component FindComponent(Transform t, string typeName)
    {
        foreach (var c in t.GetComponents<Component>())
            if (c != null && c.GetType().Name == typeName)
                return c;
        return null;
    }

    private bool PropertyExists(Component comp, string attr)
    {
        // attr may be "m_Name" or a property like "material.color.r" — check first segment
        string head = attr.Split('.')[0];
        var type = comp.GetType();
        if (type.GetField(head, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
            return true;
        if (type.GetProperty(head, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
            return true;
        return false;
    }
}