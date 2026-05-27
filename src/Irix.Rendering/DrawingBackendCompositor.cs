using System.Diagnostics;
using System.Threading;
using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

/// <summary>
/// Compositor that delegates rendering to an <see cref="IDrawingBackend"/>.
/// Owns a <see cref="RetainedRenderFrame"/> for incremental update and zero-alloc backend reads.
/// Caches hit targets from the frame for input routing.
/// This is the bridge between the RenderFrameBatch world and the IDrawingBackend world.
/// </summary>
public sealed class DrawingBackendCompositor(IDrawingBackend backend) : ICompositor, IDisposable
{
    private readonly IDrawingBackend _backend = backend;
    private readonly DrawingBackendCompositorHandoffOptions _handoffOptions;
    private readonly Lock _hitTargetsLock = new();
    private readonly RetainedRenderFrame _retainedFrame = new();
    private RetainedRenderFrameHandoffHarness? _handoffCandidateHarness;
    private HitTestTarget[] _hitTargets = [];
    private ulong _lastAppliedFrameId;
    private long _renderCount;
    private long _partialApplyCount;
    private long _fullApplyCount;
    private long _emptyFrameCount;
    private DisplayScale _displayScale = DisplayScale.Identity;
    private PixelRectangle _physicalViewport;
    private long _lastFrameTimeTicks;
    private long _totalFrameTimeTicks;
    private long _frameTimeSampleCount;
    private long _maxFrameTimeTicks;
    private CompositionAnimationPlan? _compositionAnimationPlan;
    private CompositionFrame _lastCompositionFrame;
    private CompositionBackendExecutionResult _lastCompositionExecutionResult;
    private long _compositionTickCount;
    private long _lastCompositionTickTimeTicks;
    private long _totalCompositionTickTimeTicks;
    private long _compositionTickTimeSampleCount;
    private long _maxCompositionTickTimeTicks;

    /// <summary>
    /// The dirty command ranges from the last render, if any.
    /// Reflects the actual ranges that were applied to the retained frame
    /// (may differ from the batch's dirty ranges if partial apply was refused).
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges { get; private set; } = [];

    /// <summary>
    /// Whether the last render used partial apply on the retained frame.
    /// </summary>
    public bool LastPartialApplySucceeded { get; private set; }

    /// <summary>
    /// The retained render frame owned by this compositor.
    /// Exposed for diagnostics and test assertions.
    /// </summary>
    internal RetainedRenderFrame RetainedFrame => _retainedFrame;

    internal DrawingBackendCompositorHandoffResult LastHandoffResult { get; private set; } = DrawingBackendCompositorHandoffResult.Disabled;

    internal bool HasHandoffCandidateHarness => _handoffCandidateHarness is not null;

    /// <summary>Total non-empty frames rendered.</summary>
    public long RenderCount => _renderCount;

    /// <summary>Number of renders that used partial apply.</summary>
    public long PartialApplyCount => _partialApplyCount;

    /// <summary>Number of renders that fell back to full apply.</summary>
    public long FullApplyCount => _fullApplyCount;

    /// <summary>Number of empty frames received (commands.Count == 0).</summary>
    public long EmptyFrameCount => _emptyFrameCount;

    /// <summary>Number of compositor-only animation ticks executed over the retained frame.</summary>
    public long CompositionTickCount => Volatile.Read(ref _compositionTickCount);

    public DrawingBackendClipMode BackendClipMode => _backend is IClipScissorCapability capability
        ? capability.ClipMode
        : DrawingBackendClipMode.None;

    internal DrawingBackendCompositor(
        IDrawingBackend backend,
        DrawingBackendCompositorHandoffOptions handoffOptions)
        : this(backend)
    {
        _handoffOptions = handoffOptions;
    }

    public void SetViewport(PixelRectangle physicalViewport, DisplayScale scale)
    {
        _physicalViewport = physicalViewport;
        _displayScale = scale.Normalize();
    }

    public DisplayScale CurrentDisplayScale => _displayScale;

    /// <summary>Elapsed time of the last non-empty render in microseconds.</summary>
    public long LastFrameTimeUs => Volatile.Read(ref _lastFrameTimeTicks) * 1_000_000 / Stopwatch.Frequency;

    /// <summary>Average elapsed time per non-empty render in microseconds.</summary>
    public long AverageFrameTimeUs
    {
        get
        {
            var count = Volatile.Read(ref _frameTimeSampleCount);
            return count > 0 ? Volatile.Read(ref _totalFrameTimeTicks) * 1_000_000 / Stopwatch.Frequency / count : 0;
        }
    }

