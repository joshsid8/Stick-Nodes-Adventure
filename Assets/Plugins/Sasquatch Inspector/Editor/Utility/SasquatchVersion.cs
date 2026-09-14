namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Single source of truth for the package version. This package ships as an Assets subfolder (no
    /// <c>package.json</c>), so there's no manifest version to read — <b>bump <see cref="Current"/> on every
    /// Asset Store release</b>. The first-run Welcome popup compares this version's MAJOR component against the
    /// last version the user acknowledged to decide whether to re-show the greeting (with a "What's New" panel)
    /// after a major update. See <see cref="WelcomeState"/>.
    /// </summary>
    public static class SasquatchVersion
    {
        /// <summary>The current package version, SemVer "MAJOR.MINOR.PATCH". Bump this every release.</summary>
        public const string Current = "1.0.0";

        /// <summary>The integer MAJOR component of <see cref="Current"/> (0 if unparseable).</summary>
        public static int Major => WelcomeState.ParseMajor(Current);
    }
}
