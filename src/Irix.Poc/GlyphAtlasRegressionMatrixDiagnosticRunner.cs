using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class GlyphAtlasRegressionMatrixDiagnosticRunner
{
    internal static void Run(
        TextWriter output,
        int frameCount = 3,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas,
        DisplayScale diagnosticScale = default)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale == default ? screen.Scale.Normalize() : diagnosticScale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        d3d12Renderer.TextCompositionMode = textCompositionMode;
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        var viewport = new DrawRect(0, 0, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);

        var resources = FrameDrawingResources.Rent();
        try
        {
            var commands = BuildMatrixCommands(resources, frameIndex: 0);
            resources.Seal();
            var summary = AnalyzeMatrixScene(commands, resources);

            output.WriteLine("=== Glyph Atlas Regression Matrix Diagnostic ===");
            output.WriteLine($"Frames: {frameCount}");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine($"Text composition mode: {textCompositionMode}");
            output.WriteLine(FormatSummary(summary));
            output.WriteLine(FormatContract(summary.Contract));
            output.WriteLine("Matrix cases: ASCII=True LatinExtended=True Greek=True Cyrillic=True CJK=True Arabic=True Hebrew=True MixedBidi=True Emoji=True Wrap=True Tab=True CRLF=True");
            output.WriteLine("Accepted degradation: overlay=False svgColorGlyph=True colrPaintTreeColorGlyph=True bidiBeyondResolvedLevels=True atlasFullAfterBudget=True recordFailure=True initializationFailure=True");
            output.WriteLine();
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }

        for (var i = 0; i < frameCount; i++)
        {
            resources = FrameDrawingResources.Rent();
            try
            {
                var commands = BuildMatrixCommands(resources, i);
                resources.Seal();
                d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale, i));
                d3d12Backend.Execute(commands, resources);
                d3d12Backend.EndFrame();
            }
            finally
            {
                FrameDrawingResources.Return(resources);
            }

            if (d3d12Renderer.IsDeviceRemoved)
            {
                output.WriteLine($"Device removed at frame {i}: {d3d12Renderer.DeviceError}");
                break;
            }
        }

        var finalDiag = d3d12Backend.FrameSerialDiagnostics;
        output.WriteLine($"Final: frameSerial={finalDiag.FrameSerial}, presentSerial={finalDiag.PresentSerial}, syncWaits={finalDiag.SyncWaitCount}");
        var atlasDiag = d3d12Renderer.GetGlyphAtlasTextDiagnostics();
        output.WriteLine(atlasDiag.HasValue ? $"Glyph atlas: {atlasDiag.Value.FormatSummary()}" : "Glyph atlas: (not initialized)");
        output.WriteLine("=== glyph atlas regression matrix diagnostic complete ===");
    }

    internal static DrawCommand[] BuildMatrixCommands(FrameDrawingResources resources, int frameIndex)
    {
        var noWrap = resources.AddTextStyle(CreateStyle(18, TextWrapping.NoWrap, TextHorizontalAlignment.Leading));
        var wrap = resources.AddTextStyle(CreateStyle(18, TextWrapping.Wrap, TextHorizontalAlignment.Leading));
        var centeredWrap = resources.AddTextStyle(CreateStyle(18, TextWrapping.Wrap, TextHorizontalAlignment.Center));

        var ascii = resources.AddText($"ASCII atlas {frameIndex:D3}");
        var latinExtended = resources.AddText($"Latin cafe \u00E9lan \u0100\u024F {frameIndex:D3}");
        var greek = resources.AddText($"Greek \u0391\u03A9 \u0394 {frameIndex:D3}");
        var cyrillic = resources.AddText($"Cyrillic \u0416\u042F {frameIndex:D3}");
        var cjk = resources.AddText($"CJK \u6E2C\u8A66 {frameIndex:D3}");
        var arabic = resources.AddText($"Arabic \u0645\u0631\u062D\u0628\u0627 {frameIndex:D3}");
        var hebrew = resources.AddText($"Hebrew \u05E9\u05DC\u05D5\u05DD {frameIndex:D3}");
        var mixedBidi = resources.AddText($"abc \u0645\u0631\u062D\u0628\u0627 xyz {frameIndex:D3}");
        var emoji = resources.AddText($"Emoji \ud83d\ude00 \u2764\uFE0F {frameIndex:D3}");
        var wrapped = resources.AddText($"wrap one two three {frameIndex:D3}");
        var tab = resources.AddText($"tab\tstop {frameIndex:D3}");
        var crlf = resources.AddText($"line A {frameIndex:D3}\r\nline B");
        var overHeight = resources.AddText($"alpha beta gamma delta {frameIndex:D3}");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 960, 540), Color: DrawColor.Opaque(18, 24, 32)),
            TextRun(24, 36, 260, 34, DrawColor.Opaque(238, 244, 255), ascii, noWrap),
            TextRun(24, 80, 300, 34, DrawColor.Opaque(228, 210, 255), latinExtended, noWrap),
            TextRun(24, 124, 260, 34, DrawColor.Opaque(184, 240, 180), greek, noWrap),
            TextRun(24, 168, 260, 34, DrawColor.Opaque(255, 228, 160), cyrillic, noWrap),
            TextRun(24, 212, 260, 34, DrawColor.Opaque(164, 232, 255), cjk, noWrap),
            TextRun(24, 256, 300, 34, DrawColor.Opaque(255, 216, 188), arabic, noWrap),
            TextRun(24, 300, 300, 34, DrawColor.Opaque(196, 226, 170), hebrew, noWrap),
            TextRun(24, 344, 340, 34, DrawColor.Opaque(178, 216, 255), mixedBidi, noWrap),
            TextRun(24, 388, 300, 34, DrawColor.Opaque(255, 190, 210), emoji, noWrap),
            TextRun(420, 36, 118, 76, DrawColor.Opaque(164, 232, 255), wrapped, wrap),
            TextRun(420, 132, 220, 34, DrawColor.Opaque(255, 228, 160), tab, noWrap),
            TextRun(420, 188, 220, 76, DrawColor.Opaque(184, 240, 180), crlf, noWrap),
            TextRun(420, 284, 128, 28, DrawColor.Opaque(238, 218, 154), overHeight, centeredWrap)
        ];
    }

    internal static GlyphAtlasRegressionMatrixSummary AnalyzeMatrixScene(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources)
    {
        var textRuns = 0;
        var atlasRuns = 0;
        var degradedRuns = 0;
        var wrappedRuns = 0;
        var tabRuns = 0;
        var explicitLineRuns = 0;
        var simpleBmpRuns = 0;
        var shapedRuns = 0;
        var cjkRuns = 0;
        var arabicRuns = 0;
        var hebrewRuns = 0;
        var mixedBidiRuns = 0;
        var emojiRuns = 0;
        var contract = GlyphAtlasDegradationContract.CreateDefault();

        foreach (ref readonly var command in commands)
        {
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            textRuns++;
            var style = resources.ResolveTextStyle(command.Resource).Normalize();
            var text = resources.Resolve(command.Text);
            var reason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(text, style);
            var accepted = reason == D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None || CanShapeAsAtlasRun(text, style, command.Rect.Width);
            if (accepted)
            {
                atlasRuns++;
            }
            else
            {
                degradedRuns++;
            }

            if (style.Wrapping == TextWrapping.Wrap) wrappedRuns++;
            if (GlyphAtlasTextCompositionHelpers.ContainsLineBreakOrTab(text))
            {
                if (ContainsTab(text)) tabRuns++;
                if (ContainsLineBreak(text)) explicitLineRuns++;
            }

            if (reason == D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None && ContainsNonAscii(text)) simpleBmpRuns++;
            if (reason.HasFlag(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii) && accepted) shapedRuns++;
            if (ContainsCjk(text)) cjkRuns++;
            if (ContainsArabic(text)) arabicRuns++;
            if (ContainsHebrew(text)) hebrewRuns++;
            if (ContainsMixedBidi(text)) mixedBidiRuns++;
            if (GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text)) emojiRuns++;
        }

        return new GlyphAtlasRegressionMatrixSummary(
            textRuns,
            atlasRuns,
            degradedRuns,
            wrappedRuns,
            tabRuns,
            explicitLineRuns,
            simpleBmpRuns,
            shapedRuns,
            cjkRuns,
            arabicRuns,
            hebrewRuns,
            mixedBidiRuns,
            emojiRuns,
            contract);
    }

    internal static string FormatSummary(GlyphAtlasRegressionMatrixSummary summary)
    {
        return $"Expected matrix: textRuns={summary.TextRuns}, atlasRuns={summary.AtlasRuns}, degradedRuns={summary.DegradedRuns}, wrappedRuns={summary.WrappedRuns}, tabRuns={summary.TabRuns}, explicitLineRuns={summary.ExplicitLineRuns}, simpleBmpRuns={summary.SimpleBmpRuns}, shapedRuns={summary.ShapedRuns}, cjkRuns={summary.CjkRuns}, arabicRuns={summary.ArabicRuns}, hebrewRuns={summary.HebrewRuns}, mixedBidiRuns={summary.MixedBidiRuns}, emojiRuns={summary.EmojiRuns}";
    }

    internal static string FormatContract(GlyphAtlasDegradationContract contract)
    {
        return $"Degradation contract: svgColorGlyph={contract.SvgColorGlyph}, colrPaintTreeColorGlyph={contract.ColrPaintTreeColorGlyph}, bidiBeyondResolvedLevels={contract.BidiBeyondResolvedLevels}, atlasFullAfterBudget={contract.AtlasFullAfterBudget}, recordFailure={contract.RecordFailure}, initializationFailure={contract.InitializationFailure}, overlayFallback={contract.OverlayFallback}";
    }

    private static bool CanShapeAsAtlasRun(ReadOnlySpan<char> text, TextStyle style, float width)
    {
        return (ContainsCjk(text)
                || GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate(text)
                || GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text)
                || ContainsCombiningMark(text))
            && (style.Wrapping == TextWrapping.NoWrap || (style.Wrapping == TextWrapping.Wrap && width >= 96 && ContainsWrapWhitespace(text)));
    }

    private static bool ContainsNonAscii(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character > '~')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCombiningMark(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u0300' and <= '\u036F')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWrapWhitespace(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTab(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character == '\t')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLineBreak(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (GlyphAtlasTextCompositionHelpers.IsLineBreak(text, i, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCjk(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsArabic(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u0600' and <= '\u08FF' or >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFC')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsHebrew(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u0590' and <= '\u05FF' or >= '\uFB1D' and <= '\uFB4F')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMixedBidi(ReadOnlySpan<char> text)
    {
        var firstRtl = -1;
        var lastRtl = -1;
        var hasLtrBeforeRtl = false;
        var hasLtrAfterRtl = false;
        foreach (var character in text)
        {
            if (GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character))
            {
                if (firstRtl < 0)
                {
                    firstRtl = 0;
                }

                lastRtl = 0;
                continue;
            }

            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                if (firstRtl < 0)
                {
                    hasLtrBeforeRtl = true;
                }
                else if (lastRtl >= 0)
                {
                    hasLtrAfterRtl = true;
                }
            }
        }

        return hasLtrBeforeRtl && hasLtrAfterRtl;
    }

    private static TextStyle CreateStyle(float fontSize, TextWrapping wrapping, TextHorizontalAlignment alignment)
    {
        return new TextStyle(
            TextStyle.Default.FontFamily,
            fontSize,
            TextFontWeight.Normal,
            TextStyle.Default.FontStyle,
            TextStyle.Default.FontStretch,
            alignment,
            TextVerticalAlignment.Top,
            wrapping);
    }

    private static DrawCommand TextRun(
        float x,
        float y,
        float width,
        float height,
        DrawColor color,
        TextSlice text,
        ResourceHandle style)
    {
        return new DrawCommand(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(x, y, width, height),
            Color: color,
            Resource: style,
            Text: text);
    }

    private static ScreenRegion CreatePrimaryWindowRegion(IScreenInfo screen)
    {
        const int windowWidth = 960;
        const int windowHeight = 540;
        var bounds = screen.PhysicalBounds;
        var x = bounds.X + Math.Max((bounds.Width - windowWidth) / 2, 0);
        var y = bounds.Y + Math.Max((bounds.Height - windowHeight) / 2, 0);
        return new ScreenRegion(screen.Id, new PixelRectangle(x, y, windowWidth, windowHeight));
    }
}

