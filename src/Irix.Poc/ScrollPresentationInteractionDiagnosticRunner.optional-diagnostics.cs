#if IRIX_DIAGNOSTICS
using System.Diagnostics;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class ScrollPresentationInteractionDiagnosticRunner
{
    private static readonly NodeKey ScrollTargetKey = new(1);
    private const int LifecycleStaleTickDrainDelayMs = 90;
    private static readonly RawInputEvent FixedPointerMove = new(RawInputEventKind.PointerMoved, Timestamp: 1, X: 20, Y: 190);
    private static readonly RawInputEvent FixedPointerPress = new(RawInputEventKind.PointerPressed, Timestamp: 2, X: 20, Y: 190, Button: PointerButton.Left);
    private static readonly RawInputEvent FixedPointerRelease = new(RawInputEventKind.PointerReleased, Timestamp: 3, X: 20, Y: 190, Button: PointerButton.Left);

    internal static async Task RunAsync(TextWriter output, CancellationToken cancellationToken = default)
    {
        var diagnostics = await RunCoreAsync(cancellationToken);
        output.WriteLine("=== Scroll Presentation Interaction Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== scroll presentation interaction diagnostic complete ===");
    }

    internal static async Task<ScrollPresentationInteractionDiagnostics> RunCoreAsync(CancellationToken cancellationToken = default)
    {
        return new ScrollPresentationInteractionDiagnostics(
            await RunPointerScenarioAsync(cancellationToken),
            await RunChainScenarioAsync(cancellationToken),
            await RunRapidEnsureScenarioAsync(cancellationToken),
            await RunBoundaryScenarioAsync(cancellationToken),
            await RunTopBoundaryScenarioAsync(cancellationToken),
            await RunLifecycleScenarioAsync(cancellationToken));
    }

    internal static string Format(in ScrollPresentationInteractionDiagnostics diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            FormatPointer(diagnostics.Pointer),
            FormatChain(diagnostics.Chain),
            FormatRapidEnsure(diagnostics.RapidEnsure),
            FormatBoundary(diagnostics.Boundary),
            FormatTopBoundary(diagnostics.TopBoundary),
            FormatLifecycle(diagnostics.Lifecycle));
    }

    private static async Task<ScrollPresentationPointerInteractionDiagnostics> RunPointerScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var beforeHit = session.Compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var beforeAction);
        var inputOwnershipState = new InputOwnershipState();
        var initialHitTestService = new DrawingBackendCompositorInputHitTestService(session.Compositor);
        _ = Program.TryMapInputForRuntime(FixedPointerMove, inputOwnershipState, initialHitTestService, out _);
        var staleHoverAction = inputOwnershipState.HoveredTarget;

        var wheelPixels = WheelDownPixels(1);
        session.Coordinator.AddPendingPixels(wheelPixels);
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var activeProbe = await WaitForPresentedActionAsync(session, FixedPointerMove.X, FixedPointerMove.Y, ActionIdRegistry.Decrement, cancellationToken);

        var hitTestService = new DrawingBackendCompositorInputHitTestService(session.Compositor);
        var hoverMapped = Program.TryMapInputForRuntime(FixedPointerMove, inputOwnershipState, hitTestService, out var hoverMessage);
        var hoverRefresh = hoverMessage as CounterMessage.InputVisualStateChanged;
        var renderCountBeforeHoverDispatch = session.Compositor.RenderCount;
        var executeCountBeforeHoverDispatch = session.Backend.ExecuteCount;
        var executeCompositionCountBeforeHoverDispatch = session.Backend.ExecuteCompositionCount;
        if (hoverMessage is not null)
        {
            await session.Runtime.DispatchAndWaitAsync(hoverMessage, cancellationToken);
        }

        var renderCountAfterHoverDispatch = session.Compositor.RenderCount;
        var executeCountAfterHoverDispatch = session.Backend.ExecuteCount;
        var executeCompositionCountAfterHoverDispatch = session.Backend.ExecuteCompositionCount;
        var layoutReasonAfterHoverDispatch = session.Translator.LastLayoutRebuildReason;
        var activeAfterHover = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfterHover);

        var pressMapped = Program.TryMapInputForRuntime(FixedPointerPress, inputOwnershipState, hitTestService, out var pressMessage);
        var pressRefresh = pressMessage as CounterMessage.InputVisualStateChanged;
        var pressSnapshot = pressRefresh?.Snapshot ?? inputOwnershipState.Snapshot;
        var renderCountBeforePressDispatch = session.Compositor.RenderCount;
        var executeCountBeforePressDispatch = session.Backend.ExecuteCount;
        var executeCompositionCountBeforePressDispatch = session.Backend.ExecuteCompositionCount;
        if (pressMessage is not null)
        {
            await session.Runtime.DispatchAndWaitAsync(pressMessage, cancellationToken);
        }

        var renderCountAfterPressDispatch = session.Compositor.RenderCount;
        var executeCountAfterPressDispatch = session.Backend.ExecuteCount;
        var executeCompositionCountAfterPressDispatch = session.Backend.ExecuteCompositionCount;
        var layoutReasonAfterPressDispatch = session.Translator.LastLayoutRebuildReason;
        var activeAfterPress = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfterPress);
        var releaseMapped = Program.TryMapInputForRuntime(FixedPointerRelease, inputOwnershipState, hitTestService, out var releaseMessage);
        var releaseActionKind = ResolveRoutedActionKind(releaseMessage);
        if (releaseMessage is not null)
        {
            await session.Runtime.DispatchAndWaitAsync(releaseMessage, cancellationToken);
        }

        return new ScrollPresentationPointerInteractionDiagnostics(
            FixedPointerMove.X,
            FixedPointerMove.Y,
            beforeHit,
            beforeAction,
            staleHoverAction,
            activeProbe.Hit,
            activeProbe.Action,
            activeProbe.PresentedScrollY,
            hoverMapped,
            ResolveMessageKind(hoverMessage),
            hoverRefresh?.Snapshot.HoveredTarget ?? inputOwnershipState.HoveredTarget,
            renderCountBeforeHoverDispatch,
            renderCountAfterHoverDispatch,
            executeCountBeforeHoverDispatch,
            executeCountAfterHoverDispatch,
            executeCompositionCountBeforeHoverDispatch,
            executeCompositionCountAfterHoverDispatch,
            layoutReasonAfterHoverDispatch,
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
            layoutReasonAfterPressDispatch,
            activeAfterPress,
            presentedAfterPress,
            releaseMapped,
            ResolveMessageKind(releaseMessage),
            releaseActionKind,
            session.Runtime.CurrentModel.Count,
            session.Coordinator.RetargetCount,
            session.Coordinator.PendingPixels,
            session.CompositorLoop.ScrollPresentationCancelCount,
            session.Compositor.CompositionTickCount,
            session.CompositorLoop.ScrollPresentationTickCount);
    }

    private static async Task<ScrollPresentationChainInteractionDiagnostics> RunChainScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var wheelPixels = WheelDownPixels(1);

        session.Coordinator.AddPendingPixels(wheelPixels);
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var firstTarget = session.Runtime.CurrentModel.Scroll.TargetPosition;
        var firstPosition = session.Runtime.CurrentModel.Scroll.Position;

        session.Coordinator.AddPendingPixels(wheelPixels);
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var finalTarget = session.Runtime.CurrentModel.Scroll.TargetPosition;
        var finalPosition = session.Runtime.CurrentModel.Scroll.Position;
        await session.CompositorLoop.WaitForScrollPresentationIdleAsync(cancellationToken);
        _ = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var lastPresented);

        return new ScrollPresentationChainInteractionDiagnostics(
            wheelPixels,
            firstPosition,
            firstTarget,
            finalPosition,
            finalTarget,
            session.Coordinator.RetargetCount,
            session.Coordinator.PendingPixels,
            session.CompositorLoop.ScrollPresentationCancelCount,
            session.CompositorLoop.ScrollPresentationCancellationDiagnostics.LastReason,
            session.CompositorLoop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind,
            session.Backend.ExecuteCount,
            session.Backend.ExecuteCompositionCount,
            session.Compositor.CompositionTickCount,
            session.CompositorLoop.ScrollPresentationTickCount,
            lastPresented);
    }

    private static async Task<ScrollPresentationRapidEnsureInteractionDiagnostics> RunRapidEnsureScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        const int notchCount = 8;
        var wheelPixels = WheelDownPixels(1);
        var ensureCalls = 0;
        var ensureStartedCount = 0;
        var ensureAlreadyRunningCount = 0;

        session.Coordinator.AddPendingPixels(wheelPixels);
        ensureCalls++;
        if (session.Coordinator.EnsureRunning(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken))
        {
            ensureStartedCount++;
        }
        else
        {
            ensureAlreadyRunningCount++;
        }

        for (var i = 1; i < notchCount; i++)
        {
            session.Coordinator.AddPendingPixels(wheelPixels);
            ensureCalls++;
            if (session.Coordinator.EnsureRunning(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken))
            {
                ensureStartedCount++;
            }
            else
            {
                ensureAlreadyRunningCount++;
            }
        }

        await WaitForCoordinatorIdleAsync(session, cancellationToken);
        await session.CompositorLoop.WaitForScrollPresentationIdleAsync(cancellationToken);
        _ = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var lastPresented);

        return new ScrollPresentationRapidEnsureInteractionDiagnostics(
            notchCount,
            wheelPixels,
            wheelPixels * notchCount,
            session.Runtime.CurrentModel.Scroll.Position,
            session.Runtime.CurrentModel.Scroll.TargetPosition,
            ensureAlreadyRunningCount > 0,
            ensureCalls,
            ensureStartedCount,
            ensureAlreadyRunningCount,
            session.Coordinator.RetargetCount,
            session.Coordinator.PendingPixels,
            session.CompositorLoop.ScrollPresentationCancelCount,
            session.Backend.ExecuteCount,
            session.Backend.ExecuteCompositionCount,
            session.Compositor.CompositionTickCount,
            session.CompositorLoop.ScrollPresentationTickCount,
            lastPresented);
    }

    private static Task<ScrollPresentationBoundaryInteractionDiagnostics> RunBoundaryScenarioAsync(CancellationToken cancellationToken) =>
        RunBoundaryScenarioCoreAsync(cancellationToken, isTopBoundary: false);

    private static Task<ScrollPresentationBoundaryInteractionDiagnostics> RunTopBoundaryScenarioAsync(CancellationToken cancellationToken) =>
        RunBoundaryScenarioCoreAsync(cancellationToken, isTopBoundary: true);

    private static async Task<ScrollPresentationBoundaryInteractionDiagnostics> RunBoundaryScenarioCoreAsync(CancellationToken cancellationToken, bool isTopBoundary)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var maxScrollY = session.Translator.LastMaxScrollY;
        await session.Runtime.DispatchAndWaitAsync(new CounterMessage.UpdateMaxScrollY(maxScrollY), cancellationToken);

        var startPosition = isTopBoundary ? Math.Min(1, maxScrollY) : Math.Max(maxScrollY - 1, 0);
        var nearBoundaryState = new ScrollState
        {
            Position = startPosition,
            TargetPosition = startPosition,
            MaxScrollY = maxScrollY,
            HasMaxScrollY = true
        };
        await session.Runtime.DispatchAndStageRetainedFrameAsync(
            new CounterMessage.ScrollPresentationInterrupted(
                new ScrollPresentationInterruptDecision(
                    ScrollPresentationInterruptPolicy.CommitPresented,
                    nearBoundaryState,
                    startPosition,
                    startPosition,
                    0,
                    DispatchesLayoutFrame: true)),
            cancellationToken);

        var rapidWheelPixels = isTopBoundary ? WheelUpPixels(4) : WheelDownPixels(4);
        session.Coordinator.AddPendingPixels(rapidWheelPixels);
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var targetAfterClamp = session.Runtime.CurrentModel.Scroll.TargetPosition;
        var positionAfterClamp = session.Runtime.CurrentModel.Scroll.Position;
        var retargetsAfterClamp = session.Coordinator.RetargetCount;
        var cancelsAfterClamp = session.CompositorLoop.ScrollPresentationCancelCount;
        var activeAfterClamp = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfterClamp);

        session.Coordinator.AddPendingPixels(rapidWheelPixels);
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var targetAfterOverscroll = session.Runtime.CurrentModel.Scroll.TargetPosition;
        var positionAfterOverscroll = session.Runtime.CurrentModel.Scroll.Position;
        var retargetsAfterOverscroll = session.Coordinator.RetargetCount;
        var cancelsAfterOverscroll = session.CompositorLoop.ScrollPresentationCancelCount;

        return new ScrollPresentationBoundaryInteractionDiagnostics(
            maxScrollY,
            startPosition,
            rapidWheelPixels,
            targetAfterClamp,
            positionAfterClamp,
            retargetsAfterClamp,
            cancelsAfterClamp,
            activeAfterClamp,
            presentedAfterClamp,
            targetAfterOverscroll,
            positionAfterOverscroll,
            retargetsAfterOverscroll,
            cancelsAfterOverscroll,
            session.Coordinator.PendingPixels,
            session.Compositor.CompositionTickCount,
            session.CompositorLoop.ScrollPresentationTickCount);
    }

    private static async Task<ScrollPresentationLifecycleInteractionDiagnostics> RunLifecycleScenarioAsync(CancellationToken cancellationToken)
    {
        return new ScrollPresentationLifecycleInteractionDiagnostics(
            await RunResizeLifecycleScenarioAsync(cancellationToken),
            await RunDpiLifecycleScenarioAsync(cancellationToken),
            await RunRenderInvalidationLifecycleScenarioAsync(cancellationToken),
            await RunMaxScrollLifecycleScenarioAsync(cancellationToken));
    }

    private static async Task<ScrollPresentationLifecycleScenarioDiagnostics> RunResizeLifecycleScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var activeBefore = await StartActivePresentationAsync(session, cancellationToken);
        var cancelTask = Task.CompletedTask;
        var renderTask = Task.CompletedTask;

        session.Window.SizeChanged += (width, height) =>
        {
            cancelTask = session.CompositorLoop.CancelCompositionScrollPresentationAsync(cancellationToken).AsTask();
            session.Compositor.SetViewport(new PixelRectangle(0, 0, width, height), DisplayScale.Identity);
            renderTask = session.CompositorLoop.RequestRenderAndWaitAsync(cancellationToken).AsTask();
        };

        session.Window.RaiseSizeChanged(720, 420);
        await Task.WhenAll(cancelTask, renderTask);

        return await CaptureLifecycleScenarioAsync("resize", session, activeBefore, cancellationToken);
    }

    private static async Task<ScrollPresentationLifecycleScenarioDiagnostics> RunDpiLifecycleScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var activeBefore = await StartActivePresentationAsync(session, cancellationToken);
        var cancelTask = Task.CompletedTask;
        var renderTask = Task.CompletedTask;

        session.Window.DpiChanged += scale =>
        {
            var normalized = scale.Normalize();
            cancelTask = session.CompositorLoop.CancelCompositionScrollPresentationAsync(cancellationToken).AsTask();
            session.Translator.SetDisplayScale(normalized);
            session.Compositor.SetViewport(session.Window.Region.PhysicalBounds, normalized);
            renderTask = session.CompositorLoop.RequestRenderAndWaitAsync(cancellationToken).AsTask();
        };

        session.Window.RaiseDpiChanged(new DisplayScale(1.5f, 1.5f));
        await Task.WhenAll(cancelTask, renderTask);

        return await CaptureLifecycleScenarioAsync("dpi", session, activeBefore, cancellationToken);
    }

    private static async Task<ScrollPresentationLifecycleScenarioDiagnostics> RunRenderInvalidationLifecycleScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var activeBefore = await StartActivePresentationAsync(session, cancellationToken);
        var renderTask = Task.CompletedTask;

        session.Window.SizeChanged += (width, height) =>
        {
            session.Compositor.SetViewport(new PixelRectangle(0, 0, width, height), DisplayScale.Identity);
            renderTask = session.CompositorLoop.RequestRenderAndWaitAsync(cancellationToken).AsTask();
        };

        session.Window.RaiseSizeChanged(840, 480);
        await renderTask;

        return await CaptureLifecycleScenarioAsync("renderInvalidation", session, activeBefore, cancellationToken);
    }

    private static async Task<ScrollPresentationLifecycleScenarioDiagnostics> RunMaxScrollLifecycleScenarioAsync(CancellationToken cancellationToken)
    {
        await using var session = await DiagnosticSession.StartAsync(cancellationToken);
        var initialMaxScrollY = session.Translator.LastMaxScrollY;
        await session.Runtime.DispatchAndWaitAsync(new CounterMessage.UpdateMaxScrollY(initialMaxScrollY), cancellationToken);
        var activeBefore = await StartActivePresentationAsync(session, cancellationToken);
        var nextMaxScrollY = Math.Max(activeBefore.PresentedScrollY + 120, initialMaxScrollY - 64);
        var cancelTask = session.CompositorLoop.CancelCompositionScrollPresentationAsync(cancellationToken).AsTask();
        var updateTask = session.Runtime.DispatchAndWaitAsync(new CounterMessage.UpdateMaxScrollY(nextMaxScrollY), cancellationToken);

        await Task.WhenAll(cancelTask, updateTask);

        return await CaptureLifecycleScenarioAsync("maxScroll", session, activeBefore, cancellationToken);
    }

    private static async Task<ScrollPresentationLifecycleActiveProbe> StartActivePresentationAsync(DiagnosticSession session, CancellationToken cancellationToken)
    {
        session.Coordinator.AddPendingPixels(WheelDownPixels(1));
        await session.Coordinator.RunUntilIdleAsync(session.Runtime, session.CompositorLoop, session.Translator, ScrollTargetKey, cancellationToken);
        var active = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedScrollY);
        var hit = session.Compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var action);
        return new ScrollPresentationLifecycleActiveProbe(active, presentedScrollY, hit, action, session.Compositor.RenderCount);
    }

    private static async Task<ScrollPresentationLifecycleScenarioDiagnostics> CaptureLifecycleScenarioAsync(
        string name,
        DiagnosticSession session,
        ScrollPresentationLifecycleActiveProbe activeBefore,
        CancellationToken cancellationToken)
    {
        var activeAfter = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out var presentedAfter);
        var hitAfter = session.Compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var actionAfter);
        var cancellation = session.CompositorLoop.ScrollPresentationCancellationDiagnostics;
        var viewport = session.Window.Region.PhysicalBounds;
        var scale = session.Compositor.CurrentDisplayScale;
        var renderCountAfter = session.Compositor.RenderCount;
        var compositionTickCountAfterLifecycle = session.Compositor.CompositionTickCount;
        var loopTickCountAfterLifecycle = session.CompositorLoop.ScrollPresentationTickCount;
        var layoutRebuildReasonAfter = session.Translator.LastLayoutRebuildReason;
        var maxScrollY = session.Runtime.CurrentModel.Scroll.MaxScrollY;

        await Task.Delay(LifecycleStaleTickDrainDelayMs, cancellationToken);
        await session.CompositorLoop.RequestRenderAndWaitAsync(cancellationToken);
        var activeAfterStaleWindow = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out _);
        var hitAfterStaleWindow = session.Compositor.TryGetActionIdAtPhysicalPixel(FixedPointerMove.X, FixedPointerMove.Y, out var actionAfterStaleWindow);

        return new ScrollPresentationLifecycleScenarioDiagnostics(
            name,
            activeBefore.Active,
            activeBefore.PresentedScrollY,
            activeBefore.Hit,
            activeBefore.Action,
            activeAfter,
            presentedAfter,
            hitAfter,
            actionAfter,
            session.CompositorLoop.ScrollPresentationCancelCount,
            cancellation.LastReason,
            cancellation.LastInvalidationKind,
            cancellation.ExplicitCount,
            cancellation.RenderInvalidationCount,
            activeBefore.RenderCount,
            renderCountAfter,
            layoutRebuildReasonAfter,
            viewport.Width,
            viewport.Height,
            scale.ScaleX,
            maxScrollY,
            compositionTickCountAfterLifecycle,
            loopTickCountAfterLifecycle,
            session.Compositor.CompositionTickCount,
            session.CompositorLoop.ScrollPresentationTickCount,
            activeAfterStaleWindow,
            hitAfterStaleWindow,
            actionAfterStaleWindow);
    }

    private static string FormatPointer(in ScrollPresentationPointerInteractionDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-interaction actual scenario=pointer pointer=({diagnostics.PointerX},{diagnostics.PointerY})",
            $"beforeHit={diagnostics.BeforeHit} beforeAction={diagnostics.BeforeAction.Value} staleHover={diagnostics.StaleHoverAction.Value}",
            $"activeHit={diagnostics.ActiveHit} activeAction={diagnostics.ActiveAction.Value} activePresented={diagnostics.ActivePresentedScrollY:0.##}",
            $"hoverMapped={diagnostics.HoverMappedInput} hoverMessage={diagnostics.HoverMessageKind} hovered={diagnostics.HoveredAction.Value}",
            $"renderBeforeHover={diagnostics.RenderCountBeforeHoverDispatch} renderAfterHover={diagnostics.RenderCountAfterHoverDispatch}",
            $"executeBeforeHover={diagnostics.ExecuteCountBeforeHoverDispatch} executeAfterHover={diagnostics.ExecuteCountAfterHoverDispatch}",
            $"executeCompositionBeforeHover={diagnostics.ExecuteCompositionCountBeforeHoverDispatch} executeCompositionAfterHover={diagnostics.ExecuteCompositionCountAfterHoverDispatch}",
            $"layoutAfterHover={diagnostics.LayoutRebuildReasonAfterHoverDispatch} activeAfterHover={diagnostics.ActiveAfterHover} presentedAfterHover={diagnostics.PresentedAfterHover:0.##}",
            $"pressMapped={diagnostics.PressMappedInput} pressMessage={diagnostics.PressMessageKind}",
            $"pressHover={diagnostics.PressHoveredAction.Value} pressPressed={diagnostics.PressPressedAction.Value} pressCapture={diagnostics.PressCapturedAction.Value} pressFocus={diagnostics.PressFocusedAction.Value}",
            $"renderBeforePress={diagnostics.RenderCountBeforePressDispatch} renderAfterPress={diagnostics.RenderCountAfterPressDispatch}",
            $"executeBeforePress={diagnostics.ExecuteCountBeforePressDispatch} executeAfterPress={diagnostics.ExecuteCountAfterPressDispatch}",
            $"executeCompositionBeforePress={diagnostics.ExecuteCompositionCountBeforePressDispatch} executeCompositionAfterPress={diagnostics.ExecuteCompositionCountAfterPressDispatch}",
            $"layoutAfterPress={diagnostics.LayoutRebuildReasonAfterPressDispatch} activeAfterPress={diagnostics.ActiveAfterPress} presentedAfterPress={diagnostics.PresentedAfterPress:0.##}",
            $"releaseMapped={diagnostics.ReleaseMappedInput} releaseMessage={diagnostics.ReleaseMessageKind} releaseAction={diagnostics.ReleaseActionKind} countAfterRelease={diagnostics.CountAfterRelease}",
            $"retargets={diagnostics.RetargetCount} pending={diagnostics.PendingPixels:0.##} cancels={diagnostics.CancelCount} compositionTicks={diagnostics.CompositionTickCount} loopTicks={diagnostics.LoopTickCount}"
        ]);
    }

    private static string FormatChain(in ScrollPresentationChainInteractionDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-interaction actual scenario=chain wheelPx={diagnostics.WheelPixels:0.##}",
            $"firstPosition={diagnostics.FirstPosition:0.##} firstTarget={diagnostics.FirstTargetPosition:0.##}",
            $"finalPosition={diagnostics.FinalPosition:0.##} finalTarget={diagnostics.FinalTargetPosition:0.##}",
            $"retargets={diagnostics.RetargetCount} pending={diagnostics.PendingPixels:0.##} cancels={diagnostics.CancelCount}",
            $"cancelReason={diagnostics.CancelReason} cancelInvalidation={diagnostics.CancelInvalidationKind}",
            $"execute={diagnostics.ExecuteCount} executeComposition={diagnostics.ExecuteCompositionCount}",
            $"compositionTicks={diagnostics.CompositionTickCount} loopTicks={diagnostics.LoopTickCount} lastPresented={diagnostics.LastPresentedScrollY:0.##}"
        ]);
    }

    private static string FormatRapidEnsure(in ScrollPresentationRapidEnsureInteractionDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-interaction actual scenario=rapidEnsure notches={diagnostics.NotchCount} wheelPx={diagnostics.WheelPixels:0.##} expectedTarget={diagnostics.ExpectedTargetPosition:0.##}",
            $"finalPosition={diagnostics.FinalPosition:0.##} finalTarget={diagnostics.FinalTargetPosition:0.##}",
            $"overlappedRunning={diagnostics.OverlappedRunning}",
            $"ensureCalls={diagnostics.EnsureCallCount} ensureStarted={diagnostics.EnsureStartedCount} ensureAlreadyRunning={diagnostics.EnsureAlreadyRunningCount}",
            $"retargets={diagnostics.RetargetCount} pending={diagnostics.PendingPixels:0.##} cancels={diagnostics.CancelCount}",
            $"execute={diagnostics.ExecuteCount} executeComposition={diagnostics.ExecuteCompositionCount}",
            $"compositionTicks={diagnostics.CompositionTickCount} loopTicks={diagnostics.LoopTickCount} lastPresented={diagnostics.LastPresentedScrollY:0.##}"
        ]);
    }

    private static string FormatBoundary(in ScrollPresentationBoundaryInteractionDiagnostics diagnostics)
    {
        return FormatBoundaryScenario("boundary", diagnostics);
    }

    private static string FormatTopBoundary(in ScrollPresentationBoundaryInteractionDiagnostics diagnostics)
    {
        return FormatBoundaryScenario("boundaryTop", diagnostics);
    }

    private static string FormatBoundaryScenario(string scenario, in ScrollPresentationBoundaryInteractionDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-interaction actual scenario={scenario} max={diagnostics.MaxScrollY:0.##} start={diagnostics.StartPosition:0.##} rapidWheelPx={diagnostics.RapidWheelPixels:0.##}",
            $"targetAfterClamp={diagnostics.TargetAfterClamp:0.##} positionAfterClamp={diagnostics.PositionAfterClamp:0.##}",
            $"retargetsAfterClamp={diagnostics.RetargetsAfterClamp} cancelsAfterClamp={diagnostics.CancelsAfterClamp}",
            $"activeAfterClamp={diagnostics.ActiveAfterClamp} presentedAfterClamp={diagnostics.PresentedAfterClamp:0.##}",
            $"targetAfterOverscroll={diagnostics.TargetAfterOverscroll:0.##} positionAfterOverscroll={diagnostics.PositionAfterOverscroll:0.##}",
            $"retargetsAfterOverscroll={diagnostics.RetargetsAfterOverscroll} cancelsAfterOverscroll={diagnostics.CancelsAfterOverscroll}",
            $"pending={diagnostics.PendingPixels:0.##} compositionTicks={diagnostics.CompositionTickCount} loopTicks={diagnostics.LoopTickCount}"
        ]);
    }

    private static string FormatLifecycle(in ScrollPresentationLifecycleInteractionDiagnostics diagnostics)
    {
        return string.Join(
            Environment.NewLine,
            FormatLifecycleScenario(diagnostics.Resize),
            FormatLifecycleScenario(diagnostics.Dpi),
            FormatLifecycleScenario(diagnostics.RenderInvalidation),
            FormatLifecycleScenario(diagnostics.MaxScroll));
    }

    private static string FormatLifecycleScenario(in ScrollPresentationLifecycleScenarioDiagnostics diagnostics)
    {
        return string.Join(" ", [
            $"scroll-presentation-interaction actual scenario=lifecycle-{diagnostics.Name}",
            $"activeBefore={diagnostics.ActiveBefore} presentedBefore={diagnostics.PresentedBefore:0.##} hitBefore={diagnostics.HitBefore} actionBefore={diagnostics.ActionBefore.Value}",
            $"activeAfter={diagnostics.ActiveAfter} presentedAfter={diagnostics.PresentedAfter:0.##} hitAfter={diagnostics.HitAfter} actionAfter={diagnostics.ActionAfter.Value}",
            $"cancels={diagnostics.CancelCount} cancelReason={diagnostics.CancelReason} cancelInvalidation={diagnostics.CancelInvalidationKind}",
            $"explicitCancels={diagnostics.ExplicitCancelCount} invalidationCancels={diagnostics.RenderInvalidationCancelCount}",
            $"renderBefore={diagnostics.RenderCountBefore} renderAfter={diagnostics.RenderCountAfter} layoutAfter={diagnostics.LayoutRebuildReasonAfter}",
            $"compositionTicksAfterLifecycle={diagnostics.CompositionTickCountAfterLifecycle} loopTicksAfterLifecycle={diagnostics.LoopTickCountAfterLifecycle}",
            $"compositionTicksAfterStaleWindow={diagnostics.CompositionTickCountAfterStaleWindow} loopTicksAfterStaleWindow={diagnostics.LoopTickCountAfterStaleWindow}",
            $"activeAfterStaleWindow={diagnostics.ActiveAfterStaleWindow} hitAfterStaleWindow={diagnostics.HitAfterStaleWindow} actionAfterStaleWindow={diagnostics.ActionAfterStaleWindow.Value}",
            $"viewport={diagnostics.ViewportWidth}x{diagnostics.ViewportHeight} scale={diagnostics.DisplayScaleX:0.##} maxScroll={diagnostics.MaxScrollY:0.##}"
        ]);
    }

    private static async Task<PresentedActionProbe> WaitForPresentedActionAsync(
        DiagnosticSession session,
        int x,
        int y,
        ActionId expectedAction,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var hit = false;
        var action = ActionId.None;
        var presentedScrollY = 0d;
        while (stopwatch.ElapsedMilliseconds < 2_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hit = session.Compositor.TryGetActionIdAtPhysicalPixel(x, y, out action);
            _ = session.Compositor.TryGetPresentedScrollY(ScrollTargetKey, out presentedScrollY);
            if (hit && action == expectedAction)
            {
                return new PresentedActionProbe(true, action, presentedScrollY);
            }

            await Task.Delay(1, cancellationToken);
        }

        return new PresentedActionProbe(hit, action, presentedScrollY);
    }

    private static async Task WaitForCoordinatorIdleAsync(DiagnosticSession session, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < 2_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Coordinator.IsLoopRunning && session.Coordinator.PendingPixels == 0)
            {
                return;
            }

            await Task.Delay(1, cancellationToken);
        }

        throw new TimeoutException("Scroll presentation coordinator did not become idle.");
    }

    private static double WheelDownPixels(int notches)
    {
        return ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -SystemScrollSettings.Default.WheelUnitsPerNotch * notches),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
    }

    private static double WheelUpPixels(int notches)
    {
        return ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, SystemScrollSettings.Default.WheelUnitsPerNotch * notches),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
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

    private readonly struct PresentedActionProbe(bool Hit, ActionId Action, double PresentedScrollY)
    {
        public bool Hit { get; } = Hit;
        public ActionId Action { get; } = Action;
        public double PresentedScrollY { get; } = PresentedScrollY;
    }

    private readonly struct ScrollPresentationLifecycleActiveProbe(bool Active, double PresentedScrollY, bool Hit, ActionId Action, long RenderCount)
    {
        public bool Active { get; } = Active;
        public double PresentedScrollY { get; } = PresentedScrollY;
        public bool Hit { get; } = Hit;
        public ActionId Action { get; } = Action;
        public long RenderCount { get; } = RenderCount;
    }

    private sealed class DiagnosticSession : IAsyncDisposable
    {
        private DiagnosticSession(
            DiagnosticWindow window,
            WindowDrawCommandTranslator translator,
            DiagnosticCompositionBackend backend,
            DrawingBackendCompositor compositor,
            CompositorLoop compositorLoop,
            Runtime<CounterModel, CounterMessage> runtime,
            ScrollPresentationCoordinator coordinator)
        {
            Window = window;
            Translator = translator;
            Backend = backend;
            Compositor = compositor;
            CompositorLoop = compositorLoop;
            Runtime = runtime;
            Coordinator = coordinator;
        }

        public DiagnosticWindow Window { get; }
        public WindowDrawCommandTranslator Translator { get; }
        public DiagnosticCompositionBackend Backend { get; }
        public DrawingBackendCompositor Compositor { get; }
        public CompositorLoop CompositorLoop { get; }
        public Runtime<CounterModel, CounterMessage> Runtime { get; }
        public ScrollPresentationCoordinator Coordinator { get; }

        public static async Task<DiagnosticSession> StartAsync(CancellationToken cancellationToken)
        {
            var window = new DiagnosticWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
            var translator = new WindowDrawCommandTranslator(window);
            var backend = new DiagnosticCompositionBackend();
            var compositor = new DrawingBackendCompositor(backend);
            compositor.SetViewport(window.Region.PhysicalBounds, DisplayScale.Identity);
            var compositorLoop = new CompositorLoop(translator, compositor);
            var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), compositorLoop);
            await runtime.StartAsync(cancellationToken);
            await compositorLoop.RequestRenderAndWaitAsync(cancellationToken);
            return new DiagnosticSession(window, translator, backend, compositor, compositorLoop, runtime, new ScrollPresentationCoordinator());
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await CompositorLoop.DisposeAsync();
            Compositor.Dispose();
            Window.Dispose();
        }
    }

    private sealed class DiagnosticWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "ScrollPresentationInteractionDiagnostic";
        public ScreenRegion Region { get; set; } = region;
        public bool ExternalRenderingEnabled { get; set; }
        public nint Handle => nint.Zero;
        public void Dispose() { }
        public void RunMessageLoop() { }
        public void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver) { }
        public void Show() { }
        public event Action<int, int>? SizeChanged;
        public event Action<DisplayScale>? DpiChanged;

        public void RaiseSizeChanged(int width, int height)
        {
            Region = new ScreenRegion(Region.ScreenId, new PixelRectangle(Region.PhysicalBounds.X, Region.PhysicalBounds.Y, width, height));
            SizeChanged?.Invoke(width, height);
        }

        public void RaiseDpiChanged(DisplayScale scale)
        {
            DpiChanged?.Invoke(scale);
        }
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

