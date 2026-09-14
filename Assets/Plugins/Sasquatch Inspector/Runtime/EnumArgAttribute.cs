using System;

namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Declares that an <c>int</c> UnityEvent argument really means an enum, so the listener card draws a
    /// dropdown (or a flags mask) instead of a raw integer box.
    ///
    /// <para>The int is not a compromise we chose — it is the only shape that can fire. A UnityEvent stores a
    /// persistent argument under <c>PersistentListenerMode.Int</c>, and Int is the only shape Unity's binder
    /// ever asks with, so it looks for <c>Method(int)</c>; a <c>Method(MyEnum)</c> overload never matches and
    /// the listener silently does nothing. Taking the int and picking the enum ABOVE it is what makes the
    /// call both configurable and callable. The value stored is identical to what Unity itself writes, so the
    /// listener round-trips: Unity's own drawer still shows it as a plain number.</para>
    ///
    /// <code>
    /// [EnumArg(typeof(Team))]
    /// public void SetTeam(int team) => _team = (Team)team;
    /// </code>
    ///
    /// <para>Applicable to the parameter, the method, or (for a property setter, which serializes as
    /// <c>set_Foo</c>) the property. If you already have a <c>SetTeam(Team)</c> overload, you do not need
    /// this at all — an <c>int</c> overload sitting beside a same-named enum overload is inferred
    /// automatically.</para>
    ///
    /// <para>Enums backed by <c>long</c>/<c>ulong</c> are ignored: the argument is serialized in an
    /// <c>int</c> field, so those values could not round-trip, and a silently truncated pick is worse than a
    /// plain integer box.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method | AttributeTargets.Property,
        AllowMultiple = false, Inherited = true)]
    public sealed class EnumArgAttribute : Attribute
    {
        /// <summary>The enum whose members the argument dropdown lists.</summary>
        public Type EnumType { get; }

        /// <summary>Creates the declaration. <paramref name="enumType"/> must be an enum; anything else is ignored.</summary>
        public EnumArgAttribute(Type enumType)
        {
            EnumType = enumType;
        }
    }
}
