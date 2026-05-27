using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class ScrollPresentationRuntimeDiagnosticRunner
{
    internal static async Task RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        var diagnostics = await RunCoreAsync(cancellationToken);
        output.WriteLine("=== Scroll Presentation Runtime Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== scroll presentation runtime diagnostic complete ===");
    }

    internal static async Task<ScrollPresentationRuntimeDiagnostics> RunCoreAsync(CancellationToken cancellationToken = default)
    {
        var window = new DiagnosticWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var translator = new WindowDrawCommandTranslator(window);
        var backend = new DiagnosticCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        await using var compositorLoop = new CompositorLoop(translator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
        await runtime.StartAsync(cancellationToken);
        await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);

        var pump = new ScrollPresentationFramePump();
        pump.AddPendingPixels(54);
        await pump.RunUntilIdleAsync(runtime, compositor, translator, new NodeKey(1), cancellationToken);

        return new ScrollPresentationRuntimeDiagnostics(
            runtime.CurrentModel.Scroll.Position,
            runtime.CurrentModel.Scroll.TargetPosition,
            runtime.CurrentModel.Scroll.IsAnimating,
            compositor.RenderCount,
            compositor.CompositionTickCount,
            pump.CompositionTickCount,
            pump.RetargetCount,
            backend.ExecuteCount,
            backend.ExecuteCompositionCount,
            pump.LastPresentedScrollY);
    }

    internal static string Format(in ScrollPresentationRuntimeDiagnostics diagnostics)
    {
        return $"scroll-presentation-runtime actual position={diagnostics.Position:0.##} target={diagnostics.TargetPosition:0.##} animating={diagnostics.IsAnimating} renderCount={diagnostics.RenderCount} compositorTicks={diagnostics.CompositorTickCount} pumpTicks={diagnostics.PumpTickCount} retargets={diagnostics.RetargetCount} execute={diagnostics.ExecuteCount} executeComposition={diagnostics.ExecuteCompositionCount} lastPresented={diagnostics.LastPresentedScrollY:0.##}";
    }

    private sealed class DiagnosticWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "ScrollPresentationRuntimeDiagnostic";
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

    private sealed class DiagnosticCompositionBackend : IDrawingBackend, ICompositionDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) => ExecuteCount++;

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: CountTranslatedCommands(compositionFrame),
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }
        public void Dispose() { }

        private static int CountTranslatedCommands(in CompositionFrame frame)
        {
            var count = 0;
            for (var i = 0; i < frame.LayerCount; i++)
            {
                var layer = frame.GetLayer(i);
                if (!layer.Transform.IsIdentity)
                {
                    count += layer.CommandCount;
                }
            }

            return count;
        }
    }
}

internal readonly struct ScrollPresentationRuntimeDiagnostics(
    double Position,
    double TargetPosition,
    bool IsAnimating,
    long RenderCount,
    long CompositorTickCount,
    long PumpTickCount,
    long RetargetCount,
    int ExecuteCount,
    int ExecuteCompositionCount,
    double LastPresentedScrollY) : IEquatable<ScrollPresentationRuntimeDiagnostics>
{
    public double Position { get; } = Position;
    public double TargetPosition { get; } = TargetPosition;
    public bool IsAnimating { get; } = IsAnimating;
    public long RenderCount { get; } = RenderCount;
    public long CompositorTickCount { get; } = CompositorTickCount;
    public long PumpTickCount { get; } = PumpTickCount;
    public long RetargetCount { get; } = RetargetCount;
    public int ExecuteCount { get; } = ExecuteCount;
    public int ExecuteCompositionCount { get; } = ExecuteCompositionCount;
    public double LastPresentedScrollY { get; } = LastPresentedScrollY;

    public bool Equals(ScrollPresentationRuntimeDiagnostics other)
    {
        return Position.Equals(other.Position)
            && TargetPosition.Equals(other.TargetPosition)
            && IsAnimating == other.IsAnimating
            && RenderCount == other.RenderCount
            && CompositorTickCount == other.CompositorTickCount
            && PumpTickCount == other.PumpTickCount
            && RetargetCount == other.RetargetCount
            && ExecuteCount == other.ExecuteCount
            && ExecuteCompositionCount == other.ExecuteCompositionCount
            && LastPresentedScrollY.Equals(other.LastPresentedScrollY);
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationRuntimeDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(TargetPosition);
        hash.Add(IsAnimating);
        hash.Add(RenderCount);
        hash.Add(CompositorTickCount);
        hash.Add(PumpTickCount);
        hash.Add(RetargetCount);
        hash.Add(ExecuteCount);
        hash.Add(ExecuteCompositionCount);
        hash.Add(LastPresentedScrollY);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScrollPresentationRuntimeDiagnostics left, ScrollPresentationRuntimeDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationRuntimeDiagnostics left, ScrollPresentationRuntimeDiagnostics right) => !left.Equals(right);
}
