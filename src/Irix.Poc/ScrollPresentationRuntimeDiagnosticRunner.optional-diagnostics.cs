#if IRIX_DIAGNOSTICS
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
        return new ScrollPresentationRuntimeDiagnostics(
            await RunRetargetScenarioAsync(segmentCount: 1, cancellationToken),
            await RunRetargetScenarioAsync(segmentCount: 2, cancellationToken),
            await RunExplicitCancellationScenarioAsync(cancellationToken),
            await RunInvalidationCancellationScenarioAsync(
                "viewport",
                new CompositionRenderInvalidation(CompositionRenderInvalidationKind.ViewportChanged),
                cancellationToken),
            await RunInvalidationCancellationScenarioAsync(
                "tree",
                new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TreeStructure),
                cancellationToken),
            await RunInvalidationCancellationScenarioAsync(
                "layout",
                new CompositionRenderInvalidation(CompositionRenderInvalidationKind.LayoutAffecting),
                cancellationToken),
            await RunInvalidationCancellationScenarioAsync(
                "text",
                new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TextSizeAffecting),
                cancellationToken),
            await RunInvalidationCancellationScenarioAsync(
                "maxScroll",
                CompositionRenderInvalidation.MaxScrollChanged,
                cancellationToken));
    }

    private static async Task<ScrollPresentationRuntimeRetargetDiagnostics> RunRetargetScenarioAsync(int segmentCount, CancellationToken cancellationToken)
    {
        var window = new DiagnosticWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var translator = new WindowDrawCommandTranslator(window);
        var backend = new DiagnosticCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        await using var compositorLoop = new CompositorLoop(translator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
        await runtime.StartAsync(cancellationToken);
        await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);

        var coordinator = new ScrollPresentationCoordinator();
        for (var i = 0; i < segmentCount; i++)
        {
            coordinator.AddPendingPixels(54);
            await coordinator.RunUntilIdleAsync(runtime, compositorLoop, translator, new NodeKey(1), cancellationToken);
        }

        await compositorLoop.WaitForScrollPresentationIdleAsync(cancellationToken);
        _ = compositorLoop.TryGetPresentedScrollY(new NodeKey(1), out var lastPresentedScrollY);

        return new ScrollPresentationRuntimeRetargetDiagnostics(
            runtime.CurrentModel.Scroll.Position,
            runtime.CurrentModel.Scroll.TargetPosition,
            runtime.CurrentModel.Scroll.IsAnimating,
            compositor.RenderCount,
            compositor.RetainedStageCount,
            compositor.CompositionTickCount,
            compositorLoop.ScrollPresentationTickCount,
            compositorLoop.ScrollPresentationCancelCount,
            compositorLoop.ScrollPresentationCancellationDiagnostics,
            coordinator.RetargetCount,
            backend.ExecuteCount,
            backend.ExecuteCompositionCount,
            lastPresentedScrollY);
    }

    private static async Task<ScrollPresentationCancellationScenarioDiagnostics> RunExplicitCancellationScenarioAsync(CancellationToken cancellationToken)
    {
        var translator = new InvalidatingDiagnosticTranslator(CompositionRenderInvalidation.None);
        var compositor = new ScrollPresentationCancellationCompositor();
        await using var compositorLoop = new CompositorLoop(translator, compositor);
        using var retainedFrame = BuildRetainedScrollFrame(out var snapshot);

        await compositorLoop.StartCompositionScrollPresentationAsync(
            CreateScrollPresentationDeclaration(0, 54),
            snapshot,
            cancellationToken);
        await compositorLoop.CancelCompositionScrollPresentationAsync(cancellationToken);

        return new ScrollPresentationCancellationScenarioDiagnostics(
            "explicit",
            compositorLoop.ScrollPresentationCancelCount,
            compositorLoop.ScrollPresentationCancellationDiagnostics,
            translator.TranslateCallCount,
            compositor.RenderCount,
            compositor.PresentationActiveDuringLastRender,
            compositor.HasActivePresentation,
            compositorLoop.ScrollPresentationTickCount);
    }

    private static async Task<ScrollPresentationCancellationScenarioDiagnostics> RunInvalidationCancellationScenarioAsync(
        string name,
        CompositionRenderInvalidation invalidation,
        CancellationToken cancellationToken)
    {
        var translator = new InvalidatingDiagnosticTranslator(invalidation);
        var compositor = new ScrollPresentationCancellationCompositor();
        await using var compositorLoop = new CompositorLoop(translator, compositor);
        using var retainedFrame = BuildRetainedScrollFrame(out var snapshot);

        await compositorLoop.StartCompositionScrollPresentationAsync(
            CreateScrollPresentationDeclaration(0, 54),
            snapshot,
            cancellationToken);
        await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);

        return new ScrollPresentationCancellationScenarioDiagnostics(
            name,
            compositorLoop.ScrollPresentationCancelCount,
            compositorLoop.ScrollPresentationCancellationDiagnostics,
            translator.TranslateCallCount,
            compositor.RenderCount,
            compositor.PresentationActiveDuringLastRender,
            compositor.HasActivePresentation,
            compositorLoop.ScrollPresentationTickCount);
    }

    internal static string Format(in ScrollPresentationRuntimeDiagnostics diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            FormatRetarget("initial", diagnostics.Retarget),
            FormatRetarget("chain", diagnostics.RetargetChain),
            FormatCancellation(diagnostics.ExplicitCancellation),
            FormatCancellation(diagnostics.ViewportInvalidationCancellation),
            FormatCancellation(diagnostics.TreeInvalidationCancellation),
            FormatCancellation(diagnostics.LayoutInvalidationCancellation),
            FormatCancellation(diagnostics.TextInvalidationCancellation),
            FormatCancellation(diagnostics.MaxScrollInvalidationCancellation));
    }

    private static string FormatRetarget(string name, in ScrollPresentationRuntimeRetargetDiagnostics diagnostics)
    {
        var cancellation = diagnostics.Cancellation;
        return $"scroll-presentation-runtime actual position={diagnostics.Position:0.##} target={diagnostics.TargetPosition:0.##} animating={diagnostics.IsAnimating} scenario={name} renderCount={diagnostics.RenderCount} retainedStages={diagnostics.RetainedStageCount} compositorTicks={diagnostics.CompositorTickCount} loopTicks={diagnostics.LoopTickCount} cancels={diagnostics.CancelCount} cancelReason={cancellation.LastReason} cancelInvalidation={cancellation.LastInvalidationKind} explicitCancels={cancellation.ExplicitCount} invalidationCancels={cancellation.RenderInvalidationCount} retargets={diagnostics.RetargetCount} execute={diagnostics.ExecuteCount} executeComposition={diagnostics.ExecuteCompositionCount} lastPresented={diagnostics.LastPresentedScrollY:0.##}";
    }

    private static string FormatCancellation(in ScrollPresentationCancellationScenarioDiagnostics diagnostics)
    {
        var cancellation = diagnostics.Cancellation;
        return $"scroll-presentation-runtime.cancel scenario={diagnostics.Name} cancels={diagnostics.CancelCount} cancelReason={cancellation.LastReason} cancelInvalidation={cancellation.LastInvalidationKind} explicitCancels={cancellation.ExplicitCount} invalidationCancels={cancellation.RenderInvalidationCount} disposeCancels={cancellation.DisposeCount} translate={diagnostics.TranslateCount} render={diagnostics.RenderCount} activeDuringRender={diagnostics.PresentationActiveDuringRender} activeAfter={diagnostics.PresentationActiveAfter} loopTicks={diagnostics.LoopTickCount}";
    }

    private static CompositionScrollPresentationDeclaration CreateScrollPresentationDeclaration(float from, float to)
    {
        return new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.FromMilliseconds(1000)),
            new CompositionScalarAnimation(from, to));
    }

    private static RenderFrameBatch BuildRetainedScrollFrame(out RenderPipelineRetainedInputSnapshot snapshot)
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            arena.GetOrCreateSnapshot());
        snapshot = pipeline.LastRetainedInputSnapshot!;
        return frame;
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

    private sealed class InvalidatingDiagnosticTranslator(CompositionRenderInvalidation invalidation) : IPatchBatchTranslator, ICompositionInvalidationProvider
    {
        public int TranslateCallCount { get; private set; }
        public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            LastCompositionInvalidation = invalidation;
            var owner = new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 208, 32))
            ]);
            return new RenderFrameBatch(new DrawCommandBatch(owner, 1), []);
        }
    }

    private sealed class ScrollPresentationCancellationCompositor : ICompositor, ICompositionScrollPresentationCompositor
    {
        private CompositionScrollPresentationDeclaration _declaration;
        private bool _active;
        private double _presentedScrollY;

        public int RenderCount { get; private set; }
        public bool PresentationActiveDuringLastRender { get; private set; }
        public bool HasActivePresentation => _active;

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCount++;
            PresentationActiveDuringLastRender = _active;
            return ValueTask.CompletedTask;
        }

        public void SetCompositionScrollPresentationDeclaration(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot)
        {
            _declaration = declaration;
            _active = true;
        }

        public void ClearCompositionScrollPresentation()
        {
            _active = false;
        }

        public ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
            CompositionTimestamp timestamp,
            CancellationToken cancellationToken = default)
        {
            var progress = _declaration.Timeline.ProgressAt(timestamp);
            _presentedScrollY = _declaration.PresentedScrollY.Evaluate(progress);
            return ValueTask.FromResult(new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: 1,
                CommandCount: 1,
                TranslatedCommands: 1,
                OpacityAppliedCommands: 0));
        }

        public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
        {
            if (_active && targetKey == _declaration.TargetKey)
            {
                presentedScrollY = _presentedScrollY;
                return true;
            }

            presentedScrollY = 0;
            return false;
        }
    }
}

