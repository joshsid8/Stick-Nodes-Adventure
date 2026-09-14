using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// How much room Unity's OWN inspector-header icons occupy to the RIGHT of where Sasquatch parks its
    /// pencil — and therefore how far in from the header's right edge the pencil belongs.
    ///
    /// WHY THIS IS COMPUTED AND NOT A CONSTANT. Unity lays the header icon strip out right-to-left starting at
    /// the ⋮ menu, and every icon left of it is a callback that may DECLINE to draw: EditorGUIUtility's header
    /// item pass only advances its cursor for a callback that reports it drew. A declined icon therefore
    /// consumes NO slot — everything to its left (the preset icon, and the title's own clip edge) slides one
    /// slot width to the RIGHT.
    ///
    /// UNITY 6.6 (6000.6) MADE THE HELP "?" DECLINE FOR USER SCRIPTS. Its help-URL lookup now requires the
    /// target's type to live in a Unity assembly before it will fall back to the generic MonoBehaviour
    /// scripting-reference page, so a MonoBehaviour or ScriptableObject of the USER's gets a help URL only from
    /// an explicit <see cref="HelpURLAttribute"/> carrying a non-empty URL. Unity 6.0–6.5 still draw "?" on
    /// every component. A pencil pinned at a fixed inset therefore floats one empty slot away from the native
    /// icons on 6.6+, and the title — which now clips one slot further right — draws straight through it.
    ///
    /// The answer comes from Unity's own PUBLIC verdict (<see cref="Help.HasHelpForObject(UnityEngine.Object)"/>),
    /// never from a version number, so it is right per ROW: a project that puts [HelpURL] on one component and
    /// not the next gets both placed correctly, and a future Unity that changes the rule again is followed with
    /// no edit here. (§2.5.g: public API only — no internal type, no GUIStyle name lookup.)
    /// </summary>
    internal static class NativeInspectorHeaderIcons
    {
        /// <summary>Unity's header icons are 16px boxes in both header styles.</summary>
        internal const float IconSize = 16f;

        /// <summary>
        /// One icon slot in a COMPONENT titlebar: Unity's own 16px icon width plus its 4px inter-icon spacing.
        /// This is the exact amount the strip compacts by when the help "?" declines to draw.
        /// </summary>
        internal const float ComponentTitlebarSlot = IconSize + 4f;

        /// <summary>
        /// One icon slot in the BIG asset/ScriptableObject header. That header runs the same right-to-left
        /// pass but with ZERO inter-icon spacing, and its slot width is the ⋮ button's own measured size —
        /// which is what <c>EditorStyles.iconButton</c> reports for a 16px image (its fixed width when it sets
        /// one, otherwise its horizontal padding plus the icon). Read live so an editor-skin retune follows.
        /// </summary>
        internal static float AssetHeaderSlot
        {
            get
            {
                var iconButton = EditorStyles.iconButton;
                if (iconButton == null) return IconSize;
                return iconButton.fixedWidth > 0f
                    ? iconButton.fixedWidth
                    : iconButton.padding.horizontal + IconSize;
            }
        }

        // Per-type verdicts. Both inputs to the answer — the target's assembly and its [HelpURL] — are
        // properties of the TYPE, so one lookup per type is exact; and the only way either can change is a
        // recompile, which clears these statics with the domain reload. That matters: Unity's own help lookup
        // reflects over the type's attributes on every call, which is far too expensive to repeat per row on
        // the continuous header scan.
        private static readonly Dictionary<Type, bool> s_HelpByType = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, bool> s_ExplicitUrlByType = new Dictionary<Type, bool>();

        // Does THIS editor still fall back to the generic MonoBehaviour help page for a plain user component?
        // Seeded from the editor version purely so the first frame is right, then OVERWRITTEN by Unity's live
        // verdict the moment any row hands us a real component (see DrawsHelpButton). It is consulted only for
        // a row we could not correlate to a live instance at all — a locked inspector's uncorrelatable row —
        // so a wrong seed self-heals on the next scan that can see one.
        private static bool s_MonoBehaviourHelpFallback = !EditorVersionAtLeast(6000, 6);

        /// <summary>
        /// Will Unity draw its help "?" in this row's header? <paramref name="instance"/> is the live target
        /// when the row could be correlated to one, and is what makes the answer authoritative; passing null
        /// falls back to what the type alone can prove.
        /// </summary>
        internal static bool DrawsHelpButton(Type type, UnityEngine.Object instance)
        {
            if (type == null) return s_MonoBehaviourHelpFallback;
            if (s_HelpByType.TryGetValue(type, out bool cached)) return cached;

            if (instance == null)
            {
                // Deliberately NOT cached: a later scan that can see a live instance gets Unity's real verdict,
                // and caching a guess here would freeze it in for the rest of the domain.
                return HasExplicitHelpUrl(type) || s_MonoBehaviourHelpFallback;
            }

            bool live = Help.HasHelpForObject(instance);
            s_HelpByType[type] = live;

            // Learn the editor-wide rule, but only from a COMPONENT with no URL of its own — that is the exact
            // case the two Unity behaviours disagree on. (A ScriptableObject never got the fallback on any
            // version, so it can teach us nothing.)
            if (instance is Component && !HasExplicitHelpUrl(type))
                s_MonoBehaviourHelpFallback = live;

            return live;
        }

        /// <summary>
        /// Inset from the header's right edge to the LEFT edge of a 16px box sitting FLUSH in the
        /// <paramref name="slotIndex"/>'th native icon slot, counting from the right edge (slot 1 is the one
        /// the ⋮ menu owns, slot 2 the preset icon, so a third widget wants slot 3).
        ///
        /// Derived rather than eyeballed, because the slot width is the editor skin's own button size: it is
        /// not 16, it moves with the skin, and a hand-tuned constant that was close on one skin reads as a
        /// visible gap on another. The big ASSET header packs its icons with ZERO spacing — it passes no
        /// spacing argument to Unity's header-item pass — so "flush" there really does mean touching, and a
        /// widget parked a few px short of that is the one thing in the strip that looks bolted on.
        /// </summary>
        internal static float FlushSlotInset(int slotIndex, float slot)
            => slotIndex * slot - (slot - IconSize) * 0.5f;

        /// <summary>
        /// Inset for a pencil whose measured baseline was taken WITH Unity's help "?" drawing beside it — the
        /// COMPONENT titlebar, where every user MonoBehaviour got a "?" up to Unity 6.5. One slot is given back
        /// when the "?" declines, so the pencil follows the strip as it compacts right.
        /// </summary>
        internal static float InsetFromHelpPresent(float insetWithHelp, float slot, Type type, UnityEngine.Object instance)
            => DrawsHelpButton(type, instance) ? insetWithHelp : insetWithHelp - slot;

        /// <summary>
        /// Inset for a pencil whose measured baseline was taken with NO help "?" beside it — the big
        /// ASSET/ScriptableObject header. A user ScriptableObject has never had a "?" on ANY Unity version
        /// (Unity's fallback to the generic scripting-reference page only ever applied to a Component or a
        /// MonoScript, and an SO is neither), so 6.6 changed nothing there. What CAN put a "?" in that header
        /// is the user's own [HelpURL] on the type — and then the strip grows one slot LEFT and a fixed inset
        /// parks the pencil right on top of the new icon. One slot is added for exactly that case.
        /// </summary>
        internal static float InsetFromHelpAbsent(float insetWithoutHelp, float slot, Type type, UnityEngine.Object instance)
            => DrawsHelpButton(type, instance) ? insetWithoutHelp + slot : insetWithoutHelp;

        private static bool HasExplicitHelpUrl(Type type)
        {
            if (type == null) return false;
            if (s_ExplicitUrlByType.TryGetValue(type, out bool cached)) return cached;

            bool has = false;
            // inherit:true — Unity's own lookup reads inherited [HelpURL] too. An EMPTY url does NOT count:
            // 6.6 skips it outright, and while 6.0-6.5 do accept one (they resolve it to a junk doc topic and
            // the "?" appears), that is the very case the MonoBehaviour fallback would have covered anyway —
            // so treating blank as "no explicit url" leaves the version-learning below reading the right
            // signal on both.
            var attributes = type.GetCustomAttributes(typeof(HelpURLAttribute), true);
            for (int i = 0; i < attributes.Length; i++)
            {
                if (attributes[i] is HelpURLAttribute helpUrl && !string.IsNullOrEmpty(helpUrl.URL))
                {
                    has = true;
                    break;
                }
            }

            s_ExplicitUrlByType[type] = has;
            return has;
        }

        // "6000.6.0f1" → major 6000, minor 6. Parses defensively: an unreadable version reads as OLDER, which
        // seeds the historical "?"-always-drawn layout — the same layout every 6.0–6.5 editor actually uses.
        private static bool EditorVersionAtLeast(int major, int minor)
        {
            string version = Application.unityVersion;
            if (string.IsNullOrEmpty(version)) return false;

            var parts = version.Split('.');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out int actualMajor)) return false;

            // The minor field can carry a suffix in some builds; take its leading digits only.
            int digits = 0;
            while (digits < parts[1].Length && char.IsDigit(parts[1][digits])) digits++;
            if (digits == 0) return false;
            if (!int.TryParse(parts[1].Substring(0, digits), out int actualMinor)) return false;

            return actualMajor > major || (actualMajor == major && actualMinor >= minor);
        }
    }
}