    /// <summary>Maximum elapsed time of any non-empty render in microseconds.</summary>
    public long MaxFrameTimeUs => Volatile.Read(ref _maxFrameTimeTicks) * 1_000_000 / Stopwatch.Frequency;

    /// <summary>Elapsed time of the last compositor-only animation tick in microseconds.</summary>
    public long LastCompositionTickTimeUs => Volatile.Read(ref _lastCompositionTickTimeTicks) * 1_000_000 / Stopwatch.Frequency;

    /// <summary>Average elapsed time per compositor-only animation tick in microseconds.</summary>
    public long AverageCompositionTickTimeUs
    {
        get
        {
            var count = Volatile.Read(ref _compositionTickTimeSampleCount);
            return count > 0 ? Volatile.Read(ref _totalCompositionTickTimeTicks) * 1_000_000 / Stopwatch.Frequency / count : 0;
        }
    }

    /// <summary>Maximum elapsed time of any compositor-only animation tick in microseconds.</summary>
    public long MaxCompositionTickTimeUs => Volatile.Read(ref _maxCompositionTickTimeTicks) * 1_000_000 / Stopwatch.Frequency;

    internal CompositionFrame LastCompositionFrame => _lastCompositionFrame;

    internal CompositionBackendExecutionResult LastCompositionExecutionResult => _lastCompositionExecutionResult;

    internal CompositionAnimationPlan? CompositionAnimationPlan => _compositionAnimationPlan;

    internal void SetCompositionAnimationPlan(in CompositionAnimationPlan plan)
    {
        if (_retainedFrame.CommandCount > 0 && !plan.IsValidForCommandCount(_retainedFrame.CommandCount))
        {
            throw new ArgumentException("Composition animation plan layer range must fit the retained command frame.", nameof(plan));
        }

        _compositionAnimationPlan = plan;
    }

