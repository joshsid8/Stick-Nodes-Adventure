using System;
using UnityEditor;
using SasquatchInspector.Runtime;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Precision-correct numeric read/write shared by the Slider, ProgressBar, and Stepper widgets. Reads route
    /// through <see cref="SerializedProperty.longValue"/> / <see cref="SerializedProperty.doubleValue"/> (both
    /// 64-bit), so every whole-number width (int/uint/short/ushort/byte/sbyte/long/ulong) and <c>double</c> edit
    /// at native precision. Going through <c>intValue</c>/<c>floatValue</c> instead silently truncates
    /// <c>long</c> and <c>double</c> fields, so never route these reads that way.
    /// </summary>
    public static partial class BindingFactory
    {
        /// <summary>Whether the property is a whole-number or floating-point field the numeric widgets accept.</summary>
        internal static bool IsNumericProperty(SerializedProperty p) =>
            p != null && (p.propertyType == SerializedPropertyType.Integer ||
                          p.propertyType == SerializedPropertyType.Float);

        internal static bool IsIntegerProperty(SerializedProperty p) =>
            p != null && p.propertyType == SerializedPropertyType.Integer;

        /// <summary>
        /// True when an Integer property fits losslessly in a 32-bit <c>int</c>, so Unity's <c>SliderInt</c> /
        /// <c>IntegerField</c> (which bind via <c>intValue</c>) can drive it directly. UInt32/Int64/UInt64 do
        /// NOT fit and must use the wide (long/double) manual path.
        /// </summary>
        internal static bool FitsInt32(SerializedProperty p)
        {
            if (p == null || p.propertyType != SerializedPropertyType.Integer) return false;
            switch (p.numericType)
            {
                case SerializedPropertyNumericType.Int8:
                case SerializedPropertyNumericType.UInt8:
                case SerializedPropertyNumericType.Int16:
                case SerializedPropertyNumericType.UInt16:
                case SerializedPropertyNumericType.Int32:
                    return true;
                default: // UInt32, Int64, UInt64 (and Unknown)
                    return false;
            }
        }

        /// <summary>True when a Float property is a 64-bit <c>double</c> (needs DoubleField, not FloatField/Slider).</summary>
        internal static bool IsDoubleProperty(SerializedProperty p) =>
            p != null && p.propertyType == SerializedPropertyType.Float &&
            p.numericType == SerializedPropertyNumericType.Double;

        /// <summary>Reads the property's numeric value as a double (for slider-fraction math and display).</summary>
        internal static double ReadNumeric(SerializedProperty p) =>
            p.propertyType == SerializedPropertyType.Integer ? (double)p.longValue : p.doubleValue;

        /// <summary>
        /// Writes <paramref name="value"/> back at the property's native precision (integers round to the nearest
        /// whole number; floats/doubles keep full precision). Returns true only if the stored value changed.
        /// </summary>
        internal static bool WriteNumeric(SerializedProperty p, double value)
        {
            if (p.propertyType == SerializedPropertyType.Integer)
            {
                long v = (long)Math.Round(value, MidpointRounding.AwayFromZero);
                if (p.longValue == v) return false;
                p.longValue = v;
                return true;
            }

            if (p.doubleValue.Equals(value)) return false;
            p.doubleValue = value;
            return true;
        }

        /// <summary>
        /// Exact stepper increment: integers step by whole units (never a double round-trip that could lose a
        /// 64-bit value), floats/doubles by the raw amount. A fractional step on an integer field rounds, with
        /// a ±1 floor so a small step never becomes a no-op. Returns true only if the value changed.
        ///
        /// <para>The stepped result is CLAMPED to whichever bounds <see cref="ResolveStepperBounds"/> found. Without
        /// that, pressing + past a <c>[Range(0, 10)]</c> ceiling kept writing 11, 12, 13… while the drawn slider
        /// showed a pinned 10 — the stored data silently left the range the field declares, and the user then had
        /// to press − all the way back down before anything appeared to move.</para>
        /// </summary>
        internal static bool StepNumeric(SerializedProperty p, double amount,
            float lo = 0f, float hi = 0f, bool hasLo = false, bool hasHi = false)
        {
            if (p.propertyType == SerializedPropertyType.Integer)
            {
                long delta = (long)Math.Round(amount, MidpointRounding.AwayFromZero);
                if (delta == 0) delta = amount > 0 ? 1 : amount < 0 ? -1 : 0;
                if (delta == 0) return false;
                long clamped = ValidationBinder.ClampLong(p.longValue + delta, lo, hi, hasLo, hasHi);
                if (clamped == p.longValue) return false;   // already pinned at the bound — no dirty write
                p.longValue = clamped;
                return true;
            }

            if (amount == 0) return false;
            double stepped = ValidationBinder.ClampDouble(p.doubleValue + amount, lo, hi, hasLo, hasHi);
            if (stepped.Equals(p.doubleValue)) return false;
            p.doubleValue = stepped;
            return true;
        }

        /// <summary>
        /// The bounds a stepper press must respect, tightened from every source that claims one: the field's
        /// <c>[Range]</c> / <c>[Min]</c> attributes (Unity's own drawers coerce the DRAWN value to those, so a
        /// widget writing the property directly has to match or the data and the display disagree), plus the
        /// binding's own enforced validation range when Clamp to Range is on. Warn-only validation deliberately
        /// does NOT bound the stepper — going out of range is how the warning is meant to appear.
        /// </summary>
        internal static void ResolveStepperBounds(BindingNode node, SerializedProperty prop,
            out float lo, out float hi, out bool hasLo, out bool hasHi)
        {
            float resolvedLo = 0f, resolvedHi = 0f;
            bool foundLo = false, foundHi = false;

            void TightenLo(float v) { if (!foundLo || v > resolvedLo) { resolvedLo = v; foundLo = true; } }
            void TightenHi(float v) { if (!foundHi || v < resolvedHi) { resolvedHi = v; foundHi = true; } }

            var range = GetRangeAttribute(prop);
            if (range != null) { TightenLo(range.min); TightenHi(range.max); }

            var min = GetMinAttribute(prop);
            if (min != null) TightenLo(min.min);

            if (node != null && node.ValidateRange && node.ClampToRange)
            {
                if (node.RangeHasMin) TightenLo(node.ValidationMin);
                if (node.RangeHasMax) TightenHi(node.ValidationMax);
            }

            // Contradictory claims (say [Range(0, 10)] under a validation floor of 20): the tighter FLOOR wins and
            // the ceiling follows it, so the value lands on one reachable point instead of an empty window.
            if (foundLo && foundHi && resolvedLo > resolvedHi) resolvedHi = resolvedLo;

            lo = resolvedLo; hi = resolvedHi; hasLo = foundLo; hasHi = foundHi;
        }
    }
}
