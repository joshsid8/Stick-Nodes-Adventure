using System;
using System.Collections.Generic;

namespace SasquatchInspector.Editor
{
    /// <summary>The kind of a single ordered block on a Tutorial page.</summary>
    internal enum TutorialBlockKind
    {
        /// <summary>A run of <see cref="SasquatchMarkdown"/> body copy.</summary>
        Markdown,
        /// <summary>An illustrative still image (see <see cref="TutorialMedia"/>).</summary>
        Image,
        /// <summary>A live, framed inspector preview built by a <see cref="TutorialPreview"/> factory.</summary>
        Preview,
        /// <summary>A primary-action button (label + callback).</summary>
        Button,
    }

    /// <summary>
    /// One ordered block of a Tutorial page. Pages are authored as a list of these so text, images, previews and
    /// buttons can be interleaved in any order (e.g. an image after a specific paragraph). Pure data — built in
    /// <see cref="TutorialContent"/>, rendered by <see cref="SasquatchTutorialWindow"/>.
    /// </summary>
    internal sealed class TutorialBlock
    {
        public TutorialBlockKind Kind;

        public string Markdown;                       // Markdown
        public TutorialMedia Media;                   // Image
        public Func<TutorialPreview> PreviewFactory;  // Preview
        public string ButtonLabel;                    // Button
        public Action ButtonAction;                   // Button
        public bool Secondary;                        // Button — render as a soft inline "link" rather than a primary button
        public UnityEngine.Color? ButtonColor;        // Button — optional custom background color

        public static TutorialBlock Text(string markdown) =>
            new TutorialBlock { Kind = TutorialBlockKind.Markdown, Markdown = markdown };

        public static TutorialBlock Image(string id, string caption = null) =>
            new TutorialBlock { Kind = TutorialBlockKind.Image, Media = new TutorialMedia(id, caption) };

        public static TutorialBlock Live(Func<TutorialPreview> factory) =>
            new TutorialBlock { Kind = TutorialBlockKind.Preview, PreviewFactory = factory };

        public static TutorialBlock Action(string label, Action action, UnityEngine.Color? color = null) =>
            new TutorialBlock { Kind = TutorialBlockKind.Button, ButtonLabel = label, ButtonAction = action, ButtonColor = color };

        /// <summary>A soft, borderless "link"-styled action — a low-pressure alternative to a primary
        /// <see cref="Action"/> button (e.g. the end-of-tutorial "give feedback" / "keep exploring" paths).</summary>
        public static TutorialBlock Link(string label, Action action) =>
            new TutorialBlock { Kind = TutorialBlockKind.Button, ButtonLabel = label, ButtonAction = action, Secondary = true };
    }

    /// <summary>
    /// One page of the guided Tutorial: a title, an optional icon, and an ordered list of content blocks
    /// (markdown, images, a live inspector preview, an action button). Pure data — authored in
    /// <see cref="TutorialContent"/> and rendered by <see cref="SasquatchTutorialWindow"/>.
    /// </summary>
    internal sealed class TutorialStep
    {
        /// <summary>The step heading, shown in the window header.</summary>
        public string Title;

        /// <summary>A <see cref="SasquatchIcons.Ids"/> id shown beside the title (optional).</summary>
        public string IconId;

        /// <summary>The page's blocks, rendered top-to-bottom in order.</summary>
        public List<TutorialBlock> Content = new List<TutorialBlock>();
    }

    /// <summary>
    /// A single illustrative image on a Tutorial step. <see cref="Id"/> is the source file's name
    /// (without extension) under <c>Editor/Tutorial/Media/</c>; <see cref="Caption"/> is an optional line shown
    /// beneath it. Rendered as a still by <see cref="TutorialMediaView"/> (PNG/JPG/… — anything Unity imports
    /// as a Texture2D).
    /// </summary>
    internal sealed class TutorialMedia
    {
        public readonly string Id;
        public readonly string Caption;

        public TutorialMedia(string id, string caption = null)
        {
            Id = id;
            Caption = caption;
        }
    }
}
