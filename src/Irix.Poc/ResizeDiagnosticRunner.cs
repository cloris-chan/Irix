using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class ResizeDiagnosticRunner
{
    private const string ScaleModePhysicalPixelsV0 = "PhysicalPixelsV0";

    internal static void Run(
        TextWriter output,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas,
        DisplayScale diagnosticScale = default)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale.Normalize();
        if (diagnosticScale == default)
        {
            displayScale = screen.Scale.Normalize();
        }
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        d3d12Renderer.TextCompositionMode = textCompositionMode;
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        compositor.SetViewport(window.Region.PhysicalBounds, displayScale);
        var lastAppliedPendingResize = window.Region.PhysicalBounds;
        var translator = new WindowDrawCommandTranslator(
            window,
            () =>
            {
                if (d3d12Renderer.ApplyPendingResize())
                {
                    var bounds = window.Region.PhysicalBounds;
                    lastAppliedPendingResize = new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
                }
            },
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: null,
            displayScale: displayScale);

        var arena = new VirtualTextArena();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1200,
            children:
            [
                VirtualNodeBuilder.Text(arena, "Resize Diagnostic: renderer/layout viewport", new NodeKey(1201)),
                VirtualNodeFactory.Rectangle(new NodeKey(1202), VirtualNodeProperty.Width(300), VirtualNodeProperty.Height(44)),
                VirtualNodeBuilder.Button(arena, "ResizeBtn", new NodeKey(1203), VirtualNodeProperty.Action(new ActionId(400)))
            ]);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (var i = 0; i < 80; i++)
        {
            var width = 720 + i % 17 * 19;
            var height = 420 + i % 11 * 17;
            var oldBounds = window.Region.PhysicalBounds;
            window.Region = new ScreenRegion(window.Region.ScreenId, new PixelRectangle(oldBounds.X, oldBounds.Y, width, height));
            d3d12Renderer.Resize(width, height);

            using var patch = i == 0
                ? VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, arena.GetOrCreateSnapshot()))
                : PatchBatch.CreateRenderRequest();
            using var frame = translator.Translate(patch);
            compositor.RenderAsync(frame).AsTask().GetAwaiter().GetResult();

            if (d3d12Renderer.IsDeviceRemoved)
            {
                break;
            }

            if (i % 8 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        var windowBounds = window.Region.PhysicalBounds;
        var rendererBounds = new PixelRectangle(windowBounds.X, windowBounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
        var logicalViewport = displayScale.IsIdentity
            ? rendererBounds
            : new PixelRectangle(0, 0, (int)(rendererBounds.Width / displayScale.ScaleX), (int)(rendererBounds.Height / displayScale.ScaleY));
        var snapshot = new ViewportDiagnosticsSnapshot(
            windowBounds,
            rendererBounds,
            translator.LastViewport,
            translator.LastLayoutViewport,
            lastAppliedPendingResize,
            compositor.RenderCount,
            translator.LayoutRebuildCount,
            translator.LastLayoutRebuildReason.ToString(),
            screen.DpiScale,
            "ProcessDefault",
            ScaleModePhysicalPixelsV0,
            displayScale,
            logicalViewport);

        WriteReport(
            output,
            d3d12Renderer.IsDeviceRemoved,
            d3d12Renderer.DeviceErrorReason,
            d3d12Renderer.Width,
            d3d12Renderer.Height,
            snapshot,
            d3d12Renderer.TextCompositionMode,
            d3d12Renderer.GetGlyphAtlasTextDiagnostics());
    }

    internal static void WriteReport(
        TextWriter output,
        bool deviceRemoved,
        string? deviceErrorReason,
        int swapchainWidth,
        int swapchainHeight,
        ViewportDiagnosticsSnapshot snapshot,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas,
        D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics? glyphAtlasDiagnostics = null)
    {
        output.WriteLine("=== D3D12 Resize Diagnostics ===");
        output.WriteLine($"Device removed: {deviceRemoved}");
        output.WriteLine($"Device error reason: {deviceErrorReason ?? "(none)"}");
        output.WriteLine($"Swapchain size: {swapchainWidth}x{swapchainHeight}");
        output.WriteLine($"Text composition mode: {textCompositionMode}");
        foreach (var line in DiagnosticsFormatter.BuildResizeViewportDiagnosticLines(snapshot))
        {
            output.WriteLine(line);
        }
        if (glyphAtlasDiagnostics.HasValue)
        {
            output.WriteLine($"Glyph atlas: {glyphAtlasDiagnostics.Value.FormatSummary()}");
        }
        output.WriteLine("=== Resize diagnostic mode complete ===");
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