    internal void ClearCompositionAnimationPlan()
    {
        _compositionAnimationPlan = null;
        _lastCompositionFrame = default;
        _lastCompositionExecutionResult = default;
    }

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        return RenderAsync(renderFrameBatch, null, cancellationToken);
    }

    internal ValueTask RenderAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken = default)
    {
        return RenderAsync(renderFrameBatch, ownership, CreateBackendFrameContext(), cancellationToken);
    }

    internal ValueTask RenderAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        FrameContext frameContext,
        CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            lock (_hitTargetsLock)
            {
                _hitTargets = [];
            }

            Interlocked.Increment(ref _emptyFrameCount);
            _retainedFrame.ReleaseResources();
            _retainedFrame.Invalidate();
            _lastAppliedFrameId = 0;
            ClearCompositionAnimationPlan();
            LastDirtyCommandRanges = renderFrameBatch.DirtyCommandRanges;
            LastPartialApplySucceeded = false;
            LastHandoffResult = ResolveHandoffSelection(renderFrameBatch, ownership).Result;
            return ValueTask.CompletedTask;
        }

        var sw = Stopwatch.StartNew();

        // Cross-frame partial apply guard: only allow partial apply when the batch's
        // resources are the same FrameDrawingResources instance AND same rental cycle
        // (FrameId) as the last apply. This prevents a recycled pooled instance from
        // being misidentified as "same frame scope".
        var batchFrameId = renderFrameBatch.Resources is FrameDrawingResources fdr ? fdr.FrameId : 0ul;
        var isSameFrameScope = batchFrameId != 0 && batchFrameId == _lastAppliedFrameId;

        var retainedPartialApplySucceeded = false;
        if (isSameFrameScope && renderFrameBatch.DirtyCommandRanges.Count > 0)
        {
            retainedPartialApplySucceeded = _retainedFrame.TryApplyPartial(renderFrameBatch);
        }

        if (!retainedPartialApplySucceeded)
        {
            // Release old retained resources before taking new ones
            _retainedFrame.ReleaseResources();
            _retainedFrame.ApplyFull(renderFrameBatch);
            // Take ownership: prevent batch.Dispose() from returning resources to pool
            _retainedFrame.RetainResources();
        }

        _lastAppliedFrameId = batchFrameId;
        LastDirtyCommandRanges = _retainedFrame.DirtyCommandRanges;

        var handoffSelection = ResolveHandoffSelection(renderFrameBatch, ownership);
        if (handoffSelection.Selected)
        {
            LastHandoffResult = ExecuteSelectedHandoffFrame(ownership!, handoffSelection.OwnerResult, frameContext);
            LastPartialApplySucceeded = LastHandoffResult.CandidateResult.Counters.LastPartialApplySucceeded;
            Interlocked.Increment(ref _renderCount);
            if (LastPartialApplySucceeded)
            {
                Interlocked.Increment(ref _partialApplyCount);
            }
            else
            {
                Interlocked.Increment(ref _fullApplyCount);
            }

            RecordFrameTime(sw);

            lock (_hitTargetsLock)
            {
                _hitTargets = ownership!.RuntimeOwner!.HitTargets.ToArray();
            }

            return ValueTask.CompletedTask;
        }

        LastHandoffResult = handoffSelection.Result;
        LastPartialApplySucceeded = retainedPartialApplySucceeded;
        Interlocked.Increment(ref _renderCount);
        if (LastPartialApplySucceeded)
        {
            Interlocked.Increment(ref _partialApplyCount);
        }
        else
        {
            Interlocked.Increment(ref _fullApplyCount);
        }

        // Execute backend from the retained frame (zero-alloc: no ToBatch copy).
        if (_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            // Propagate dirty ranges to backend for diagnostics (read-only).
            if (_backend is IDirtyRangeAware dirtyAware)
            {
                dirtyAware.SetDirtyCommandRanges(LastDirtyCommandRanges);
            }

            try
            {
                _backend.BeginFrame(frameContext);
                _backend.Execute(commands, resources);
                _backend.EndFrame();
            }
            catch (Exception ex)
            {
                // Device-lost: attempt recovery, skip frame if successful.
                if (TryHandleDeviceLost(ex))
                {
                    return ValueTask.CompletedTask;
                }

                throw;
            }
        }

        RecordFrameTime(sw);

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. _retainedFrame.HitTargets];
        }
        return ValueTask.CompletedTask;
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAsync(
        CancellationToken cancellationToken = default)
    {
        return RenderCompositionAnimationTickAtAsync(CompositionTimestamp.Now(), cancellationToken);
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<CompositionBackendExecutionResult>(cancellationToken);
        }

        if (_compositionAnimationPlan is not { } plan)
        {
            throw new InvalidOperationException("A composition animation plan must be set before compositor-only animation ticks can be rendered.");
        }

        if (_backend is not ICompositionDrawingBackend compositionBackend
            || (compositionBackend.CompositionCapabilities & CompositionBackendCapabilities.TransformOpacity) != CompositionBackendCapabilities.TransformOpacity)
        {
            throw new InvalidOperationException("The drawing backend must expose transform/opacity composition execution for compositor-only animation ticks.");
        }

        if (!_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            throw new InvalidOperationException("A retained render frame must exist before compositor-only animation ticks can be rendered.");
        }

        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        var sw = Stopwatch.StartNew();
        var frameContext = CreateBackendFrameContext(timestamp.StopwatchTicks);
        var result = default(CompositionBackendExecutionResult);
        try
        {
            _backend.BeginFrame(frameContext);
            result = compositionBackend.ExecuteComposition(commands, resources, compositionFrame);
            _backend.EndFrame();
        }
        catch (Exception ex)
        {
            if (TryHandleDeviceLost(ex))
            {
                return ValueTask.FromResult(result);
            }

            throw;
        }

        _lastCompositionFrame = compositionFrame;
        _lastCompositionExecutionResult = result;
        Interlocked.Increment(ref _compositionTickCount);
        RecordCompositionTickTime(sw);
        return ValueTask.FromResult(result);
    }

    internal bool TryGetCandidateActionIdAtPhysicalPixel(int x, int y, out ActionId actionId)
    {
        if (_handoffCandidateHarness is null)
        {
            actionId = ActionId.None;
            return false;
        }

        var logicalPoint = ToLogicalPoint(x, y);
        return _handoffCandidateHarness.TryGetActionIdAtLogicalPixel(logicalPoint.X, logicalPoint.Y, out actionId);
    }

    private HandoffSelection ResolveHandoffSelection(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership)
    {
        if (!_handoffOptions.EnableSegmentedRenderSourceCandidate)
        {
            return HandoffSelection.Fallback(SegmentedRetainedFrameProductionOwnerFeedResult.Disabled, DrawingBackendCompositorHandoffResult.Disabled);
        }

        var ownerResult = ownership?.LastResult ?? SegmentedRetainedFrameProductionOwnerFeedResult.Disabled;
        if (ownership?.RuntimeOwner is null)
        {
            return HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.MissingOwner(ownerResult));
        }

        if (!IsOwnerResultFresh(ownerResult, renderFrameBatch))
        {
            return HandoffSelection.Fallback(
                ownerResult,
                DrawingBackendCompositorHandoffResult.Rejected(ownerResult, DrawingBackendCompositorHandoffReason.StaleOwner));
        }

        if (ownerResult.Kind == SegmentedRetainedFrameShadowResultKind.ShadowRejected
            || ownerResult.ShadowResult.PlanKind == RetainedPartialApplyResultKind.Rejected)
        {
            return HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.Rejected(ownerResult));
        }

        if (ownerResult.Kind == SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull)
        {
            return HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.FallbackFull(ownerResult));
        }

        if (ownerResult.Kind != SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial)
        {
            return HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.Rejected(ownerResult));
        }

        if (!RangeUtils.TryNormalizeStrict(LastDirtyCommandRanges, renderFrameBatch.Commands.Count, out _))
        {
            return HandoffSelection.Fallback(
                ownerResult,
                DrawingBackendCompositorHandoffResult.Rejected(ownerResult, DrawingBackendCompositorHandoffReason.DirtyRangeMismatch));
        }

        if (!TryValidateSelectedRuntimeOwner(ownership.RuntimeOwner, ownerResult, renderFrameBatch, out var reason))
        {
            return reason == DrawingBackendCompositorHandoffReason.EmptySegmentRead
                ? HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.FallbackFull(ownerResult, reason))
                : HandoffSelection.Fallback(ownerResult, DrawingBackendCompositorHandoffResult.Rejected(ownerResult, reason));
        }

        return HandoffSelection.SelectedCandidate(ownerResult);
    }

    private DrawingBackendCompositorHandoffResult ExecuteSelectedHandoffFrame(
        RetainedRenderFrameSegmentOwnership ownership,
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        in FrameContext frameContext)
    {
        var harness = _handoffCandidateHarness ??= new RetainedRenderFrameHandoffHarness(new NonDisposingBackend(_backend), RetainedRenderFrameHandoffHarnessOptions.Enabled);
        try
        {
            return MapCandidateResult(ownerResult, harness.ExecuteCandidateFrame(ownership, frameContext, LastDirtyCommandRanges));
        }
        catch (Exception ex) when (TryHandleDeviceLost(ex))
        {
            return DrawingBackendCompositorHandoffResult.Executed(
                ownerResult,
                harness.LastResult,
                DrawingBackendCompositorHandoffReason.BackendThrewBeforeCommit);
        }
        catch
        {
            LastHandoffResult = DrawingBackendCompositorHandoffResult.Executed(
                ownerResult,
                harness.LastResult,
                DrawingBackendCompositorHandoffReason.BackendThrewBeforeCommit);
            throw;
        }
    }

    private readonly struct HandoffSelection(
        bool Selected,
        SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult,
        DrawingBackendCompositorHandoffResult Result) : IEquatable<HandoffSelection>
    {
        public bool Selected { get; } = Selected;
        public SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult { get; } = OwnerResult;
        public DrawingBackendCompositorHandoffResult Result { get; } = Result;

        public static HandoffSelection SelectedCandidate(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
        {
            return new HandoffSelection(true, ownerResult, DrawingBackendCompositorHandoffResult.Disabled);
        }

        public static HandoffSelection Fallback(
            SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
            DrawingBackendCompositorHandoffResult result)
        {
            return new HandoffSelection(false, ownerResult, result);
        }

        public bool Equals(HandoffSelection other)
        {
            return Selected == other.Selected
                && OwnerResult == other.OwnerResult
                && Result == other.Result;
        }

        public override bool Equals(object? obj) => obj is HandoffSelection other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Selected, OwnerResult, Result);

        public static bool operator ==(HandoffSelection left, HandoffSelection right) => left.Equals(right);

        public static bool operator !=(HandoffSelection left, HandoffSelection right) => !left.Equals(right);
    }

    private sealed class NonDisposingBackend(IDrawingBackend inner) : IDrawingBackend, IDirtyRangeAware
    {
        public void BeginFrame(in FrameContext frameContext)
        {
            inner.BeginFrame(frameContext);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            inner.Execute(commands, resources);
        }

        public void EndFrame()
        {
            inner.EndFrame();
        }

        public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
        {
            if (inner is IDirtyRangeAware dirtyRangeAware)
            {
                dirtyRangeAware.SetDirtyCommandRanges(ranges);
            }
        }

        public void Dispose()
        {
        }
    }

    private static DrawingBackendCompositorHandoffResult MapCandidateResult(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RetainedRenderFrameHandoffHarnessResult candidateResult)
    {
        return candidateResult.Kind switch
        {
            RetainedRenderFrameHandoffHarnessResultKind.Disabled => DrawingBackendCompositorHandoffResult.Disabled,
            RetainedRenderFrameHandoffHarnessResultKind.MissingSegmentedOwner => DrawingBackendCompositorHandoffResult.MissingOwner(ownerResult),
            RetainedRenderFrameHandoffHarnessResultKind.EmptyFrame => DrawingBackendCompositorHandoffResult.FallbackFull(ownerResult, DrawingBackendCompositorHandoffReason.EmptySegmentRead),
            _ => DrawingBackendCompositorHandoffResult.Executed(ownerResult, candidateResult)
        };
    }

    private static bool IsOwnerResultFresh(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult, RenderFrameBatch renderFrameBatch)
    {
        return ownerResult.RuntimeOwnerEnabled
            && ReferenceEquals(ownerResult.BatchResources, renderFrameBatch.Resources)
            && ReferenceEquals(ownerResult.BatchCommandOwner, renderFrameBatch.Commands.Owner)
            && ownerResult.BatchCommandCount == renderFrameBatch.Commands.Count
            && ownerResult.BatchFrameId == GetBatchFrameId(renderFrameBatch);
    }

    private static ulong GetBatchFrameId(RenderFrameBatch renderFrameBatch)
    {
        return renderFrameBatch.Resources is FrameDrawingResources frameResources ? frameResources.FrameId : 0;
    }

    private static bool TryValidateSelectedRuntimeOwner(
        SegmentedRetainedFrameRuntimeOwner runtimeOwner,
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RenderFrameBatch renderFrameBatch,
        out DrawingBackendCompositorHandoffReason reason)
    {
        reason = DrawingBackendCompositorHandoffReason.None;
        IReadOnlyList<SegmentedFrameRead> runtimeReads;
        try
        {
            runtimeReads = runtimeOwner.ReadSegments();
        }
        catch (InvalidOperationException)
        {
            reason = DrawingBackendCompositorHandoffReason.MalformedSegmentCoverage;
            return false;
        }

        if (runtimeReads.Count == 0 || ownerResult.ShadowResult.Reads.Count == 0)
        {
            reason = DrawingBackendCompositorHandoffReason.EmptySegmentRead;
            return false;
        }

        if (!ReadsEqual(ownerResult.ShadowResult.Reads, runtimeReads)
            || !TryValidateSegmentCoverage(runtimeReads, runtimeOwner.ResourceSegments, renderFrameBatch.Commands.Count))
        {
            reason = DrawingBackendCompositorHandoffReason.MalformedSegmentCoverage;
            return false;
        }

        return true;
    }

    private static bool TryValidateSegmentCoverage(
        IReadOnlyList<SegmentedFrameRead> reads,
        IReadOnlyList<RetainedResourceSegment> resourceSegments,
        int commandCount)
    {
        if (reads.Count == 0 || reads.Count != resourceSegments.Count)
        {
            return false;
        }

        var cursor = 0;
        for (var i = 0; i < reads.Count; i++)
        {
            var read = reads[i];
            var segment = resourceSegments[i];
            if (read.CommandStart != cursor
                || segment.CommandStart != cursor
                || read.Commands.Length <= 0
                || segment.CommandCount != read.Commands.Length
                || !ReferenceEquals(segment.Snapshot.Resolver, read.Resolver))
            {
                return false;
            }

            cursor += read.Commands.Length;
            if (cursor > commandCount)
            {
                return false;
            }
        }

        return cursor == commandCount;
    }

    private static bool ReadsEqual(IReadOnlyList<SegmentedFrameRead> left, IReadOnlyList<SegmentedFrameRead> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].CommandStart != right[i].CommandStart
                || !ReferenceEquals(left[i].Resolver, right[i].Resolver)
                || left[i].Commands.Length != right[i].Commands.Length)
            {
                return false;
            }

            for (var commandIndex = 0; commandIndex < left[i].Commands.Length; commandIndex++)
            {
                if (left[i].Commands[commandIndex] != right[i].Commands[commandIndex])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Input-facing hit test API. Coordinates are physical pixels from platform input.
    /// </summary>
    public bool TryGetActionIdAtPhysicalPixel(int x, int y, out ActionId actionId)
    {
        var logicalPoint = ToLogicalPoint(x, y);
        return TryGetActionIdAtLogicalPixel(logicalPoint.X, logicalPoint.Y, out actionId);
    }

    /// <summary>
    /// Internal/test hit test API for retained logical hit targets. Do not use for platform input.
    /// </summary>
    internal bool TryGetActionIdAtLogicalPixel(int x, int y, out ActionId actionId)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (x < hitTarget.Bounds.X
                    || y < hitTarget.Bounds.Y
                    || x >= hitTarget.Bounds.X + hitTarget.Bounds.Width
                    || y >= hitTarget.Bounds.Y + hitTarget.Bounds.Height)
                {
                    continue;
                }

                if (hitTarget.ClipBounds.Width > 0 && hitTarget.ClipBounds.Height > 0)
                {
                    if (x < hitTarget.ClipBounds.X
                        || y < hitTarget.ClipBounds.Y
                        || x >= hitTarget.ClipBounds.X + hitTarget.ClipBounds.Width
                        || y >= hitTarget.ClipBounds.Y + hitTarget.ClipBounds.Height)
                    {
                        continue;
                    }
                }

                actionId = hitTarget.ActionId;
                return true;
            }
        }

        actionId = ActionId.None;
        return false;
    }

    private FrameContext CreateBackendFrameContext(long timestamp = 0)
    {
        return new FrameContext(_physicalViewport.Width, _physicalViewport.Height, _displayScale, timestamp);
    }

    private (int X, int Y) ToLogicalPoint(int physicalX, int physicalY)
    {
        if (_displayScale.IsIdentity)
        {
            return (physicalX, physicalY);
        }

        return (
            (int)(physicalX / _displayScale.ScaleX),
            (int)(physicalY / _displayScale.ScaleY));
    }

    private void RecordFrameTime(Stopwatch sw)
    {
        sw.Stop();
        var ticks = sw.ElapsedTicks;
        Volatile.Write(ref _lastFrameTimeTicks, ticks);
        Interlocked.Add(ref _totalFrameTimeTicks, ticks);
        Interlocked.Increment(ref _frameTimeSampleCount);

        // Update max (non-atomic but acceptable for diagnostics)
        var currentMax = Volatile.Read(ref _maxFrameTimeTicks);
        if (ticks > currentMax)
        {
            Volatile.Write(ref _maxFrameTimeTicks, ticks);
        }
    }

    private void RecordCompositionTickTime(Stopwatch sw)
    {
        sw.Stop();
        var ticks = sw.ElapsedTicks;
        Volatile.Write(ref _lastCompositionTickTimeTicks, ticks);
        Interlocked.Add(ref _totalCompositionTickTimeTicks, ticks);
        Interlocked.Increment(ref _compositionTickTimeSampleCount);

        var currentMax = Volatile.Read(ref _maxCompositionTickTimeTicks);
        if (ticks > currentMax)
        {
            Volatile.Write(ref _maxCompositionTickTimeTicks, ticks);
        }
    }

    /// <summary>
    /// Exception filter: returns true if the backend is device-removed and recovery succeeds.
    /// When true, the exception is swallowed and the frame is skipped.
    /// </summary>
    private bool TryHandleDeviceLost(Exception ex)
    {
        if (_backend is not IDeviceRecovery recovery || !recovery.IsDeviceRemoved)
        {
            return false;
        }

        System.Diagnostics.Debug.WriteLine($"[DrawingBackendCompositor] Device-lost detected: {ex.Message}. Attempting recovery...");
        if (recovery.TryRecover())
        {
            System.Diagnostics.Debug.WriteLine("[DrawingBackendCompositor] Recovery succeeded. Skipping frame.");
            return true;
        }

        System.Diagnostics.Debug.WriteLine("[DrawingBackendCompositor] Recovery failed. Propagating exception.");
        return false;
    }

    public void Dispose()
    {
        _handoffCandidateHarness?.Dispose();
        _retainedFrame.ReleaseResources();
        _retainedFrame.Dispose();
        _backend.Dispose();
    }
}