internal readonly struct ScrollPresentationInteractionDiagnostics(
    ScrollPresentationPointerInteractionDiagnostics Pointer,
    ScrollPresentationChainInteractionDiagnostics Chain,
    ScrollPresentationRapidEnsureInteractionDiagnostics RapidEnsure,
    ScrollPresentationBoundaryInteractionDiagnostics Boundary,
    ScrollPresentationBoundaryInteractionDiagnostics TopBoundary,
    ScrollPresentationLifecycleInteractionDiagnostics Lifecycle)
{
    public ScrollPresentationPointerInteractionDiagnostics Pointer { get; } = Pointer;
    public ScrollPresentationChainInteractionDiagnostics Chain { get; } = Chain;
    public ScrollPresentationRapidEnsureInteractionDiagnostics RapidEnsure { get; } = RapidEnsure;
    public ScrollPresentationBoundaryInteractionDiagnostics Boundary { get; } = Boundary;
    public ScrollPresentationBoundaryInteractionDiagnostics TopBoundary { get; } = TopBoundary;
    public ScrollPresentationLifecycleInteractionDiagnostics Lifecycle { get; } = Lifecycle;
}

internal readonly struct ScrollPresentationPointerInteractionDiagnostics(
    int PointerX,
    int PointerY,
    bool BeforeHit,
    ActionId BeforeAction,
    ActionId StaleHoverAction,
    bool ActiveHit,
    ActionId ActiveAction,
    double ActivePresentedScrollY,
    bool HoverMappedInput,
    string HoverMessageKind,
    ActionId HoveredAction,
    long RenderCountBeforeHoverDispatch,
    long RenderCountAfterHoverDispatch,
    int ExecuteCountBeforeHoverDispatch,
    int ExecuteCountAfterHoverDispatch,
    int ExecuteCompositionCountBeforeHoverDispatch,
    int ExecuteCompositionCountAfterHoverDispatch,
    LayoutRebuildReason LayoutRebuildReasonAfterHoverDispatch,
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
    LayoutRebuildReason LayoutRebuildReasonAfterPressDispatch,
    bool ActiveAfterPress,
    double PresentedAfterPress,
    bool ReleaseMappedInput,
    string ReleaseMessageKind,
    string ReleaseActionKind,
    int CountAfterRelease,
    long RetargetCount,
    double PendingPixels,
    long CancelCount,
    long CompositionTickCount,
    long LoopTickCount)
{
    public int PointerX { get; } = PointerX;
    public int PointerY { get; } = PointerY;
    public bool BeforeHit { get; } = BeforeHit;
    public ActionId BeforeAction { get; } = BeforeAction;
    public ActionId StaleHoverAction { get; } = StaleHoverAction;
    public bool ActiveHit { get; } = ActiveHit;
    public ActionId ActiveAction { get; } = ActiveAction;
    public double ActivePresentedScrollY { get; } = ActivePresentedScrollY;
    public bool HoverMappedInput { get; } = HoverMappedInput;
    public string HoverMessageKind { get; } = HoverMessageKind;
    public ActionId HoveredAction { get; } = HoveredAction;
    public long RenderCountBeforeHoverDispatch { get; } = RenderCountBeforeHoverDispatch;
    public long RenderCountAfterHoverDispatch { get; } = RenderCountAfterHoverDispatch;
    public int ExecuteCountBeforeHoverDispatch { get; } = ExecuteCountBeforeHoverDispatch;
    public int ExecuteCountAfterHoverDispatch { get; } = ExecuteCountAfterHoverDispatch;
    public int ExecuteCompositionCountBeforeHoverDispatch { get; } = ExecuteCompositionCountBeforeHoverDispatch;
    public int ExecuteCompositionCountAfterHoverDispatch { get; } = ExecuteCompositionCountAfterHoverDispatch;
    public LayoutRebuildReason LayoutRebuildReasonAfterHoverDispatch { get; } = LayoutRebuildReasonAfterHoverDispatch;
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
    public LayoutRebuildReason LayoutRebuildReasonAfterPressDispatch { get; } = LayoutRebuildReasonAfterPressDispatch;
    public bool ActiveAfterPress { get; } = ActiveAfterPress;
    public double PresentedAfterPress { get; } = PresentedAfterPress;
    public bool ReleaseMappedInput { get; } = ReleaseMappedInput;
    public string ReleaseMessageKind { get; } = ReleaseMessageKind;
    public string ReleaseActionKind { get; } = ReleaseActionKind;
    public int CountAfterRelease { get; } = CountAfterRelease;
    public long RetargetCount { get; } = RetargetCount;
    public double PendingPixels { get; } = PendingPixels;
    public long CancelCount { get; } = CancelCount;
    public long CompositionTickCount { get; } = CompositionTickCount;
    public long LoopTickCount { get; } = LoopTickCount;
}

