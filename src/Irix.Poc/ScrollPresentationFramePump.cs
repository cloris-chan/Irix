using Irix.Rendering;

namespace Irix.Poc;

internal sealed class ScrollPresentationFramePump
{
    private const int AnimationDurationMs = 160;
    private const int FrameDelayMs = 8;
    private const int IdleTickExitThreshold = 2;

    private int _loopRunning;
    private long _pendingPixelsBits;
    private long _compositionTickCount;
    private long _retargetCount;
    private long _lastPresentedScrollYBits;

    public bool IsLoopRunning => Volatile.Read(ref _loopRunning) != 0;
    public double PendingPixels => ReadDouble(ref _pendingPixelsBits);
    public long CompositionTickCount => Volatile.Read(ref _compositionTickCount);
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
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(translator);
        if (Interlocked.Exchange(ref _loopRunning, 1) != 0)
        {
            return false;
        }

        var runTask = RunAsync(runtime, compositor, translator, scrollTargetKey, cancellationToken, releaseLoopFlag: true);
        _ = runTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    internal Task RunUntilIdleAsync(
        Runtime<CounterModel, CounterMessage> runtime,
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(translator);
        if (Interlocked.Exchange(ref _loopRunning, 1) != 0)
        {
            throw new InvalidOperationException("A scroll presentation frame pump is already running.");
        }

        return RunAsync(runtime, compositor, translator, scrollTargetKey, cancellationToken, releaseLoopFlag: true);
    }

    private async Task RunAsync(
        Runtime<CounterModel, CounterMessage> runtime,
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        CancellationToken cancellationToken,
        bool releaseLoopFlag)
    {
        var hasActiveSegment = false;
        var segmentStart = CompositionTimestamp.Zero;
        var segmentDuration = CompositionDuration.Zero;
        try
        {
            var idleTicks = 0;
            while (idleTicks < IdleTickExitThreshold)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pendingPixels = DrainPendingPixels();
                if (pendingPixels != 0)
                {
                    var retarget = await RetargetAsync(runtime, compositor, translator, scrollTargetKey, pendingPixels, cancellationToken);
                    if (!retarget.Succeeded)
                    {
                        return;
                    }

                    hasActiveSegment = true;
                    segmentStart = retarget.SegmentStart;
                    segmentDuration = retarget.SegmentDuration;
                    idleTicks = 0;
                }

                if (hasActiveSegment)
                {
                    var now = CompositionTimestamp.Now();
                    var end = segmentStart + segmentDuration;
                    if (now > end)
                    {
                        now = end;
                    }

                    _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(now, cancellationToken);
                    Interlocked.Increment(ref _compositionTickCount);
                    RecordPresented(compositor, scrollTargetKey);
                    if (now >= end)
                    {
                        hasActiveSegment = false;
                    }

                    await Task.Delay(FrameDelayMs, cancellationToken);
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
                EnsureRunning(runtime, compositor, translator, scrollTargetKey, cancellationToken);
            }
        }
    }

    private async Task<ScrollPresentationRetargetResult> RetargetAsync(
        Runtime<CounterModel, CounterMessage> runtime,
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        NodeKey scrollTargetKey,
        double pendingPixels,
        CancellationToken cancellationToken)
    {
        var state = runtime.CurrentModel.Scroll;
        var from = ResolvePresentedOrPosition(compositor, scrollTargetKey, state);
        var decision = ScrollPresentationInputBridge.TryResolveWheelRetarget(
            compositor,
            scrollTargetKey,
            state,
            pendingPixels,
            out var inputDecision)
            ? inputDecision.Interrupt
            : ScrollController.ResolvePresentationInterrupt(
                state,
                from,
                new ScrollDelta(ScrollDeltaUnit.Pixel, pendingPixels),
                ScrollMetrics.DefaultText,
                SystemScrollSettings.Default,
                ScrollPresentationInterruptPolicy.RetargetFromPresented);
        var layoutState = ScrollController.CommitPresented(decision.NextState, decision.NextState.TargetPosition);
        await runtime.DispatchAndWaitAsync(new CounterMessage.ScrollPresentationInterrupted(decision with { NextState = layoutState }), cancellationToken);
        var snapshot = translator.LastRetainedInputSnapshot;
        if (snapshot is null)
        {
            return default;
        }

        var retainedScrollY = ScrollController.GetScrollY(runtime.CurrentModel.Scroll);
        var segmentStart = CompositionTimestamp.Now();
        var segmentDuration = CompositionDuration.FromMilliseconds(AnimationDurationMs);
        var declaration = new CompositionScrollPresentationDeclaration(
            scrollTargetKey,
            new CompositionAnimationTimeline(segmentStart, segmentDuration),
            new CompositionScalarAnimation((float)from, retainedScrollY, CompositionAnimationEasing.SineInOut));
        compositor.SetCompositionScrollPresentationDeclaration(declaration, snapshot);
        Interlocked.Increment(ref _retargetCount);
        return new ScrollPresentationRetargetResult(true, segmentStart, segmentDuration);
    }

    private void RecordPresented(DrawingBackendCompositor compositor, NodeKey scrollTargetKey)
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

    private static double ResolvePresentedOrPosition(
        DrawingBackendCompositor compositor,
        NodeKey scrollTargetKey,
        ScrollState state)
    {
        return compositor.TryGetPresentedScrollY(scrollTargetKey, out var presentedScrollY)
            ? presentedScrollY
            : state.Position;
    }

    private static double ReadDouble(ref long bits)
    {
        return BitConverter.Int64BitsToDouble(Volatile.Read(ref bits));
    }

    private static void WriteDouble(ref long bits, double value)
    {
        Interlocked.Exchange(ref bits, BitConverter.DoubleToInt64Bits(value));
    }

    private readonly struct ScrollPresentationRetargetResult(
        bool Succeeded,
        CompositionTimestamp SegmentStart,
        CompositionDuration SegmentDuration) : IEquatable<ScrollPresentationRetargetResult>
    {
        public bool Succeeded { get; } = Succeeded;
        public CompositionTimestamp SegmentStart { get; } = SegmentStart;
        public CompositionDuration SegmentDuration { get; } = SegmentDuration;

        public bool Equals(ScrollPresentationRetargetResult other)
        {
            return Succeeded == other.Succeeded
                && SegmentStart == other.SegmentStart
                && SegmentDuration == other.SegmentDuration;
        }

        public override bool Equals(object? obj) => obj is ScrollPresentationRetargetResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Succeeded, SegmentStart, SegmentDuration);

        public static bool operator ==(ScrollPresentationRetargetResult left, ScrollPresentationRetargetResult right) => left.Equals(right);

        public static bool operator !=(ScrollPresentationRetargetResult left, ScrollPresentationRetargetResult right) => !left.Equals(right);
    }
}