internal readonly struct ScrollPresentationRuntimeDiagnostics(
    ScrollPresentationRuntimeRetargetDiagnostics Retarget,
    ScrollPresentationRuntimeRetargetDiagnostics RetargetChain,
    ScrollPresentationCancellationScenarioDiagnostics ExplicitCancellation,
    ScrollPresentationCancellationScenarioDiagnostics ViewportInvalidationCancellation,
    ScrollPresentationCancellationScenarioDiagnostics TreeInvalidationCancellation,
    ScrollPresentationCancellationScenarioDiagnostics LayoutInvalidationCancellation,
    ScrollPresentationCancellationScenarioDiagnostics TextInvalidationCancellation,
    ScrollPresentationCancellationScenarioDiagnostics MaxScrollInvalidationCancellation) : IEquatable<ScrollPresentationRuntimeDiagnostics>
{
    public ScrollPresentationRuntimeRetargetDiagnostics Retarget { get; } = Retarget;
    public ScrollPresentationRuntimeRetargetDiagnostics RetargetChain { get; } = RetargetChain;
    public ScrollPresentationCancellationScenarioDiagnostics ExplicitCancellation { get; } = ExplicitCancellation;
    public ScrollPresentationCancellationScenarioDiagnostics ViewportInvalidationCancellation { get; } = ViewportInvalidationCancellation;
    public ScrollPresentationCancellationScenarioDiagnostics TreeInvalidationCancellation { get; } = TreeInvalidationCancellation;
    public ScrollPresentationCancellationScenarioDiagnostics LayoutInvalidationCancellation { get; } = LayoutInvalidationCancellation;
    public ScrollPresentationCancellationScenarioDiagnostics TextInvalidationCancellation { get; } = TextInvalidationCancellation;
    public ScrollPresentationCancellationScenarioDiagnostics MaxScrollInvalidationCancellation { get; } = MaxScrollInvalidationCancellation;
    public double Position => Retarget.Position;
    public double TargetPosition => Retarget.TargetPosition;
    public bool IsAnimating => Retarget.IsAnimating;
    public long RenderCount => Retarget.RenderCount;
    public long RetainedStageCount => Retarget.RetainedStageCount;
    public long CompositorTickCount => Retarget.CompositorTickCount;
    public long LoopTickCount => Retarget.LoopTickCount;
    public long CancelCount => Retarget.CancelCount;
    public ScrollPresentationCancellationSnapshot Cancellation => Retarget.Cancellation;
    public long RetargetCount => Retarget.RetargetCount;
    public int ExecuteCount => Retarget.ExecuteCount;
    public int ExecuteCompositionCount => Retarget.ExecuteCompositionCount;
    public double LastPresentedScrollY => Retarget.LastPresentedScrollY;

    public bool Equals(ScrollPresentationRuntimeDiagnostics other)
    {
        return Retarget == other.Retarget
            && RetargetChain == other.RetargetChain
            && ExplicitCancellation == other.ExplicitCancellation
            && ViewportInvalidationCancellation == other.ViewportInvalidationCancellation
            && TreeInvalidationCancellation == other.TreeInvalidationCancellation
            && LayoutInvalidationCancellation == other.LayoutInvalidationCancellation
            && TextInvalidationCancellation == other.TextInvalidationCancellation
            && MaxScrollInvalidationCancellation == other.MaxScrollInvalidationCancellation;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationRuntimeDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Retarget);
        hash.Add(RetargetChain);
        hash.Add(ExplicitCancellation);
        hash.Add(ViewportInvalidationCancellation);
        hash.Add(TreeInvalidationCancellation);
        hash.Add(LayoutInvalidationCancellation);
        hash.Add(TextInvalidationCancellation);
        hash.Add(MaxScrollInvalidationCancellation);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScrollPresentationRuntimeDiagnostics left, ScrollPresentationRuntimeDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationRuntimeDiagnostics left, ScrollPresentationRuntimeDiagnostics right) => !left.Equals(right);
}

