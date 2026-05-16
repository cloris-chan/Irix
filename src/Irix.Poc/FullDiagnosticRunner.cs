using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class FullDiagnosticRunner
{
    internal static void Run(TextWriter output)
    {
        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);

        // Build a test frame: one rectangle + one text + one button
        var resources = FrameDrawingResources.Rent();
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        var bgText = resources.AddText("Diagnostic Mode: Hello World 🎯");
        var btnText = resources.AddText("Click Me");
        resources.Seal();

        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(0, 0, 960, 540),
                Color: DrawColor.Opaque(32, 32, 32)),
            new(DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 200, 40),
                Color: DrawColor.Opaque(52, 120, 246)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32),
                Resource: textStyle,
                Text: bgText,
                Color: DrawColor.Opaque(255, 255, 255)),
            new(DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 200, 40),
                Resource: textStyle,
                Text: btnText,
                Color: DrawColor.Opaque(255, 255, 255))
        };

        // Render 3 frames via compositor to warm caches and collect stats
        for (var i = 0; i < 3; i++)
        {
            var batch = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commands.Length),
                [],
                resources,
                i == 0 ? [] : [(0, commands.Length)]); // frame 0: full; frames 1-2: dirty ranges

            compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
            batch.Dispose();
        }

        // Dump diagnostics
        var diag = d3d12Renderer.GetTextDiagnostics();
        if (diag.HasValue)
        {
            var d = diag.Value;
            output.WriteLine("=== D3D12 Text Renderer Diagnostics ===");
            output.WriteLine($"Format cache: {d.CachedFormats} entries, {d.FormatHits} hits, {d.FormatMisses} misses, {d.FormatEvictions} evictions");
            output.WriteLine($"Layout cache: {d.CachedLayouts} entries, {d.LayoutHits} hits, {d.LayoutMisses} misses, {d.LayoutEvictions} evictions");
            output.WriteLine($"Format hit rate: {(d.FormatHits + d.FormatMisses > 0 ? 100.0 * d.FormatHits / (d.FormatHits + d.FormatMisses) : 0):F1}%");
            output.WriteLine($"Layout hit rate: {(d.LayoutHits + d.LayoutMisses > 0 ? 100.0 * d.LayoutHits / (d.LayoutHits + d.LayoutMisses) : 0):F1}%");
        }

        var initialBackendSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        foreach (var line in DiagnosticsFormatter.BuildBackendDeviceDiagnosticLines(initialBackendSnapshot))
        {
            output.WriteLine(line);
        }
        output.WriteLine($"Swapchain size: {d3d12Renderer.Width}x{d3d12Renderer.Height}");
        foreach (var line in DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePreset.DefaultName, RenderStylePreset.Default))
        {
            output.WriteLine(line);
        }
        output.WriteLine($"=== Compositor Diagnostics ===");
        var renderCount = compositor.RenderCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var fullApplyCount = compositor.FullApplyCount;
        var emptyFrameCount = compositor.EmptyFrameCount;

        var frameSerial = d3d12Backend.FrameSerialDiagnostics;
        output.WriteLine($"Frame serial: rects={frameSerial.FrameSerial}, presents={frameSerial.PresentSerial}");
        output.WriteLine($"Text overlay sync strategy: {frameSerial.SyncStrategy}");
        output.WriteLine($"Sync wait: count={frameSerial.SyncWaitCount}, total={frameSerial.SyncWaitMs:F2}ms, avg={(frameSerial.SyncWaitCount > 0 ? frameSerial.SyncWaitMs / frameSerial.SyncWaitCount : 0):F2}ms");
        output.WriteLine($"Back buffer index: {frameSerial.BackBufferIndex}");
        output.WriteLine($"Compositor frame time: last={compositor.LastFrameTimeUs}us, avg={compositor.AverageFrameTimeUs}us, max={compositor.MaxFrameTimeUs}us");
        var dirty = compositor.LastDirtyCommandRanges;
        var backendDirty = d3d12Backend.LastDirtyCommandRanges;
        var backendClippedCommandCount = d3d12Backend.ClippedCommandCount;
        var arena = new VirtualTextArena();

        // Layout-driven frame: render through VirtualNode \u2192 Layout \u2192 Pipeline \u2192 Compositor
        // to verify the clip chain produces clipped commands
        var layoutPipeline = new RenderPipeline();
        var layoutRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(arena, "Layout Pipeline Test", new NodeKey(2)),
            VirtualNodeBuilder.Button(arena, "LayoutBtn", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(100))));
        var layoutViewport = new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height);
        using var layoutBatch = layoutPipeline.Build(layoutRoot, layoutViewport, arena.GetOrCreateSnapshot());

        // Render the layout-driven frame through the compositor
        compositor.RenderAsync(layoutBatch).AsTask().GetAwaiter().GetResult();

        var layoutClipCount = 0;
        for (var j = 0; j < layoutBatch.Commands.Count; j++)
        {
            var cmd = layoutBatch.Commands.Memory.Span[j];
            if (cmd.ClipBounds.Width > 0 && cmd.ClipBounds.Height > 0)
            {
                layoutClipCount++;
            }
        }
        var renderingSnapshot = new RenderingPipelineDiagnosticSnapshot(
            renderCount,
            partialApplyCount,
            fullApplyCount,
            emptyFrameCount,
            dirty,
            backendDirty,
            backendClippedCommandCount,
            layoutBatch.Commands.Count,
            layoutClipCount,
            layoutPipeline.LayoutRebuildCount,
            layoutPipeline.LastLayoutRebuildReason,
            ResolveInvalidationKind(layoutPipeline.LastDirtyClassifications, layoutPipeline.LastLayoutRebuildReason),
            layoutPipeline.LastDirtyClassifications,
            layoutBatch.HitTargets,
            layoutPipeline.LastLayoutResult?.ScrollDiagnostics ?? []);
        foreach (var line in DiagnosticsFormatter.BuildRenderingPipelineCompositorDiagnosticLines(renderingSnapshot))
        {
            output.WriteLine(line);
        }
        output.WriteLine($"=== Layout Pipeline Diagnostics ===");
        foreach (var line in DiagnosticsFormatter.BuildRenderingPipelineLayoutDiagnosticLines(renderingSnapshot))
        {
            output.WriteLine(line);
        }
        foreach (var line in StyleOnlyPatchPlanSmokeDiagnostics.BuildDiagnosticLines())
        {
            output.WriteLine(line);
        }
        output.WriteLine($"=== Pipeline Scissor Smoke ===");
        d3d12Backend.SetClipMode(DrawingBackendClipMode.Scissor);
        BackendClipTextSmokeDiagnostics.RunPipelineScissorSmokeDiagnostic(compositor, d3d12Backend, d3d12Renderer);
        var pipelineScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        output.WriteLine(DiagnosticsFormatter.BuildPipelineScissorSmokeDiagnosticLine(pipelineScissorSnapshot));
        output.WriteLine($"=== Pipeline Text Clip Smoke ===");
        BackendClipTextSmokeDiagnostics.RunPipelineTextClipSmokeDiagnostic(compositor, d3d12Backend, d3d12Renderer);
        var pipelineTextClipSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        output.WriteLine(DiagnosticsFormatter.BuildPipelineTextClipSmokeDiagnosticLine(pipelineTextClipSnapshot));

        output.WriteLine($"=== Clip Scissor Diagnostics ===");
        var smokeClip = new DrawRect(32, 32, 80, 40);
        d3d12Backend.SetClipMode(DrawingBackendClipMode.Scissor);
        BackendClipTextSmokeDiagnostics.RunClipScissorSmokeDiagnostic(d3d12Backend);
        var clipScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        output.WriteLine(DiagnosticsFormatter.BuildBackendClipModeDiagnosticLine(clipScissorSnapshot));
        output.WriteLine(DiagnosticsFormatter.BuildClipScissorSmokeDiagnosticLine(smokeClip, clipScissorSnapshot));
        output.WriteLine($"=== Empty Scissor Diagnostics ===");
        BackendClipTextSmokeDiagnostics.RunEmptyScissorSmokeDiagnostic(d3d12Backend);
        var emptyScissorSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        output.WriteLine(DiagnosticsFormatter.BuildEmptyScissorSmokeDiagnosticLine(emptyScissorSnapshot));
        output.WriteLine($"=== Text Clip Diagnostics ===");
        BackendClipTextSmokeDiagnostics.RunTextClipSmokeDiagnostic(d3d12Backend);
        var textClipSnapshot = BackendClipTextDiagnosticSnapshot.FromBackend(d3d12Backend, d3d12Renderer);
        output.WriteLine(DiagnosticsFormatter.BuildTextClipSmokeDiagnosticLine(textClipSnapshot));
        output.WriteLine("=== Diagnostic mode complete ===");

        FrameDrawingResources.Return(resources);
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

    private static InvalidationKind ResolveInvalidationKind(
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        LayoutRebuildReason rebuildReason)
    {
        if (dirtyClassifications.Count == 0)
        {
            return rebuildReason switch
            {
                LayoutRebuildReason.TreeStructure => InvalidationKind.TreeStructure,
                LayoutRebuildReason.ViewportChanged => InvalidationKind.ViewportChanged,
                LayoutRebuildReason.LayoutAffecting => InvalidationKind.Layout,
                LayoutRebuildReason.TextSizeAffecting => InvalidationKind.TextMeasure,
                LayoutRebuildReason.StyleOnly => InvalidationKind.VisualOnly,
                _ => InvalidationKind.None,
            };
        }

        var kind = InvalidationKind.None;
        foreach (var classification in dirtyClassifications)
        {
            kind = MaxInvalidationKind(kind, classification.InvalidationKind);
        }

        return kind;
    }

    private static InvalidationKind MaxInvalidationKind(InvalidationKind left, InvalidationKind right)
    {
        return InvalidationPriority(left) >= InvalidationPriority(right) ? left : right;
    }

    private static int InvalidationPriority(InvalidationKind kind)
    {
        return kind switch
        {
            InvalidationKind.ViewportChanged => 6,
            InvalidationKind.TreeStructure => 5,
            InvalidationKind.Layout => 4,
            InvalidationKind.TextMeasure => 3,
            InvalidationKind.VisualOnly => 2,
            InvalidationKind.CompositeOnly => 1,
            _ => 0,
        };
    }
}
