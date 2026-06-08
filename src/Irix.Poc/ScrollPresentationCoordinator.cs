using Irix.Rendering;

namespace Irix.Poc;

internal sealed class ScrollPresentationCoordinator
{
    private const int AnimationDurationMs = 160;
    private const int IdleTickExitThreshold = 2;
    private const double TargetEpsilon = 0.001;
    private const CompositionAnimationEasing RetargetEasing = CompositionAnimationEasing.SineOut;

    private readonly ICompositionClockSource _clockSource;
    private int _loopRunning;
    private long _pendingPixelsBits;
    private long _retargetCount;
    private long _lastPresentedScrollYBits;

    internal ScrollPresentationCoordinator()
        : this(new SystemCompositionClockSource())
    {
    }

    internal ScrollPresentationCoordinator(ICompositionClockSource clockSource)
    {
        ArgumentNullException.ThrowIfNull(clockSource);

        _clockSource = clockSource;
    }

    public bool IsLoopRunning => Volatile.Read(ref _loopRunning) != 0;
    public double PendingPixels => ReadDouble(ref _pendingPixelsBits);
    public long RetargetCount => Volatile.Read(ref _retargetCount);
    public double LastPresentedScrollY => ReadDouble(ref _lastPresentedScrollYBits);

    public void AddPendingPixels(double pixels)
    {
        long current;
        long updated;
        do
        {
            current = Volatile.Read(ref _pendingPixelsBits);
            var currentDouble = BitConverter.Int64BitsToDouble(current);
            updated = BitConverter.DoubleToInt64Bits(currentDouble + pixels);
        } while (Interlocked.CompareExchange(ref _pendingPixelsBits, updated, current) != current);
    }

    public bool EnsureRunning(
        Runtime<CounterModel, CounterMessage> runtime,
        CompositorLoop compositorLoop,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(compositorLoop);
        ArgumentNullException.ThrowIfNull(translator);
        return EnsureRunning(
            new CounterScrollPresentationRuntimeAdapter(runtime),
            new CompositorLoopScrollPresentationAdapter(compositorLoop),
            new WindowDrawCommandTranslatorRetainedSnapshotProvider(translator),
            scrollTargetKey,
            cancellationToken);
    }

