using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Imports the user's EXISTING Unity curve / gradient presets into Sasquatch Inspector's own preset libraries, so people
    /// don't lose the presets they already have when they switch to these widgets.
    ///
    /// Unity stores its preset libraries as YAML <c>.curves</c> / <c>.gradients</c> files. We read the files as
    /// TEXT and parse the small, stable preset schema ourselves, building presets via the
    /// <see cref="Keyframe"/>/<see cref="AnimationCurve"/>/<see cref="Gradient"/> APIs.
    ///
    /// Parsing is schema-targeted and defensive: a malformed or future-version entry is skipped rather than aborting
    /// the whole import (the keyframe / gradient serialized layouts have been stable across many Unity versions).
    /// </summary>
    internal static class UnityPresetImporter
    {
        // ----------------------------------------------------------------- public entry points ----

        public static (int added, int skipped) ImportCurves(SasquatchCurvePresetLibrary lib)
        {
            if (lib == null) return (0, 0);
            var items = new List<(string, AnimationCurve)>();
            foreach (var file in FindPresetFiles(".curves"))
            {
                string label = Path.GetFileNameWithoutExtension(file);
                try
                {
                    int n = 0;
                    foreach (var (rawName, curve) in ParseCurves(File.ReadAllText(file)))
                    {
                        n++;
                        items.Add((DisplayName(rawName, label, n), curve));
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[Sasquatch Inspector] Could not parse curve preset file '{file}': {e.Message}"); }
            }
            return lib.AddRangeUnique(items);
        }

        public static (int added, int skipped) ImportGradients(SasquatchGradientPresetLibrary lib)
        {
            if (lib == null) return (0, 0);
            var items = new List<(string, Gradient)>();
            foreach (var file in FindPresetFiles(".gradients"))
            {
                string label = Path.GetFileNameWithoutExtension(file);
                try
                {
                    int n = 0;
                    foreach (var (rawName, gradient) in ParseGradients(File.ReadAllText(file)))
                    {
                        n++;
                        items.Add((DisplayName(rawName, label, n), gradient));
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[Sasquatch Inspector] Could not parse gradient preset file '{file}': {e.Message}"); }
            }
            return lib.AddRangeUnique(items);
        }

        private static string DisplayName(string rawName, string fileLabel, int ordinal)
            => string.IsNullOrWhiteSpace(rawName) ? $"{fileLabel} {ordinal}" : rawName.Trim();

        // ----------------------------------------------------------------- file discovery ----

        // Unity preset libraries live (a) per-user in the editor Preferences/Presets folder and (b) optionally
        // project-scoped under Assets/. The Preferences folder is literally named "Editor-5.x" even on Unity 6.
        private static IEnumerable<string> FindPresetFiles(string extension)
        {
            var results = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                AddFiles(Path.Combine(appData, "Unity", "Editor-5.x", "Preferences", "Presets"), extension, results, false);
                AddFiles(Path.Combine(appData, "Unity", "Editor", "Preferences", "Presets"), extension, results, false);
            }
            // Project-scoped libraries (these extensions aren't reliably AssetDatabase-indexed, so enumerate the disk).
            AddFiles(Application.dataPath, extension, results, true);
            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddFiles(string dir, string extension, List<string> into, bool recursive)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                into.AddRange(Directory.GetFiles(dir, "*" + extension, opt));
            }
            catch (Exception) { /* permission / IO — skip this location */ }
        }

        // ----------------------------------------------------------------- curve parsing ----

        // Walks the m_Presets list. Each preset begins at a "- m_Name:" item; its m_Curve holds the keyframe list
        // ("- serializedVersion: 3" items with time/value/slopes/weights) plus m_PreInfinity / m_PostInfinity.
        private static IEnumerable<(string name, AnimationCurve curve)> ParseCurves(string text)
        {
            var output = new List<(string, AnimationCurve)>();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int start = IndexOfKey(lines, "m_Presets:");
            if (start < 0) return output;

            string curName = null;
            var keys = new List<Keyframe>();
            int pre = 0, post = 0;
            bool inEntry = false;

            // Current keyframe scratch.
            bool inKey = false;
            float time = 0, value = 0, inSlope = 0, outSlope = 0, inWeight = 0, outWeight = 0;
            int weighted = 0;

            void FlushKey()
            {
                if (!inKey) return;
                var k = new Keyframe(time, value, inSlope, outSlope, inWeight, outWeight) { weightedMode = (WeightedMode)weighted };
                keys.Add(k);
                inKey = false;
                time = value = inSlope = outSlope = inWeight = outWeight = 0; weighted = 0;
            }
            void FlushEntry()
            {
                FlushKey();
                if (inEntry && keys.Count > 0)
                {
                    var curve = new AnimationCurve(keys.ToArray()) { preWrapMode = (WrapMode)pre, postWrapMode = (WrapMode)post };
                    output.Add((curName, curve));
                }
                inEntry = false; curName = null; keys.Clear(); pre = post = 0;
            }

            for (int i = start + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                string t = line.TrimStart();
                if (t.Length == 0) continue;

                if (t.StartsWith("- m_Name:"))
                {
                    FlushEntry();
                    inEntry = true;
                    curName = ValueAfter(t, "- m_Name:");
                    continue;
                }
                if (!inEntry) continue;

                if (t.StartsWith("- serializedVersion:")) { FlushKey(); inKey = true; continue; }

                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                string key = t.Substring(0, colon).Trim();
                string val = t.Substring(colon + 1).Trim();

                switch (key)
                {
                    case "time": time = ParseFloat(val); break;
                    case "value": value = ParseFloat(val); break;
                    case "inSlope": inSlope = ParseFloat(val); break;
                    case "outSlope": outSlope = ParseFloat(val); break;
                    case "inWeight": inWeight = ParseFloat(val); break;
                    case "outWeight": outWeight = ParseFloat(val); break;
                    case "weightedMode": weighted = (int)ParseFloat(val); break;
                    case "m_PreInfinity": FlushKey(); pre = (int)ParseFloat(val); break;
                    case "m_PostInfinity": post = (int)ParseFloat(val); break;
                }
            }
            FlushEntry();
            return output;
        }

        // ----------------------------------------------------------------- gradient parsing ----

        // Walks the m_Presets list. Each preset begins at "- m_Name:"; its m_Gradient holds key0..key7 ({r,g,b,a}),
        // ctime0..ctime7 / atime0..atime7 (ushort 0..65535), m_Mode, m_NumColorKeys, m_NumAlphaKeys.
        private static IEnumerable<(string name, Gradient gradient)> ParseGradients(string text)
        {
            var output = new List<(string, Gradient)>();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int start = IndexOfKey(lines, "m_Presets:");
            if (start < 0) return output;

            string curName = null;
            bool inEntry = false;
            var col = new Color[8];
            var ctime = new int[8];
            var atime = new int[8];
            int mode = 0, numColor = 0, numAlpha = 0;

            void FlushEntry()
            {
                if (inEntry && numColor > 0)
                {
                    int nc = Mathf.Clamp(numColor, 0, 8);
                    int na = Mathf.Clamp(numAlpha, 0, 8);
                    var ck = new GradientColorKey[Mathf.Max(1, nc)];
                    for (int j = 0; j < ck.Length; j++)
                        ck[j] = new GradientColorKey(new Color(col[j].r, col[j].g, col[j].b), Mathf.Clamp01(ctime[j] / 65535f));
                    var ak = new GradientAlphaKey[Mathf.Max(1, na)];
                    for (int j = 0; j < ak.Length; j++)
                        ak[j] = new GradientAlphaKey(na > 0 ? col[j].a : 1f, Mathf.Clamp01(atime[j] / 65535f));

                    var g = new Gradient();
                    g.SetKeys(ck, ak);
                    g.mode = (GradientMode)mode;
                    output.Add((curName, g));
                }
                inEntry = false; curName = null; mode = numColor = numAlpha = 0;
                for (int j = 0; j < 8; j++) { col[j] = Color.black; ctime[j] = 0; atime[j] = 0; }
            }

            for (int i = start + 1; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.Length == 0) continue;

                if (t.StartsWith("- m_Name:"))
                {
                    FlushEntry();
                    inEntry = true;
                    curName = ValueAfter(t, "- m_Name:");
                    continue;
                }
                if (!inEntry) continue;

                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                string key = t.Substring(0, colon).Trim();
                string val = t.Substring(colon + 1).Trim();

                if (key.StartsWith("key") && val.StartsWith("{"))
                {
                    if (TryIndex(key, "key", out int idx) && idx >= 0 && idx < 8)
                        col[idx] = ParseInlineColor(val);
                }
                else if (key.StartsWith("ctime"))
                {
                    if (TryIndex(key, "ctime", out int idx) && idx >= 0 && idx < 8) ctime[idx] = (int)ParseFloat(val);
                }
                else if (key.StartsWith("atime"))
                {
                    if (TryIndex(key, "atime", out int idx) && idx >= 0 && idx < 8) atime[idx] = (int)ParseFloat(val);
                }
                else if (key == "m_Mode") mode = (int)ParseFloat(val);
                else if (key == "m_NumColorKeys") numColor = (int)ParseFloat(val);
                else if (key == "m_NumAlphaKeys") numAlpha = (int)ParseFloat(val);
            }
            FlushEntry();
            return output;
        }

        // ----------------------------------------------------------------- small helpers ----

        private static int IndexOfKey(string[] lines, string key)
        {
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].TrimStart().StartsWith(key)) return i;
            return -1;
        }

        private static string ValueAfter(string line, string prefix)
        {
            int idx = line.IndexOf(prefix, StringComparison.Ordinal);
            return idx < 0 ? "" : line.Substring(idx + prefix.Length).Trim();
        }

        private static bool TryIndex(string key, string prefix, out int index)
        {
            index = -1;
            if (!key.StartsWith(prefix)) return false;
            return int.TryParse(key.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
        }

        private static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            // Unity YAML uses invariant '.' decimals; tolerate a trailing comment or stray token.
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        // Parse an inline flow map like "{r: 0.5, g: 0.25, b: 0, a: 1}".
        private static Color ParseInlineColor(string s)
        {
            float r = 0, g = 0, b = 0, a = 1;
            int open = s.IndexOf('{'); int close = s.LastIndexOf('}');
            if (open >= 0 && close > open) s = s.Substring(open + 1, close - open - 1);
            foreach (var part in s.Split(','))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                string k = part.Substring(0, c).Trim();
                float v = ParseFloat(part.Substring(c + 1).Trim());
                switch (k) { case "r": r = v; break; case "g": g = v; break; case "b": b = v; break; case "a": a = v; break; }
            }
            return new Color(r, g, b, a);
        }
    }
}
