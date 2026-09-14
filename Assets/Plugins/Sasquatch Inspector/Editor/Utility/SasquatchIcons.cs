using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SasquatchInspector.Editor
{
    /// <summary>
    /// Registry of the vector icons used across the designer window, rendered by
    /// <see cref="SasquatchIconElement"/>. Geometry is embedded as SVG path data in a 24x24
    /// viewBox, taken from the Lucide icon set (https://lucide.dev, ISC license — see
    /// "Third-Party Notices.md" at the package root), so no texture or font assets ship with
    /// the package and icons stay crisp at any editor scaling.
    ///
    /// Path data is parsed once per icon per session into flat Move/Line/Cubic commands
    /// (arcs and quadratics are converted to cubics) shared by every element instance.
    /// </summary>
    internal static class SasquatchIcons
    {
        public const float ViewBoxSize = 24f;

        /// <summary>Lucide's native stroke width (in viewBox units) — the default hollow outline weight.</summary>
        public const float DefaultStrokeWidth = 2f;

        /// <summary>
        /// Heavier stroke width (viewBox units) for the "bold" variant: lines thicken substantially
        /// but the glyph stays hollow, so a bold icon reads as emphasized without becoming a solid
        /// blob. Used to signal state at a glance (the header pencil-ruler goes bold once a custom
        /// layout exists). Single tweak point shared by both the Painter2D and texture render paths.
        /// </summary>
        public const float BoldStrokeWidth = 4f;

        /// <summary>Matches the window's standard label color so icons sit naturally beside text (skin-aware).</summary>
        public static Color DefaultTint => DesignerPalette.IsLight
            ? new Color(0.13f, 0.13f, 0.13f, 1f)
            : new Color(0.85f, 0.85f, 0.85f, 1f);

        /// <summary>Icon id constants — prefer these over raw strings at call sites.</summary>
        internal static class Ids
        {
            public const string AlignCenter = "align-center";
            public const string AlignCenterVertical = "align-center-vertical";
            public const string AlignEndVertical = "align-end-vertical";
            public const string AlignLeft = "align-left";
            public const string AlignRight = "align-right";
            public const string AlignStartVertical = "align-start-vertical";
            public const string ArrowDown = "arrow-down";
            public const string ArrowDownAZ = "arrow-down-a-z";
            public const string ArrowLeft = "arrow-left";
            public const string ArrowRight = "arrow-right";
            public const string ArrowUp = "arrow-up";
            public const string Check = "check";
            public const string ChevronDown = "chevron-down";
            public const string ChevronLeft = "chevron-left";
            public const string ChevronRight = "chevron-right";
            public const string ChevronUp = "chevron-up";
            public const string ChevronsDownUp = "chevrons-down-up";
            public const string ChevronsUpDown = "chevrons-up-down";
            public const string CircleAlert = "circle-alert";
            public const string Columns3 = "columns-3";
            public const string Copy = "copy";
            public const string CornerDownRight = "corner-down-right";
            public const string Crosshair = "crosshair";
            public const string Diamond = "diamond";
            public const string Dot = "dot";
            public const string Eraser = "eraser";
            public const string ExternalLink = "external-link";
            public const string FileCode = "file-code";
            public const string Folder = "folder";
            public const string FolderOpen = "folder-open";
            public const string FolderPlus = "folder-plus";
            public const string Frame = "frame";
            public const string GalleryVertical = "gallery-vertical";
            public const string Group = "group";
            public const string Info = "info";
            public const string Layers = "layers";
            public const string LayoutGrid = "layout-grid";
            public const string Link = "link";
            public const string List = "list";
            public const string ListCollapse = "list-collapse";
            public const string MousePointerClick = "mouse-pointer-click";
            public const string OctagonAlert = "octagon-alert";
            public const string PaintRoller = "paint-roller";
            public const string PanelsTopLeft = "panels-top-left";
            public const string Pencil = "pencil";
            public const string PencilRuler = "pencil-ruler";
            public const string Play = "play";
            public const string RectangleHorizontal = "rectangle-horizontal";
            public const string RefreshCw = "refresh-cw";
            public const string RotateCcw = "rotate-ccw";
            public const string Rows3 = "rows-3";
            public const string Ruler = "ruler";
            public const string Scan = "scan";
            public const string Scissors = "scissors";
            public const string Settings = "settings";
            public const string Sparkles = "sparkles";
            public const string Square = "square";
            public const string SquareStack = "square-stack";
            public const string Trash2 = "trash-2";
            public const string TriangleAlert = "triangle-alert";
            public const string Ungroup = "ungroup";
            public const string Unlink = "unlink";
            public const string X = "x";
            public const string Zap = "zap";

            // Expressive action / tooling / game icons for user button labels.
            // Source-block order here is just for readability; the PICKER order lives in AllIds.
            public const string Save = "save";
            public const string Download = "download";
            public const string Upload = "upload";
            public const string Plus = "plus";
            public const string Minus = "minus";
            public const string Search = "search";
            public const string Filter = "filter";
            public const string Send = "send";
            public const string Pause = "pause";
            public const string Power = "power";
            public const string Wrench = "wrench";
            public const string Bug = "bug";
            public const string Hammer = "hammer";
            public const string Terminal = "terminal";
            public const string Database = "database";
            public const string Rocket = "rocket";
            public const string WandSparkles = "wand-sparkles";

            // Transport glyphs for the Live Debug playhead bar. Deliberately absent from AllIds: they are
            // chrome for one control, not expressive icons anyone would pick for an Action Button, and the
            // picker's value is that everything in it is worth scrolling past.
            public const string SkipBack = "skip-back";
            public const string SkipForward = "skip-forward";
            public const string Circle = "circle";
            public const string CircleDot = "circle-dot";
            public const string GitBranch = "git-branch";
            public const string Gamepad2 = "gamepad-2";
            public const string Heart = "heart";
            public const string Shield = "shield";
            public const string Star = "star";
            public const string Trophy = "trophy";
            public const string Flame = "flame";
            public const string Swords = "swords";
            public const string Coins = "coins";
            public const string Key = "key";
            public const string Lock = "lock";
            public const string Eye = "eye";
            public const string EyeOff = "eye-off";
            public const string Volume2 = "volume-2";

            // Chrome for the List eyedropper (pick-into-list) header button. Also absent from AllIds for
            // the same reason as the transport glyphs above: it is one control's furniture, not an
            // expressive icon anyone would pick for an Action Button.
            public const string Pipette = "pipette";
        }

        /// <summary>
        /// Every icon id, for pickers (e.g. the button icon dropdown). MAINTAINED BY HAND alongside
        /// <see cref="Ids"/> — add new icons to both (there's no reflection sweep).
        /// LIST ORDER == PICKER DISPLAY ORDER (the popup does not sort), so this is deliberately
        /// curated "most interesting first": expressive game/flair and common actions lead, and the
        /// alignment / arrow / chevron chrome sinks to the bottom. Add new icons where they best fit,
        /// not at the end.
        /// </summary>
        public static readonly IReadOnlyList<string> AllIds = new List<string>
        {
            // Flair / game — the eye-catchers most people scroll for.
            Ids.Rocket, Ids.Sparkles, Ids.WandSparkles, Ids.Zap, Ids.Flame, Ids.Star, Ids.Trophy,
            Ids.Heart, Ids.Shield, Ids.Swords, Ids.Gamepad2, Ids.Crosshair, Ids.Diamond, Ids.Coins,
            Ids.Key, Ids.Bug,
            // Common button actions.
            Ids.Save, Ids.Download, Ids.Upload, Ids.Play, Ids.Pause, Ids.Power, Ids.Search, Ids.Plus,
            Ids.Minus, Ids.Send, Ids.Filter, Ids.Eye, Ids.EyeOff, Ids.Lock, Ids.RefreshCw, Ids.RotateCcw, Ids.Copy,
            Ids.Trash2, Ids.Eraser, Ids.Check,
            // Dev / tooling.
            Ids.Wrench, Ids.Hammer, Ids.Terminal, Ids.Database, Ids.GitBranch, Ids.FileCode,
            Ids.Settings, Ids.Volume2, Ids.Scan, Ids.Scissors, Ids.PaintRoller, Ids.Pencil,
            Ids.PencilRuler, Ids.Ruler, Ids.MousePointerClick,
            // Organize / structure.
            Ids.Folder, Ids.FolderOpen, Ids.FolderPlus, Ids.Layers, Ids.Group, Ids.Ungroup, Ids.Link, Ids.Unlink,
            Ids.ExternalLink,
            // Info / status.
            Ids.Info, Ids.CircleAlert, Ids.TriangleAlert, Ids.OctagonAlert, Ids.Dot, Ids.X,
            // Containers / layout.
            Ids.LayoutGrid, Ids.Frame, Ids.Square, Ids.SquareStack, Ids.RectangleHorizontal,
            Ids.Columns3, Ids.Rows3, Ids.GalleryVertical, Ids.PanelsTopLeft, Ids.List, Ids.ListCollapse,
            // Alignment / arrows / chevrons — least exciting, kept last.
            Ids.AlignLeft, Ids.AlignCenter, Ids.AlignRight, Ids.AlignCenterVertical,
            Ids.AlignStartVertical, Ids.AlignEndVertical, Ids.ArrowUp, Ids.ArrowDown, Ids.ArrowLeft,
            Ids.ArrowRight, Ids.ArrowDownAZ, Ids.CornerDownRight, Ids.ChevronUp, Ids.ChevronDown,
            Ids.ChevronRight, Ids.ChevronsUpDown, Ids.ChevronsDownUp
        };

        internal enum CmdKind { Move, Line, Cubic, Close }

        internal readonly struct PathCmd
        {
            public readonly CmdKind Kind;
            public readonly Vector2 P0; // Move/Line target, or first cubic control point
            public readonly Vector2 P1; // second cubic control point
            public readonly Vector2 P2; // cubic end point

            public PathCmd(CmdKind kind, Vector2 p0 = default, Vector2 p1 = default, Vector2 p2 = default)
            {
                Kind = kind;
                P0 = p0;
                P1 = p1;
                P2 = p2;
            }
        }

        /// <summary>Factory used by all call sites. Pass <paramref name="bold"/> for a thick-stroke (still hollow) glyph.</summary>
        public static SasquatchIconElement Create(string id, float size = 14f, Color? tint = null, bool bold = false)
        {
            return new SasquatchIconElement(id, size, tint ?? DefaultTint) { Bold = bold };
        }

        /// <summary>Default tint for foldout chevrons skinned via <see cref="SkinFoldout"/> (skin-aware).</summary>
        public static Color FoldoutChevronTint => DesignerPalette.IsLight
            ? new Color(0.35f, 0.35f, 0.35f, 1f)
            : new Color(0.6f, 0.6f, 0.6f, 1f);

        /// <summary>
        /// Replaces a <see cref="Foldout"/>'s built-in triangle with the shared chevron icon so
        /// every disclosure arrow in the tool (sections, group widgets, spacing rows) matches.
        /// </summary>
        public static void SkinFoldout(Foldout foldout, float size = 12f, Color? tint = null)
        {
            var checkmark = foldout.Q<VisualElement>(className: "unity-foldout__checkmark");
            if (checkmark == null) return;

            checkmark.style.backgroundImage = StyleKeyword.None;
            // Some editor themes rotate the checkmark for the expanded state; neutralize that —
            // we spin our own chevron child between the open/closed angles instead.
            checkmark.style.rotate = new Rotate(0);
            checkmark.style.alignItems = Align.Center;
            checkmark.style.justifyContent = Justify.Center;
            checkmark.style.flexShrink = 0;

            // A single chevron that SPINS 0°→90° on open (rather than swapping glyphs), so the
            // disclosure arrow rotates with a spring like the rest of the designer's carets.
            var icon = Create(Ids.ChevronRight, size, tint ?? FoldoutChevronTint);
            checkmark.Add(icon);
            EditorSpringFX.SpringCaret(icon, foldout.value, animate: false);

            // The content element Unity shows/hides on toggle — we spring-fold it instead of
            // letting it snap. Register on the foldout's own header toggle, not the foldout itself —
            // nested foldouts' change events bubble through the parent and would flip this chevron.
            var content = foldout.Q<VisualElement>(className: "unity-foldout__content");
            foldout.Q<Toggle>()?.RegisterValueChangedCallback(evt =>
            {
                EditorSpringFX.SpringCaret(icon, evt.newValue);
                EditorSpringFX.FoldNativeFoldout(content, evt.newValue);
            });
        }

        private static readonly Dictionary<string, List<PathCmd>> _geometryCache = new();
        private static readonly HashSet<string> _warnedIds = new();

        /// <summary>The cached vector path commands for icon <paramref name="id"/> (parsed from the lucide SVG once).</summary>
        public static IReadOnlyList<PathCmd> GetGeometry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_geometryCache.TryGetValue(id, out var cached)) return cached;

            if (!PathData.TryGetValue(id, out var dStrings))
            {
                if (_warnedIds.Add(id))
                {
                    Debug.LogWarning($"[Sasquatch Inspector] Unknown icon id '{id}', falling back to '{Ids.Dot}'.");
                }
                return GetGeometry(Ids.Dot);
            }

            var commands = new List<PathCmd>();
            foreach (var d in dStrings)
            {
                SvgPath.Parse(d, commands);
            }
            CloseCoincidentSubpaths(commands);
            _geometryCache[id] = commands;
            return commands;
        }

        /// <summary>viewBox units within which a sub-path's last point counts as landing back on its first.</summary>
        private const float SeamEpsilon = 0.05f;

        /// <summary>
        /// How far (viewBox units) a sub-path must travel from its start before it can count as a LOOP.
        /// Lucide draws dots as degenerate strokes — `M12 16h.01`, the "!" mark under every alert glyph,
        /// and the three bullets of `list` — whose ends are 0.01 apart and so pass <see cref="SeamEpsilon"/>
        /// on a technicality. Closing one replaces its two round CAPS (which are the whole dot) with a
        /// round join on a zero-length segment, and the dot vanishes. Every real ring clears this by a
        /// wide margin: the smallest is `dot` at r=1, which reaches 2.
        /// </summary>
        private const float MinLoopExtent = 0.5f;

        /// <summary>
        /// Appends an explicit <see cref="CmdKind.Close"/> to every sub-path that already ends where it
        /// started. Lucide writes its rings as arc PAIRS with no trailing "Z" — our `circle` conversion
        /// (`M{cx-r} {cy}a{r} {r} 0 1 0 {2r} 0a{r} {r} 0 1 0 -{2r} 0`) and the `settings` cog both do —
        /// so the stroke stays geometrically closed but formally OPEN. Painter2D then puts two round CAPS
        /// at the seam instead of one round JOIN, and below roughly 28px the caps stop covering it: the
        /// gear's r=3 inner circle rendered as a "C" plus a loose dot — a smiley face — at the 11-24px
        /// sizes the tool actually uses. Closing costs nothing (the closing segment has zero length) and
        /// makes the seam a join. Both render paths honour it: Painter2D calls ClosePath, and
        /// RenderToTexture's distance scan already discards zero-length segments.
        /// </summary>
        private static void CloseCoincidentSubpaths(List<PathCmd> cmds)
        {
            // Backwards, so an insert never shifts an index still to be visited.
            int end = cmds.Count; // one past the last command of the sub-path under test
            for (int i = cmds.Count - 1; i >= 0; i--)
            {
                if (cmds[i].Kind != CmdKind.Move) continue;
                if (end - i >= 2 && cmds[end - 1].Kind != CmdKind.Close)
                {
                    var start = cmds[i].P0;
                    var last = cmds[end - 1];
                    var tip = last.Kind == CmdKind.Cubic ? last.P2 : last.P0;
                    float extent = 0f;
                    for (int j = i + 1; j < end; j++)
                    {
                        var c = cmds[j];
                        extent = Mathf.Max(extent, (c.P0 - start).sqrMagnitude);
                        if (c.Kind == CmdKind.Cubic) extent = Mathf.Max(extent, (c.P2 - start).sqrMagnitude);
                    }
                    if ((tip - start).sqrMagnitude <= SeamEpsilon * SeamEpsilon &&
                        extent >= MinLoopExtent * MinLoopExtent)
                        cmds.Insert(end, new PathCmd(CmdKind.Close));
                }
                end = i;
            }
        }

        private static readonly Dictionary<string, Texture2D> _textureCache = new();

        static SasquatchIcons()
        {
            // The rasterized icons are HideAndDontSave, so they'd leak as native objects across a domain reload.
            // Destroy them on reload/quit (mirrors TutorialImageScaler / AudioWaveformRenderer). Bounded by the
            // icon set, so no in-session cap is needed — only the reload/quit sweep.
            AssemblyReloadEvents.beforeAssemblyReload += ClearTextureCache;
            EditorApplication.quitting += ClearTextureCache;
        }

        /// <summary>Destroys all rasterized icon textures and empties the cache (reload / quit).</summary>
        private static void ClearTextureCache()
        {
            foreach (var tex in _textureCache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            _textureCache.Clear();
        }

        /// <summary>
        /// Rasterizes an icon into a cached <see cref="Texture2D"/> for the few places that draw
        /// with IMGUI (e.g. the inspector header fallback) where Painter2D isn't available.
        /// Distance-field stroking of the same geometry the vector element uses, so the two render
        /// paths look identical. <paramref name="glyphScale"/> is the fraction of the texture the
        /// 24-unit viewBox occupies (baked-in padding for ScaleToFit consumers).
        /// </summary>
        public static Texture2D RenderToTexture(string id, int texSize, Color tint, float glyphScale = 1f, bool bold = false)
        {
            string key = $"{id}:{texSize}:{glyphScale}:{(Color32)tint}:{(bold ? 'B' : 'N')}";
            if (_textureCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var geometry = GetGeometry(id);
            if (geometry == null) return null;

            // Flatten the command list to line segments in viewBox space (round caps/joins come
            // free with distance-to-segment coverage).
            var points = new List<Vector2>();
            var segments = new List<(Vector2 a, Vector2 b)>();
            Vector2 cur = Vector2.zero, subpathStart = Vector2.zero;
            void FlushPoint(Vector2 p)
            {
                if (points.Count > 0) segments.Add((points[points.Count - 1], p));
                points.Add(p);
            }
            foreach (var cmd in geometry)
            {
                switch (cmd.Kind)
                {
                    case CmdKind.Move:
                        points.Clear();
                        points.Add(cmd.P0);
                        cur = subpathStart = cmd.P0;
                        break;
                    case CmdKind.Line:
                        FlushPoint(cmd.P0);
                        cur = cmd.P0;
                        break;
                    case CmdKind.Cubic:
                        const int steps = 16;
                        for (int s = 1; s <= steps; s++)
                        {
                            float t = s / (float)steps, u = 1f - t;
                            var p = u * u * u * cur + 3f * u * u * t * cmd.P0 + 3f * u * t * t * cmd.P1 + t * t * t * cmd.P2;
                            FlushPoint(p);
                        }
                        cur = cmd.P2;
                        break;
                    case CmdKind.Close:
                        FlushPoint(subpathStart);
                        cur = subpathStart;
                        break;
                }
            }

            float scale = texSize * glyphScale / ViewBoxSize;
            float offset = (texSize - ViewBoxSize * scale) * 0.5f;
            // Bold variant just thickens the stroke; the glyph stays hollow. Same clamp as the element.
            float strokeWidth = bold ? BoldStrokeWidth : DefaultStrokeWidth;
            float halfWidth = Mathf.Max(strokeWidth * scale, 1f) * 0.5f;

            // ---- Hoist every loop-invariant out of the pixel loops -----------------------------------
            // The distance loop below runs segments.Count x texSize^2 times — 448 x 4,096 for the heaviest
            // 64px glyph — and the ORIGINAL form recomputed the scaled endpoints, the ab vector and its
            // squared length inside it, for every pixel, having already computed them for the pixel next
            // door. Everything here is a function of the segment alone, so it is lifted out verbatim: the
            // same operations, on the same floats, in the same order, just done once each.
            //
            // The reject box built alongside is the bigger half of the win. Coverage is Clamp01(reach - d),
            // so a segment whose distance to this pixel is >= reach contributes EXACTLY zero — and reach is
            // ~2.7px on a 64px glyph, so for almost every (pixel, segment) pair the sqrt is computed only to
            // be thrown away. The closest point of a segment always lies inside the segment's own bounding
            // box, so a pixel outside that box inflated by reach is provably further than reach away and can
            // be skipped without touching the result. The inflation carries a hair of extra slack so that
            // rounding in the box bounds themselves can never clip a pixel the exact test would have kept;
            // coverage is long since zero out there, so the slack costs nothing.
            //
            // Both halves are exactness-critical, so they are pinned by a differential test that re-derives
            // every pixel with a verbatim copy of the pre-change loop — TestIconRasterMatchesADirectScan.
            int segCount = segments.Count;
            var segSax = new float[segCount];
            var segSay = new float[segCount];
            var segAbx = new float[segCount];
            var segAby = new float[segCount];
            var segLen2 = new float[segCount];
            var segMinX = new float[segCount];
            var segMaxX = new float[segCount];
            var segMinY = new float[segCount];
            var segMaxY = new float[segCount];

            float reach = halfWidth + 0.5f;
            float rejectReach = reach + 0.01f;
            for (int i = 0; i < segCount; i++)
            {
                var (a, b) = segments[i];
                var sa = a * scale; sa.x += offset; sa.y += offset;
                var sb = b * scale; sb.x += offset; sb.y += offset;
                var ab = sb - sa;
                segSax[i] = sa.x; segSay[i] = sa.y;
                segAbx[i] = ab.x; segAby[i] = ab.y;
                segLen2[i] = ab.sqrMagnitude;
                segMinX[i] = Mathf.Min(sa.x, sb.x) - rejectReach;
                segMaxX[i] = Mathf.Max(sa.x, sb.x) + rejectReach;
                segMinY[i] = Mathf.Min(sa.y, sb.y) - rejectReach;
                segMaxY[i] = Mathf.Max(sa.y, sb.y) + rejectReach;
            }

            var pixels = new Color32[texSize * texSize];
            for (int y = 0; y < texSize; y++)
            {
                // Texture rows run bottom-up; geometry is y-down — flip. Constant across the row.
                float py = texSize - 1 - y + 0.5f;
                int rowBase = y * texSize;
                for (int x = 0; x < texSize; x++)
                {
                    float px = x + 0.5f;
                    float dist = float.MaxValue;
                    bool hit = false;
                    for (int i = 0; i < segCount; i++)
                    {
                        if (px < segMinX[i] || px > segMaxX[i] || py < segMinY[i] || py > segMaxY[i]) continue;
                        float sax = segSax[i], say = segSay[i];
                        float abx = segAbx[i], aby = segAby[i];
                        float len2 = segLen2[i];
                        // Kept as a DIVISION, not a precomputed reciprocal: x/y and x*(1/y) differ in the
                        // last ulp, and the byte this feeds is one rounding step away.
                        float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((px - sax) * abx + (py - say) * aby) / len2);
                        float dx = px - (sax + abx * t);
                        float dy = py - (say + aby * t);
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < dist) dist = d;
                        hit = true;
                    }
                    // Every segment rejected means every distance was past reach, so coverage is exactly 0.
                    // Said outright rather than left to fall out of float.MaxValue arithmetic.
                    float coverage = hit ? Mathf.Clamp01(reach - dist) : 0f;
                    pixels[rowBase + x] = new Color(tint.r, tint.g, tint.b, tint.a * coverage);
                }
            }

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true);
            _textureCache[key] = tex;
            return tex;
        }

        /// <summary>
        /// A CURSOR-ready raster of an icon: the glyph in <paramref name="tint"/> composited over a bold, near-
        /// black copy of itself. A hardware cursor floats over whatever the window happens to be showing, with no
        /// hover chrome to sit on, so a single-tint stroke disappears against half the canvas — the dark halo is
        /// what keeps it legible on light fields, dark chrome and colored cards alike.
        /// <para>Goes through the same cache (and therefore the same reload/quit sweep) as
        /// <see cref="RenderToTexture"/>; both source rasters are cached too, so this costs one composite pass
        /// per session. 32px is the practical ceiling for an OS cursor on Windows.</para>
        /// </summary>
        public static Texture2D RenderCursorTexture(string id, int texSize = 32, float glyphScale = 0.78f, Color? tint = null)
        {
            var ink = tint ?? Color.white;
            string key = $"cursor:{id}:{texSize}:{glyphScale}:{(Color32)ink}";
            if (_textureCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var halo = RenderToTexture(id, texSize, new Color(0f, 0f, 0f, 0.9f), glyphScale, bold: true);
            var glyph = RenderToTexture(id, texSize, ink, glyphScale);
            if (halo == null || glyph == null) return null;

            var under = halo.GetPixels32();
            var over = glyph.GetPixels32();
            var composited = new Color32[under.Length];
            for (int i = 0; i < composited.Length; i++)
            {
                // Straight-alpha source-over: the glyph on top of the halo.
                Color b = under[i], f = over[i];
                float a = f.a + b.a * (1f - f.a);
                composited[i] = a < 0.0001f
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color((f.r * f.a + b.r * b.a * (1f - f.a)) / a,
                                (f.g * f.a + b.g * b.a * (1f - f.a)) / a,
                                (f.b * f.a + b.b * b.a * (1f - f.a)) / a,
                                a);
            }

            // No mip chain: a cursor is always sampled 1:1, and mips only soften it.
            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(composited);
            tex.Apply(updateMipmaps: false);
            _textureCache[key] = tex;
            return tex;
        }

        // SVG "d" strings, one entry per <path> in the source SVG. <circle>/<rect>/<line>/
        // <polyline> primitives were normalized to path syntax when the data was embedded.
        // Source: lucide-static v1.18.0 (do not hand-edit coordinates; re-export instead).
        private static readonly Dictionary<string, string[]> PathData = new()
        {
            ["align-center"] = new[] { "M21 5H3", "M17 12H7", "M19 19H5" },
            ["align-center-vertical"] = new[] { "M12 2v20", "M8 10H4a2 2 0 0 1-2-2V6c0-1.1.9-2 2-2h4", "M16 10h4a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-4", "M8 20H7a2 2 0 0 1-2-2v-2c0-1.1.9-2 2-2h1", "M16 14h1a2 2 0 0 1 2 2v2a2 2 0 0 1-2 2h-1" },
            ["align-end-vertical"] = new[] { "M4 4h12a2 2 0 0 1 2 2v2a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-2a2 2 0 0 1 2 -2Z", "M11 14h5a2 2 0 0 1 2 2v2a2 2 0 0 1 -2 2h-5a2 2 0 0 1 -2 -2v-2a2 2 0 0 1 2 -2Z", "M22 22V2" },
            ["align-left"] = new[] { "M21 5H3", "M15 12H3", "M17 19H3" },
            ["align-right"] = new[] { "M21 5H3", "M21 12H9", "M21 19H7" },
            ["align-start-vertical"] = new[] { "M8 14h5a2 2 0 0 1 2 2v2a2 2 0 0 1 -2 2h-5a2 2 0 0 1 -2 -2v-2a2 2 0 0 1 2 -2Z", "M8 4h12a2 2 0 0 1 2 2v2a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-2a2 2 0 0 1 2 -2Z", "M2 2v20" },
            ["arrow-down"] = new[] { "M12 5v14", "m19 12-7 7-7-7" },
            ["arrow-left"] = new[] { "m12 19-7-7 7-7", "M19 12H5" },
            ["arrow-right"] = new[] { "M5 12h14", "m12 5 7 7-7 7" },
            ["arrow-up"] = new[] { "m5 12 7-7 7 7", "M12 19V5" },
            ["arrow-down-a-z"] = new[] { "m3 16 4 4 4-4", "M7 20V4", "M20 8h-5", "M15 10V6.5a2.5 2.5 0 0 1 5 0V10", "M15 14h5l-5 6h5" },
            ["check"] = new[] { "M20 6 9 17l-5-5" },
            ["chevron-down"] = new[] { "m6 9 6 6 6-6" },
            ["chevron-left"] = new[] { "m15 18-6-6 6-6" },
            ["chevron-right"] = new[] { "m9 18 6-6-6-6" },
            // Transport glyphs (Live Debug). Circles use the same two-arc normalized form as "settings" above,
            // because the path parser converts arcs to cubics and a single 360° arc is degenerate.
            ["circle"] = new[] { "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0" },
            ["circle-dot"] = new[] { "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" },
            ["skip-back"] = new[] { "M17.971 4.285A2 2 0 0 1 21 6v12a2 2 0 0 1-3.029 1.715l-9.997-5.998a2 2 0 0 1-.003-3.432z", "M3 20V4" },
            ["skip-forward"] = new[] { "M21 4v16", "M6.029 4.285A2 2 0 0 0 3 6v12a2 2 0 0 0 3.029 1.715l9.997-5.998a2 2 0 0 0 .003-3.432z" },
            ["chevron-up"] = new[] { "m18 15-6-6-6 6" },
            ["chevrons-down-up"] = new[] { "m7 20 5-5 5 5", "m7 4 5 5 5-5" },
            ["chevrons-up-down"] = new[] { "m7 15 5 5 5-5", "m7 9 5-5 5 5" },
            ["circle-alert"] = new[] { "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M12 8L12 12", "M12 16L12.01 16" },
            ["columns-3"] = new[] { "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z", "M9 3v18", "M15 3v18" },
            // lucide "copy": the front <rect x=8 y=8 w=14 h=14 rx=2> normalized to a rounded-rect path + the back path.
            ["copy"] = new[] { "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2Z", "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2" },
            ["corner-down-right"] = new[] { "m15 10 5 5-5 5", "M4 4v7a4 4 0 0 0 4 4h12" },
            ["crosshair"] = new[] { "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M22 12L18 12", "M6 12L2 12", "M12 6L12 2", "M12 22L12 18" },
            ["diamond"] = new[] { "M2.7 10.3a2.41 2.41 0 0 0 0 3.41l7.59 7.59a2.41 2.41 0 0 0 3.41 0l7.59-7.59a2.41 2.41 0 0 0 0-3.41l-7.59-7.59a2.41 2.41 0 0 0-3.41 0Z" },
            ["dot"] = new[] { "M11.1 12.1a1 1 0 1 0 2 0a1 1 0 1 0 -2 0" },
            ["eraser"] = new[] { "M21 21H8a2 2 0 0 1-1.42-.587l-3.994-3.999a2 2 0 0 1 0-2.828l10-10a2 2 0 0 1 2.829 0l5.999 6a2 2 0 0 1 0 2.828L12.834 21", "m5.082 11.09 8.828 8.828" },
            ["external-link"] = new[] { "M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" },
            ["file-code"] = new[] { "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z", "M14 2v5a1 1 0 0 0 1 1h5", "M10 12.5 8 15l2 2.5", "m14 12.5 2 2.5-2 2.5" },
            ["folder"] = new[] { "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z" },
            ["folder-open"] = new[] { "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2" },
            ["folder-plus"] = new[] { "M12 10v6", "M9 13h6", "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z" },
            ["frame"] = new[] { "M22 6H2", "M22 18H2", "M6 2v20", "M18 2v20" },
            ["gallery-vertical"] = new[] { "M3 2h18", "M5 6h14a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2Z", "M3 22h18" },
            ["group"] = new[] { "M3 7V5c0-1.1.9-2 2-2h2", "M17 3h2c1.1 0 2 .9 2 2v2", "M21 17v2c0 1.1-.9 2-2 2h-2", "M7 21H5c-1.1 0-2-.9-2-2v-2", "M8 7h5a1 1 0 0 1 1 1v3a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-3a1 1 0 0 1 1 -1Z", "M11 12h5a1 1 0 0 1 1 1v3a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-3a1 1 0 0 1 1 -1Z" },
            ["info"] = new[] { "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0", "M12 16v-4", "M12 8h.01" },
            ["layers"] = new[] { "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z", "M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12", "M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17" },
            ["layout-grid"] = new[] { "M4 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z", "M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z", "M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z", "M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z" },
            ["link"] = new[] { "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71", "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" },
            ["list"] = new[] { "M3 5h.01", "M3 12h.01", "M3 19h.01", "M8 5h13", "M8 12h13", "M8 19h13" },
            ["list-collapse"] = new[] { "M10 5h11", "M10 12h11", "M10 19h11", "m3 10 3-3-3-3", "m3 20 3-3-3-3" },
            ["mouse-pointer-click"] = new[] { "M14 4.1 12 6", "m5.1 8-2.9-.8", "m6 12-1.9 2", "M7.2 2.2 8 5.1", "M9.037 9.69a.498.498 0 0 1 .653-.653l11 4.5a.5.5 0 0 1-.074.949l-4.349 1.041a1 1 0 0 0-.74.739l-1.04 4.35a.5.5 0 0 1-.95.074z" },
            ["octagon-alert"] = new[] { "M12 16h.01", "M12 8v4", "M15.312 2a2 2 0 0 1 1.414.586l4.688 4.688A2 2 0 0 1 22 8.688v6.624a2 2 0 0 1-.586 1.414l-4.688 4.688a2 2 0 0 1-1.414.586H8.688a2 2 0 0 1-1.414-.586l-4.688-4.688A2 2 0 0 1 2 15.312V8.688a2 2 0 0 1 .586-1.414l4.688-4.688A2 2 0 0 1 8.688 2z" },
            ["paint-roller"] = new[] { "M4 2h12a2 2 0 0 1 2 2v2a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-2a2 2 0 0 1 2 -2Z", "M10 16v-2a2 2 0 0 1 2-2h8a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2", "M9 16h2a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1Z" },
            ["panels-top-left"] = new[] { "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z", "M3 9h18", "M9 21V9" },
            ["pencil"] = new[] { "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z", "m15 5 4 4" },
            ["pencil-ruler"] = new[] { "M13 7 8.7 2.7a2.41 2.41 0 0 0-3.4 0L2.7 5.3a2.41 2.41 0 0 0 0 3.4L7 13", "m8 6 2-2", "m18 16 2-2", "m17 11 4.3 4.3c.94.94.94 2.46 0 3.4l-2.6 2.6c-.94.94-2.46.94-3.4 0L11 17", "M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497z", "m15 5 4 4" },
            // lucide "pipette" — chrome for the list eyedropper button; deliberately absent from AllIds.
            ["pipette"] = new[] { "m2 22 1-1h3l9-9", "M3 21v-3l9-9", "m15 6 3.4-3.4a2.1 2.1 0 1 1 3 3L18 9l.4.4a1 1 0 1 1-3 3l-3.8-3.8a1 1 0 1 1 3-3z" },
            ["play"] = new[] { "M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z" },
            ["rectangle-horizontal"] = new[] { "M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2Z" },
            ["refresh-cw"] = new[] { "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8", "M21 3v5h-5", "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16", "M8 16H3v5" },
            ["rotate-ccw"] = new[] { "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5" },
            ["rows-3"] = new[] { "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z", "M21 9H3", "M21 15H3" },
            ["scan"] = new[] { "M3 7V5a2 2 0 0 1 2-2h2", "M17 3h2a2 2 0 0 1 2 2v2", "M21 17v2a2 2 0 0 1-2 2h-2", "M7 21H5a2 2 0 0 1-2-2v-2" },
            ["scissors"] = new[] { "M3 6a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M8.12 8.12 12 12", "M20 4 8.12 15.88", "M3 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M14.8 14.8 20 20" },
            ["ruler"] = new[] { "M21.3 15.3a2.4 2.4 0 0 1 0 3.4l-2.6 2.6a2.4 2.4 0 0 1-3.4 0L2.7 8.7a2.41 2.41 0 0 1 0-3.4l2.6-2.6a2.41 2.41 0 0 1 3.4 0Z", "m14.5 12.5 2-2", "m11.5 9.5 2-2", "m8.5 6.5 2-2", "m17.5 15.5 2-2" },
            ["settings"] = new[] { "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915", "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" },
            ["sparkles"] = new[] { "M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z", "M20 2v4", "M22 4h-4", "M2 20a2 2 0 1 0 4 0a2 2 0 1 0 -4 0" },
            ["square"] = new[] { "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z" },
            ["square-stack"] = new[] { "M4 10c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h4c1.1 0 2 .9 2 2", "M10 16c-1.1 0-2-.9-2-2v-4c0-1.1.9-2 2-2h4c1.1 0 2 .9 2 2", "M16 14h4a2 2 0 0 1 2 2v4a2 2 0 0 1 -2 2h-4a2 2 0 0 1 -2 -2v-4a2 2 0 0 1 2 -2Z" },
            ["trash-2"] = new[] { "M10 11v6", "M14 11v6", "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6", "M3 6h18", "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" },
            ["triangle-alert"] = new[] { "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3", "M12 9v4", "M12 17h.01" },
            ["ungroup"] = new[] { "M6 4h6a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1Z", "M12 14h6a1 1 0 0 1 1 1v4a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1Z" },
            ["unlink"] = new[] { "m18.84 12.25 1.72-1.71h-.02a5.004 5.004 0 0 0-.12-7.07 5.006 5.006 0 0 0-6.95 0l-1.72 1.71", "m5.17 11.75-1.71 1.71a5.004 5.004 0 0 0 .12 7.07 5.006 5.006 0 0 0 6.95 0l1.71-1.71", "M8 2L8 5", "M2 8L5 8", "M16 19L16 22", "M19 16L22 16" },
            ["x"] = new[] { "M18 6 6 18", "m6 6 12 12" },
            ["zap"] = new[] { "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z" },
            // Expressive action / tooling / game batch (lucide-static v1.18.0 — same pin).
            ["save"] = new[] { "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7", "M7 3v4a1 1 0 0 0 1 1h7" },
            ["download"] = new[] { "M12 15V3", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5" },
            ["upload"] = new[] { "M12 3v12", "m17 8-5-5-5 5", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" },
            ["plus"] = new[] { "M5 12h14", "M12 5v14" },
            ["minus"] = new[] { "M5 12h14" },
            ["search"] = new[] { "m21 21-4.34-4.34", "M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0" },
            ["filter"] = new[] { "M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z" },
            ["send"] = new[] { "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z", "m21.854 2.147-10.94 10.939" },
            ["pause"] = new[] { "M15 3h3a1 1 0 0 1 1 1v16a1 1 0 0 1 -1 1h-3a1 1 0 0 1 -1 -1v-16a1 1 0 0 1 1 -1Z", "M6 3h3a1 1 0 0 1 1 1v16a1 1 0 0 1 -1 1h-3a1 1 0 0 1 -1 -1v-16a1 1 0 0 1 1 -1Z" },
            ["power"] = new[] { "M12 2v10", "M18.4 6.6a9 9 0 1 1-12.77.04" },
            ["wrench"] = new[] { "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.106-3.105c.32-.322.863-.22.983.218a6 6 0 0 1-8.259 7.057l-7.91 7.91a1 1 0 0 1-2.999-3l7.91-7.91a6 6 0 0 1 7.057-8.259c.438.12.54.662.219.984z" },
            ["bug"] = new[] { "M12 20v-9", "M14 7a4 4 0 0 1 4 4v3a6 6 0 0 1-12 0v-3a4 4 0 0 1 4-4z", "M14.12 3.88 16 2", "M21 21a4 4 0 0 0-3.81-4", "M21 5a4 4 0 0 1-3.55 3.97", "M22 13h-4", "M3 21a4 4 0 0 1 3.81-4", "M3 5a4 4 0 0 0 3.55 3.97", "M6 13H2", "m8 2 1.88 1.88", "M9 7.13V6a3 3 0 1 1 6 0v1.13" },
            ["hammer"] = new[] { "m15 12-9.373 9.373a1 1 0 0 1-3.001-3L12 9", "m18 15 4-4", "m21.5 11.5-1.914-1.914A2 2 0 0 1 19 8.172v-.344a2 2 0 0 0-.586-1.414l-1.657-1.657A6 6 0 0 0 12.516 3H9l1.243 1.243A6 6 0 0 1 12 8.485V10l2 2h1.172a2 2 0 0 1 1.414.586L18.5 14.5" },
            ["terminal"] = new[] { "M12 19h8", "m4 17 6-6-6-6" },
            ["database"] = new[] { "M3 5a9 3 0 1 0 18 0a9 3 0 1 0 -18 0", "M3 5V19A9 3 0 0 0 21 19V5", "M3 12A9 3 0 0 0 21 12" },
            ["rocket"] = new[] { "M12 15v5s3.03-.55 4-2c1.08-1.62 0-5 0-5", "M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.71-.84.7-2.13-.09-2.91a2.18 2.18 0 0 0-2.91-.09", "M9 12a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.4 22.4 0 0 1-4 2z", "M9 12H4s.55-3.03 2-4c1.62-1.08 5 .05 5 .05" },
            ["wand-sparkles"] = new[] { "m21.64 3.64-1.28-1.28a1.21 1.21 0 0 0-1.72 0L2.36 18.64a1.21 1.21 0 0 0 0 1.72l1.28 1.28a1.2 1.2 0 0 0 1.72 0L21.64 5.36a1.2 1.2 0 0 0 0-1.72", "m14 7 3 3", "M5 6v4", "M19 14v4", "M10 2v2", "M7 8H3", "M21 16h-4", "M11 3H9" },
            ["git-branch"] = new[] { "M15 6a9 9 0 0 0-9 9V3", "M15 6a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M3 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" },
            ["gamepad-2"] = new[] { "M6 11L10 11", "M8 9L8 13", "M15 12L15.01 12", "M18 10L18.01 10", "M17.32 5H6.68a4 4 0 0 0-3.978 3.59c-.006.052-.01.101-.017.152C2.604 9.416 2 14.456 2 16a3 3 0 0 0 3 3c1 0 1.5-.5 2-1l1.414-1.414A2 2 0 0 1 9.828 16h4.344a2 2 0 0 1 1.414.586L17 18c.5.5 1 1 2 1a3 3 0 0 0 3-3c0-1.545-.604-6.584-.685-7.258-.007-.05-.011-.1-.017-.151A4 4 0 0 0 17.32 5z" },
            ["heart"] = new[] { "M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5" },
            ["shield"] = new[] { "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" },
            ["star"] = new[] { "M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z" },
            ["trophy"] = new[] { "M10 14.66v1.626a2 2 0 0 1-.976 1.696A5 5 0 0 0 7 21.978", "M14 14.66v1.626a2 2 0 0 0 .976 1.696A5 5 0 0 1 17 21.978", "M18 9h1.5a1 1 0 0 0 0-5H18", "M4 22h16", "M6 9a6 6 0 0 0 12 0V3a1 1 0 0 0-1-1H7a1 1 0 0 0-1 1z", "M6 9H4.5a1 1 0 0 1 0-5H6" },
            ["flame"] = new[] { "M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4" },
            ["swords"] = new[] { "M14.5 17.5L3 6L3 3L6 3L17.5 14.5", "M13 19L19 13", "M16 16L20 20", "M19 21L21 19", "M14.5 6.5L18 3L21 3L21 6L17.5 9.5", "M5 14L9 18", "M7 17L4 20", "M3 19L5 21" },
            ["coins"] = new[] { "M13.744 17.736a6 6 0 1 1-7.48-7.48", "M15 6h1v4", "m6.134 14.768.866-.5 2 3.464", "M10 8a6 6 0 1 0 12 0a6 6 0 1 0 -12 0" },
            ["key"] = new[] { "m15.5 7.5 2.3 2.3a1 1 0 0 0 1.4 0l2.1-2.1a1 1 0 0 0 0-1.4L19 4", "m21 2-9.6 9.6", "M2 15.5a5.5 5.5 0 1 0 11 0a5.5 5.5 0 1 0 -11 0" },
            ["lock"] = new[] { "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-7a2 2 0 0 1 2 -2Z", "M7 11V7a5 5 0 0 1 10 0v4" },
            ["eye"] = new[] { "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0", "M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0" },
            ["eye-off"] = new[] { "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49", "M14.084 14.158a3 3 0 0 1-4.242-4.242", "M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143", "m2 2 20 20" },
            ["volume-2"] = new[] { "M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z", "M16 9a5 5 0 0 1 0 6", "M19.364 18.364a9 9 0 0 0 0-12.728" },
        };

        /// <summary>
        /// Minimal SVG path-data parser. Emits absolute Move/Line/Cubic/Close commands:
        /// H/V/relative forms are resolved, S/T reflection is applied, quadratics are
        /// degree-elevated to cubics, and elliptical arcs are converted to cubic segments
        /// (SVG spec F.6.5 endpoint-to-center parameterization).
        /// </summary>
        private static class SvgPath
        {
            /// <summary>Parses an SVG path <paramref name="d"/> string into <paramref name="output"/> path commands.</summary>
        public static void Parse(string d, List<PathCmd> output)
            {
                int i = 0;
                char cmd = '\0';
                char prevCmd = '\0';
                Vector2 cur = Vector2.zero;
                Vector2 subpathStart = Vector2.zero;
                Vector2 prevCubicCtrl = Vector2.zero;
                Vector2 prevQuadCtrl = Vector2.zero;

                while (true)
                {
                    SkipSeparators(d, ref i);
                    if (i >= d.Length) break;

                    char c = d[i];
                    if (char.IsLetter(c))
                    {
                        cmd = c;
                        i++;
                    }
                    else if (cmd == 'M')
                    {
                        cmd = 'L'; // implicit lineto after moveto
                    }
                    else if (cmd == 'm')
                    {
                        cmd = 'l';
                    }
                    // other commands implicitly repeat themselves

                    bool relative = char.IsLower(cmd);
                    char upper = char.ToUpperInvariant(cmd);

                    switch (upper)
                    {
                        case 'M':
                        {
                            var p = ReadPoint(d, ref i);
                            cur = relative ? cur + p : p;
                            subpathStart = cur;
                            output.Add(new PathCmd(CmdKind.Move, cur));
                            break;
                        }
                        case 'L':
                        {
                            var p = ReadPoint(d, ref i);
                            cur = relative ? cur + p : p;
                            output.Add(new PathCmd(CmdKind.Line, cur));
                            break;
                        }
                        case 'H':
                        {
                            float x = ReadNumber(d, ref i);
                            cur = new Vector2(relative ? cur.x + x : x, cur.y);
                            output.Add(new PathCmd(CmdKind.Line, cur));
                            break;
                        }
                        case 'V':
                        {
                            float y = ReadNumber(d, ref i);
                            cur = new Vector2(cur.x, relative ? cur.y + y : y);
                            output.Add(new PathCmd(CmdKind.Line, cur));
                            break;
                        }
                        case 'C':
                        {
                            var c1 = ReadPoint(d, ref i);
                            var c2 = ReadPoint(d, ref i);
                            var p = ReadPoint(d, ref i);
                            if (relative) { c1 += cur; c2 += cur; p += cur; }
                            output.Add(new PathCmd(CmdKind.Cubic, c1, c2, p));
                            prevCubicCtrl = c2;
                            cur = p;
                            break;
                        }
                        case 'S':
                        {
                            var c2 = ReadPoint(d, ref i);
                            var p = ReadPoint(d, ref i);
                            if (relative) { c2 += cur; p += cur; }
                            char pu = char.ToUpperInvariant(prevCmd);
                            var c1 = (pu == 'C' || pu == 'S') ? cur * 2f - prevCubicCtrl : cur;
                            output.Add(new PathCmd(CmdKind.Cubic, c1, c2, p));
                            prevCubicCtrl = c2;
                            cur = p;
                            break;
                        }
                        case 'Q':
                        {
                            var q = ReadPoint(d, ref i);
                            var p = ReadPoint(d, ref i);
                            if (relative) { q += cur; p += cur; }
                            EmitQuadratic(cur, q, p, output);
                            prevQuadCtrl = q;
                            cur = p;
                            break;
                        }
                        case 'T':
                        {
                            var p = ReadPoint(d, ref i);
                            if (relative) p += cur;
                            char pu = char.ToUpperInvariant(prevCmd);
                            var q = (pu == 'Q' || pu == 'T') ? cur * 2f - prevQuadCtrl : cur;
                            EmitQuadratic(cur, q, p, output);
                            prevQuadCtrl = q;
                            cur = p;
                            break;
                        }
                        case 'A':
                        {
                            float rx = ReadNumber(d, ref i);
                            float ry = ReadNumber(d, ref i);
                            float rot = ReadNumber(d, ref i);
                            bool largeArc = ReadFlag(d, ref i);
                            bool sweep = ReadFlag(d, ref i);
                            var p = ReadPoint(d, ref i);
                            if (relative) p += cur;
                            EmitArc(cur, rx, ry, rot, largeArc, sweep, p, output);
                            cur = p;
                            break;
                        }
                        case 'Z':
                        {
                            output.Add(new PathCmd(CmdKind.Close));
                            cur = subpathStart;
                            break;
                        }
                        default:
                            // Unknown command — abort this path rather than loop forever.
                            return;
                    }

                    prevCmd = cmd;
                }
            }

            private static void SkipSeparators(string d, ref int i)
            {
                while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\t' || d[i] == '\n' || d[i] == '\r'))
                    i++;
            }

            private static float ReadNumber(string d, ref int i)
            {
                SkipSeparators(d, ref i);
                int start = i;
                if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                bool seenDot = false;
                while (i < d.Length)
                {
                    char c = d[i];
                    if (char.IsDigit(c)) { i++; }
                    else if (c == '.' && !seenDot) { seenDot = true; i++; }
                    else if ((c == 'e' || c == 'E') && i > start)
                    {
                        i++;
                        if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
                    }
                    else break;
                }
                return float.Parse(d.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            private static Vector2 ReadPoint(string d, ref int i)
            {
                float x = ReadNumber(d, ref i);
                float y = ReadNumber(d, ref i);
                return new Vector2(x, y);
            }

            // Arc flags are single characters and may be packed without separators
            // ("a7 7 0 015 5" = flags 0,1 then x=5), so they can't go through ReadNumber.
            private static bool ReadFlag(string d, ref int i)
            {
                SkipSeparators(d, ref i);
                char c = d[i];
                i++;
                return c == '1';
            }

            private static void EmitQuadratic(Vector2 p0, Vector2 q, Vector2 p1, List<PathCmd> output)
            {
                // Degree elevation: cubic controls at 2/3 toward the quadratic control.
                var c1 = p0 + (q - p0) * (2f / 3f);
                var c2 = p1 + (q - p1) * (2f / 3f);
                output.Add(new PathCmd(CmdKind.Cubic, c1, c2, p1));
            }

            // SVG spec F.6.5: endpoint parameterization -> center parameterization, then
            // approximate each <=90-degree sweep with one cubic (k = 4/3 * tan(dTheta/4)).
            private static void EmitArc(Vector2 p0, float rx, float ry, float xAxisRotationDeg,
                bool largeArc, bool sweep, Vector2 p1, List<PathCmd> output)
            {
                if (p0 == p1) return;
                rx = Mathf.Abs(rx);
                ry = Mathf.Abs(ry);
                if (rx < 1e-5f || ry < 1e-5f)
                {
                    output.Add(new PathCmd(CmdKind.Line, p1));
                    return;
                }

                float phi = xAxisRotationDeg * Mathf.Deg2Rad;
                float cosPhi = Mathf.Cos(phi);
                float sinPhi = Mathf.Sin(phi);

                // Transform to the ellipse-aligned frame.
                float dx2 = (p0.x - p1.x) * 0.5f;
                float dy2 = (p0.y - p1.y) * 0.5f;
                float x1p = cosPhi * dx2 + sinPhi * dy2;
                float y1p = -sinPhi * dx2 + cosPhi * dy2;

                // Scale radii up if they can't span the endpoints.
                float lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
                if (lambda > 1f)
                {
                    float s = Mathf.Sqrt(lambda);
                    rx *= s;
                    ry *= s;
                }

                float rx2 = rx * rx, ry2 = ry * ry;
                float num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
                float den = rx2 * y1p * y1p + ry2 * x1p * x1p;
                float coef = Mathf.Sqrt(Mathf.Max(num / den, 0f));
                if (largeArc == sweep) coef = -coef;

                float cxp = coef * rx * y1p / ry;
                float cyp = -coef * ry * x1p / rx;
                float cx = cosPhi * cxp - sinPhi * cyp + (p0.x + p1.x) * 0.5f;
                float cy = sinPhi * cxp + cosPhi * cyp + (p0.y + p1.y) * 0.5f;

                float theta1 = Mathf.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
                float theta2 = Mathf.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
                float dTheta = theta2 - theta1;
                if (!sweep && dTheta > 0) dTheta -= 2f * Mathf.PI;
                else if (sweep && dTheta < 0) dTheta += 2f * Mathf.PI;

                int segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(dTheta) / (Mathf.PI * 0.5f)));
                float delta = dTheta / segments;
                float k = (4f / 3f) * Mathf.Tan(delta * 0.25f);

                Vector2 EllipsePoint(float t) => new Vector2(
                    cx + rx * Mathf.Cos(t) * cosPhi - ry * Mathf.Sin(t) * sinPhi,
                    cy + rx * Mathf.Cos(t) * sinPhi + ry * Mathf.Sin(t) * cosPhi);
                Vector2 EllipseDerivative(float t) => new Vector2(
                    -rx * Mathf.Sin(t) * cosPhi - ry * Mathf.Cos(t) * sinPhi,
                    -rx * Mathf.Sin(t) * sinPhi + ry * Mathf.Cos(t) * cosPhi);

                float theta = theta1;
                var current = EllipsePoint(theta);
                for (int s = 0; s < segments; s++)
                {
                    float next = theta + delta;
                    var nextPoint = EllipsePoint(next);
                    var c1 = current + k * EllipseDerivative(theta);
                    var c2 = nextPoint - k * EllipseDerivative(next);
                    output.Add(new PathCmd(CmdKind.Cubic, c1, c2, nextPoint));
                    theta = next;
                    current = nextPoint;
                }
            }
        }
    }
}
