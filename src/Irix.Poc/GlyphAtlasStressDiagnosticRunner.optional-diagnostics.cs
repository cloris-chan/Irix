#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class GlyphAtlasStressDiagnosticRunner
{
    private const int RunCount = 32;
    private const string StandardScenarioName = "AtlasFull";
    private const string MixedFallbackScenarioName = "MixedAtlasFull";
    private const string ReuseScenarioName = "MixedAtlasFullReuse";

    internal static void Run(TextWriter output, bool mixedFallback = false, bool reusePage = false)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = screen.Scale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        d3d12Renderer.TextCompositionMode = TextCompositionMode.GlyphAtlas;
        var resources = FrameDrawingResources.Rent();
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        compositor.SetViewport(window.Region.PhysicalBounds, displayScale);

        var ascii = new string(Enumerable.Range(32, 95).Select(static code => (char)code).ToArray());
        var bounds = window.Region.PhysicalBounds;
        var commands = mixedFallback
            ? BuildMixedFallbackStressCommands(resources, ascii, bounds.Width, bounds.Height)
            : BuildStressCommands(resources, ascii, bounds.Width, bounds.Height);

        resources.Seal();
        using var batch = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commands.Length),
            [],
            resources);
        compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();

        var renderedRunCount = CountTextRuns(commands);
        if (reusePage)
        {
            var reuseResources = FrameDrawingResources.Rent();
            var reuseCommands = BuildReuseCommands(reuseResources, bounds.Width, bounds.Height);
            reuseResources.Seal();
            using var reuseBatch = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(reuseCommands), reuseCommands.Length),
                [],
                reuseResources);
            compositor.RenderAsync(reuseBatch).AsTask().GetAwaiter().GetResult();
            renderedRunCount += CountTextRuns(reuseCommands);
        }

        WriteReport(
            output,
            d3d12Renderer.TextCompositionMode,
            screen.RefreshRateHz,
            displayScale,
            renderedRunCount,
            ascii.Length,
            reusePage ? ReuseScenarioName : mixedFallback ? MixedFallbackScenarioName : StandardScenarioName,
            d3d12Renderer.IsDeviceRemoved,
            d3d12Renderer.DeviceError,
            d3d12Backend.FrameSerialDiagnostics,
            d3d12Renderer.GetGlyphAtlasTextDiagnostics());
    }

    internal static DrawCommand[] BuildStressCommands(FrameDrawingResources resources, string ascii, int width, int height)
    {
        var commands = new DrawCommand[RunCount + 1];
        commands[0] = Background(width, height);
        AppendStressRuns(commands, startIndex: 1, resources, ascii);
        return commands;
    }

    internal static DrawCommand[] BuildMixedFallbackStressCommands(FrameDrawingResources resources, string ascii, int width, int height)
    {
        var commands = new DrawCommand[RunCount + 4];
        commands[0] = Background(width, height);
        commands[1] = TextRun(resources, "Atlas prefix A", 16, 12, 320, 36, 18, TextFontWeight.Normal, TextFontStyle.Normal, DrawColor.Opaque(220, 244, 255));
        commands[2] = TextRun(resources, "Atlas prefix B", 16, 52, 320, 36, 20, TextFontWeight.SemiBold, TextFontStyle.Normal, DrawColor.Opaque(180, 220, 255));
        AppendStressRuns(commands, startIndex: 3, resources, ascii);
        commands[^1] = TextRun(resources, "AtlasFull 後 fallback", 16, 292, 420, 40, 20, TextFontWeight.Normal, TextFontStyle.Normal, DrawColor.Opaque(255, 210, 160));
        return commands;
    }

    internal static DrawCommand[] BuildReuseCommands(FrameDrawingResources resources, int width, int height)
    {
        return
        [
            Background(width, height),
            TextRun(resources, "Atlas reuse A", 16, 18, 260, 36, 20, TextFontWeight.Normal, TextFontStyle.Normal, DrawColor.Opaque(210, 245, 225)),
            TextRun(resources, "Atlas reuse B", 16, 58, 260, 36, 20, TextFontWeight.SemiBold, TextFontStyle.Normal, DrawColor.Opaque(190, 230, 255))
        ];
    }

    internal static void WriteReport(
        TextWriter output,
        TextCompositionMode textCompositionMode,
        int refreshRateHz,
        DisplayScale displayScale,
        int runCount,
        int asciiCharsPerRun,
        string scenarioName,
        bool deviceRemoved,
        DeviceErrorDiagnostic deviceError,
        D3D12Renderer.FrameSerialDiagnostics frameSerialDiagnostics,
        D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics? glyphAtlasDiagnostics)
    {
        output.WriteLine("=== Glyph Atlas Stress Diagnostic ===");
        output.WriteLine($"Scenario: {scenarioName}");
        output.WriteLine($"Text composition mode: {textCompositionMode}");
        output.WriteLine($"Display refresh: {refreshRateHz}Hz");
        output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        output.WriteLine($"Runs: {runCount}");
        output.WriteLine($"ASCII chars per run: {asciiCharsPerRun}");
        output.WriteLine($"Device removed: {deviceRemoved}");
        output.WriteLine($"Device error reason: {deviceError}");
        output.WriteLine($"Frame serial: frameSerial={frameSerialDiagnostics.FrameSerial}, presentSerial={frameSerialDiagnostics.PresentSerial}, syncWaits={frameSerialDiagnostics.SyncWaitCount}");
        if (glyphAtlasDiagnostics.HasValue)
        {
            output.WriteLine($"Glyph atlas: {glyphAtlasDiagnostics.Value.FormatSummary()}");
        }
        else
        {
            output.WriteLine("Glyph atlas: (not initialized)");
        }
        output.WriteLine("=== Glyph atlas stress diagnostic complete ===");
    }

    private static DrawCommand Background(int width, int height)
    {
        return new DrawCommand(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(0, 0, width, height),
            Color: DrawColor.Opaque(18, 18, 18));
    }

    private static void AppendStressRuns(DrawCommand[] commands, int startIndex, FrameDrawingResources resources, string ascii)
    {
        for (var i = 0; i < RunCount; i++)
        {
            var fontSize = 48f + i * 4f;
            commands[startIndex + i] = TextRun(
                resources,
                ascii,
                8,
                8 + (i % 6) * 40,
                60000,
                MathF.Ceiling(fontSize * 2.2f),
                fontSize,
                i % 3 == 0 ? TextFontWeight.Bold : TextFontWeight.Normal,
                i % 4 == 0 ? TextFontStyle.Italic : TextFontStyle.Normal,
                DrawColor.Opaque(245, 245, 245));
        }
    }

    private static DrawCommand TextRun(
        FrameDrawingResources resources,
        string text,
        float x,
        float y,
        float width,
        float height,
        float fontSize,
        TextFontWeight fontWeight,
        TextFontStyle fontStyle,
        DrawColor color)
    {
        var style = new TextStyle(
            TextFontFamily.SegoeUi,
            fontSize,
            fontWeight,
            fontStyle,
            TextFontStretch.Normal,
            TextHorizontalAlignment.Leading,
            TextVerticalAlignment.Top,
            TextWrapping.NoWrap);
        var styleHandle = resources.AddTextStyle(style);
        var textSlice = resources.AddText(text);
        return new DrawCommand(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(x, y, width, height),
            Resource: styleHandle,
            Text: textSlice,
            Color: color);
    }

    private static int CountTextRuns(ReadOnlySpan<DrawCommand> commands)
    {
        var count = 0;
        foreach (var command in commands)
        {
            if (command.Kind == DrawCommandKind.DrawTextRun)
            {
                count++;
            }
        }

        return count;
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
#endif
