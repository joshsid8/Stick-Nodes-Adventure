using System.Collections.Generic;
using UnityEngine;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// The Tutorial's script: a short, paginated tour teaching the "plain script → custom inspector" workflow on
    /// the bundled <see cref="SasquatchInspector.Samples.PlayerStats"/> sample, ending by opening the real Layout
    /// Designer. Content lives here (separate from the window) so it stays easy to edit and unit-testable; each
    /// page is an ordered list of <see cref="TutorialBlock"/>s (text / image / live preview / button) so images
    /// can sit between specific paragraphs. The only blocks that touch the host window are the final page's graduate links.
    ///
    /// To illustrate a step with a screenshot, drop the file into <c>Editor/Tutorial/Media/</c> and add a
    /// <c>TutorialBlock.Image("file-name")</c> at the right spot. Anything Unity imports as a Texture2D works
    /// (PNG/JPG/…), and it renders as a STILL — there is no animated-image path. They import crisp and
    /// render at native size; a missing file degrades to a tidy placeholder, so a page referencing art you haven't
    /// added yet still renders cleanly.
    /// </summary>
    internal static class TutorialContent
    {
        /// <summary>Builds the ordered step list. <paramref name="host"/> may be <c>null</c> — the only action that
        /// needs it guards against that.</summary>
        public static List<TutorialStep> BuildSteps(SasquatchTutorialWindow host)
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Title = "Meet Sasquatch Inspector",
                    IconId = SasquatchIcons.Ids.Sparkles,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Sasquatch Inspector turns plain scripts into **beautiful, custom inspectors**:\n\n" +
                            "- No `[CustomEditor]`\n" +
                            "- No boilerplate\n" +
                            "- No compile times\n\n" +
                            "In this tutorial we'll practice on an included sample, **PlayerStats**. Here's where you're " +
                            "headed — the same fields, but *designed*.\n\n" +
                            "At the end you'll do it for real on this very " +
                            "asset."),
                        TutorialBlock.Live(() => TutorialPreview.Build(TutorialLayouts.PlayerStatsAfter())),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 1 — Select your object",
                    IconId = SasquatchIcons.Ids.MousePointerClick,
                    Content =
                    {
                        TutorialBlock.Text(
                            "It always starts with a selection. Click a **GameObject** in the Hierarchy (or in this case....a " +
                            "**ScriptableObject** asset in the Project window) so it appears in the Inspector."),
                        TutorialBlock.Action("Ping PlayerStats Asset", TutorialSample.PingSampleAsset),
                        // Drop a screenshot named "select-object" into Editor/Tutorial/Media/ to illustrate this.
                        TutorialBlock.Image("select-object"),
                        TutorialBlock.Text(
                            "Out of the box, your script renders with Unity's **stock inspector** — like this. " +
                            "Perfectly functional, just… plain. Let's upgrade it."),
                        //TutorialBlock.Live(() => TutorialPreview.Build(TutorialLayouts.PlayerStatsBefore())),
                        TutorialBlock.Image("stock-inspector"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 2 — Open the Layout Designer",
                    IconId = SasquatchIcons.Ids.PencilRuler,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Two ways in — pick whichever feels natural:\n\n" +
                            "- **#1 - **Click the **pencil-ruler icon** in the component's header.\n\n"),
                        TutorialBlock.Image("open-designer-pencil"),
                        TutorialBlock.Text("What's **really cool** about THIS method....is it shows a THIN outline when NO layout exists...\n"),
                        TutorialBlock.Image("thin-pencil"),
                        TutorialBlock.Text("and a **BOLD** outline when a layout DOES exist...\n(meaning...you're using Sasquatch Inspector to make a custom inspector on that script"),
                        TutorialBlock.Image("bold-pencil"),
                        // Drop screenshots named "open-designer-pencil" / "open-designer-rightclick" into
                        // Editor/Tutorial/Media/ (placeholders show until you do).
                        TutorialBlock.Text(
                            "- **#2 - ** OR...**Right-click** the component header ▸ *Edit Sasquatch Layout*."),
                        TutorialBlock.Image("open-designer-rightclick"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 3 — It opens empty (that's expected)",
                    IconId = SasquatchIcons.Ids.FileCode,
                    Content =
                    {
                        TutorialBlock.Text(
                            "A brand-new layout starts with **just a Script Header** — it does NOT auto-fill your " +
                            "fields, and it drops you on the **Tools** tab where our import options are.\n\n" +
                            "You're one click away from a full inspector. Here's what you should see " +
                            "in the **layout designer** right now:"),
                        // Drop a screenshot named "designer-empty" into Editor/Tutorial/Media/.
                        TutorialBlock.Image("designer-empty"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 4 — Smart Attribute Import",
                    IconId = SasquatchIcons.Ids.Zap,
                    Content =
                    {
                        TutorialBlock.Text(
                            "On the **Tools** tab, under **Setup & Generation**, click **Smart Attribute Import**."),
                        // Drop a screenshot named "smart-attribute-import" into Editor/Tutorial/Media/.
                        TutorialBlock.Image("smart-attribute-import"),
                        TutorialBlock.Text(
                            "Every serialized field appears at once — grouped into sections by your `[Header]`s — and " +
                            "because **Use Layout Presets** is on by default, each already wears a sensible widget.\n\n" +
                            "You can completely customize how these import settings work in Tools ▸ Sasquatch Inspector ▸ Layout Presets."),
                        // Drop a screenshot named "import-result" into Editor/Tutorial/Media/.
                        TutorialBlock.Text(
                            "It's a great start — If you want to see how this WILL LOOK in the inspector...switch to **Live Preview**...\n\n" +
                            "Drag cards around, group them, swap widgets. But you'll notice it's " +
                            "not quite the same as our inspector from **Page 1**. Let's fix that."),
                        // Drop a screenshot named "import-vs-final" into Editor/Tutorial/Media/.
                        TutorialBlock.Image("import-vs-final"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 5 — Style the Identity card",
                    IconId = SasquatchIcons.Ids.Pencil,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Time to make it *yours*. Make sure you're back in **Design Mode**, select a field on the canvas, then tweak it in the " +
                            "**Properties** panel on the right. Start with **Identity**:\n\n" +
                            "- **Character Description** → turn on **Rich Text Preview** so bold/italic/color tags " +
                            "render live as you type.\n\n"),
                        TutorialBlock.Image("character-description"),
                        TutorialBlock.Text(
                            "- **Level** → in the slider's style options, set **Knob Shape** to *Grip* and " +
                            "**Size** to *Large* for that mixer-fader look.\n\n"),
                        TutorialBlock.Image("character-level"),
                        TutorialBlock.Text(
                            "- **Character Class** → switch **Display Type** from *Dropdown* to **Enum Badges** for " +
                            "enum buttons."),
                        TutorialBlock.Image("character-class"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 6 — Style the Vitals card",
                    IconId = SasquatchIcons.Ids.RectangleHorizontal,
                    Content =
                    {
                        TutorialBlock.Text(
                            "On to **Vitals**:\n\n" +
                            "- **Health** → change **Display Type** from *Slider* to **Progress Bar** — it reads as a " +
                            "filling bar with a value/max readout.\n\n"),
                        TutorialBlock.Image("character-health"),
                        TutorialBlock.Text(
                            "- **Invincible** → set **Toggle Style** to **Rounded** (flip on **Show ON/OFF Text** for " +
                            "a labeled switch, if you like).\n\n"),
                        TutorialBlock.Image("character-invincible"),
                        TutorialBlock.Text(
                            "- **Invincibility Duration** → open **Conditionals ▸ + Add Condition** and show it only " +
                            "when *Invincible* is **true**, so it appears on demand.\n\n"),
                        TutorialBlock.Image("character-inv-duration"),
                        TutorialBlock.Text(
                            "*Conditions hide a field in the real inspector. To test it inside the Designer, flip on " +
                            "**Interact** (top of the canvas) or switch to **Live Preview**, then toggle Invincible.*"),
                    }
                },

                // Styling the Appearance widgets and color-coding the sections are deliberately two pages: on one
                // page they run to 4 screenshots plus a full 3-card live preview, which reads as cramped.
                new TutorialStep
                {
                    Title = "Step 7 — Polish the Appearance card",
                    IconId = SasquatchIcons.Ids.LayoutGrid,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Last touches on **Appearance**:\n\n" +
                            "- **Tint** → change **Display Type** from *Default* to **Color Palette**. Try both **Layout** " +
                            "options — **Bar** (a clean swatch strip) and **Grid** — and keep whichever you like better.\n\n"),
                        TutorialBlock.Image("character-tint"),
                        TutorialBlock.Text(
                            "**Optionally**, if you want to change the in-line animation curve for **Character Acceleration**" +
                            ", you can revert it back to Unity's default Animation Curve by changing it's **Display Type** to **Default**" +
                            "...I'm going to keep it as is though.\n\n"),
                        TutorialBlock.Image("animation-curve"),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 8 — Color-code your sections",
                    IconId = SasquatchIcons.Ids.Layers,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Now **color-code your sections**: select each card (Identity / Vitals / Appearance) " +
                            "and set its **Section Tint** — a subtle wash + accent rail that makes the inspector " +
                            "scannable at a glance.\n\n"),
                        TutorialBlock.Image("container-tint"),
                        TutorialBlock.Text("(**Hint** - you can also select the **Container** cards in the **Hierarchy** to find them more easily.)\n\n"),
                        // Drop a screenshot named "style-appearance" into Editor/Tutorial/Media/.
                        TutorialBlock.Image("hierarchy"),
                        TutorialBlock.Text(
                            "Here's how your **live preview** should look now.\n"),
                        TutorialBlock.Live(() => TutorialPreview.Build(TutorialLayouts.PlayerStatsAfter())),
                    }
                },

                new TutorialStep
                {
                    Title = "Step 9 — Save, and you're done",
                    IconId = SasquatchIcons.Ids.Check,
                    Content =
                    {
                        TutorialBlock.Text(
                            "Hit **Save** (or flip on **Auto-Save**) and your inspector takes over **immediately** — " +
                            "the header pencil goes **bold** to show a custom " +
                            "layout is live.\n\n" +
                            "*(If a type already has its own `[CustomEditor]`, Sasquatch Inspector automatically defers to it " +
                            "and your layout won't draw — but PlayerStats has none, so it just works...\n**NOTE:** You CAN click 'Force Takeover' on a layout that already has a `[CustomEditor]` if you'd rather use Sasquatch Inspector instead. The choice is yours!)*\n\n" +
                            "**Your turn.** Try it on one of your own scripts!\n\n" +
                            "And there's plenty more where this came from: every widget, layout trick, and effect " +
                            "lives in the **Showcase Sandbox**, one page away. You can also open the included **Demo** scene in your project to play around with even more examples and see how different layouts are built."),
                        TutorialBlock.Live(() => TutorialPreview.Build(TutorialLayouts.PlayerStatsAfter())),
                    }
                },

                // The send-off page, reached by everyone who finishes: a rating ask as the primary action, with two
                // secondary links — send feedback directly instead, or keep exploring in the Showcase. The review
                // link is a redirect we own (WelcomeLinks.ReviewUrl), so it can be wired up before a store page
                // exists, and the button is a no-op while it's blank.
                new TutorialStep
                {
                    Title = "You're all set!",
                    IconId = SasquatchIcons.Ids.Star,
                    Content =
                    {
                        TutorialBlock.Text(
                            "That's the basics — a plain script turned into a fully custom inspector, no code " +
                            "required.\n\n" +
                            "You can also open the included **Demo** scene to give yourself even more stuff to work with, play around with, and see how certain things are built!\n\n" +
                            "If Sasquatch Inspector is making your life easier, I'd be hugely grateful for a quick " +
                            "**rating on the Asset Store**. It takes about 20 seconds, it genuinely makes a solo " +
                            "dev's week, and it's the single biggest thing that helps other devs find the tool.\n\n" +
                            "No pressure at all — only if you're feeling it. Thank you for giving it a try."),
                        TutorialBlock.Action("Leave a Review", () =>
                        {
                            // Guarded so a blank/un-set link never opens a broken tab in front of a user.
                            if (!string.IsNullOrEmpty(WelcomeLinks.ReviewUrl))
                                Application.OpenURL(WelcomeLinks.ReviewUrl);
                        }, DesignerPalette.ButtonAmber),
                        TutorialBlock.Link("    Not quite there yet? Tell me what would make it better  →",
                            SasquatchBugReportWindow.Open),
                        TutorialBlock.Action("Showcase Sandbox", () =>
                        {
                            SasquatchShowcaseWindow.Open();
                            if (host != null) host.Close();
                        }),
                        TutorialBlock.Action("Ping Demo Scene", TutorialSample.PingDemoScene),
                        TutorialBlock.Live(() => TutorialPreview.Build(TutorialLayouts.PlayerStatsAfter())),
                    }
                },
            };
        }
    }
}