internal readonly struct GlyphAtlasRegressionMatrixSummary(
    int TextRuns,
    int AtlasRuns,
    int DegradedRuns,
    int WrappedRuns,
    int TabRuns,
    int ExplicitLineRuns,
    int SimpleBmpRuns,
    int ShapedRuns,
    int CjkRuns,
    int ArabicRuns,
    int HebrewRuns,
    int MixedBidiRuns,
    int EmojiRuns,
    GlyphAtlasDegradationContract Contract) : IEquatable<GlyphAtlasRegressionMatrixSummary>
{
    public int TextRuns { get; } = TextRuns;
    public int AtlasRuns { get; } = AtlasRuns;
    public int DegradedRuns { get; } = DegradedRuns;
    public int WrappedRuns { get; } = WrappedRuns;
    public int TabRuns { get; } = TabRuns;
    public int ExplicitLineRuns { get; } = ExplicitLineRuns;
    public int SimpleBmpRuns { get; } = SimpleBmpRuns;
    public int ShapedRuns { get; } = ShapedRuns;
    public int CjkRuns { get; } = CjkRuns;
    public int ArabicRuns { get; } = ArabicRuns;
    public int HebrewRuns { get; } = HebrewRuns;
    public int MixedBidiRuns { get; } = MixedBidiRuns;
    public int EmojiRuns { get; } = EmojiRuns;
    public GlyphAtlasDegradationContract Contract { get; } = Contract;

    public bool Equals(GlyphAtlasRegressionMatrixSummary other)
    {
        return TextRuns == other.TextRuns
            && AtlasRuns == other.AtlasRuns
            && DegradedRuns == other.DegradedRuns
            && WrappedRuns == other.WrappedRuns
            && TabRuns == other.TabRuns
            && ExplicitLineRuns == other.ExplicitLineRuns
            && SimpleBmpRuns == other.SimpleBmpRuns
            && ShapedRuns == other.ShapedRuns
            && CjkRuns == other.CjkRuns
            && ArabicRuns == other.ArabicRuns
            && HebrewRuns == other.HebrewRuns
            && MixedBidiRuns == other.MixedBidiRuns
            && EmojiRuns == other.EmojiRuns
            && Contract == other.Contract;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasRegressionMatrixSummary other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TextRuns);
        hash.Add(AtlasRuns);
        hash.Add(DegradedRuns);
        hash.Add(WrappedRuns);
        hash.Add(TabRuns);
        hash.Add(ExplicitLineRuns);
        hash.Add(SimpleBmpRuns);
        hash.Add(ShapedRuns);
        hash.Add(CjkRuns);
        hash.Add(ArabicRuns);
        hash.Add(HebrewRuns);
        hash.Add(MixedBidiRuns);
        hash.Add(EmojiRuns);
        hash.Add(Contract);
        return hash.ToHashCode();
    }

    public static bool operator ==(GlyphAtlasRegressionMatrixSummary left, GlyphAtlasRegressionMatrixSummary right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasRegressionMatrixSummary left, GlyphAtlasRegressionMatrixSummary right) => !left.Equals(right);
}

