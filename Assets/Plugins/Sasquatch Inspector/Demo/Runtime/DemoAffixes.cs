using System;
using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Demo
{
    /// <summary>
    /// Exhibit 12 — prefixes and suffixes, end to end.
    ///
    /// An affix is the little "$" before a number or the "kg" after it. Sasquatch Inspector gives EVERY field
    /// a prefix side and a suffix side, and each side carries five independent choices: text, an icon, where
    /// it mounts (inside the input box or beside it), its own text styling, and an optional coloured pill.
    /// They apply to every widget — not just sliders — and they survive into an exported custom editor.
    ///
    /// <para>The fields below exist to put every one of those axes on screen at once. Read the designed panel
    /// top to bottom and each card answers one question:</para>
    /// <list type="bullet">
    ///   <item><b>Sides</b> — prefix only, suffix only, both at once.</item>
    ///   <item><b>Content</b> — text, an icon, or an icon and text together.</item>
    ///   <item><b>Mount</b> — inside the box, or outside beside it, and why long units need Outside.</item>
    ///   <item><b>Dress</b> — a bare grey unit, a coloured pill, or a fully restyled affix.</item>
    ///   <item><b>Hosts</b> — the same affix on a plain float, a slider, a [Range] slider, a progress bar,
    ///         a min-max slider, a text field, a dropdown and a vector.</item>
    ///   <item><b>Composites &amp; tables</b> — repeated on every vector component, or folded up into a
    ///         table column header so the cells stay clean.</item>
    /// </list>
    ///
    /// <para>Nothing here is an attribute. Every affix on this panel was set in the Layout Designer's affix
    /// row, and lives in the layout asset — so the same script could carry different units in two projects.</para>
    /// </summary>
    public class DemoAffixes : MonoBehaviour
    {
        public enum Currency { USD, EUR, GBP, JPY }

        [Serializable]
        public class Ingredient
        {
            public string label = "Iron Ore";
            [Tooltip("The affix on this column is folded into the HEADER, so the cells stay bare numbers.")]
            public float grams = 250f;
            [Range(0f, 100f)] public float purity = 82f;
        }

        // ---- Sides: prefix, suffix, both -----------------------------------------------------------------

        [Tooltip("PREFIX only — a '$' pinned inside the leading edge of the box.")]
        public float price = 24.99f;

        [Tooltip("SUFFIX only — a 'kg' pill inside the trailing edge.")]
        public float weight = 3.4f;

        [Tooltip("BOTH sides at once. The two demote symmetrically as the field narrows.")]
        public float budget = 1250f;

        // ---- Content: text, icon, icon + text -------------------------------------------------------------

        [Tooltip("ICON only — an icon affix needs no text at all.")]
        public float temperature = 21.5f;

        [Tooltip("ICON + TEXT. On a suffix the icon trails the text, reading outward from the field.")]
        public float power = 780f;

        [Tooltip("A prefix icon on a STRING field — affixes are not a numeric feature.")]
        public string apiKey = "sk-demo-0000";

        // ---- Mount: inside vs outside ---------------------------------------------------------------------

        [Tooltip("A short unit mounts INSIDE the box and the box pads itself so typed text never slides under it.")]
        public float distance = 128f;

        [Tooltip("A LONG unit is mounted OUTSIDE on purpose. Inside, it would have to eat the whole input to " +
                 "fit; outside, it sits beside the box and the number stays typable. This is the axis the " +
                 "Mount option exists for.")]
        public float throughput = 4200f;

        [Tooltip("An OUTSIDE prefix on a string — the classic 'https://' lead-in.")]
        public string serverUrl = "sasquatchbstudios.com";

        // ---- Dress: plain, pill, restyled -----------------------------------------------------------------

        [Tooltip("The default dress: dim 10px grey, no pill. Quiet on purpose.")]
        public float latency = 42f;

        [Tooltip("A coloured PILL — the affix rides a soft rounded chip.")]
        public float mana = 65f;

        [Tooltip("Fully restyled: bold, larger, its own colour. Each side styles independently.")]
        public float discount = 15f;

        // ---- Hosts: the same idea on eight different widgets -----------------------------------------------

        [Tooltip("On a SLIDER, the affix rides the value read-out beside the track.")]
        [Range(0f, 100f)] public float volume = 72f;

        [Tooltip("A stock [Range] slider is its own host shape — the number is a separate box under the track.")]
        [Range(0f, 10f)] public float cooldown = 2.5f;

        [Tooltip("On a PROGRESS BAR the affix folds into the bar's own read-out text.")]
        [Range(0f, 100f)] public float integrity = 88f;

        [Tooltip("A MIN-MAX slider carries an affix on each of its two editable labels.")]
        public Vector2 spawnDelay = new Vector2(0.8f, 3.2f);

        [Tooltip("A DROPDOWN takes an affix too — here a leading icon that names the axis.")]
        public Currency currency = Currency.USD;

        // ---- Composites and tables -------------------------------------------------------------------------

        [Tooltip("PER-COMPONENT: the 'm' repeats inside every X/Y/Z box rather than sitting once at the end.")]
        public Vector3 roomSize = new Vector3(8f, 3f, 12f);

        [Tooltip("The same vector with the affix NOT repeated — one pair at the end of the whole row.")]
        public Vector3 windVector = new Vector3(1.2f, 0f, -0.4f);

        [Tooltip("In a table, an affix can fold into the COLUMN HEADER ('Grams (g)') so every cell stays a " +
                 "bare number. Nineteen rows of 'g' is noise; one in the header is information.")]
        public List<Ingredient> recipe = new List<Ingredient>
        {
            new Ingredient { label = "Iron Ore",   grams = 250f, purity = 82f },
            new Ingredient { label = "Coal Dust",  grams = 90f,  purity = 61f },
            new Ingredient { label = "Ember Salt", grams = 12.5f, purity = 97f },
        };
    }
}