internal readonly struct ScrollPresentationChainInteractionDiagnostics(
    double WheelPixels,
    double FirstPosition,
    double FirstTargetPosition,
    double FinalPosition,
    double FinalTargetPosition,
    long RetargetCount,
    double PendingPixels,
    long CancelCount,
    ScrollPresentationCancellationReason CancelReason,
    CompositionRenderInvalidationKind CancelInvalidationKind,
    int ExecuteCount,
    int ExecuteCompositionCount,
    long CompositionTickCount,
    long LoopTickCount,
    double LastPresentedScrollY)
{
    public double WheelPixels { get; } = WheelPixels;
    public double FirstPosition { get; } = FirstPosition;
    public double FirstTargetPosition { get; } = FirstTargetPosition;
    public double FinalPosition { get; } = FinalPosition;
    public double FinalTargetPosition { get; } = FinalTargetPosition;
    public long RetargetCount { get; } = RetargetCount;
    public double PendingPixels { get; } = PendingPixels;
    public long CancelCount { get; } = CancelCount;
    public ScrollPresentationCancellationReason CancelReason { get; } = CancelReason;
    public CompositionRenderInvalidationKind CancelInvalidationKind { get; } = CancelInvalidationKind;
    public int ExecuteCount { get; } = ExecuteCount;
    public int ExecuteCompositionCount { get; } = ExecuteCompositionCount;
    public long CompositionTickCount { get; } = CompositionTickCount;
    public long LoopTickCount { get; } = LoopTickCount;
    public double LastPresentedScrollY { get; } = LastPresentedScrollY;
}