    internal bool EnsureRunning<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
        where TRuntime : IScrollPresentationRuntimeAdapter
        where TCompositor : IScrollPresentationCompositorAdapter
        where TSnapshotProvider : IScrollPresentationRetainedSnapshotProvider
    {
        if (Interlocked.Exchange(ref _loopRunning, 1) != 0)
        {
            return false;
        }

        var runTask = RunAsync(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken, releaseLoopFlag: true);
        _ = runTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    internal Task RunUntilIdleAsync(
        Runtime<CounterModel, CounterMessage> runtime,
        CompositorLoop compositorLoop,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(compositorLoop);
        ArgumentNullException.ThrowIfNull(translator);
        return RunUntilIdleAsync(
            new CounterScrollPresentationRuntimeAdapter(runtime),
            new CompositorLoopScrollPresentationAdapter(compositorLoop),
            new WindowDrawCommandTranslatorRetainedSnapshotProvider(translator),
            scrollTargetKey,
            cancellationToken);
    }

    internal Task RunUntilIdleAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
        where TRuntime : IScrollPresentationRuntimeAdapter
        where TCompositor : IScrollPresentationCompositorAdapter
        where TSnapshotProvider : IScrollPresentationRetainedSnapshotProvider
    {
        if (Interlocked.Exchange(ref _loopRunning, 1) != 0)
        {
            throw new InvalidOperationException("A scroll presentation coordinator is already running.");
        }

        return RunAsync(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken, releaseLoopFlag: true);
    }

    private async Task RunAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken,
        bool releaseLoopFlag)
        where TRuntime : IScrollPresentationRuntimeAdapter
        where TCompositor : IScrollPresentationCompositorAdapter
        where TSnapshotProvider : IScrollPresentationRetainedSnapshotProvider
    {
        try
        {
            var idleTicks = 0;
            while (idleTicks < IdleTickExitThreshold)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pendingPixels = DrainPendingPixels();
                if (pendingPixels != 0)
                {
                    if (!await RetargetAsync(runtime, compositor, snapshotProvider, scrollTargetKey, pendingPixels, cancellationToken))
                    {
                        return;
                    }

                    idleTicks = 0;
                    await Task.Yield();
                    continue;
                }

                idleTicks++;
                await Task.Yield();
            }
        }
        finally
        {
            if (releaseLoopFlag)
            {
                Interlocked.Exchange(ref _loopRunning, 0);
            }

            if (PendingPixels != 0 && !cancellationToken.IsCancellationRequested)
            {
                EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
            }
        }
    }

    private async Task<bool> RetargetAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        NodeKey scrollTargetKey,
        double pendingPixels,
        CancellationToken cancellationToken)
        where TRuntime : IScrollPresentationRuntimeAdapter
        where TCompositor : IScrollPresentationCompositorAdapter
        where TSnapshotProvider : IScrollPresentationRetainedSnapshotProvider
    {
        var state = runtime.CurrentScroll;
        var delta = new ScrollDelta(ScrollDeltaUnit.Pixel, pendingPixels);
        var targetProbe = ScrollController.ResolvePresentationInterrupt(
            state,
            state.Position,
            delta,
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);
        if (!ShouldStartRetargetSegment(state, targetProbe))
        {
            return true;
        }

        var activeSample = await compositor.SampleAndCancelAsync(
            scrollTargetKey,
            cancellationToken);
        var from = activeSample.HasValue ? activeSample.PresentedScrollY : state.Position;
        var decision = ScrollController.ResolvePresentationInterrupt(
            state,
            from,
            delta,
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);
        if (!ShouldStartRetargetSegment(state, decision))
        {
            return true;
        }

        var layoutState = ScrollController.CommitPresented(decision.NextState, decision.NextState.TargetPosition);
        await runtime.DispatchScrollPresentationInterruptedAsync(decision with { NextState = layoutState }, cancellationToken);
        var snapshot = snapshotProvider.LastRetainedInputSnapshot;
        if (snapshot is null)
        {
            return false;
        }

        var retainedScrollY = ScrollController.GetScrollY(runtime.CurrentScroll);
        var segmentStart = _clockSource.TimestampNow();
        var segmentDuration = CompositionDuration.FromMilliseconds(AnimationDurationMs);
        var declaration = new CompositionScrollPresentationDeclaration(
            scrollTargetKey,
            new CompositionAnimationTimeline(segmentStart, segmentDuration),
            new CompositionScalarAnimation((float)from, retainedScrollY, RetargetEasing));
        await compositor.StartAsync(declaration, snapshot, cancellationToken);
        RecordPresented(compositor, scrollTargetKey);
        Interlocked.Increment(ref _retargetCount);
        return true;
    }

    internal static bool ShouldStartRetargetSegment(in ScrollState currentState, in ScrollPresentationInterruptDecision decision)
    {
        return Math.Abs(currentState.TargetPosition - decision.NextState.TargetPosition) > TargetEpsilon;
    }

    private void RecordPresented<TCompositor>(TCompositor compositor, NodeKey scrollTargetKey)
        where TCompositor : IScrollPresentationCompositorAdapter
    {
        if (compositor.TryGetPresentedScrollY(scrollTargetKey, out var presentedScrollY))
        {
            WriteDouble(ref _lastPresentedScrollYBits, presentedScrollY);
        }
    }

    private double DrainPendingPixels()
    {
        var bits = Interlocked.Exchange(ref _pendingPixelsBits, 0);
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static double ReadDouble(ref long bits)
    {
        return BitConverter.Int64BitsToDouble(Volatile.Read(ref bits));
    }

    private static void WriteDouble(ref long bits, double value)
    {
        Interlocked.Exchange(ref bits, BitConverter.DoubleToInt64Bits(value));
    }

}