internal readonly struct ScrollPresentationRuntimeRetargetDiagnostics(
    double Position,
    double TargetPosition,
    bool IsAnimating,
    long RenderCount,
    long RetainedStageCount,
    long CompositorTickCount,
    long LoopTickCount,
    long CancelCount,
    ScrollPresentationCancellationSnapshot Cancellation,
    long RetargetCount,
    int ExecuteCount,
    int ExecuteCompositionCount,
    double LastPresentedScrollY) : IEquatable<ScrollPresentationRuntimeRetargetDiagnostics>
{
    public double Position { get; } = Position;
    public double TargetPosition { get; } = TargetPosition;
    public bool IsAnimating { get; } = IsAnimating;
    public long RenderCount { get; } = RenderCount;
    public long RetainedStageCount { get; } = RetainedStageCount;
    public long CompositorTickCount { get; } = CompositorTickCount;
    public long LoopTickCount { get; } = LoopTickCount;
    public long CancelCount { get; } = CancelCount;
    public ScrollPresentationCancellationSnapshot Cancellation { get; } = Cancellation;
    public long RetargetCount { get; } = RetargetCount;
    public int ExecuteCount { get; } = ExecuteCount;
    public int ExecuteCompositionCount { get; } = ExecuteCompositionCount;
    public double LastPresentedScrollY { get; } = LastPresentedScrollY;

    public bool Equals(ScrollPresentationRuntimeRetargetDiagnostics other)
    {
        return Position.Equals(other.Position)
            && TargetPosition.Equals(other.TargetPosition)
            && IsAnimating == other.IsAnimating
            && RenderCount == other.RenderCount
            && RetainedStageCount == other.RetainedStageCount
            && CompositorTickCount == other.CompositorTickCount
            && LoopTickCount == other.LoopTickCount
            && CancelCount == other.CancelCount
            && Cancellation == other.Cancellation
            && RetargetCount == other.RetargetCount
            && ExecuteCount == other.ExecuteCount
            && ExecuteCompositionCount == other.ExecuteCompositionCount
            && LastPresentedScrollY.Equals(other.LastPresentedScrollY);
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationRuntimeRetargetDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(TargetPosition);
        hash.Add(IsAnimating);
        hash.Add(RenderCount);
        hash.Add(RetainedStageCount);
        hash.Add(CompositorTickCount);
        hash.Add(LoopTickCount);
        hash.Add(CancelCount);
        hash.Add(Cancellation);
        hash.Add(RetargetCount);
        hash.Add(ExecuteCount);
        hash.Add(ExecuteCompositionCount);
        hash.Add(LastPresentedScrollY);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScrollPresentationRuntimeRetargetDiagnostics left, ScrollPresentationRuntimeRetargetDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationRuntimeRetargetDiagnostics left, ScrollPresentationRuntimeRetargetDiagnostics right) => !left.Equals(right);
}

