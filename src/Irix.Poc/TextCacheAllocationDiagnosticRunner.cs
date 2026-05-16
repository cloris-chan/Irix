using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class TextCacheAllocationDiagnosticRunner
{
    internal static void Run(TextWriter output, int frameCount = 180)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = screen.Scale;
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        compositor.SetViewport(window.Region.PhysicalBounds, displayScale);

        var translator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: null,
            displayScale: displayScale);

        output.WriteLine("=== D3D12 Text Cache / Allocation Diagnostics ===");
        output.WriteLine($"Frames per scenario: {frameCount}");
        output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
        output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        output.WriteLine();

        var arena = new VirtualTextArena();
        RunScenario(output, "static", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (i, _) =>
        {
            var root = BuildRoot(arena, "Static cache baseline", scrollY: 0);
            return new VirtualNodeTree(root, arena.GetOrCreateSnapshot());
        });
        RunScenario(output, "scroll", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (i, _) =>
        {
            var root = BuildRoot(arena, "Scrolling cache baseline", scrollY: i * 2);
            return new VirtualNodeTree(root, arena.GetOrCreateSnapshot());
        });
        RunScenario(output, "scale-change", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (i, scale) =>
        {
            var root = BuildRoot(arena, $"Scale cache baseline {scale.ScaleX:0.##}x", scrollY: 0);
            return new VirtualNodeTree(root, arena.GetOrCreateSnapshot());
        }, scaleChangeAtHalf: true);

        output.WriteLine("=== Text cache / allocation diagnostic complete ===");
    }

    private static void RunScenario(
        TextWriter output,
        string name,
        int frameCount,
        D3D12Renderer renderer,
        D3D12DrawingBackend backend,
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale,
        Func<int, DisplayScale, VirtualNodeTree> treeFactory,
        bool scaleChangeAtHalf = false)
    {
        renderer.ResetTextDiagnostics();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var poolBefore = FrameDrawingResources.GetPoolDiagnostics();
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var previousTree = default(VirtualNodeTree);
        var hasPreviousTree = false;
        var activeScale = displayScale;

        for (var i = 0; i < frameCount; i++)
        {
            if (scaleChangeAtHalf && i == frameCount / 2)
            {
                activeScale = displayScale.ScaleX >= 2f || displayScale.ScaleY >= 2f
                    ? DisplayScale.Identity
                    : new DisplayScale(2f, 2f);
                translator.SetDisplayScale(activeScale);
                compositor.SetViewport(new PixelRectangle(0, 0, renderer.Width, renderer.Height), activeScale);
            }

            var nextTree = treeFactory(i, activeScale);
            using var patch = hasPreviousTree
                ? VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree)
                : VirtualNodeDiffer.CreatePatchBatch(default, nextTree);
            using var batch = translator.Translate(patch);
            previousTree = nextTree;
            hasPreviousTree = true;

            compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
            if (renderer.IsDeviceRemoved)
            {
                output.WriteLine($"Scenario {name}: device removed at frame {i}: {renderer.DeviceErrorReason}");
                break;
            }
        }

        translator.SetDisplayScale(displayScale);
        compositor.SetViewport(new PixelRectangle(0, 0, renderer.Width, renderer.Height), displayScale);

        var allocatedAfter = GC.GetTotalAllocatedBytes(true);
        var poolDelta = FrameDrawingResources.GetPoolDiagnostics().Delta(poolBefore);
        var textDiagnostics = renderer.GetTextDiagnostics();

        output.WriteLine($"--- Scenario: {name} ---");
        if (textDiagnostics.HasValue)
        {
            var d = textDiagnostics.Value;
            var formatTotal = d.FormatHits + d.FormatMisses;
            var layoutTotal = d.LayoutHits + d.LayoutMisses;
            output.WriteLine($"Format cache: hits={d.FormatHits}, misses={d.FormatMisses}, hitRate={(formatTotal > 0 ? 100.0 * d.FormatHits / formatTotal : 0):F1}%, cached={d.CachedFormats}, evictions={d.FormatEvictions}");
            output.WriteLine($"Layout cache: hits={d.LayoutHits}, misses={d.LayoutMisses}, hitRate={(layoutTotal > 0 ? 100.0 * d.LayoutHits / layoutTotal : 0):F1}%, cached={d.CachedLayouts}, evictions={d.LayoutEvictions}");
        }

        var allocatedBytes = allocatedAfter - allocatedBefore;
        output.WriteLine($"Allocation: total={allocatedBytes} bytes, perFrame={(frameCount > 0 ? allocatedBytes / frameCount : 0)} bytes");
        output.WriteLine($"FrameDrawingResources: rents={poolDelta.RentCount}, created={poolDelta.CreatedCount}, reused={poolDelta.ReusedCount}, returns={poolDelta.ReturnCallCount}, returnedToPool={poolDelta.ReturnedToPoolCount}, retainedSkips={poolDelta.RetainedReturnSkipCount}, duplicateSkips={poolDelta.DuplicateReturnSkipCount}, staleSkips={poolDelta.StaleReturnSkipCount}, overflowDisposals={poolDelta.DisposedOverflowCount}, poolCount={poolDelta.PoolCount}");
        output.WriteLine();
    }

    private static VirtualNode BuildRoot(VirtualTextArena arena, string text, int scrollY)
    {
        return new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [new VirtualNodeProperty(VirtualPropertyKey.ScrollY, PropertyValue.FromNumber(scrollY))],
            children:
            [
                VirtualNodeBuilder.Button(arena, "Cache A", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Text(arena, text, new NodeKey(4)),
                VirtualNodeBuilder.Button(arena, "Cache B", new NodeKey(5), VirtualNodeProperty.Action(new ActionId(201))),
            ]);
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
