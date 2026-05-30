#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionMarkerRuntimeDiagnosticRunner
{
    private static readonly TimeSpan RuntimeDispatchTimeout = TimeSpan.FromSeconds(5);

    internal static async Task RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        var diagnostics = await RunCoreAsync(cancellationToken);
        output.WriteLine("=== Composition Marker Runtime Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== composition marker runtime diagnostic complete ===");
    }

    internal static async Task<CompositionMarkerRuntimeDiagnostics> RunCoreAsync(CancellationToken cancellationToken = default)
    {
        var window = new DiagnosticWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var translator = new WindowDrawCommandTranslator(window);
        var backend = new MarkerDiagnosticCompositionBackend();
        using var drawingCompositor = new DrawingBackendCompositor(backend);
        await using var compositorLoop = new CompositorLoop(translator, drawingCompositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
        await runtime.StartAsync(cancellationToken);
        await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);

        var declaration = new CompositionAnimationDeclaration(
            new NodeKey(6),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(1),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(1),
                CounterCompositionMarkerRuntimeEventIds.Increment,
                CompositionAnimationMarkerTrigger.AtProgress(0.5f))]);
        drawingCompositor.SetCompositionAnimationDeclaration(declaration, translator.LastRetainedInputSnapshot!);

        _ = await drawingCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(20), cancellationToken);
        _ = await drawingCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);

        var mapper = new CounterCompositionMarkerMapper();
        var dispatchResult = CompositionMarkerEventPump.DrainAndDispatch(drawingCompositor, ref mapper, runtime);
        using var waitTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitTimeout.CancelAfter(RuntimeDispatchTimeout);
        await WaitForCountAsync(runtime, 1, waitTimeout.Token);

        return new CompositionMarkerRuntimeDiagnostics(
            dispatchResult.DrainedEvents,
            dispatchResult.DispatchedMessages,
            dispatchResult.UnmappedEvents,
            runtime.CurrentModel.Count,
            backend.ExecuteCompositionCount,
            backend.LastCompositionFrame.Layer.Id);
    }

    internal static string Format(in CompositionMarkerRuntimeDiagnostics diagnostics)
    {
        return $"composition-marker-runtime actual drainedEvents={diagnostics.DrainedEvents} dispatchedMessages={diagnostics.DispatchedMessages} unmappedEvents={diagnostics.UnmappedEvents} finalCount={diagnostics.FinalCount} executeCompositionCount={diagnostics.ExecuteCompositionCount} layerId={diagnostics.LayerId.Value}";
    }

    private static async Task WaitForCountAsync(Runtime<CounterModel, CounterMessage> runtime, int expectedCount, CancellationToken cancellationToken)
    {
        while (runtime.CurrentModel.Count != expectedCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(1, cancellationToken);
        }
    }

    private sealed class DiagnosticWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "CompositionMarkerRuntimeDiagnostic";
        public ScreenRegion Region { get; set; } = region;
        public bool ExternalRenderingEnabled { get; set; }
        public nint Handle => nint.Zero;

        public void Dispose() { }
        public void RunMessageLoop() { }
        public void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver) { }
        public void Show() { }
        public event Action<int, int>? SizeChanged { add { } remove { } }
        public event Action<DisplayScale>? DpiChanged { add { } remove { } }
    }

    private sealed class MarkerDiagnosticCompositionBackend : IDrawingBackend, ICompositionDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public CompositionFrame LastCompositionFrame { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            LastCompositionFrame = compositionFrame;
            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: compositionFrame.Layer.Transform.IsIdentity ? 0 : compositionFrame.Layer.CommandCount,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }
        public void Dispose() { }
    }
}

internal readonly struct CompositionMarkerRuntimeDiagnostics(
    int DrainedEvents,
    int DispatchedMessages,
    int UnmappedEvents,
    int FinalCount,
    int ExecuteCompositionCount,
    CompositionLayerId LayerId) : IEquatable<CompositionMarkerRuntimeDiagnostics>
{
    public int DrainedEvents { get; } = DrainedEvents;
    public int DispatchedMessages { get; } = DispatchedMessages;
    public int UnmappedEvents { get; } = UnmappedEvents;
    public int FinalCount { get; } = FinalCount;
    public int ExecuteCompositionCount { get; } = ExecuteCompositionCount;
    public CompositionLayerId LayerId { get; } = LayerId;

    public bool Equals(CompositionMarkerRuntimeDiagnostics other)
    {
        return DrainedEvents == other.DrainedEvents
            && DispatchedMessages == other.DispatchedMessages
            && UnmappedEvents == other.UnmappedEvents
            && FinalCount == other.FinalCount
            && ExecuteCompositionCount == other.ExecuteCompositionCount
            && LayerId == other.LayerId;
    }

    public override bool Equals(object? obj) => obj is CompositionMarkerRuntimeDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DrainedEvents, DispatchedMessages, UnmappedEvents, FinalCount, ExecuteCompositionCount, LayerId);
}
#endif
