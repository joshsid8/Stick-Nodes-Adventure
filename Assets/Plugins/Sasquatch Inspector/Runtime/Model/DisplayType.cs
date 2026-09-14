namespace SasquatchInspector.Runtime
{
    /// <summary>
    /// Selects how a bound property is rendered: <see cref="Default"/> uses Unity's field (honoring custom
    /// drawers); every other value picks a Sasquatch Inspector widget. Append-only — values are serialized as int.
    /// </summary>
    public enum DisplayType
    {
        // The Unity default field (PropertyField) — honors custom drawers + built-in attributes.
        // Value 0, so existing serialized layouts (stored by int) are unaffected by the rename from "Auto".
        Default,
        Slider,
        Toggle,
        Text,
        MinMaxSlider,
        // NOTE: ints 5, 6, 7 and 9 are RETIRED (the pre-release ValueDropdown/Tag/Layer widgets — all
        // consolidated into DropDown — and the string Scene widget; SceneReference has its own drawer).
        // BindingNode.OnAfterDeserialize migrates any stored 5/6/7 → DropDown and 9 → Default, so these
        // values MUST NOT be reused (a new widget on one of them would be silently rewritten on load).
        ProgressBar = 8,
        // Unified "pick from a list" widget (value 10) — tags, layers, enums, or a value-source method on
        // your class. The DropdownSource sub-option (+ field type) decides what populates it.
        DropDown = 10,
        // Object-reference widget (value 11) with a thumbnail/asset preview — absorbs the old Default-only
        // "Show Asset Preview" and the "Inline Preview" toggle.
        AssetPreview,
        // AudioClip widget (value 12) with in-editor playback. Standard mode = object field + play button +
        // a sliding progress bar; Show Soundwaves mode = waveform display with a scrolling playhead and
        // optional interactive scrubbing. Appended (value 12) to preserve int-based serialization back-compat.
        AudioPreview,
        // Object-reference widget (value 13) that embeds the referenced object's own fields inline (its own
        // promoted "widget kind", split out of the old AssetPreview "Inline Editor" mode). Supports an
        // optional Expandable collapse (see BindingNode.Expandable / DefaultToOpen). Appended (value 13)
        // to preserve int-based serialization back-compat.
        InlineEditor,
        // Polymorphic [SerializeReference] widget (value 14). For a MANAGED-reference field (interface or
        // abstract base), shows a searchable picker of the concrete implementors, instantiates the chosen
        // type, and draws its fields inline — unlike InlineEditor (a UnityEngine.Object reference), the
        // value lives inline on the SAME SerializedObject. Auto-applied to any managed-reference field and
        // also offered explicitly in the designer. Appended (value 14) to preserve int-based back-compat.
        SerializeReference,
        // Color-field widget (value 15). Renders a grid (or bar) of clickable swatches drawn from a
        // palette in the project's SasquatchColorPaletteLibrary instead of opening the OS color picker. Clicking a swatch sets the
        // field and (optionally) copies its hex to the clipboard. Appended (value 15) to preserve back-compat.
        ColorPalette,
        // Custom UnityEvent drawer (value 16). Replaces Unity's built-in IMGUI UnityEventDrawer with a
        // UI Toolkit card UI (searchable method picker, invoke/duplicate, reorder).
        // Auto-applied to any UnityEventBase field; appended (value 16) to preserve int-based back-compat.
        UnityEvent,
        // ParticleSystem/GameObject preview widget (value 17). Renders a live, in-editor thumbnail of the
        // referenced effect (PreviewRenderUtility) with a ▶ overlay: clicking plays the effect ONCE — simulated
        // from t=0 to the system's duration regardless of whether the system loops — then returns to a rendered
        // poster frame. Built on ParticleSystem.Simulate + PreviewRenderUtility. Appended (value 17) to preserve
        // int-based back-compat.
        ParticleSystemPreview,
        // Animation-curve editor (value 18): a modern, spring-driven alternative to Unity's CurveField — a custom
        // Painter2D graph well with keyframe diamonds, smart tangent modes (Auto/Flat/Linear/Constant/Free), a
        // preset ribbon, and an expandable, freely-resizable editor window. Appended (value 18) to preserve
        // int-based serialization back-compat.
        AnimationCurveEditor,
        // Gradient editor (value 19): a modern, spring-driven alternative to Unity's GradientField — a sleek
        // composited strip (gradient over a checkerboard) with draggable color/alpha pins, native blend modes
        // (Blend/Fixed/PerceptualBlend), full Sasquatch palette integration, and an expandable, freely-resizable
        // editor window. The Gradient lives on the bound SerializedProperty (read/written via the public
        // SerializedProperty.boxedValue API). Appended (value 19) to preserve int-based back-compat.
        Gradient,
        // Enum Badge Grid (value 20): renders an enum field as a grid of clickable toggle chips instead of a
        // dropdown. A [System.Flags] enum becomes a MULTI-select grid (click chips to flip bits, with optional
        // None/All quick chips and "partial" state for compound flags); a plain enum becomes a SINGLE-select
        // segmented control. The enum value lives on the bound SerializedProperty (read/written via
        // enumValueFlag); only the grid's presentation options live on the node. Appended (value 20) to
        // preserve int-based serialization back-compat.
        EnumBadgeGrid,
        // NOTE: int 21 is RETIRED — it was the former standalone Rich Text widget. BindingNode.OnAfterDeserialize
        // migrates any value-21 node to Text + RichTextPreview, so Spring MUST NOT reuse 21 (it would be silently
        // rewritten to Text on every deserialize). Spring is therefore explicitly value 22, skipping the retired 21.
        // Spring config editor (value 22): a custom UI-Toolkit widget for the bonus SasquatchSpring SpringConfig —
        // a preset dropdown, Duration/Bounce sliders, a live Painter2D spring-response graph, and a novel
        // "drag & release" slider whose handle springs back to center using the configured spring so you can FEEL
        // it. SpringConfig is a plain [Serializable] struct (read/written via FindPropertyRelative — no reflection).
        // A SpringConfig field is ALWAYS rendered with this widget (auto-routed) and offers no other kind.
        Spring = 22,
        // Rating (value 23): renders a small int as a clickable row of pips (stars/hearts/dots). Click the Nth
        // glyph to set the value to N; click the current value again to clear to 0. Offered only for `int`.
        // Appended (value 23) to preserve int-based serialization back-compat.
        Rating = 23,
        // Tag Chips (value 24): renders a string[] / List<string> as a row of removable chips plus an inline
        // "add" field — a compact multi-string editor. Bound to the WHOLE array field (not an element).
        // Appended (value 24) to preserve int-based serialization back-compat.
        TagChips = 24,
        // XY Pad (value 25): a Painter2D pad for a Vector2 (or Vector2Int) — drag the handle to set (x, y) within
        // the field's numeric range. Appended (value 25) to preserve int-based serialization back-compat.
        XYPad = 25,
        // NOTE: int 26 is RETIRED — it was the "Asset Picker" widget (AssetDropdown), an object field paired
        // with a searchable project-asset popup. It duplicated Unity's own ObjectField picker plus the Asset
        // Preview widget, so it was pulled. BindingNode.OnAfterDeserialize migrates any stored 26 → Default, so
        // this value MUST NOT be reused (a new widget on 26 would be silently rewritten to Default on load).
        // Euler Angles (value 27): edits a Quaternion as three degree fields (X/Y/Z) round-tripped through
        // Quaternion.eulerAngles, so a rotation reads in human degrees. Appended (value 27).
        EulerAngles = 27,
        // Vector Sliders (value 28): a Vector3/Vector4 as per-axis sliders with a Normalize button — good for
        // direction/normal fields. Appended (value 28) to preserve int-based serialization back-compat.
        VectorSliders = 28,
        // Custom Widget (value 29): renders a no-code, layer-based custom visual (a SasquatchCustomWidget asset —
        // radial graphs, gauges, bar clusters…) authored in the Widget Composer. The bound field feeds the
        // widget's PRIMARY input; sibling properties map to its other inputs (see BindingNode.CustomWidgetGuid /
        // CustomWidgetInputs). Interactive: layer hits write clicks/drags back to the fields. Offered (static
        // primary-kind check in DisplayTypeCatalog) for numeric / int / enum / bool / Color / Vector2 / float[] /
        // bool[] fields. Appended (value 29) to preserve int-based serialization back-compat.
        CustomWidget = 29,
        // Dictionary (value 30): renders a NATIVE Unity Dictionary<K, V> field — serializable from Unity 6.6
        // only — as the same polished Key/Value table the bonus SerializedDictionary gets, instead of Unity's
        // stock DictionaryView. Adds what Unity's own dictionary UI has no answer for: drag-to-reorder,
        // per-row right-click menu, and full Sasquatch value editors (colour swatches, object pickers, the
        // Spring widget). Honours Unity's own [DictionaryDisplay] for the column labels and key-column width,
        // and takes its duplicate/empty-key marks straight from SerializedProperty.GetDictionaryIgnoredEntries()
        // — i.e. exactly the rows Unity's runtime will drop.
        //
        // OFFERED only on 6000.6+ (DisplayTypeCatalog.AllOffered), but deliberately still APPLICABLE on older
        // editors so a layout authored on 6.6 and opened on 6.0 keeps the node instead of the designer's Display
        // Type self-heal permanently rewriting it to Default. Same reasoning as ParticleSystemPreview's #else.
        // Appended (value 30) to preserve int-based serialization back-compat; value 26 is RETIRED, never reuse it.
        Dictionary = 30
    }

    /// <summary>Glyph the Rating widget draws for each pip: filled/hollow stars, hearts, or simple dots.</summary>
    public enum RatingIconStyle
    {
        Stars,
        Hearts,
        Dots
    }

    /// <summary>
    /// Inline render/edit mode for the Animation Curve editor. Inline (value 0) = a compact, live-editable
    /// graph well shown directly under the field (with an expand button to the resizable window); Expandable =
    /// a caret left of the field that spring-opens the well below it (like the particle Expandable mode);
    /// ExpandableWithPreview = like Expandable, but also shows a small read-only curve preview beside the field
    /// while collapsed (so you can see the shape without opening it). Inline is value 0 so default/existing
    /// nodes get the compact well without migration.
    /// </summary>
    public enum CurveEditorMode
    {
        Inline,
        Expandable,
        ExpandableWithPreview
    }

    /// <summary>Text-widget input mode: a plain string field, or a file/folder path field with a Browse button.</summary>
    public enum TextPathMode
    {
        Plain,
        Path
    }

    /// <summary>
    /// Where a numeric field's −/+ stepper buttons sit. <see cref="Split"/> (value 0, the original look) puts −
    /// on the left and + on the right of the field; <see cref="GroupedRight"/> keeps both buttons together to the
    /// right of the field; <see cref="GroupedLeft"/> keeps both together to its left. Split is value 0 so existing
    /// layouts render unchanged.
    /// </summary>
    public enum StepperPlacement
    {
        Split,
        GroupedRight,
        GroupedLeft
    }

    /// <summary>
    /// Inline render/edit mode for the Gradient editor (mirrors <see cref="CurveEditorMode"/>). Inline (value 0) =
    /// a compact, live-editable strip with pins shown directly under the field; Expandable = a caret left of the
    /// field that spring-opens the full editor below it; ExpandableWithPreview = like Expandable, but also shows a
    /// small read-only gradient strip beside the field while collapsed. Inline is value 0 so default/existing
    /// nodes get the compact strip without migration.
    /// </summary>
    public enum GradientEditorMode
    {
        Inline,
        Expandable,
        ExpandableWithPreview
    }

    /// <summary>
    /// Pin layout / reconciliation for the Gradient editor. DualTrack (value 0) = the FAITHFUL Unity model: alpha
    /// pins on the strip's top edge and color pins on the bottom edge, with independent times/counts (up to 8
    /// each) — fully lossless. Merged = split-hemisphere pins on one track (top half = solid color, bottom half =
    /// color × alpha over a checkerboard), each locking a colorKey + alphaKey at the SAME time. Switching
    /// DualTrack → Merged unions the channels' key times (resampling the missing channel) so the result stays a
    /// valid round-trippable Unity gradient; Dual-Track is the lossless escape hatch. DualTrack is value 0 so
    /// existing nodes default to the faithful model.
    /// </summary>
    public enum GradientPinLayout
    {
        DualTrack,
        Merged
    }

    /// <summary>
    /// Per-keyframe tangent behaviour surfaced as the editor's pill row. Auto = smooth clamped-auto tangents;
    /// Flat = zeroed tangents; Linear / Constant = straight / stepped segments; Free = broken tangents with
    /// independently draggable in/out handle nubs. Transient UI state — NOT serialized (the curve's keyframes
    /// carry the actual tangent data).
    /// </summary>
    public enum CurveTangentMode
    {
        Auto,
        Flat,
        Linear,
        Constant,
        Free
    }

    /// <summary>
    /// A built-in curve shape the preset ribbon can spring-morph the curve to (preserving the existing time
    /// span where sensible). Transient — never serialized on the node.
    /// </summary>
    public enum CurvePreset
    {
        Linear,
        EaseInOut,
        Constant,
        Flat,
        Smooth
    }

    /// <summary>
    /// Authoring mode for a plain Color field (<see cref="DisplayType.Default"/>) — mirrors Shader Graph's
    /// Default/HDR color mode. <see cref="Default"/> = a standard 0–1 color picker; <see cref="HDR"/> = an HDR
    /// picker with an intensity control, letting the color exceed 1 (for bloom / emissive workflows). A field's
    /// <c>[ColorUsage(showAlpha, hdr)]</c> attribute imports onto this (<c>hdr</c> → <see cref="HDR"/>), but the
    /// mode can also be set by hand with no attribute at all. Default is value 0 so existing Color fields are
    /// unaffected (they keep rendering through Unity's standard field).
    /// </summary>
    public enum ColorFieldMode
    {
        Default,
        HDR
    }

    /// <summary>Layout for the ColorPalette widget: Grid = wrapping square swatches; Bar = one continuous rounded bar of segments (the hovered segment springs up and grows).</summary>
    public enum ColorPaletteLayout
    {
        Grid,
        Bar
    }

    /// <summary>
    /// Where the optional "current color" bar sits relative to the palette: Left/Right share the row (~28% width),
    /// Above/Below stack it on its own row. Right is value 0 so existing nodes default to it without migration.
    /// </summary>
    public enum ColorBarPlacement
    {
        Right,
        Left,
        Above,
        Below
    }

    /// <summary>
    /// What populates a DropDown widget. Auto picks by field type (enum → values, string → Tags, int → Layer,
    /// LayerMask → LayerMask); explicit values force a source (Method reads the node's ValueSourceMethod).
    /// </summary>
    public enum DropdownSource
    {
        Auto,
        Tags,
        Layer,
        Method,
        LayerMask
    }

    /// <summary>How a ProgressBar widget formats its overlay text (value + max, percentage, value only, or none).</summary>
    public enum ProgressBarTextMode
    {
        ValueAndMax,
        Percentage,
        ValueOnly,
        None
    }

    /// <summary>Render mode for the AssetPreview widget: a Standard object field with thumbnail, thumbnail only, or
    /// an Expandable fold (object field as the header, preview in the collapsible body).</summary>
    public enum AssetPreviewMode
    {
        Standard,
        ThumbnailOnly,
        // value 2 was the long-removed "InlineEditor" mode (promoted into its own DisplayType). It is intentionally
        // SKIPPED so any legacy layout still carrying the orphaned int 2 clamps back to Standard in OnAfterDeserialize.
        Expandable = 3
    }

    /// <summary>
    /// Render mode for the ParticleSystemPreview widget. FieldAndThumbnail (value 0) is today's behavior — the
    /// object field with the live thumbnail stacked below it — so existing layouts keep their look without
    /// migration. Standard puts the field left and the preview right on one line (like the asset preview's
    /// "beside" layout); ThumbnailOnly shows just the live thumbnail (assign via the corner button or
    /// drag-and-drop); Expandable adds a caret left of the field that spring-opens the preview panel.
    /// </summary>
    public enum ParticlePreviewMode
    {
        FieldAndThumbnail,
        Standard,
        ThumbnailOnly,
        Expandable
    }

    /// <summary>
    /// 9-position anchor for the play/stop badge over a preview thumbnail — a Rect-Transform-style pivot with
    /// far fewer (discrete) options. Center (value 4) keeps the historical centered badge; a manual Vector2
    /// offset nudges from the chosen anchor.
    /// </summary>
    public enum PreviewAnchor
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    /// <summary>Horizontal alignment for a widget's image/preview (and the audio play button).</summary>
    public enum ImageAlignment
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// Unified 4-way placement for a widget's secondary element (preview thumbnail, audio waveform, color
    /// bar, button icon, …) relative to its primary content: Left/Right share the row, Above/Below stack it
    /// on its own row. Right is value 0 so converted fields default to it without migration. Supersedes the
    /// older per-widget <see cref="ColorBarPlacement"/> (identical order) and the 3-way <see cref="ImageAlignment"/>;
    /// both are kept for serialization back-compat while their owners migrate onto this.
    /// </summary>
    public enum ElementPlacement
    {
        Right,
        Left,
        Above,
        Below
    }

    /// <summary>
    /// Display mode for the AudioPreview widget: Standard = object field + play button + a thin progress bar;
    /// ShowSoundwaves = a taller row that also renders the clip's waveform with a scrolling playhead.
    /// </summary>
    public enum AudioPreviewMode
    {
        Standard,
        ShowSoundwaves
    }
}
