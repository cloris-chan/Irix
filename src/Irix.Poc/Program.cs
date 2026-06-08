using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static partial class Program
{
    public static async Task Main(string[] args)
    {
        var diagnosticTask = TryCreateDiagnosticCliTask(args);
        if (diagnosticTask is not null)
        {
            await diagnosticTask;
            return;
        }

        if (args.Contains("--composition-demo"))
        {
            var durationMs = 4000;
            var durationArg = args.SkipWhile(a => a != "--composition-demo").Skip(1).FirstOrDefault();
            if (int.TryParse(durationArg, out var n) && n > 0) durationMs = n;
            using var diagnosticOutput = TryCreateDiagnosticOutput(args);
            await CompositionTransformDemoRunner.RunAsync(
                diagnosticOutput ?? Console.Out,
                durationMs,
                ParseDiagnosticScale(args));
            return;
        }

        using var platformHost = new WindowsPlatformHost();
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(platformHost.Screens[0]));
        window.ExternalRenderingEnabled = true;
        window.Show();

        // D3D12 rendering path
        var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        var clipMode = ParseClipMode(args);
        var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer, clipMode);
        var enablePartialApply = !args.Contains("--no-partial-apply");
        d3d12Renderer.TextCompositionMode = ParseTextCompositionMode(args);
        var displayScale = platformHost.Screens[0].Scale.Normalize();

        Action<double>? maxScrollYCallback = null;
        WindowDrawCommandTranslator? drawCommandTranslator = null;
        var ownerOptions = enablePartialApply
            ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
            : RenderPipelineProductionOwnerOptions.Disabled;
        drawCommandTranslator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: maxScrollY =>
            {
                maxScrollYCallback?.Invoke(maxScrollY);
                RefreshDebugUiLayoutDiagnosticsAfterFrame(drawCommandTranslator);
            },
            ownerOptions: ownerOptions,
            displayScale: displayScale);
        var handoffOptions = enablePartialApply
            ? DrawingBackendCompositorHandoffOptions.Enabled
            : DrawingBackendCompositorHandoffOptions.Disabled;
        using var d3d12Compositor = new DrawingBackendCompositor(d3d12Backend, handoffOptions);
        d3d12Compositor.SetViewport(window.Region.PhysicalBounds, displayScale);
        var compositor = args.Contains("--console")
            ? new CompositeCompositor(new ConsoleCompositor(Console.Out), d3d12Compositor)
            : (ICompositor)d3d12Compositor;
        Func<RetainedRenderFrameSegmentOwnership?>? ownershipProvider = enablePartialApply
            ? () => drawCommandTranslator?.SegmentOwnership
            : null;
        await using var compositorLoop = new CompositorLoop(drawCommandTranslator, compositor, ownershipProvider);
        var counterApplication = new CounterApplication();
        ConfigureDebugUi(args, counterApplication, window, d3d12Renderer, drawCommandTranslator, displayScale);
        await using var runtime = new Runtime<CounterModel, CounterMessage>(counterApplication, compositorLoop);
        var runtimeDispatchSink = new CounterRuntimeDispatchSink(runtime);
        var styleTransitionCompletionTracker = new StyleTransitionCompletionTracker();
        await using var styleTransitionCompletionPump = new StyleTransitionCompletionPump(
            d3d12Compositor,
            styleTransitionCompletionTracker,
            new DrawingBackendStyleTransitionCompositorAdapter(d3d12Compositor),
            new WindowDrawCommandTranslatorRetainedSnapshotProvider(drawCommandTranslator));
        SetDebugUiRuntime(runtime);
        var scrollFramePump = new ScrollFramePump();
        var scrollPresentationCoordinator = new ScrollPresentationCoordinator();
        var inputOwnershipState = new InputOwnershipState();
        SetDebugUiRuntimeSources(scrollFramePump, inputOwnershipState, d3d12Backend.ClipMode);

        double? lastKnownMaxScrollY = null;
        maxScrollYCallback = maxScrollY =>
        {
            _ = TryDispatchMaxScrollFeedbackForRuntime(
                maxScrollY,
                lastKnownMaxScrollY,
                out lastKnownMaxScrollY,
                () => _ = compositorLoop.CancelCompositionScrollPresentationAsync(),
                new CounterAppMessageDispatchMapper(),
                runtimeDispatchSink);
        };

        window.SizeChanged += (w, h) =>
        {
            _ = compositorLoop.CancelCompositionScrollPresentationAsync();
            d3d12Renderer.Resize(w, h);
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, w, h), displayScale);
            var handledByDebugUi = false;
            RequestDebugUiRenderAfterViewportChange(compositorLoop, ref handledByDebugUi);
            if (!handledByDebugUi)
            {
                _ = compositorLoop.RequestRenderAsync();
            }
        };

        window.DpiChanged += newScale =>
        {
            _ = compositorLoop.CancelCompositionScrollPresentationAsync();
            displayScale = newScale.Normalize();
            drawCommandTranslator.SetDisplayScale(displayScale);
            d3d12Compositor.SetViewport(new PixelRectangle(0, 0, d3d12Renderer.Width, d3d12Renderer.Height), displayScale);
            UpdateDebugUiViewportContext(window, d3d12Renderer, drawCommandTranslator, displayScale);
            var handledByDebugUi = false;
            RequestDebugUiRenderAfterViewportChange(compositorLoop, ref handledByDebugUi);
            if (!handledByDebugUi)
            {
                _ = compositorLoop.RequestRenderAsync();
            }

            Console.WriteLine($"DPI changed: scale={displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        };

        using var inputSubscription = platformHost.RawInputEvents.Subscribe(new PlatformInputObserver(HandleInput));

        platformHost.TopologyChanged += OnTopologyChanged;

        Console.WriteLine($"Detected screens: {platformHost.Screens.Count}");
        Console.WriteLine("Rendering: D3D12 (clear color from FillRect)");
        Console.WriteLine($"Backend clip mode: {d3d12Backend.ClipMode}");
        Console.WriteLine($"Partial apply: {(enablePartialApply ? "ENABLED (default)" : "DISABLED (--no-partial-apply)")}");
        Console.WriteLine($"Text composition mode: {d3d12Renderer.TextCompositionMode}");
        Console.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        Console.WriteLine("Controls: Click buttons, Up/Down = +/-1, R = reset, Mouse wheel = +/-1.");

        await runtime.StartAsync();

        window.RunMessageLoop();

        Console.WriteLine($"Final count: {runtime.CurrentModel.Count}");

        void HandleInput(RawInputEvent inputEvent)
        {
            var previousOwnership = inputOwnershipState.Snapshot;
            var hitTestService = new DrawingBackendCompositorInputHitTestService(d3d12Compositor);
            if (TryMapInputForRuntime(inputEvent, inputOwnershipState, hitTestService, out var message))
            {
                var nextOwnership = inputOwnershipState.Snapshot;
                if (message is CounterMessage.WheelRaw wheel)
                {
                    var wheelDispatchSink = new ScrollPresentationWheelDispatchSink(
                        scrollPresentationCoordinator,
                        runtime,
                        compositorLoop,
                        drawCommandTranslator,
                        new NodeKey(1));
                    _ = TryDispatchWheelInputForRuntime(wheel, wheelDispatchSink);
                }
                else if (message is not null)
                {
                    var hasActiveScrollPresentation = compositorLoop.HasActiveScrollPresentation(new NodeKey(1));
                    var transitionLifecycle = CounterStyleTransitionBridge.EvaluateInputTransition(
                        previousOwnership,
                        nextOwnership,
                        hasActiveScrollPresentation,
                        CompositionTimestamp.Now());
                    if (transitionLifecycle.HasTransitionBatch)
                    {
                        var transitionTask = CounterStyleTransitionRuntimeBridge.DispatchAndActivateInputTransitionBatchAsync(
                            runtime,
                            message,
                            transitionLifecycle.Batch,
                            new DrawingBackendStyleTransitionPresentationActivationCompositorAdapter(d3d12Compositor),
                            new WindowDrawCommandTranslatorRetainedSnapshotProvider(drawCommandTranslator),
                            styleTransitionCompletionTracker,
                            retimestampAfterDispatch: true).AsTask();
                        _ = transitionTask.ContinueWith(
                            static (task, state) =>
                            {
                                if (task.Status != TaskStatus.RanToCompletion)
                                {
                                    return;
                                }

                                var context = ((StyleTransitionBatchContinuationContext)state!);
                                if (task.Result.Kind == StyleTransitionBatchPresentationActivationKind.Activated)
                                {
                                    context.CompletionPump.EnsureRunning();
                                    return;
                                }

                                AbortStyleTransitionPresentationForRuntime(
                                    task.Result,
                                    context.CompletionTracker,
                                    new DrawingBackendStyleTransitionCompositorAdapter(context.Compositor));
                            },
                            new StyleTransitionBatchContinuationContext(
                                styleTransitionCompletionPump,
                                styleTransitionCompletionTracker,
                                d3d12Compositor),
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        _ = transitionTask.ContinueWith(
                            static task => _ = task.Exception,
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    else if (transitionLifecycle.HasTransitionDecision)
                    {
                        var transitionTask = CounterStyleTransitionRuntimeBridge.DispatchAndApplyInputTransitionAsync(
                            runtime,
                            message,
                            transitionLifecycle.Decision,
                            new DrawingBackendStyleTransitionCompositorAdapter(d3d12Compositor),
                            new WindowDrawCommandTranslatorRetainedSnapshotProvider(drawCommandTranslator),
                            completionTracker: styleTransitionCompletionTracker,
                            retimestampAfterDispatch: true).AsTask();
                        _ = transitionTask.ContinueWith(
                            static (task, state) =>
                            {
                                if (task.Status == TaskStatus.RanToCompletion
                                    && task.Result.Kind is StyleTransitionRuntimeResultKind.Started or StyleTransitionRuntimeResultKind.Retargeted)
                                {
                                    ((StyleTransitionCompletionPump)state!).EnsureRunning();
                                }
                            },
                            styleTransitionCompletionPump,
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        _ = transitionTask.ContinueWith(
                            static task => _ = task.Exception,
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    else
                    {
                        AbortStyleTransitionPresentationForRuntime(
                            transitionLifecycle,
                            styleTransitionCompletionTracker,
                            new DrawingBackendStyleTransitionCompositorAdapter(d3d12Compositor));
                        _ = TryDispatchAppMessageForRuntime(message, runtimeDispatchSink);
                    }
                }
            }
        }

        void OnTopologyChanged(object? sender, ScreenTopologyChangedEventArgs args)
        {
            Console.WriteLine($"Topology changed. Screen count: {args.Screens.Count}");
        }

        platformHost.TopologyChanged -= OnTopologyChanged;
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

    private static Task? TryCreateDiagnosticCliTask(string[] args)
    {
        Task? task = null;
        CreateDiagnosticCliTask(args, ref task);
        return task;
    }

    static partial void CreateDiagnosticCliTask(string[] args, ref Task? task);

    static partial void ConfigureDebugUi(
        string[] args,
        CounterApplication application,
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale);

    static partial void SetDebugUiRuntime(Runtime<CounterModel, CounterMessage> runtime);

    static partial void SetDebugUiRuntimeSources(
        ScrollFramePump scrollFramePump,
        InputOwnershipState inputOwnershipState,
        DrawingBackendClipMode backendClipMode);

    static partial void RefreshDebugUiLayoutDiagnosticsAfterFrame(WindowDrawCommandTranslator? translator);

    static partial void UpdateDebugUiViewportContext(
        INativeWindow window,
        D3D12Renderer renderer,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale);

    static partial void RequestDebugUiRenderAfterViewportChange(CompositorLoop compositorLoop, ref bool handled);

    private static StreamWriter? TryCreateDiagnosticOutput(string[] args)
    {
        var outputPath = args.SkipWhile(a => a != "--diagnostic-output").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(fullPath, false) { AutoFlush = true };
    }

    internal static TextCompositionMode ParseTextCompositionMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--text-composition").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return TextCompositionMode.GlyphAtlas;
        }

        return value?.ToLowerInvariant() switch
        {
            "glyph-atlas" or "glyphatlas" or "atlas" => TextCompositionMode.GlyphAtlas,
            _ => throw new ArgumentException($"Unsupported text composition mode '{value}'. GlyphAtlas is the only active text composition mode.")
        };
    }

    internal static DrawingBackendClipMode ParseClipMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--clip-mode").Skip(1).FirstOrDefault();
        return value?.ToLowerInvariant() switch
        {
            "diagnostic" or "diagnostics" => DrawingBackendClipMode.Diagnostic,
            "scissor" => DrawingBackendClipMode.Scissor,
            _ when args.Contains("--disable-scissor") => DrawingBackendClipMode.Diagnostic,
            _ => DrawingBackendClipMode.Scissor
        };
    }

    internal static DisplayScale ParseDiagnosticScale(string[] args)
    {
        var value = args.SkipWhile(a => a != "--diagnose-scale").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        value = value.Trim().TrimEnd('%');
        if (!float.TryParse(value, out var parsed) || parsed <= 0 || !float.IsFinite(parsed))
        {
            return default;
        }

        var scale = parsed > 10 ? parsed / 100f : parsed;
        return new DisplayScale(scale, scale).Normalize();
    }

    private sealed class PlatformInputObserver(Action<RawInputEvent> onNext) : IObserver<RawInputEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(RawInputEvent value)
        {
            onNext(value);
        }
    }

    internal static bool TryMapInputForRuntime<THitTestService>(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        THitTestService hitTestService,
        out CounterMessage? message)
        where THitTestService : struct, IInputHitTestService
    {
        var dispatchMapper = new CounterAppMessageDispatchMapper();
        return TryMapInputForRuntime(inputEvent, ownershipState, hitTestService, dispatchMapper, out message);
    }

    internal static bool TryMapInputForRuntime<THitTestService, TDispatchMapper>(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        THitTestService hitTestService,
        TDispatchMapper dispatchMapper,
        out CounterMessage? message)
        where THitTestService : struct, IInputHitTestService
        where TDispatchMapper : struct, IAppMessageDispatchMapper<CounterMessage, CounterMessage>
    {
        var before = ownershipState.Snapshot;
        var actionMapper = new CounterInputActionMapper();
        var mapped = CounterInputRouter.TryMapInput(inputEvent, ownershipState, hitTestService, actionMapper, out var mappedMessage);
        var after = ownershipState.Snapshot;

        if (mapped)
        {
            var intent = AppDispatchIntent<CounterMessage>.Input(mappedMessage, in after);
            return dispatchMapper.TryMapIntent(in intent, out message);
        }

        if (before != after)
        {
            var intent = AppDispatchIntent<CounterMessage>.InputOwnershipChanged(in after);
            return dispatchMapper.TryMapIntent(in intent, out message);
        }

        message = null;
        return false;
    }

    internal static bool TryMapMaxScrollFeedbackForRuntime<TFeedbackMapper>(
        double maxScrollY,
        TFeedbackMapper feedbackMapper,
        out CounterMessage? message)
        where TFeedbackMapper : struct, IControlFeedbackDispatchMapper<CounterMessage>
    {
        if (!IsValidMaxScrollY(maxScrollY))
        {
            message = null;
            return false;
        }

        var intent = AppDispatchIntent<CounterMessage>.MaxScrollFeedback(maxScrollY);
        if (feedbackMapper.TryMapControlFeedbackIntent(in intent, out var mappedMessage))
        {
            message = mappedMessage;
            return true;
        }

        message = null;
        return false;
    }

    internal static bool TryDispatchMaxScrollFeedbackForRuntime<TFeedbackMapper, TDispatchSink>(
        double maxScrollY,
        double? lastKnownMaxScrollY,
        out double? nextKnownMaxScrollY,
        Action cancelScrollPresentation,
        TFeedbackMapper feedbackMapper,
        TDispatchSink dispatchSink)
        where TFeedbackMapper : struct, IControlFeedbackDispatchMapper<CounterMessage>
        where TDispatchSink : struct, IAppRuntimeDispatchSink<CounterMessage>
    {
        ArgumentNullException.ThrowIfNull(cancelScrollPresentation);
        nextKnownMaxScrollY = lastKnownMaxScrollY;
        if (!IsValidMaxScrollY(maxScrollY))
        {
            return false;
        }

        if (lastKnownMaxScrollY.HasValue && Math.Abs(maxScrollY - lastKnownMaxScrollY.Value) <= 0.5)
        {
            return false;
        }

        nextKnownMaxScrollY = maxScrollY;
        cancelScrollPresentation();
        if (TryMapMaxScrollFeedbackForRuntime(maxScrollY, feedbackMapper, out var message))
        {
            _ = TryDispatchAppMessageForRuntime(message, dispatchSink);
        }

        return true;
    }

    private static bool IsValidMaxScrollY(double maxScrollY)
    {
        return maxScrollY >= 0 && double.IsFinite(maxScrollY);
    }

    internal static bool TryDispatchWheelInputForRuntime<TDispatchSink>(
        CounterMessage.WheelRaw wheel,
        TDispatchSink dispatchSink)
        where TDispatchSink : struct, IWheelInputDispatchSink
    {
        ArgumentNullException.ThrowIfNull(wheel);
        var intent = WheelInputDispatchIntent.FromRawDelta(wheel.RawDelta);
        return ScrollInputDispatchAdapter.TryDispatchIntent(in intent, dispatchSink);
    }

    internal static bool TryDispatchAppMessageForRuntime<TDispatchSink>(
        CounterMessage? message,
        TDispatchSink dispatchSink)
        where TDispatchSink : struct, IAppRuntimeDispatchSink<CounterMessage>
    {
        return AppRuntimeDispatchAdapter.TryDispatchMessage(message, dispatchSink);
    }

    internal static StyleTransitionRuntimeResult AbortStyleTransitionPresentationForRuntime<TCompositor>(
        in CounterStyleTransitionLifecycleResult lifecycle,
        StyleTransitionCompletionTracker completionTracker,
        TCompositor compositor,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionCompositorAdapter
    {
        ArgumentNullException.ThrowIfNull(completionTracker);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lifecycle.RequiresStyleTransitionAbort)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        var aborted = completionTracker.AbortActiveTransition();
        if (aborted.Kind != StyleTransitionCompletionResultKind.Aborted)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        return StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
            StyleTransitionRuntimeDecision.Cancel(aborted.TargetKey),
            compositor,
            new FixedStyleTransitionRetainedSnapshotProvider(null),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    internal static StyleTransitionRuntimeResult AbortStyleTransitionPresentationForRuntime<TCompositor>(
        in StyleTransitionBatchPresentationActivationResult activationResult,
        StyleTransitionCompletionTracker completionTracker,
        TCompositor compositor,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionCompositorAdapter
    {
        ArgumentNullException.ThrowIfNull(completionTracker);
        cancellationToken.ThrowIfCancellationRequested();
        if (activationResult.Kind != StyleTransitionBatchPresentationActivationKind.Blocked)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        var aborted = completionTracker.AbortActiveTransition();
        if (aborted.Kind != StyleTransitionCompletionResultKind.Aborted)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        return StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
            StyleTransitionRuntimeDecision.Cancel(aborted.TargetKey),
            compositor,
            new FixedStyleTransitionRetainedSnapshotProvider(null),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class StyleTransitionBatchContinuationContext(
        StyleTransitionCompletionPump CompletionPump,
        StyleTransitionCompletionTracker CompletionTracker,
        DrawingBackendCompositor Compositor)
    {
        public StyleTransitionCompletionPump CompletionPump { get; } = CompletionPump;
        public StyleTransitionCompletionTracker CompletionTracker { get; } = CompletionTracker;
        public DrawingBackendCompositor Compositor { get; } = Compositor;
    }

}
