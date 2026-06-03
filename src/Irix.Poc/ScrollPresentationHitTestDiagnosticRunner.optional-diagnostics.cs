#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class ScrollPresentationHitTestDiagnosticRunner
{
    private static readonly NodeKey ScrollTargetKey = new(1);
    private static readonly RawInputEvent FixedPointerMove = new(RawInputEventKind.PointerMoved, Timestamp: 1, X: 20, Y: 28);
    private static readonly RawInputEvent FixedPointerPress = new(RawInputEventKind.PointerPressed, Timestamp: 2, X: 20, Y: 28, Button: PointerButton.Left);
    private static readonly RawInputEvent FixedPointerRelease = new(RawInputEventKind.PointerReleased, Timestamp: 3, X: 20, Y: 28, Button: PointerButton.Left);

    internal static async Task RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        var diagnostics = await RunCoreAsync(cancellationToken);
        output.WriteLine("=== Scroll Presentation HitTest Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== scroll presentation hittest diagnostic complete ===");
    }

    internal static async Task<ScrollPresentationHitTestDiagnostics> RunCoreAsync(CancellationToken cancellationToken = default)
    {
        var window = new DiagnosticWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 240, 120)));
        var translator = new WindowDrawCommandTranslator(window);
        var backend = new DiagnosticCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetViewport(window.Region.PhysicalBounds, DisplayScale.Identity);
        await using var compositorLoop = new CompositorLoop(translator, compositor);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new DiagnosticApplication(), compositorLoop);

        await runtime.StartAsync(cancellationToken);
        await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);

        var beforeHit = compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var beforeAction);
        var stalePressOwnershipState = new InputOwnershipState();
        var initialHitTestResolver = new DrawingBackendCompositorActionHitTestResolver(compositor);
        _ = Program.TryMapInputForRuntime(FixedPointerMove, stalePressOwnershipState, initialHitTestResolver, out _);
        var staleHoverAction = stalePressOwnershipState.HoveredTarget;
        var snapshot = translator.LastRetainedInputSnapshot ?? throw new InvalidOperationException("Retained input snapshot was not produced.");
        compositor.SetCompositionScrollPresentationDeclaration(
            new CompositionScrollPresentationDeclaration(
                ScrollTargetKey,
                new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(10)),
                new CompositionScalarAnimation(40, 10)),
            snapshot);
        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken);

        var activeHit = compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var activeAction);
        var activeHitResult = compositor.TryHitTestLogicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var activeResult);
        var hitTestResolver = new DrawingBackendCompositorActionHitTestResolver(compositor);
        var mapped = Program.TryMapInputForRuntime(FixedPointerMove, new InputOwnershipState(), hitTestResolver, out var message);
        var hoverMessage = message as CounterMessage.InputVisualStateChanged;
        var renderCountBeforeHoverDispatch = compositor.RenderCount;
        var executeCountBeforeHoverDispatch = backend.ExecuteCount;
        var executeCompositionCountBeforeHoverDispatch = backend.ExecuteCompositionCount;
        if (hoverMessage is not null)
        {
            await runtime.DispatchAndWaitAsync(hoverMessage, cancellationToken);
        }

        var renderCountAfterHoverDispatch = compositor.RenderCount;
        var executeCountAfterHoverDispatch = backend.ExecuteCount;
        var executeCompositionCountAfterHoverDispatch = backend.ExecuteCompositionCount;
        var afterHoverHit = compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var afterHoverAction);
        var activeAfterHover = compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfterHover);
        var pressMapped = Program.TryMapInputForRuntime(FixedPointerPress, stalePressOwnershipState, hitTestResolver, out var pressMessage);
        var pressRefresh = pressMessage as CounterMessage.InputVisualStateChanged;
        var pressSnapshot = pressRefresh?.Snapshot ?? stalePressOwnershipState.Snapshot;
        var renderCountBeforePressDispatch = compositor.RenderCount;
        var executeCountBeforePressDispatch = backend.ExecuteCount;
        var executeCompositionCountBeforePressDispatch = backend.ExecuteCompositionCount;
        if (pressRefresh is not null)
        {
            await runtime.DispatchAndWaitAsync(pressRefresh, cancellationToken);
        }

        var renderCountAfterPressDispatch = compositor.RenderCount;
        var executeCountAfterPressDispatch = backend.ExecuteCount;
        var executeCompositionCountAfterPressDispatch = backend.ExecuteCompositionCount;
        var activeAfterPress = compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfterPress);
        var releaseMapped = Program.TryMapInputForRuntime(FixedPointerRelease, stalePressOwnershipState, hitTestResolver, out var releaseMessage);
        var releaseActionKind = ResolveRoutedActionKind(releaseMessage);
        if (releaseMessage is not null)
        {
            await runtime.DispatchAndWaitAsync(releaseMessage, cancellationToken);
        }

        return new ScrollPresentationHitTestDiagnostics(
            FixedPointerMove.X,
            FixedPointerMove.Y,
            beforeHit,
            beforeAction,
            staleHoverAction,
            activeHit,
            activeAction,
            activeHitResult,
            activeResult.MappedThroughComposition,
            activeResult.MappedThroughFixedClip,
            activeResult.AppliedLayerCount,
            activeResult.LocalY,
            mapped,
            ResolveMessageKind(message),
            hoverMessage?.Snapshot.HoveredTarget ?? ActionId.None,
            renderCountBeforeHoverDispatch,
            renderCountAfterHoverDispatch,
            executeCountBeforeHoverDispatch,
            executeCountAfterHoverDispatch,
            executeCompositionCountBeforeHoverDispatch,
            executeCompositionCountAfterHoverDispatch,
            afterHoverHit,
            afterHoverAction,
            activeAfterHover,
            presentedAfterHover,
            pressMapped,
            ResolveMessageKind(pressMessage),
            pressSnapshot.HoveredTarget,
            pressSnapshot.PressedTarget,
            pressSnapshot.CapturedTarget,
            pressSnapshot.FocusedTarget,
            renderCountBeforePressDispatch,
            renderCountAfterPressDispatch,
            executeCountBeforePressDispatch,
            executeCountAfterPressDispatch,
            executeCompositionCountBeforePressDispatch,
            executeCompositionCountAfterPressDispatch,
            activeAfterPress,
            presentedAfterPress,
            releaseMapped,
            ResolveMessageKind(releaseMessage),
            releaseActionKind,
            runtime.CurrentModel.Count,
            translator.LayoutRebuildCount,
            translator.LastLayoutRebuildReason,
            compositor.CompositionTickCount);
    }

    internal static string Format(in ScrollPresentationHitTestDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-hittest actual pointer=({diagnostics.PointerX},{diagnostics.PointerY})",
            $"beforeHit={diagnostics.BeforeHit} beforeAction={diagnostics.BeforeAction.Value} staleHover={diagnostics.StaleHoverAction.Value}",
            $"activeHit={diagnostics.ActiveHit} activeAction={diagnostics.ActiveAction.Value}",
            $"activeMapped={diagnostics.ActiveMappedThroughComposition} activeFixedClip={diagnostics.ActiveMappedThroughFixedClip} activeLayers={diagnostics.ActiveAppliedLayerCount} activeLocalY={diagnostics.ActiveLocalY:0.##}",
            $"mapped={diagnostics.MappedInput} message={diagnostics.MessageKind} hovered={diagnostics.HoveredAction.Value}",
            $"renderBeforeHover={diagnostics.RenderCountBeforeHoverDispatch} renderAfterHover={diagnostics.RenderCountAfterHoverDispatch}",
            $"executeBeforeHover={diagnostics.ExecuteCountBeforeHoverDispatch} executeAfterHover={diagnostics.ExecuteCountAfterHoverDispatch}",
            $"executeCompositionBeforeHover={diagnostics.ExecuteCompositionCountBeforeHoverDispatch} executeCompositionAfterHover={diagnostics.ExecuteCompositionCountAfterHoverDispatch}",
            $"afterHoverHit={diagnostics.AfterHoverHit} afterHoverAction={diagnostics.AfterHoverAction.Value}",
            $"activeAfterHover={diagnostics.ActiveAfterHover} presentedAfterHover={diagnostics.PresentedAfterHover:0.##}",
            $"pressMapped={diagnostics.PressMappedInput} pressMessage={diagnostics.PressMessageKind}",
            $"pressHover={diagnostics.PressHoveredAction.Value} pressPressed={diagnostics.PressPressedAction.Value}",
            $"pressCapture={diagnostics.PressCapturedAction.Value} pressFocus={diagnostics.PressFocusedAction.Value}",
            $"renderBeforePress={diagnostics.RenderCountBeforePressDispatch} renderAfterPress={diagnostics.RenderCountAfterPressDispatch}",
            $"executeBeforePress={diagnostics.ExecuteCountBeforePressDispatch} executeAfterPress={diagnostics.ExecuteCountAfterPressDispatch}",
            $"executeCompositionBeforePress={diagnostics.ExecuteCompositionCountBeforePressDispatch} executeCompositionAfterPress={diagnostics.ExecuteCompositionCountAfterPressDispatch}",
            $"activeAfterPress={diagnostics.ActiveAfterPress} presentedAfterPress={diagnostics.PresentedAfterPress:0.##}",
            $"releaseMapped={diagnostics.ReleaseMappedInput} releaseMessage={diagnostics.ReleaseMessageKind}",
            $"releaseAction={diagnostics.ReleaseActionKind} countAfterRelease={diagnostics.CountAfterRelease}",
            $"layoutRebuilds={diagnostics.LayoutRebuildCount} layoutReason={diagnostics.LayoutRebuildReason} compositionTicks={diagnostics.CompositionTickCount}"
        ]);
    }

    private static string ResolveMessageKind(CounterMessage? message)
    {
        return message switch
        {
            CounterMessage.InputVisualStateChanged => nameof(CounterMessage.InputVisualStateChanged),
            CounterMessage.RoutedInput => nameof(CounterMessage.RoutedInput),
            CounterMessage.WheelRaw => nameof(CounterMessage.WheelRaw),
            null => "None",
            _ => message.GetType().Name
        };
    }

    private static string ResolveRoutedActionKind(CounterMessage? message)
    {
        return message is CounterMessage.RoutedInput routed
            ? ResolveMessageKind(routed.Action)
            : "None";
    }

    private sealed class DiagnosticApplication : IApplication<CounterModel, CounterMessage>
    {
        private readonly VirtualTextArena _arena = new();

        public CounterModel Initialize()
        {
            return new CounterModel(
                0,
                new ScrollState
                {
                    Position = 40,
                    TargetPosition = 40,
                    MaxScrollY = 120,
                    HasMaxScrollY = true
                },
                default);
        }

        public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message)
        {
            return message switch
            {
                CounterMessage.InputVisualStateChanged input => new UpdateResult<CounterModel, CounterMessage>(model with { InputOwnership = input.Snapshot }),
                CounterMessage.RoutedInput input => ApplyRoutedInput(model, input),
                _ => new UpdateResult<CounterModel, CounterMessage>(model)
            };
        }

        public VirtualNodeTree BuildView(CounterModel model)
        {
            _arena.BeginFrame();
            var ownership = model.InputOwnership;
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: ScrollTargetKey,
                properties:
                [
                    VirtualNodeProperty.Height(60),
                    VirtualNodeProperty.ScrollY(ScrollController.GetScrollY(model.Scroll))
                ],
                children:
                [
                    BuildButton("Increment", new NodeKey(2), ActionIdRegistry.Increment, ownership),
                    BuildButton("Decrement", new NodeKey(3), ActionIdRegistry.Decrement, ownership),
                    BuildButton("Reset", new NodeKey(4), ActionIdRegistry.Reset, ownership)
                ]);

            return new VirtualNodeTree(root, _arena.GetOrCreateSnapshot());
        }

        private VirtualNode BuildButton(string label, NodeKey key, ActionId actionId, OwnershipSnapshot ownership)
        {
            return VirtualNodeBuilder.Button(
                _arena,
                label,
                key,
                ButtonPropertyBundle.Create(actionId, ControlVisualStateProjection.Project(ownership, actionId)));
        }

        private static UpdateResult<CounterModel, CounterMessage> ApplyRoutedInput(CounterModel model, CounterMessage.RoutedInput input)
        {
            var modelWithInput = model with { InputOwnership = input.Snapshot };
            return input.Action switch
            {
                CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = modelWithInput.Count + 1 }),
                CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = modelWithInput.Count - 1 }),
                CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = reset.Value }),
                _ => new UpdateResult<CounterModel, CounterMessage>(modelWithInput)
            };
        }
    }

    private sealed class DiagnosticWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "ScrollPresentationHitTestDiagnostic";
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
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

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

