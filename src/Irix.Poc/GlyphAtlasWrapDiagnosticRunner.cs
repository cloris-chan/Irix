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
            output.WriteLine("Wrap degradation: overlay=False asciiSpaceWrap=True explicitLineBreak=True hardWord=True nonAscii=True");
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
        var hardWordText = resources.AddText($"supercalifragilisticexpialidocious{frameIndex:D3}");
        var nonAsciiText = resources.AddText($"wrap 測試 {frameIndex:D3}");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 960, 540), Color: DrawColor.Opaque(18, 24, 32)),
            TextRun(24, 44, 260, 42, DrawColor.Opaque(238, 244, 255), noWrapText, noWrapStyle),
            TextRun(24, 104, 118, 76, DrawColor.Opaque(164, 232, 255), wrappedText, wrapStyle),
            TextRun(24, 204, 190, 76, DrawColor.Opaque(184, 240, 180), explicitLineText, noWrapStyle),
            TextRun(24, 304, 42, 76, DrawColor.Opaque(255, 198, 128), hardWordText, wrapStyle),
            TextRun(24, 404, 180, 76, DrawColor.Opaque(255, 160, 220), nonAsciiText, centeredWrapStyle)
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
                degradedCandidateRuns++;
                nonAsciiFallbackRuns++;
                continue;
            }

            if (IsHardWrapCandidate(text, style, command.Rect.Width))
            {
                degradedCandidateRuns++;
                wrappingFallbackRuns++;
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
            nonAsciiFallbackRuns);
    }

    internal static string FormatExpectedLine(GlyphAtlasWrapSceneSummary summary)
    {
        return $"Expected GlyphAtlas per frame: textRuns={summary.TextRuns}, atlasRuns={summary.AtlasCandidateRuns}, "
            + $"degradedRuns={summary.DegradedCandidateRuns}, wrappedAtlasRuns={summary.WrappedAtlasCandidateRuns}, "
            + $"Wrapping={summary.WrappingFallbackRuns}, NonAscii={summary.NonAsciiFallbackRuns}";
    }

    private static bool IsHardWrapCandidate(ReadOnlySpan<char> text, TextStyle style, float width)
    {
        return style.Wrapping == TextWrapping.Wrap
            && width < 96
            && !ContainsSpace(text);
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
    int NonAsciiFallbackRuns) : IEquatable<GlyphAtlasWrapSceneSummary>
{
    public int TextRuns { get; } = TextRuns;
    public int AtlasCandidateRuns { get; } = AtlasCandidateRuns;
    public int DegradedCandidateRuns { get; } = DegradedCandidateRuns;
    public int WrappedAtlasCandidateRuns { get; } = WrappedAtlasCandidateRuns;
    public int WrappingFallbackRuns { get; } = WrappingFallbackRuns;
    public int NonAsciiFallbackRuns { get; } = NonAsciiFallbackRuns;

    public bool Equals(GlyphAtlasWrapSceneSummary other)
    {
        return TextRuns == other.TextRuns
            && AtlasCandidateRuns == other.AtlasCandidateRuns
            && DegradedCandidateRuns == other.DegradedCandidateRuns
            && WrappedAtlasCandidateRuns == other.WrappedAtlasCandidateRuns
            && WrappingFallbackRuns == other.WrappingFallbackRuns
            && NonAsciiFallbackRuns == other.NonAsciiFallbackRuns;
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
            NonAsciiFallbackRuns);
    }

    public static bool operator ==(GlyphAtlasWrapSceneSummary left, GlyphAtlasWrapSceneSummary right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasWrapSceneSummary left, GlyphAtlasWrapSceneSummary right) => !left.Equals(right);
}