internal readonly struct ScrollPresentationRapidEnsureInteractionDiagnostics(
    int NotchCount,
    double WheelPixels,
    double ExpectedTargetPosition,
    double FinalPosition,
    double FinalTargetPosition,
    bool OverlappedRunning,
    int EnsureCallCount,
    int EnsureStartedCount,
    int EnsureAlreadyRunningCount,
    long RetargetCount,
    double PendingPixels,
    long CancelCount,
    int ExecuteCount,
    int ExecuteCompositionCount,
    long CompositionTickCount,
    long LoopTickCount,
    double LastPresentedScrollY)
{
    public int NotchCount { get; } = NotchCount;
    public double WheelPixels { get; } = WheelPixels;
    public double ExpectedTargetPosition { get; } = ExpectedTargetPosition;
    public double FinalPosition { get; } = FinalPosition;
    public double FinalTargetPosition { get; } = FinalTargetPosition;
    public bool OverlappedRunning { get; } = OverlappedRunning;
    public int EnsureCallCount { get; } = EnsureCallCount;
    public int EnsureStartedCount { get; } = EnsureStartedCount;
    public int EnsureAlreadyRunningCount { get; } = EnsureAlreadyRunningCount;
    public long RetargetCount { get; } = RetargetCount;
    public double PendingPixels { get; } = PendingPixels;
    public long CancelCount { get; } = CancelCount;
    public int ExecuteCount { get; } = ExecuteCount;
    public int ExecuteCompositionCount { get; } = ExecuteCompositionCount;
    public long CompositionTickCount { get; } = CompositionTickCount;
    public long LoopTickCount { get; } = LoopTickCount;
    public double LastPresentedScrollY { get; } = LastPresentedScrollY;
}

