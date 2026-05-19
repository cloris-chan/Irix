using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class GlyphAtlasMixedFallbackDiagnosticRunner
{
    private static readonly DrawRect ClippedAsciiBounds = new(24, 136, 112, 26);
    private static readonly DrawRect ClippedNonAsciiBounds = new(24, 176, 112, 26);

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
            var commands = BuildMixedFallbackCommands(resources, frameIndex: 0);
            resources.Seal();
            var expected = AnalyzeMixedFallbackScene(commands, resources);
            var subsetParity = AnalyzeOverlaySubsetInputs(commands, resources, displayScale, viewport);

            output.WriteLine("=== Glyph Atlas Mixed Fallback Diagnostic ===");
            output.WriteLine($"Frames: {frameCount}");
            output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
            output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
            output.WriteLine($"Text composition mode: {textCompositionMode}");
            output.WriteLine(
                $"Expected GlyphAtlas per frame: textRuns={expected.TextRuns}, atlasRuns={expected.AtlasCandidateRuns}, "
                + $"degradedRuns={expected.DegradedCandidateRuns}, NonAscii={expected.NonAsciiFallbackRuns}, "
                + $"clippedAtlasRuns={expected.ClippedAtlasCandidateRuns}, clippedDegradedRuns={expected.ClippedDegradedCandidateRuns}");
            output.WriteLine($"Ordering: {BuildOrderingLine(expected, textCompositionMode)}");
            if (textCompositionMode == TextCompositionMode.GlyphAtlas)
            {
                output.WriteLine("Unsupported text degradation: overlay=False resolver=True style=True clip=True scale=True");
            }
            else
            {
                output.WriteLine("Whole-frame overlay input: resolver=True style=True clip=True scale=True");
                output.WriteLine($"Overlay subset parity: {subsetParity.FormatSummary()}");
            }
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
                var commands = BuildMixedFallbackCommands(resources, i);
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
        output.WriteLine($"Backend text clip: textClipSkipped={d3d12Backend.TextClipSkippedCount}, lastEffectiveTextClip={FormatEffectiveScissor(d3d12Backend.LastEffectiveTextClip)}");
        var atlasDiag = d3d12Renderer.GetGlyphAtlasTextDiagnostics();
        if (atlasDiag.HasValue)
        {
            output.WriteLine($"Glyph atlas: {atlasDiag.Value.FormatSummary()}");
        }
        output.WriteLine("=== Glyph atlas mixed fallback diagnostic complete ===");
    }

    internal static DrawCommand[] BuildMixedFallbackCommands(FrameDrawingResources resources, int frameIndex)
    {
        var atlasStyle = resources.AddTextStyle(CreateStyle(18, TextFontWeight.Normal));
        var fallbackStyle = resources.AddTextStyle(CreateStyle(20, TextFontWeight.SemiBold));
        var clippedAtlasStyle = resources.AddTextStyle(CreateStyle(16, TextFontWeight.Normal));
        var clippedFallbackStyle = resources.AddTextStyle(CreateStyle(22, TextFontWeight.Normal));

        var ascii = resources.AddText($"ASCII atlas {frameIndex:D3}");
        var nonAscii = resources.AddText($"Fallback 測試 {frameIndex:D3}");
        var clippedAscii = resources.AddText($"Clipped ASCII {frameIndex:D3}");
        var clippedNonAscii = resources.AddText($"裁剪 fallback {frameIndex:D3}");

        return
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 960, 540), Color: DrawColor.Opaque(18, 24, 32)),
            TextRun(24, 56, 320, 34, DrawColor.Opaque(238, 244, 255), ascii, atlasStyle),
            TextRun(24, 96, 320, 34, DrawColor.Opaque(255, 224, 128), nonAscii, fallbackStyle),
            TextRun(24, 136, 320, 34, DrawColor.Opaque(128, 232, 255), clippedAscii, clippedAtlasStyle, ClippedAsciiBounds),
            TextRun(24, 176, 320, 34, DrawColor.Opaque(255, 160, 220), clippedNonAscii, clippedFallbackStyle, ClippedNonAsciiBounds)
        ];
    }

    internal static GlyphAtlasMixedFallbackSceneSummary AnalyzeMixedFallbackScene(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources)
    {
        var textRuns = 0;
        var atlasCandidateRuns = 0;
        var degradedCandidateRuns = 0;
        var nonAsciiFallbackRuns = 0;
        var clippedAtlasCandidateRuns = 0;
        var clippedDegradedCandidateRuns = 0;
        var sawDegraded = false;
        var hasDegradedBeforeLaterAtlas = false;

        foreach (var command in commands)
        {
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            textRuns++;
            var style = resources.ResolveTextStyle(command.Resource).Normalize();
            var reason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(resources.Resolve(command.Text), style);
            var hasClip = command.ClipBounds.Width > 0 && command.ClipBounds.Height > 0;
            if (reason == D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None)
            {
                atlasCandidateRuns++;
                if (hasClip)
                {
                    clippedAtlasCandidateRuns++;
                }

                hasDegradedBeforeLaterAtlas |= sawDegraded;
                continue;
            }

            degradedCandidateRuns++;
            if (reason.HasFlag(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii))
            {
                nonAsciiFallbackRuns++;
            }
            if (hasClip)
            {
                clippedDegradedCandidateRuns++;
            }

            sawDegraded = true;
        }

        return new GlyphAtlasMixedFallbackSceneSummary(
            textRuns,
            atlasCandidateRuns,
            degradedCandidateRuns,
            nonAsciiFallbackRuns,
            clippedAtlasCandidateRuns,
            clippedDegradedCandidateRuns,
            hasDegradedBeforeLaterAtlas);
    }

    internal static string BuildOrderingLine(
        GlyphAtlasMixedFallbackSceneSummary summary,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas)
    {
        if (textCompositionMode == TextCompositionMode.Overlay)
        {
            return "commands=atlas,fallback,atlas,fallback; actualPassOrder=rects,wholeFrameOverlayRuns,present; zOrderLimit=FalseForOverlayRollback";
        }

        return summary.HasDegradedBeforeLaterAtlas
            ? "commands=atlas,degraded,atlas,degraded; actualPassOrder=rects,atlasAcceptedRuns,present; zOrderLimit=FalseForDegradedText"
            : "commands=atlasThenDegraded; actualPassOrder=rects,atlasAcceptedRuns,present; zOrderLimit=False";
    }

    internal static GlyphAtlasOverlaySubsetParity AnalyzeOverlaySubsetInputs(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        DisplayScale scale,
        DrawRect viewport)
    {
        scale = scale.Normalize();
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var wholeFrameTextRuns = new FrameRenderList<D3D12TextRenderer.TextData>();
        using var overlayFallbackRuns = new FrameRenderList<D3D12TextRenderer.TextData>();

        D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            viewport,
            commands,
            resources,
            scale,
            rects,
            wholeFrameTextRuns);

        AppendUnsupportedOverlayFallbackRuns(wholeFrameTextRuns.Span, resources, overlayFallbackRuns);
        var fallbackRuns = overlayFallbackRuns.Span;
        var wholeFrameRuns = wholeFrameTextRuns.Span;
        var matchesWholeFrame = IsSubsequence(wholeFrameRuns, fallbackRuns);
        var resolverPreserved = true;
        var stylePreserved = true;
        var clipPreserved = true;
        var scalePreserved = true;
        var colorPreserved = true;

        foreach (var fallbackRun in fallbackRuns)
        {
            if (!TryFindSourceCommand(commands, fallbackRun, out var sourceCommand))
            {
                resolverPreserved = false;
                stylePreserved = false;
                clipPreserved = false;
                scalePreserved = false;
                colorPreserved = false;
                continue;
            }

            var scaledCommand = sourceCommand.Scale(scale);
            var textClipPlan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, viewport, scaledCommand.ClipBounds);
            var resolvedStyle = D3D12DrawingBackend.ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(sourceCommand.Resource), scale);
            resolverPreserved &= ReferenceEquals(fallbackRun.Resolver, resources);
            stylePreserved &= fallbackRun.Style == sourceCommand.Resource && fallbackRun.ResolvedStyle == resolvedStyle;
            clipPreserved &= fallbackRun.ClipEnabled == textClipPlan.ClipEnabled && fallbackRun.EffectiveClip == textClipPlan.EffectiveClip;
            scalePreserved &= fallbackRun.X == scaledCommand.Rect.X
                && fallbackRun.Y == scaledCommand.Rect.Y
                && fallbackRun.Width == scaledCommand.Rect.Width
                && fallbackRun.Height == scaledCommand.Rect.Height;
            colorPreserved &= fallbackRun.R == scaledCommand.Color.R / 255f
                && fallbackRun.G == scaledCommand.Color.G / 255f
                && fallbackRun.B == scaledCommand.Color.B / 255f
                && fallbackRun.A == scaledCommand.Color.A / 255f;
        }

        return new GlyphAtlasOverlaySubsetParity(
            wholeFrameRuns.Length,
            fallbackRuns.Length,
            matchesWholeFrame,
            resolverPreserved,
            stylePreserved,
            clipPreserved,
            scalePreserved,
            colorPreserved);
    }

    private static void AppendUnsupportedOverlayFallbackRuns(
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources,
        FrameRenderList<D3D12TextRenderer.TextData> overlayFallbackRuns)
    {
        foreach (var textRun in textRuns)
        {
            var runResolver = textRun.Resolver ?? resources;
            var text = runResolver.Resolve(textRun.Text);
            var style = (textRun.ResolvedStyle != default ? textRun.ResolvedStyle : runResolver.ResolveTextStyle(textRun.Style)).Normalize();
            if (GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(text, style) == D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None)
            {
                continue;
            }

            overlayFallbackRuns.Add(textRun);
        }
    }

    private static bool IsSubsequence(
        ReadOnlySpan<D3D12TextRenderer.TextData> source,
        ReadOnlySpan<D3D12TextRenderer.TextData> subset)
    {
        var sourceIndex = 0;
        foreach (var item in subset)
        {
            var found = false;
            while (sourceIndex < source.Length)
            {
                if (source[sourceIndex++] == item)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindSourceCommand(
        ReadOnlySpan<DrawCommand> commands,
        D3D12TextRenderer.TextData fallbackRun,
        out DrawCommand sourceCommand)
    {
        foreach (var command in commands)
        {
            if (command.Kind == DrawCommandKind.DrawTextRun && command.Text == fallbackRun.Text && command.Resource == fallbackRun.Style)
            {
                sourceCommand = command;
                return true;
            }
        }

        sourceCommand = default;
        return false;
    }

    private static TextStyle CreateStyle(float fontSize, TextFontWeight weight)
    {
        return new TextStyle(
            TextStyle.Default.FontFamily,
            fontSize,
            weight,
            TextStyle.Default.FontStyle,
            TextStyle.Default.FontStretch,
            TextHorizontalAlignment.Leading,
            TextVerticalAlignment.Center,
            TextWrapping.NoWrap);
    }

    private static DrawCommand TextRun(
        float x,
        float y,
        float width,
        float height,
        DrawColor color,
        TextSlice text,
        ResourceHandle style,
        DrawRect clipBounds = default)
    {
        return new DrawCommand(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(x, y, width, height),
            Color: color,
            Resource: style,
            Text: text,
            ClipBounds: clipBounds);
    }

    private static string FormatEffectiveScissor(EffectiveScissor scissor)
    {
        return scissor.IsEmpty ? "empty" : $"({scissor.Bounds.X:0.##},{scissor.Bounds.Y:0.##},{scissor.Bounds.Width:0.##},{scissor.Bounds.Height:0.##})";
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

internal readonly struct GlyphAtlasMixedFallbackSceneSummary(
    int TextRuns,
    int AtlasCandidateRuns,
    int DegradedCandidateRuns,
    int NonAsciiFallbackRuns,
    int ClippedAtlasCandidateRuns,
    int ClippedDegradedCandidateRuns,
    bool HasDegradedBeforeLaterAtlas) : IEquatable<GlyphAtlasMixedFallbackSceneSummary>
{
    public int TextRuns { get; } = TextRuns;
    public int AtlasCandidateRuns { get; } = AtlasCandidateRuns;
    public int DegradedCandidateRuns { get; } = DegradedCandidateRuns;
    public int NonAsciiFallbackRuns { get; } = NonAsciiFallbackRuns;
    public int ClippedAtlasCandidateRuns { get; } = ClippedAtlasCandidateRuns;
    public int ClippedDegradedCandidateRuns { get; } = ClippedDegradedCandidateRuns;
    public bool HasDegradedBeforeLaterAtlas { get; } = HasDegradedBeforeLaterAtlas;

    public bool Equals(GlyphAtlasMixedFallbackSceneSummary other)
    {
        return TextRuns == other.TextRuns
            && AtlasCandidateRuns == other.AtlasCandidateRuns
            && DegradedCandidateRuns == other.DegradedCandidateRuns
            && NonAsciiFallbackRuns == other.NonAsciiFallbackRuns
            && ClippedAtlasCandidateRuns == other.ClippedAtlasCandidateRuns
            && ClippedDegradedCandidateRuns == other.ClippedDegradedCandidateRuns
            && HasDegradedBeforeLaterAtlas == other.HasDegradedBeforeLaterAtlas;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasMixedFallbackSceneSummary other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            TextRuns,
            AtlasCandidateRuns,
            DegradedCandidateRuns,
            NonAsciiFallbackRuns,
            ClippedAtlasCandidateRuns,
            ClippedDegradedCandidateRuns,
            HasDegradedBeforeLaterAtlas);
    }

    public static bool operator ==(GlyphAtlasMixedFallbackSceneSummary left, GlyphAtlasMixedFallbackSceneSummary right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasMixedFallbackSceneSummary left, GlyphAtlasMixedFallbackSceneSummary right) => !left.Equals(right);
}

internal readonly struct GlyphAtlasOverlaySubsetParity(
    int WholeFrameOverlayRuns,
    int FallbackSubsetRuns,
    bool MatchesWholeFrame,
    bool ResolverPreserved,
    bool StylePreserved,
    bool ClipPreserved,
    bool ScalePreserved,
    bool ColorPreserved) : IEquatable<GlyphAtlasOverlaySubsetParity>
{
    public int WholeFrameOverlayRuns { get; } = WholeFrameOverlayRuns;
    public int FallbackSubsetRuns { get; } = FallbackSubsetRuns;
    public bool MatchesWholeFrame { get; } = MatchesWholeFrame;
    public bool ResolverPreserved { get; } = ResolverPreserved;
    public bool StylePreserved { get; } = StylePreserved;
    public bool ClipPreserved { get; } = ClipPreserved;
    public bool ScalePreserved { get; } = ScalePreserved;
    public bool ColorPreserved { get; } = ColorPreserved;
    public bool IsAcceptable => MatchesWholeFrame && ResolverPreserved && StylePreserved && ClipPreserved && ScalePreserved && ColorPreserved;

    public string FormatSummary()
    {
        return $"fallbackRuns={FallbackSubsetRuns}, wholeFrameOverlayRuns={WholeFrameOverlayRuns}, matchesWholeFrame={MatchesWholeFrame}, resolver={ResolverPreserved}, style={StylePreserved}, clip={ClipPreserved}, scale={ScalePreserved}, color={ColorPreserved}";
    }

    public bool Equals(GlyphAtlasOverlaySubsetParity other)
    {
        return WholeFrameOverlayRuns == other.WholeFrameOverlayRuns
            && FallbackSubsetRuns == other.FallbackSubsetRuns
            && MatchesWholeFrame == other.MatchesWholeFrame
            && ResolverPreserved == other.ResolverPreserved
            && StylePreserved == other.StylePreserved
            && ClipPreserved == other.ClipPreserved
            && ScalePreserved == other.ScalePreserved
            && ColorPreserved == other.ColorPreserved;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasOverlaySubsetParity other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            WholeFrameOverlayRuns,
            FallbackSubsetRuns,
            MatchesWholeFrame,
            ResolverPreserved,
            StylePreserved,
            ClipPreserved,
            ScalePreserved,
            ColorPreserved);
    }

    public static bool operator ==(GlyphAtlasOverlaySubsetParity left, GlyphAtlasOverlaySubsetParity right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasOverlaySubsetParity left, GlyphAtlasOverlaySubsetParity right) => !left.Equals(right);
}