internal readonly struct GlyphAtlasDegradationContract(
    bool SvgColorGlyph,
    bool ColrPaintTreeColorGlyph,
    bool BidiBeyondResolvedLevels,
    bool AtlasFullAfterBudget,
    bool RecordFailure,
    bool InitializationFailure,
    bool OverlayFallback) : IEquatable<GlyphAtlasDegradationContract>
{
    public bool SvgColorGlyph { get; } = SvgColorGlyph;
    public bool ColrPaintTreeColorGlyph { get; } = ColrPaintTreeColorGlyph;
    public bool BidiBeyondResolvedLevels { get; } = BidiBeyondResolvedLevels;
    public bool AtlasFullAfterBudget { get; } = AtlasFullAfterBudget;
    public bool RecordFailure { get; } = RecordFailure;
    public bool InitializationFailure { get; } = InitializationFailure;
    public bool OverlayFallback { get; } = OverlayFallback;

    public static GlyphAtlasDegradationContract CreateDefault() =>
        new(
            SvgColorGlyph: true,
            ColrPaintTreeColorGlyph: true,
            BidiBeyondResolvedLevels: true,
            AtlasFullAfterBudget: true,
            RecordFailure: true,
            InitializationFailure: true,
            OverlayFallback: false);

    public bool Equals(GlyphAtlasDegradationContract other)
    {
        return SvgColorGlyph == other.SvgColorGlyph
            && ColrPaintTreeColorGlyph == other.ColrPaintTreeColorGlyph
            && BidiBeyondResolvedLevels == other.BidiBeyondResolvedLevels
            && AtlasFullAfterBudget == other.AtlasFullAfterBudget
            && RecordFailure == other.RecordFailure
            && InitializationFailure == other.InitializationFailure
            && OverlayFallback == other.OverlayFallback;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasDegradationContract other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(SvgColorGlyph, ColrPaintTreeColorGlyph, BidiBeyondResolvedLevels, AtlasFullAfterBudget, RecordFailure, InitializationFailure, OverlayFallback);

    public static bool operator ==(GlyphAtlasDegradationContract left, GlyphAtlasDegradationContract right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasDegradationContract left, GlyphAtlasDegradationContract right) => !left.Equals(right);
}