internal readonly struct ScrollPresentationBoundaryInteractionDiagnostics(
    double MaxScrollY,
    double StartPosition,
    double RapidWheelPixels,
    double TargetAfterClamp,
    double PositionAfterClamp,
    long RetargetsAfterClamp,
    long CancelsAfterClamp,
    bool ActiveAfterClamp,
    double PresentedAfterClamp,
    double TargetAfterOverscroll,
    double PositionAfterOverscroll,
    long RetargetsAfterOverscroll,
    long CancelsAfterOverscroll,
    double PendingPixels,
    long CompositionTickCount,
    long LoopTickCount)
{
    public double MaxScrollY { get; } = MaxScrollY;
    public double StartPosition { get; } = StartPosition;
    public double RapidWheelPixels { get; } = RapidWheelPixels;
    public double TargetAfterClamp { get; } = TargetAfterClamp;
    public double PositionAfterClamp { get; } = PositionAfterClamp;
    public long RetargetsAfterClamp { get; } = RetargetsAfterClamp;
    public long CancelsAfterClamp { get; } = CancelsAfterClamp;
    public bool ActiveAfterClamp { get; } = ActiveAfterClamp;
    public double PresentedAfterClamp { get; } = PresentedAfterClamp;
    public double TargetAfterOverscroll { get; } = TargetAfterOverscroll;
    public double PositionAfterOverscroll { get; } = PositionAfterOverscroll;
    public long RetargetsAfterOverscroll { get; } = RetargetsAfterOverscroll;
    public long CancelsAfterOverscroll { get; } = CancelsAfterOverscroll;
    public double PendingPixels { get; } = PendingPixels;
    public long CompositionTickCount { get; } = CompositionTickCount;
    public long LoopTickCount { get; } = LoopTickCount;
}

