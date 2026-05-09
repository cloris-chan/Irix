using System.Diagnostics;

namespace Irix.Poc;

internal sealed class ScrollFramePump
{
    private const int IdleFrameExitThreshold = 3;

    private int _loopRunning;
    private int _scrollFrameQueued;
    private long _pendingScrollDeltaBits;
    private long _dispatchedFrameCount;
    private long _renderWaitMsBits;
    private long _lastDtBits;
    private long _drainedPixelsBits;

    public double PendingPixels
    {
        get
        {
            var bits = Volatile.Read(ref _pendingScrollDeltaBits);
            return BitConverter.Int64BitsToDouble(bits);
        }
    }

    public bool IsFrameQueued => Volatile.Read(ref _scrollFrameQueued) != 0;

    public bool IsLoopRunning => Volatile.Read(ref _loopRunning) != 0;

    public long DispatchedFrameCount => Volatile.Read(ref _dispatchedFrameCount);

    public double RenderWaitMs => ReadDouble(ref _renderWaitMsBits);

    public double LastDt => ReadDouble(ref _lastDtBits);

    public double DrainedPixels => ReadDouble(ref _drainedPixelsBits);

    public void AddPendingPixels(double pixels)
    {
        long current;
        long updated;
        do
        {
            current = Volatile.Read(ref _pendingScrollDeltaBits);
            var currentDouble = BitConverter.Int64BitsToDouble(current);
            var newDouble = currentDouble + pixels;
            updated = BitConverter.DoubleToInt64Bits(newDouble);
        } while (Interlocked.CompareExchange(ref _pendingScrollDeltaBits, updated, current) != current);
    }

    public void EnsureRunning(
        Func<CounterMessage.ScrollFrame, CancellationToken, Task> dispatchFrameAsync,
        Func<ScrollState> getScrollState,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _loopRunning, 1) != 0)
        {
            return;
        }

        var runTask = RunAsync(
            dispatchFrameAsync,
            getScrollState,
            restartOnLatePending: true,
            cancellationToken);
        _ = runTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal Task RunUntilIdleAsync(
        Func<CounterMessage.ScrollFrame, CancellationToken, Task> dispatchFrameAsync,
        Func<ScrollState> getScrollState,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            dispatchFrameAsync,
            getScrollState,
            restartOnLatePending: false,
            cancellationToken);
    }

    private async Task RunAsync(
        Func<CounterMessage.ScrollFrame, CancellationToken, Task> dispatchFrameAsync,
        Func<ScrollState> getScrollState,
        bool restartOnLatePending,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var lastFrameDispatchedAt = stopwatch.Elapsed;
            var isFirstFrame = true;
            var consecutiveIdleFrames = 0;

            while (consecutiveIdleFrames < IdleFrameExitThreshold)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pendingPixels = DrainPendingPixels();
                var scrollState = getScrollState();
                if (pendingPixels == 0 && !scrollState.IsAnimating)
                {
                    consecutiveIdleFrames++;
                    if (consecutiveIdleFrames >= IdleFrameExitThreshold)
                    {
                        break;
                    }

                    await Task.Yield();
                    continue;
                }

                consecutiveIdleFrames = 0;

                var now = stopwatch.Elapsed;
                var deltaTime = isFirstFrame
                    ? 0
                    : Math.Max(0, (now - lastFrameDispatchedAt).TotalSeconds);
                isFirstFrame = false;
                lastFrameDispatchedAt = now;
                WriteDouble(ref _lastDtBits, deltaTime);
                WriteDouble(ref _drainedPixelsBits, pendingPixels);

                Volatile.Write(ref _scrollFrameQueued, 1);
                Interlocked.Increment(ref _dispatchedFrameCount);
                var renderWaitStartedAt = stopwatch.Elapsed;
                try
                {
                    var frame = new CounterMessage.ScrollFrame(
                        new ScrollDelta(ScrollDeltaUnit.Pixel, pendingPixels),
                        deltaTime);
                    await dispatchFrameAsync(frame, cancellationToken);
                }
                finally
                {
                    WriteDouble(ref _renderWaitMsBits, (stopwatch.Elapsed - renderWaitStartedAt).TotalMilliseconds);
                    Volatile.Write(ref _scrollFrameQueued, 0);
                }
                consecutiveIdleFrames = PendingPixels == 0 && !getScrollState().IsAnimating
                    ? consecutiveIdleFrames + 1
                    : 0;
            }
        }
        finally
        {
            Volatile.Write(ref _scrollFrameQueued, 0);
            Interlocked.Exchange(ref _loopRunning, 0);

            if (restartOnLatePending && PendingPixels != 0 && !cancellationToken.IsCancellationRequested)
            {
                EnsureRunning(dispatchFrameAsync, getScrollState, cancellationToken);
            }
        }
    }

    private double DrainPendingPixels()
    {
        var bits = Interlocked.Exchange(ref _pendingScrollDeltaBits, 0);
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