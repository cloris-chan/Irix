using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class GlyphAtlasSoakDiagnosticRunner
{
    private const int PressureRunCount = 32;
    private static readonly string AsciiPrintable = new(Enumerable.Range(32, 95).Select(static code => (char)code).ToArray());

    internal static void Run(
        TextWriter output,
        int frameCount = 60,
        int pressureEvery = 6,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas,
        DisplayScale diagnosticScale = default)
    {
        frameCount = Math.Max(1, frameCount);
        pressureEvery = Math.Max(1, pressureEvery);

        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale == default ? screen.Scale.Normalize() : diagnosticScale.Normalize();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        d3d12Renderer.TextCompositionMode = textCompositionMode;
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        var viewport = new DrawRect(0, 0, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var summary = GlyphAtlasSoakSummary.Empty;
        var pressureIndex = 0;
        var deviceLost = false;

        output.WriteLine("=== Glyph Atlas Soak Diagnostic ===");
        output.WriteLine($"Frames: {frameCount}");
        output.WriteLine($"Pressure cadence: every {pressureEvery} frame(s)");
        output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
        output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        output.WriteLine($"Text composition mode: {textCompositionMode}");
        output.WriteLine(FormatPagePolicy(GlyphAtlasSoakSummary.Empty));
        output.WriteLine();

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var scenario = SelectScenario(frameIndex, pressureEvery);
            var resources = FrameDrawingResources.Rent();
            try
            {
                var commands = scenario switch
                {
                    GlyphAtlasSoakScenario.Pressure => BuildPressureCommands(resources, AsciiPrintable, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height, pressureIndex++),
                    GlyphAtlasSoakScenario.Matrix => GlyphAtlasRegressionMatrixDiagnosticRunner.BuildMatrixCommands(resources, frameIndex),
                    GlyphAtlasSoakScenario.Wrap => GlyphAtlasWrapDiagnosticRunner.BuildWrapCommands(resources, frameIndex),
                    _ => GlyphAtlasStressDiagnosticRunner.BuildReuseCommands(resources, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height)
                };
                resources.Seal();
                d3d12Backend.BeginFrame(new FrameContext((int)viewport.Width, (int)viewport.Height, displayScale, frameIndex));
                d3d12Backend.Execute(commands, resources);
                d3d12Backend.EndFrame();
            }
            finally
            {
                FrameDrawingResources.Return(resources);
            }

            summary = summary.WithFrame(scenario, d3d12Renderer.GetGlyphAtlasTextDiagnostics());
            if (d3d12Renderer.IsDeviceRemoved)
            {
                deviceLost = true;
                output.WriteLine($"Device removed at frame {frameIndex}: {d3d12Renderer.DeviceError}");
                break;
            }
        }

        var finalDiag = d3d12Backend.FrameSerialDiagnostics;
        output.WriteLine($"Final: frameSerial={finalDiag.FrameSerial}, presentSerial={finalDiag.PresentSerial}, syncWaits={finalDiag.SyncWaitCount}");
        output.WriteLine(FormatSummary(summary));
        output.WriteLine(FormatThresholds());
        output.WriteLine(FormatThresholdActual(deviceLost, finalDiag.SyncWaitCount, summary));
        var atlasDiag = d3d12Renderer.GetGlyphAtlasTextDiagnostics();
        output.WriteLine(atlasDiag.HasValue ? $"Glyph atlas: {atlasDiag.Value.FormatSummary()}" : "Glyph atlas: (not initialized)");
        output.WriteLine("=== glyph atlas soak diagnostic complete ===");
    }

    internal static GlyphAtlasSoakScenario SelectScenario(int frameIndex, int pressureEvery)
    {
        pressureEvery = Math.Max(1, pressureEvery);
        if (frameIndex % pressureEvery == 0)
        {
            return GlyphAtlasSoakScenario.Pressure;
        }

        return (frameIndex & 3) switch
        {
            1 => GlyphAtlasSoakScenario.Matrix,
            2 => GlyphAtlasSoakScenario.Wrap,
            _ => GlyphAtlasSoakScenario.Reuse
        };
    }

    internal static DrawCommand[] BuildPressureCommands(FrameDrawingResources resources, string ascii, int width, int height, int pressureIndex)
    {
        var commands = new DrawCommand[PressureRunCount + 1];
        commands[0] = new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, width, height), Color: DrawColor.Opaque(18, 18, 18));
        var baseFontSize = 44f + pressureIndex * 1.75f;
        for (var i = 0; i < PressureRunCount; i++)
        {
            var fontSize = baseFontSize + i * 4f;
            commands[i + 1] = TextRun(
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

        return commands;
    }

    internal static string FormatPagePolicy(GlyphAtlasSoakSummary summary)
    {
        var budget = summary.AtlasBudgetPages == 0 ? D3D12GlyphAtlasTextRenderer.AtlasPageBudget : summary.AtlasBudgetPages;
        return $"Page policy: budgetPages={budget}, pageReuse=FormatScopedColdPage, retainedFloorGate=True, currentRecordColdReuse=True, sameRecordTouchedReuse=False, entryLru=False, subRectFreeList=False";
    }

    internal static string FormatSummary(GlyphAtlasSoakSummary summary)
    {
        return $"Soak summary: frames={summary.Frames}, pressureFrames={summary.PressureFrames}, matrixFrames={summary.MatrixFrames}, wrapFrames={summary.WrapFrames}, reuseFrames={summary.ReuseFrames}, "
            + $"maxAtlasPages={summary.MaxAtlasPages}, maxAlphaPages={summary.MaxAlphaPages}, maxBgraPages={summary.MaxBgraPages}, maxAtlasCpuBytes={summary.MaxAtlasCpuBytes} bytes, maxAtlasGpuBytes={summary.MaxAtlasGpuBytes} bytes, "
            + $"maxAtlasUsed={summary.MaxAtlasUsedPixels} px, maxAtlasFragmented={summary.MaxAtlasFragmentedPixels} px, atlasEvictions={summary.AtlasEvictions}, atlasAlphaEvictions={summary.AtlasAlphaEvictions}, atlasBgraEvictions={summary.AtlasBgraEvictions}, "
            + $"atlasPendingPageReuses={summary.AtlasPendingPageReuses}, atlasPendingAlphaPageReuses={summary.AtlasPendingAlphaPageReuses}, atlasPendingBgraPageReuses={summary.AtlasPendingBgraPageReuses}, "
            + $"atlasPageReuseRequests={summary.AtlasPageReuseRequests}, atlasAlphaPageReuseRequests={summary.AtlasAlphaPageReuseRequests}, atlasBgraPageReuseRequests={summary.AtlasBgraPageReuseRequests}, "
            + $"atlasFullWithoutPageReuse={summary.AtlasFullWithoutPageReuse}, atlasAlphaFullWithoutPageReuse={summary.AtlasAlphaFullWithoutPageReuse}, atlasBgraFullWithoutPageReuse={summary.AtlasBgraFullWithoutPageReuse}, maxDegradedRuns={summary.MaxDegradedRuns}";
    }

    internal static string FormatThresholds()
    {
        return "Soak thresholds: noDeviceLost=True, overlaySync=False, hardFullWithoutReuse=0, countersPresent=fragmentation|eviction|reuse|residentBytes";
    }

    internal static string FormatThresholdActual(bool deviceLost, long syncWaits, GlyphAtlasSoakSummary summary)
    {
        var countersPresent = summary.Frames > 0
            && summary.MaxAtlasCpuBytes >= 0
            && summary.MaxAtlasGpuBytes >= 0
            && summary.MaxAtlasFragmentedPixels >= 0
            && summary.AtlasEvictions >= 0
            && summary.AtlasPageReuseRequests >= 0;
        return $"soak.actual deviceLost={deviceLost} overlaySync=False syncWaits={syncWaits} hardFullWithoutReuse={summary.AtlasFullWithoutPageReuse} countersPresent={countersPresent}";
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

internal enum GlyphAtlasSoakScenario : byte
{
    Matrix,
    Wrap,
    Pressure,
    Reuse
}

internal readonly struct GlyphAtlasSoakSummary(
    int Frames,
    int PressureFrames,
    int MatrixFrames,
    int WrapFrames,
    int ReuseFrames,
    int AtlasBudgetPages,
    int MaxAtlasPages,
    int MaxAlphaPages,
    int MaxBgraPages,
    long MaxAtlasCpuBytes,
    long MaxAtlasGpuBytes,
    int MaxAtlasUsedPixels,
    int MaxAtlasFragmentedPixels,
    int AtlasEvictions,
    int AtlasAlphaEvictions,
    int AtlasBgraEvictions,
    int AtlasPendingPageReuses,
    int AtlasPendingAlphaPageReuses,
    int AtlasPendingBgraPageReuses,
    int AtlasPageReuseRequests,
    int AtlasAlphaPageReuseRequests,
    int AtlasBgraPageReuseRequests,
    int AtlasFullWithoutPageReuse,
    int AtlasAlphaFullWithoutPageReuse,
    int AtlasBgraFullWithoutPageReuse,
    int MaxDegradedRuns) : IEquatable<GlyphAtlasSoakSummary>
{
    public int Frames { get; } = Frames;
    public int PressureFrames { get; } = PressureFrames;
    public int MatrixFrames { get; } = MatrixFrames;
    public int WrapFrames { get; } = WrapFrames;
    public int ReuseFrames { get; } = ReuseFrames;
    public int AtlasBudgetPages { get; } = AtlasBudgetPages;
    public int MaxAtlasPages { get; } = MaxAtlasPages;
    public int MaxAlphaPages { get; } = MaxAlphaPages;
    public int MaxBgraPages { get; } = MaxBgraPages;
    public long MaxAtlasCpuBytes { get; } = MaxAtlasCpuBytes;
    public long MaxAtlasGpuBytes { get; } = MaxAtlasGpuBytes;
    public int MaxAtlasUsedPixels { get; } = MaxAtlasUsedPixels;
    public int MaxAtlasFragmentedPixels { get; } = MaxAtlasFragmentedPixels;
    public int AtlasEvictions { get; } = AtlasEvictions;
    public int AtlasAlphaEvictions { get; } = AtlasAlphaEvictions;
    public int AtlasBgraEvictions { get; } = AtlasBgraEvictions;
    public int AtlasPendingPageReuses { get; } = AtlasPendingPageReuses;
    public int AtlasPendingAlphaPageReuses { get; } = AtlasPendingAlphaPageReuses;
    public int AtlasPendingBgraPageReuses { get; } = AtlasPendingBgraPageReuses;
    public int AtlasPageReuseRequests { get; } = AtlasPageReuseRequests;
    public int AtlasAlphaPageReuseRequests { get; } = AtlasAlphaPageReuseRequests;
    public int AtlasBgraPageReuseRequests { get; } = AtlasBgraPageReuseRequests;
    public int AtlasFullWithoutPageReuse { get; } = AtlasFullWithoutPageReuse;
    public int AtlasAlphaFullWithoutPageReuse { get; } = AtlasAlphaFullWithoutPageReuse;
    public int AtlasBgraFullWithoutPageReuse { get; } = AtlasBgraFullWithoutPageReuse;
    public int MaxDegradedRuns { get; } = MaxDegradedRuns;

    public static GlyphAtlasSoakSummary Empty => default;

    public GlyphAtlasSoakSummary WithFrame(
        GlyphAtlasSoakScenario scenario,
        D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics? diagnostics)
    {
        var pressureFrames = PressureFrames + (scenario == GlyphAtlasSoakScenario.Pressure ? 1 : 0);
        var matrixFrames = MatrixFrames + (scenario == GlyphAtlasSoakScenario.Matrix ? 1 : 0);
        var wrapFrames = WrapFrames + (scenario == GlyphAtlasSoakScenario.Wrap ? 1 : 0);
        var reuseFrames = ReuseFrames + (scenario == GlyphAtlasSoakScenario.Reuse ? 1 : 0);
        if (!diagnostics.HasValue)
        {
            return new GlyphAtlasSoakSummary(
                Frames + 1,
                pressureFrames,
                matrixFrames,
                wrapFrames,
                reuseFrames,
                AtlasBudgetPages,
                MaxAtlasPages,
                MaxAlphaPages,
                MaxBgraPages,
                MaxAtlasCpuBytes,
                MaxAtlasGpuBytes,
                MaxAtlasUsedPixels,
                MaxAtlasFragmentedPixels,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                MaxDegradedRuns);
        }

        var value = diagnostics.GetValueOrDefault();
        return new GlyphAtlasSoakSummary(
            Frames + 1,
            pressureFrames,
            matrixFrames,
            wrapFrames,
            reuseFrames,
            value.AtlasBudgetPages,
            Math.Max(MaxAtlasPages, value.AtlasPages),
            Math.Max(MaxAlphaPages, value.AtlasAlphaPages),
            Math.Max(MaxBgraPages, value.AtlasBgraPages),
            Math.Max(MaxAtlasCpuBytes, value.AtlasCpuBytes),
            Math.Max(MaxAtlasGpuBytes, value.AtlasGpuBytes),
            Math.Max(MaxAtlasUsedPixels, value.AtlasUsedPixels),
            Math.Max(MaxAtlasFragmentedPixels, value.AtlasFragmentedPixels),
            value.AtlasEvictions,
            value.AtlasAlphaEvictions,
            value.AtlasBgraEvictions,
            value.AtlasPendingPageReuses,
            value.AtlasPendingAlphaPageReuses,
            value.AtlasPendingBgraPageReuses,
            value.AtlasPageReuseRequests,
            value.AtlasAlphaPageReuseRequests,
            value.AtlasBgraPageReuseRequests,
            value.AtlasFullWithoutPageReuse,
            value.AtlasAlphaFullWithoutPageReuse,
            value.AtlasBgraFullWithoutPageReuse,
            Math.Max(MaxDegradedRuns, value.DegradedRuns));
    }

    public bool Equals(GlyphAtlasSoakSummary other)
    {
        return Frames == other.Frames
            && PressureFrames == other.PressureFrames
            && MatrixFrames == other.MatrixFrames
            && WrapFrames == other.WrapFrames
            && ReuseFrames == other.ReuseFrames
            && AtlasBudgetPages == other.AtlasBudgetPages
            && MaxAtlasPages == other.MaxAtlasPages
            && MaxAlphaPages == other.MaxAlphaPages
            && MaxBgraPages == other.MaxBgraPages
            && MaxAtlasCpuBytes == other.MaxAtlasCpuBytes
            && MaxAtlasGpuBytes == other.MaxAtlasGpuBytes
            && MaxAtlasUsedPixels == other.MaxAtlasUsedPixels
            && MaxAtlasFragmentedPixels == other.MaxAtlasFragmentedPixels
            && AtlasEvictions == other.AtlasEvictions
            && AtlasAlphaEvictions == other.AtlasAlphaEvictions
            && AtlasBgraEvictions == other.AtlasBgraEvictions
            && AtlasPendingPageReuses == other.AtlasPendingPageReuses
            && AtlasPendingAlphaPageReuses == other.AtlasPendingAlphaPageReuses
            && AtlasPendingBgraPageReuses == other.AtlasPendingBgraPageReuses
            && AtlasPageReuseRequests == other.AtlasPageReuseRequests
            && AtlasAlphaPageReuseRequests == other.AtlasAlphaPageReuseRequests
            && AtlasBgraPageReuseRequests == other.AtlasBgraPageReuseRequests
            && AtlasFullWithoutPageReuse == other.AtlasFullWithoutPageReuse
            && AtlasAlphaFullWithoutPageReuse == other.AtlasAlphaFullWithoutPageReuse
            && AtlasBgraFullWithoutPageReuse == other.AtlasBgraFullWithoutPageReuse
            && MaxDegradedRuns == other.MaxDegradedRuns;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasSoakSummary other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Frames);
        hash.Add(PressureFrames);
        hash.Add(MatrixFrames);
        hash.Add(WrapFrames);
        hash.Add(ReuseFrames);
        hash.Add(AtlasBudgetPages);
        hash.Add(MaxAtlasPages);
        hash.Add(MaxAlphaPages);
        hash.Add(MaxBgraPages);
        hash.Add(MaxAtlasCpuBytes);
        hash.Add(MaxAtlasGpuBytes);
        hash.Add(MaxAtlasUsedPixels);
        hash.Add(MaxAtlasFragmentedPixels);
        hash.Add(AtlasEvictions);
        hash.Add(AtlasAlphaEvictions);
        hash.Add(AtlasBgraEvictions);
        hash.Add(AtlasPendingPageReuses);
        hash.Add(AtlasPendingAlphaPageReuses);
        hash.Add(AtlasPendingBgraPageReuses);
        hash.Add(AtlasPageReuseRequests);
        hash.Add(AtlasAlphaPageReuseRequests);
        hash.Add(AtlasBgraPageReuseRequests);
        hash.Add(AtlasFullWithoutPageReuse);
        hash.Add(AtlasAlphaFullWithoutPageReuse);
        hash.Add(AtlasBgraFullWithoutPageReuse);
        hash.Add(MaxDegradedRuns);
        return hash.ToHashCode();
    }

    public static bool operator ==(GlyphAtlasSoakSummary left, GlyphAtlasSoakSummary right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasSoakSummary left, GlyphAtlasSoakSummary right) => !left.Equals(right);
}
