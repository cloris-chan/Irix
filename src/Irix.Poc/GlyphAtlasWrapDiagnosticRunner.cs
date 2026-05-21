using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class GlyphAtlasWrapDiagnosticRunner
{
    internal static void Run(
        TextWriter output,
        int frameCount = 30,
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
            var commands = BuildWrapCommands(resources, frameIndex: 0);
            resources.Seal();
            var expected = AnalyzeWrapScene(commands, resources);

            output.WriteLine("=== Glyph Atlas Wrap Diagnostic ===");
            output.WriteLine($"Frames: {frameCount}");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine($"Text composition mode: {textCompositionMode}");
            output.WriteLine(FormatExpectedLine(expected));
            output.WriteLine("Wrap degradation: overlay=False asciiSpaceWrap=True explicitLineBreak=True tab=True simpleBmp=True hardWordClip=True shapedWrap=True ltrComplex=True");
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
                var commands = BuildWrapCommands(resources, i);
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
        if (atlasDiag.HasValue)
        {
            output.WriteLine($"Glyph atlas: {atlasDiag.Value.FormatSummary()}");
        }
        else
        {
            output.WriteLine("Glyph atlas: (not initialized)");
        }
        output.WriteLine("=== Glyph atlas wrap diagnostic complete ===");
    }

    internal static DrawCommand[] BuildWrapCommands(FrameDrawingResources resources, int frameIndex)
    {
        var noWrapStyle = resources.AddTextStyle(CreateStyle(18, TextWrapping.NoWrap, TextHorizontalAlignment.Leading));
        var wrapStyle = resources.AddTextStyle(CreateStyle(18, TextWrapping.Wrap, TextHorizontalAlignment.Leading));
        var centeredWrapStyle = resources.AddTextStyle(CreateStyle(18, TextWrapping.Wrap, TextHorizontalAlignment.Center));

        var noWrapText = resources.AddText($"atlas nowrap {frameIndex:D3}");
        var wrappedText = resources.AddText($"one two three four {frameIndex:D3}");
        var explicitLineText = resources.AddText($"line A {frameIndex:D3}\nline B");
        var tabbedText = resources.AddText($"tab\tstop {frameIndex:D3}");
        var simpleBmpText = resources.AddText($"cafe \u00E9lan \u0394\u0416 {frameIndex:D3}");
        var hardWordText = resources.AddText($"supercalifragilisticexpialidocious{frameIndex:D3}");
        var nonAsciiText = resources.AddText($"shape\tcafe\u0301 next cafe\u0301 {frameIndex:D3}");
        var emojiText = resources.AddText($"emoji \ud83d\ude00 heart \u2764\uFE0F {frameIndex:D3}");
        var thaiText = resources.AddText($"thai \u0E44\u0E17\u0E22 {frameIndex:D3}");
        var complexScriptText = resources.AddText($"arabic \u0645\u0631\u062D\u0628\u0627 {frameIndex:D3}");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 960, 540), Color: DrawColor.Opaque(18, 24, 32)),
            TextRun(24, 44, 260, 42, DrawColor.Opaque(238, 244, 255), noWrapText, noWrapStyle),
            TextRun(24, 104, 118, 76, DrawColor.Opaque(164, 232, 255), wrappedText, wrapStyle),
            TextRun(24, 204, 190, 76, DrawColor.Opaque(184, 240, 180), explicitLineText, noWrapStyle),
            TextRun(24, 304, 190, 42, DrawColor.Opaque(255, 228, 160), tabbedText, noWrapStyle),
            TextRun(24, 364, 220, 42, DrawColor.Opaque(228, 210, 255), simpleBmpText, noWrapStyle),
            TextRun(272, 364, 42, 76, DrawColor.Opaque(255, 198, 128), hardWordText, wrapStyle),
            TextRun(24, 444, 132, 96, DrawColor.Opaque(255, 160, 220), nonAsciiText, wrapStyle),
            TextRun(272, 444, 220, 42, DrawColor.Opaque(255, 190, 210), emojiText, noWrapStyle),
            TextRun(520, 444, 220, 42, DrawColor.Opaque(180, 232, 255), thaiText, noWrapStyle),
            TextRun(272, 496, 220, 42, DrawColor.Opaque(210, 190, 255), complexScriptText, noWrapStyle)
        ];
    }

    internal static GlyphAtlasWrapSceneSummary AnalyzeWrapScene(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources)
    {
        var textRuns = 0;
        var atlasCandidateRuns = 0;
        var degradedCandidateRuns = 0;
        var wrappedAtlasCandidateRuns = 0;
        var wrappingFallbackRuns = 0;
        var nonAsciiFallbackRuns = 0;
        var colorGlyphFallbackRuns = 0;
        var complexScriptFallbackRuns = 0;

        foreach (var command in commands)
        {
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            textRuns++;
            var style = resources.ResolveTextStyle(command.Resource).Normalize();
            var text = resources.Resolve(command.Text);
            var reason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(text, style);
            if (reason.HasFlag(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii))
            {
                if (CanShapeAsAtlasRun(text, style, command.Rect.Width))
                {
                    atlasCandidateRuns++;
                    if (style.Wrapping == TextWrapping.Wrap)
                    {
                        wrappedAtlasCandidateRuns++;
                    }

                    continue;
                }

                degradedCandidateRuns++;
                nonAsciiFallbackRuns++;
                if (GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector(text))
                {
                    colorGlyphFallbackRuns++;
                }

                if (GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate(text))
                {
                    complexScriptFallbackRuns++;
                }

                continue;
            }

            atlasCandidateRuns++;
            if (style.Wrapping == TextWrapping.Wrap)
            {
                wrappedAtlasCandidateRuns++;
            }
        }

        return new GlyphAtlasWrapSceneSummary(
            textRuns,
            atlasCandidateRuns,
            degradedCandidateRuns,
            wrappedAtlasCandidateRuns,
            wrappingFallbackRuns,
            nonAsciiFallbackRuns,
            colorGlyphFallbackRuns,
            complexScriptFallbackRuns);
    }

    internal static string FormatExpectedLine(GlyphAtlasWrapSceneSummary summary)
    {
        return $"Expected GlyphAtlas per frame: textRuns={summary.TextRuns}, atlasRuns={summary.AtlasCandidateRuns}, "
            + $"degradedRuns={summary.DegradedCandidateRuns}, wrappedAtlasRuns={summary.WrappedAtlasCandidateRuns}, "
            + $"Wrapping={summary.WrappingFallbackRuns}, NonAscii={summary.NonAsciiFallbackRuns}, "
            + $"ColorGlyph={summary.ColorGlyphFallbackRuns}, ComplexScript={summary.ComplexScriptFallbackRuns}";
    }

    private static bool CanShapeAsAtlasRun(ReadOnlySpan<char> text, TextStyle style, float width)
    {
        return !GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate(text)
            && (GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector(text) || ContainsCombiningMark(text) || GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate(text))
            && (style.Wrapping == TextWrapping.NoWrap || (style.Wrapping == TextWrapping.Wrap && width >= 96 && ContainsWrapWhitespace(text)));
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

    private static bool ContainsSpace(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (character == ' ')
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

internal readonly struct GlyphAtlasWrapSceneSummary(
    int TextRuns,
    int AtlasCandidateRuns,
    int DegradedCandidateRuns,
    int WrappedAtlasCandidateRuns,
    int WrappingFallbackRuns,
    int NonAsciiFallbackRuns,
    int ColorGlyphFallbackRuns,
    int ComplexScriptFallbackRuns) : IEquatable<GlyphAtlasWrapSceneSummary>
{
    public int TextRuns { get; } = TextRuns;
    public int AtlasCandidateRuns { get; } = AtlasCandidateRuns;
    public int DegradedCandidateRuns { get; } = DegradedCandidateRuns;
    public int WrappedAtlasCandidateRuns { get; } = WrappedAtlasCandidateRuns;
    public int WrappingFallbackRuns { get; } = WrappingFallbackRuns;
    public int NonAsciiFallbackRuns { get; } = NonAsciiFallbackRuns;
    public int ColorGlyphFallbackRuns { get; } = ColorGlyphFallbackRuns;
    public int ComplexScriptFallbackRuns { get; } = ComplexScriptFallbackRuns;

    public bool Equals(GlyphAtlasWrapSceneSummary other)
    {
        return TextRuns == other.TextRuns
            && AtlasCandidateRuns == other.AtlasCandidateRuns
            && DegradedCandidateRuns == other.DegradedCandidateRuns
            && WrappedAtlasCandidateRuns == other.WrappedAtlasCandidateRuns
            && WrappingFallbackRuns == other.WrappingFallbackRuns
            && NonAsciiFallbackRuns == other.NonAsciiFallbackRuns
            && ColorGlyphFallbackRuns == other.ColorGlyphFallbackRuns
            && ComplexScriptFallbackRuns == other.ComplexScriptFallbackRuns;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasWrapSceneSummary other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            TextRuns,
            AtlasCandidateRuns,
            DegradedCandidateRuns,
            WrappedAtlasCandidateRuns,
            WrappingFallbackRuns,
            NonAsciiFallbackRuns,
            ColorGlyphFallbackRuns,
            ComplexScriptFallbackRuns);
    }

    public static bool operator ==(GlyphAtlasWrapSceneSummary left, GlyphAtlasWrapSceneSummary right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasWrapSceneSummary left, GlyphAtlasWrapSceneSummary right) => !left.Equals(right);
}
