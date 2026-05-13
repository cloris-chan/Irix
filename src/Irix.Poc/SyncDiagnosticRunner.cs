using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

/// <summary>
/// Measures GPU sync wait overhead during text-overlay rendering.
/// Renders N frames with text, reports per-frame and aggregate sync wait stats.
/// Run at different refresh rates to validate sync cost is acceptable.
/// </summary>
internal static class SyncDiagnosticRunner
{
    internal static void Run(TextWriter output, int frameCount = 300, int sampleCount = 1, TextOverlaySyncStrategy syncStrategy = TextOverlaySyncStrategy.D3D12FenceAfterOverlay)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = screen.Scale;
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
    d3d12Renderer.TextOverlaySyncStrategy = syncStrategy;
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

        output.WriteLine("=== D3D12 Sync Diagnostic ===");
        output.WriteLine($"Frames: {frameCount}");
        output.WriteLine($"Samples: {sampleCount}");
        output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
        output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        output.WriteLine($"SyncTextOverlay: {d3d12Renderer.SyncTextOverlay}");
        output.WriteLine($"Text overlay sync strategy: {d3d12Renderer.TextOverlaySyncStrategy}");
        output.WriteLine();

        var sampleSummaries = new List<SyncSampleSummary>(sampleCount);

        for (var sample = 0; sample < sampleCount; sample++)
        {
            output.WriteLine($"--- Sample {sample + 1}/{sampleCount} ---");
            sampleSummaries.Add(RunSample(output, frameCount, translator, compositor, d3d12Backend, d3d12Renderer));
            output.WriteLine();
        }

        if (sampleSummaries.Count > 1)
        {
            output.WriteLine("--- Sample Summary ---");
            output.WriteLine($"Avg sync wait range: min={sampleSummaries.Min(s => s.AvgWaitMs):F3}ms, max={sampleSummaries.Max(s => s.AvgWaitMs):F3}ms");
            output.WriteLine($"P95 sync wait range: min={sampleSummaries.Min(s => s.P95WaitMs):F3}ms, max={sampleSummaries.Max(s => s.P95WaitMs):F3}ms");
            output.WriteLine($"Max sync wait range: min={sampleSummaries.Min(s => s.MaxWaitMs):F3}ms, max={sampleSummaries.Max(s => s.MaxWaitMs):F3}ms");
            output.WriteLine();
        }

        var finalDiag = d3d12Backend.FrameSerialDiagnostics;
        output.WriteLine($"Final: frameSerial={finalDiag.FrameSerial}, presentSerial={finalDiag.PresentSerial}, syncWaits={finalDiag.SyncWaitCount}");
        output.WriteLine("=== Sync diagnostic complete ===");
    }

    private static SyncSampleSummary RunSample(
        TextWriter output,
        int frameCount,
        WindowDrawCommandTranslator translator,
        DrawingBackendCompositor compositor,
        D3D12DrawingBackend d3d12Backend,
        D3D12Renderer d3d12Renderer)
    {
        var syncWaits = new List<double>(frameCount);
        var frameTimes = new List<long>(frameCount);
        var previousRoot = default(VirtualNode);
        var hasPreviousRoot = false;

        for (var i = 0; i < frameCount; i++)
        {
            var scrollY = i * 2;
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("SyncTest", 2,
                        VirtualNodeAttribute.Action(new ActionId(300))),
                    VirtualNodeFactory.Text($"Frame {i} — sync overhead measurement", 3),
                    VirtualNodeFactory.Button("Another", 4,
                        VirtualNodeAttribute.Action(new ActionId(301))),
                ]);

            using var patch = hasPreviousRoot
                ? VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(previousRoot), new VirtualNodeTree(root))
                : VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root));
            using var batch = translator.Translate(patch);
            previousRoot = root;
            hasPreviousRoot = true;

            var diagBefore = d3d12Backend.FrameSerialDiagnostics;
            compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
            var diagAfter = d3d12Backend.FrameSerialDiagnostics;

            var syncWaitDelta = diagAfter.SyncWaitTicks - diagBefore.SyncWaitTicks;
            var syncWaitMs = syncWaitDelta / (double)System.Diagnostics.Stopwatch.Frequency * 1000;
            syncWaits.Add(syncWaitMs);
            frameTimes.Add(compositor.LastFrameTimeUs);

            if (d3d12Renderer.IsDeviceRemoved)
            {
                output.WriteLine($"Device removed at frame {i}: {d3d12Renderer.DeviceErrorReason}");
                break;
            }
        }

        // Report
        output.WriteLine("--- Per-frame sample (first 10, last 5) ---");
        output.WriteLine("Frame  | SyncWait(ms) | FrameTime(us)");
        var sampleEnd = Math.Min(10, syncWaits.Count);
        for (var i = 0; i < sampleEnd; i++)
        {
            output.WriteLine($"{i,5} | {syncWaits[i],12:F3} | {frameTimes[i],13}");
        }

        if (syncWaits.Count > 15)
        {
            output.WriteLine("  ...");
            for (var i = Math.Max(10, syncWaits.Count - 5); i < syncWaits.Count; i++)
            {
                output.WriteLine($"{i,5} | {syncWaits[i],12:F3} | {frameTimes[i],13}");
            }
        }

        output.WriteLine();
        output.WriteLine("--- Aggregate ---");
        output.WriteLine($"Total frames: {syncWaits.Count}");

        if (syncWaits.Count > 0)
        {
            var minWait = syncWaits.Min();
            var maxWait = syncWaits.Max();
            var avgWait = syncWaits.Average();
            var p95Wait = syncWaits.OrderBy(x => x).ElementAt((int)(syncWaits.Count * 0.95));
            var waitsOver2ms = syncWaits.Count(w => w > 2.0);

            output.WriteLine($"Sync wait: min={minWait:F3}ms, max={maxWait:F3}ms, avg={avgWait:F3}ms, p95={p95Wait:F3}ms");
            output.WriteLine($"Waits >2ms: {waitsOver2ms}/{syncWaits.Count} ({100.0 * waitsOver2ms / syncWaits.Count:F1}%)");

            var minFrame = frameTimes.Min();
            var maxFrame = frameTimes.Max();
            var avgFrame = frameTimes.Average();
            output.WriteLine($"Frame time: min={minFrame}us, max={maxFrame}us, avg={avgFrame:F0}us");

            var verdict = avgWait < 2.0 ? "ACCEPTABLE" : "EXCEEDS TARGET (2ms)";
            output.WriteLine($"Verdict: {verdict} (target: <2ms/frame avg sync wait)");
        }

        output.WriteLine();

        return syncWaits.Count > 0
            ? new SyncSampleSummary(syncWaits.Average(), syncWaits.OrderBy(x => x).ElementAt(Math.Min(syncWaits.Count - 1, (int)(syncWaits.Count * 0.95))), syncWaits.Max())
            : default;
    }

    private readonly record struct SyncSampleSummary(double AvgWaitMs, double P95WaitMs, double MaxWaitMs);

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
