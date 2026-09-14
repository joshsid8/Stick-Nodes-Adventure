using System;
using UnityEngine;

namespace SasquatchInspector.Runtime
{
    /// <summary>A float paired with an "is overridden" flag; <see cref="Resolve"/> returns the value when overridden, else a caller-supplied default. Lets a style field opt in without a magic sentinel.</summary>
    [Serializable]
    public struct OverridableFloat
    {
        public bool isOverridden;
        public float value;

        /// <summary>Returns the overridden <c>value</c>, or <paramref name="defaultValue"/> when not overridden.</summary>
        public float Resolve(float defaultValue) => isOverridden ? value : defaultValue;
    }

    /// <summary>A <see cref="Color"/> paired with an "is overridden" flag; <see cref="Resolve"/> returns the value when overridden, else a caller-supplied default.</summary>
    [Serializable]
    public struct OverridableColor
    {
        public bool isOverridden;
        public Color value;

        /// <summary>Returns the overridden <c>value</c>, or <paramref name="defaultValue"/> when not overridden.</summary>
        public Color Resolve(Color defaultValue) => isOverridden ? value : defaultValue;
    }

    /// <summary>An enum value paired with an "is overridden" flag; <see cref="Resolve"/> returns the value when overridden, else a caller-supplied default.</summary>
    [Serializable]
    public struct OverridableEnum<T> where T : struct, Enum
    {
        public bool isOverridden;
        public T value;

        /// <summary>Returns the overridden <c>value</c>, or <paramref name="defaultValue"/> when not overridden.</summary>
        public T Resolve(T defaultValue) => isOverridden ? value : defaultValue;
    }
}