internal readonly struct ScrollPresentationLifecycleInteractionDiagnostics(
    ScrollPresentationLifecycleScenarioDiagnostics Resize,
    ScrollPresentationLifecycleScenarioDiagnostics Dpi,
    ScrollPresentationLifecycleScenarioDiagnostics RenderInvalidation,
    ScrollPresentationLifecycleScenarioDiagnostics MaxScroll)
{
    public ScrollPresentationLifecycleScenarioDiagnostics Resize { get; } = Resize;
    public ScrollPresentationLifecycleScenarioDiagnostics Dpi { get; } = Dpi;
    public ScrollPresentationLifecycleScenarioDiagnostics RenderInvalidation { get; } = RenderInvalidation;
    public ScrollPresentationLifecycleScenarioDiagnostics MaxScroll { get; } = MaxScroll;
}

internal readonly struct ScrollPresentationLifecycleScenarioDiagnostics(
    string Name,
    bool ActiveBefore,
    double PresentedBefore,
    bool HitBefore,
    ActionId ActionBefore,
    bool ActiveAfter,
    double PresentedAfter,
    bool HitAfter,
    ActionId ActionAfter,
    long CancelCount,
    ScrollPresentationCancellationReason CancelReason,
    CompositionRenderInvalidationKind CancelInvalidationKind,
    long ExplicitCancelCount,
    long RenderInvalidationCancelCount,
    long RenderCountBefore,
    long RenderCountAfter,
    LayoutRebuildReason LayoutRebuildReasonAfter,
    int ViewportWidth,
    int ViewportHeight,
    float DisplayScaleX,
    double MaxScrollY,
    long CompositionTickCountAfterLifecycle,
    long LoopTickCountAfterLifecycle,
    long CompositionTickCountAfterStaleWindow,
    long LoopTickCountAfterStaleWindow,
    bool ActiveAfterStaleWindow,
    bool HitAfterStaleWindow,
    ActionId ActionAfterStaleWindow)
{
    public string Name { get; } = Name;
    public bool ActiveBefore { get; } = ActiveBefore;
    public double PresentedBefore { get; } = PresentedBefore;
    public bool HitBefore { get; } = HitBefore;
    public ActionId ActionBefore { get; } = ActionBefore;
    public bool ActiveAfter { get; } = ActiveAfter;
    public double PresentedAfter { get; } = PresentedAfter;
    public bool HitAfter { get; } = HitAfter;
    public ActionId ActionAfter { get; } = ActionAfter;
    public long CancelCount { get; } = CancelCount;
    public ScrollPresentationCancellationReason CancelReason { get; } = CancelReason;
    public CompositionRenderInvalidationKind CancelInvalidationKind { get; } = CancelInvalidationKind;
    public long ExplicitCancelCount { get; } = ExplicitCancelCount;
    public long RenderInvalidationCancelCount { get; } = RenderInvalidationCancelCount;
    public long RenderCountBefore { get; } = RenderCountBefore;
    public long RenderCountAfter { get; } = RenderCountAfter;
    public LayoutRebuildReason LayoutRebuildReasonAfter { get; } = LayoutRebuildReasonAfter;
    public int ViewportWidth { get; } = ViewportWidth;
    public int ViewportHeight { get; } = ViewportHeight;
    public float DisplayScaleX { get; } = DisplayScaleX;
    public double MaxScrollY { get; } = MaxScrollY;
    public long CompositionTickCountAfterLifecycle { get; } = CompositionTickCountAfterLifecycle;
    public long LoopTickCountAfterLifecycle { get; } = LoopTickCountAfterLifecycle;
    public long CompositionTickCountAfterStaleWindow { get; } = CompositionTickCountAfterStaleWindow;
    public long LoopTickCountAfterStaleWindow { get; } = LoopTickCountAfterStaleWindow;
    public bool ActiveAfterStaleWindow { get; } = ActiveAfterStaleWindow;
    public bool HitAfterStaleWindow { get; } = HitAfterStaleWindow;
    public ActionId ActionAfterStaleWindow { get; } = ActionAfterStaleWindow;
}
#endif
