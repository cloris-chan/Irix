using Irix;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class ResizeDiagnosticRunner
{
    private const string ScaleModePhysicalPixelsV0 = "PhysicalPixelsV0";

    internal static void Run(TextWriter output)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
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
            postFrameCallback: null);

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1200,
            children:
            [
                VirtualNodeFactory.Text("Resize Diagnostic: renderer/layout viewport", 1201),
                VirtualNodeFactory.Rectangle(300, 44, 1202),
                VirtualNodeFactory.Button("ResizeBtn", 1203, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("ResizeBtn")))
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
                ? VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root))
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
            ScaleModePhysicalPixelsV0);

        WriteReport(output, d3d12Renderer.IsDeviceRemoved, d3d12Renderer.DeviceErrorReason, d3d12Renderer.Width, d3d12Renderer.Height, snapshot);
    }

    internal static void WriteReport(
        TextWriter output,
        bool deviceRemoved,
        string? deviceErrorReason,
        int swapchainWidth,
        int swapchainHeight,
        ViewportDiagnosticsSnapshot snapshot)
    {
        output.WriteLine("=== D3D12 Resize Diagnostics ===");
        output.WriteLine($"Device removed: {deviceRemoved}");
        output.WriteLine($"Device error reason: {deviceErrorReason ?? "(none)"}");
        output.WriteLine($"Swapchain size: {swapchainWidth}x{swapchainHeight}");
        foreach (var line in DiagnosticsFormatter.BuildResizeViewportDiagnosticLines(snapshot))
        {
            output.WriteLine(line);
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