internal readonly struct ScrollPresentationHitTestDiagnostics(
    int PointerX,
    int PointerY,
    bool BeforeHit,
    ActionId BeforeAction,
    ActionId StaleHoverAction,
    bool ActiveHit,
    ActionId ActiveAction,
    bool ActiveHitResult,
    bool ActiveMappedThroughComposition,
    bool ActiveMappedThroughFixedClip,
    int ActiveAppliedLayerCount,
    float ActiveLocalY,
    bool MappedInput,
    string MessageKind,
    ActionId HoveredAction,
    long RenderCountBeforeHoverDispatch,
    long RenderCountAfterHoverDispatch,
    int ExecuteCountBeforeHoverDispatch,
    int ExecuteCountAfterHoverDispatch,
    int ExecuteCompositionCountBeforeHoverDispatch,
    int ExecuteCompositionCountAfterHoverDispatch,
    bool AfterHoverHit,
    ActionId AfterHoverAction,
    bool ActiveAfterHover,
    double PresentedAfterHover,
    bool PressMappedInput,
    string PressMessageKind,
    ActionId PressHoveredAction,
    ActionId PressPressedAction,
    ActionId PressCapturedAction,
    ActionId PressFocusedAction,
    long RenderCountBeforePressDispatch,
    long RenderCountAfterPressDispatch,
    int ExecuteCountBeforePressDispatch,
    int ExecuteCountAfterPressDispatch,
    int ExecuteCompositionCountBeforePressDispatch,
    int ExecuteCompositionCountAfterPressDispatch,
    bool ActiveAfterPress,
    double PresentedAfterPress,
    bool ReleaseMappedInput,
    string ReleaseMessageKind,
    string ReleaseActionKind,
    int CountAfterRelease,
    long LayoutRebuildCount,
    LayoutRebuildReason LayoutRebuildReason,
    long CompositionTickCount) : IEquatable<ScrollPresentationHitTestDiagnostics>
{
    public int PointerX { get; } = PointerX;
    public int PointerY { get; } = PointerY;
    public bool BeforeHit { get; } = BeforeHit;
    public ActionId BeforeAction { get; } = BeforeAction;
    public ActionId StaleHoverAction { get; } = StaleHoverAction;
    public bool ActiveHit { get; } = ActiveHit;
    public ActionId ActiveAction { get; } = ActiveAction;
    public bool ActiveHitResult { get; } = ActiveHitResult;
    public bool ActiveMappedThroughComposition { get; } = ActiveMappedThroughComposition;
    public bool ActiveMappedThroughFixedClip { get; } = ActiveMappedThroughFixedClip;
    public int ActiveAppliedLayerCount { get; } = ActiveAppliedLayerCount;
    public float ActiveLocalY { get; } = ActiveLocalY;
    public bool MappedInput { get; } = MappedInput;
    public string MessageKind { get; } = MessageKind;
    public ActionId HoveredAction { get; } = HoveredAction;
    public long RenderCountBeforeHoverDispatch { get; } = RenderCountBeforeHoverDispatch;
    public long RenderCountAfterHoverDispatch { get; } = RenderCountAfterHoverDispatch;
    public int ExecuteCountBeforeHoverDispatch { get; } = ExecuteCountBeforeHoverDispatch;
    public int ExecuteCountAfterHoverDispatch { get; } = ExecuteCountAfterHoverDispatch;
    public int ExecuteCompositionCountBeforeHoverDispatch { get; } = ExecuteCompositionCountBeforeHoverDispatch;
    public int ExecuteCompositionCountAfterHoverDispatch { get; } = ExecuteCompositionCountAfterHoverDispatch;
    public bool AfterHoverHit { get; } = AfterHoverHit;
    public ActionId AfterHoverAction { get; } = AfterHoverAction;
    public bool ActiveAfterHover { get; } = ActiveAfterHover;
    public double PresentedAfterHover { get; } = PresentedAfterHover;
    public bool PressMappedInput { get; } = PressMappedInput;
    public string PressMessageKind { get; } = PressMessageKind;
    public ActionId PressHoveredAction { get; } = PressHoveredAction;
    public ActionId PressPressedAction { get; } = PressPressedAction;
    public ActionId PressCapturedAction { get; } = PressCapturedAction;
    public ActionId PressFocusedAction { get; } = PressFocusedAction;
    public long RenderCountBeforePressDispatch { get; } = RenderCountBeforePressDispatch;
    public long RenderCountAfterPressDispatch { get; } = RenderCountAfterPressDispatch;
    public int ExecuteCountBeforePressDispatch { get; } = ExecuteCountBeforePressDispatch;
    public int ExecuteCountAfterPressDispatch { get; } = ExecuteCountAfterPressDispatch;
    public int ExecuteCompositionCountBeforePressDispatch { get; } = ExecuteCompositionCountBeforePressDispatch;
    public int ExecuteCompositionCountAfterPressDispatch { get; } = ExecuteCompositionCountAfterPressDispatch;
    public bool ActiveAfterPress { get; } = ActiveAfterPress;
    public double PresentedAfterPress { get; } = PresentedAfterPress;
    public bool ReleaseMappedInput { get; } = ReleaseMappedInput;
    public string ReleaseMessageKind { get; } = ReleaseMessageKind;
    public string ReleaseActionKind { get; } = ReleaseActionKind;
    public int CountAfterRelease { get; } = CountAfterRelease;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LayoutRebuildReason { get; } = LayoutRebuildReason;
    public long CompositionTickCount { get; } = CompositionTickCount;

    public bool Equals(ScrollPresentationHitTestDiagnostics other)
    {
        return PointerX == other.PointerX
            && PointerY == other.PointerY
            && BeforeHit == other.BeforeHit
            && BeforeAction == other.BeforeAction
            && StaleHoverAction == other.StaleHoverAction
            && ActiveHit == other.ActiveHit
            && ActiveAction == other.ActiveAction
            && ActiveHitResult == other.ActiveHitResult
            && ActiveMappedThroughComposition == other.ActiveMappedThroughComposition
            && ActiveMappedThroughFixedClip == other.ActiveMappedThroughFixedClip
            && ActiveAppliedLayerCount == other.ActiveAppliedLayerCount
            && ActiveLocalY.Equals(other.ActiveLocalY)
            && MappedInput == other.MappedInput
            && MessageKind == other.MessageKind
            && HoveredAction == other.HoveredAction
            && RenderCountBeforeHoverDispatch == other.RenderCountBeforeHoverDispatch
            && RenderCountAfterHoverDispatch == other.RenderCountAfterHoverDispatch
            && ExecuteCountBeforeHoverDispatch == other.ExecuteCountBeforeHoverDispatch
            && ExecuteCountAfterHoverDispatch == other.ExecuteCountAfterHoverDispatch
            && ExecuteCompositionCountBeforeHoverDispatch == other.ExecuteCompositionCountBeforeHoverDispatch
            && ExecuteCompositionCountAfterHoverDispatch == other.ExecuteCompositionCountAfterHoverDispatch
            && AfterHoverHit == other.AfterHoverHit
            && AfterHoverAction == other.AfterHoverAction
            && ActiveAfterHover == other.ActiveAfterHover
            && PresentedAfterHover.Equals(other.PresentedAfterHover)
            && PressMappedInput == other.PressMappedInput
            && PressMessageKind == other.PressMessageKind
            && PressHoveredAction == other.PressHoveredAction
            && PressPressedAction == other.PressPressedAction
            && PressCapturedAction == other.PressCapturedAction
            && PressFocusedAction == other.PressFocusedAction
            && RenderCountBeforePressDispatch == other.RenderCountBeforePressDispatch
            && RenderCountAfterPressDispatch == other.RenderCountAfterPressDispatch
            && ExecuteCountBeforePressDispatch == other.ExecuteCountBeforePressDispatch
            && ExecuteCountAfterPressDispatch == other.ExecuteCountAfterPressDispatch
            && ExecuteCompositionCountBeforePressDispatch == other.ExecuteCompositionCountBeforePressDispatch
            && ExecuteCompositionCountAfterPressDispatch == other.ExecuteCompositionCountAfterPressDispatch
            && ActiveAfterPress == other.ActiveAfterPress
            && PresentedAfterPress.Equals(other.PresentedAfterPress)
            && ReleaseMappedInput == other.ReleaseMappedInput
            && ReleaseMessageKind == other.ReleaseMessageKind
            && ReleaseActionKind == other.ReleaseActionKind
            && CountAfterRelease == other.CountAfterRelease
            && LayoutRebuildCount == other.LayoutRebuildCount
            && LayoutRebuildReason == other.LayoutRebuildReason
            && CompositionTickCount == other.CompositionTickCount;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationHitTestDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PointerX);
        hash.Add(PointerY);
        hash.Add(BeforeHit);
        hash.Add(BeforeAction);
        hash.Add(StaleHoverAction);
        hash.Add(ActiveHit);
        hash.Add(ActiveAction);
        hash.Add(ActiveHitResult);
        hash.Add(ActiveMappedThroughComposition);
        hash.Add(ActiveMappedThroughFixedClip);
        hash.Add(ActiveAppliedLayerCount);
        hash.Add(ActiveLocalY);
        hash.Add(MappedInput);
        hash.Add(MessageKind);
        hash.Add(HoveredAction);
        hash.Add(RenderCountBeforeHoverDispatch);
        hash.Add(RenderCountAfterHoverDispatch);
        hash.Add(ExecuteCountBeforeHoverDispatch);
        hash.Add(ExecuteCountAfterHoverDispatch);
        hash.Add(ExecuteCompositionCountBeforeHoverDispatch);
        hash.Add(ExecuteCompositionCountAfterHoverDispatch);
        hash.Add(AfterHoverHit);
        hash.Add(AfterHoverAction);
        hash.Add(ActiveAfterHover);
        hash.Add(PresentedAfterHover);
        hash.Add(PressMappedInput);
        hash.Add(PressMessageKind);
        hash.Add(PressHoveredAction);
        hash.Add(PressPressedAction);
        hash.Add(PressCapturedAction);
        hash.Add(PressFocusedAction);
        hash.Add(RenderCountBeforePressDispatch);
        hash.Add(RenderCountAfterPressDispatch);
        hash.Add(ExecuteCountBeforePressDispatch);
        hash.Add(ExecuteCountAfterPressDispatch);
        hash.Add(ExecuteCompositionCountBeforePressDispatch);
        hash.Add(ExecuteCompositionCountAfterPressDispatch);
        hash.Add(ActiveAfterPress);
        hash.Add(PresentedAfterPress);
        hash.Add(ReleaseMappedInput);
        hash.Add(ReleaseMessageKind);
        hash.Add(ReleaseActionKind);
        hash.Add(CountAfterRelease);
        hash.Add(LayoutRebuildCount);
        hash.Add(LayoutRebuildReason);
        hash.Add(CompositionTickCount);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScrollPresentationHitTestDiagnostics left, ScrollPresentationHitTestDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationHitTestDiagnostics left, ScrollPresentationHitTestDiagnostics right) => !left.Equals(right);
}
#endif
