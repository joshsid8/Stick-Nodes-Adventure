namespace SasquatchInspector.Editor
{
    /// <summary>
    /// External links and "What's New" copy for the Welcome popup, kept in one place so they're easy to update
    /// each release.
    /// </summary>
    internal static class WelcomeLinks
    {
        /// <summary>
        /// Optional ONLINE documentation URL. When empty (the default), the "Open Documentation" button opens the
        /// bundled <c>SasquatchInspector_User_Manual.pdf</c>. Set this to a hosted docs URL to have the button open that
        /// in a browser instead (see <see cref="SasquatchWelcomeWindow"/>).
        /// </summary>
        public const string Docs = "";

        /// <summary>The Sasquatch B Studios YouTube channel (the one brand URL already used in the codebase).</summary>
        public const string YouTube = "https://www.youtube.com/@sasquatchbgames";

        /// <summary>Support email — shown in the Welcome popup and used as the bug-report email fallback
        /// (see <see cref="SasquatchBugReportWindow"/>).</summary>
        public const string SupportEmail = "sasquatchbstudios@gmail.com";

        /// <summary>
        /// The "Leave a Review" link shown on the final page of the Tutorial (see <see cref="TutorialContent"/>).
        /// A redirect on our OWN domain, so the destination can be re-aimed without a code change in a shipped
        /// package — it points somewhere harmless until the listing exists, then at the listing's reviews tab.
        ///
        /// The redirect must be a 302 (temporary), NOT a 301 (permanent): browsers cache a 301, so once a build
        /// has shipped with the link pointing at the placeholder, those users could never be moved to the real
        /// reviews page. Blank leaves the button inert (no broken tab), so an unset link never bites an end user.
        ///
        /// Re-aiming it is a release step, not a code change: see the "Review link" item in the pre-release
        /// checklist. The final target looks like
        /// <c>https://assetstore.unity.com/packages/slug/&lt;listing-slug&gt;/reviews</c>.
        /// </summary>
        public const string ReviewUrl = "https://sasquatchbstudios.com/inspector-review";

        /// <summary>Privacy page for the issue tracker bug reports are sent to (Codecks). Linked from the
        /// "What gets sent" box in <see cref="SasquatchBugReportWindow"/>.</summary>
        public const string CodecksPrivacy = "https://www.codecks.io/privacy/";

        /// <summary>
        /// Short markdown shown in the "What's New" card when the popup re-appears after a MAJOR update (never on
        /// a fresh install). Keyed by major version; falls back to a generic line. Add a <c>case</c> per major
        /// release. Rendered with <see cref="SasquatchMarkdown"/>.
        /// </summary>
        public static string WhatsNew(int major)
        {
            switch (major)
            {
                // Example (uncomment + edit when you ship a v2):
                // case 2:
                //     return "- A brand-new **Preset Editor**\n" +
                //            "- The live **Condition Debugger** overlay\n" +
                //            "- Inline **Gradient** and **Animation Curve** widgets";
                default:
                    return "- Thanks for updating! Check the documentation for the full changelog.";
            }
        }
    }
}