internal readonly struct ScrollPresentationCancellationScenarioDiagnostics(
    string Name,
    long CancelCount,
    ScrollPresentationCancellationSnapshot Cancellation,
    int TranslateCount,
    int RenderCount,
    bool PresentationActiveDuringRender,
    bool PresentationActiveAfter,
    long LoopTickCount) : IEquatable<ScrollPresentationCancellationScenarioDiagnostics>
{
    public string Name { get; } = Name;
    public long CancelCount { get; } = CancelCount;
    public ScrollPresentationCancellationSnapshot Cancellation { get; } = Cancellation;
    public int TranslateCount { get; } = TranslateCount;
    public int RenderCount { get; } = RenderCount;
    public bool PresentationActiveDuringRender { get; } = PresentationActiveDuringRender;
    public bool PresentationActiveAfter { get; } = PresentationActiveAfter;
    public long LoopTickCount { get; } = LoopTickCount;

    public bool Equals(ScrollPresentationCancellationScenarioDiagnostics other)
    {
        return Name == other.Name
            && CancelCount == other.CancelCount
            && Cancellation == other.Cancellation
            && TranslateCount == other.TranslateCount
            && RenderCount == other.RenderCount
            && PresentationActiveDuringRender == other.PresentationActiveDuringRender
            && PresentationActiveAfter == other.PresentationActiveAfter
            && LoopTickCount == other.LoopTickCount;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationCancellationScenarioDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, CancelCount, Cancellation, TranslateCount, RenderCount, PresentationActiveDuringRender, PresentationActiveAfter, LoopTickCount);

    public static bool operator ==(ScrollPresentationCancellationScenarioDiagnostics left, ScrollPresentationCancellationScenarioDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationCancellationScenarioDiagnostics left, ScrollPresentationCancellationScenarioDiagnostics right) => !left.Equals(right);
}
#endif
