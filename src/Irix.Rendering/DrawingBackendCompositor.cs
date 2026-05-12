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
        _displayScale = scale;
    }

    public DisplayScale CurrentDisplayScale => _displayScale;

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        return RenderAsync(renderFrameBatch, null, new FrameContext(0, 0), cancellationToken);
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
            LastDirtyCommandRanges = renderFrameBatch.DirtyCommandRanges;
            LastPartialApplySucceeded = false;
            LastHandoffResult = ResolveHandoffSelection(renderFrameBatch, ownership).Result;
            return ValueTask.CompletedTask;
        }

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

            var backendFrameContext = new FrameContext(_physicalViewport.Width, _physicalViewport.Height, _displayScale);
            _backend.BeginFrame(backendFrameContext);
            try
            {
                _backend.Execute(commands, resources);
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

            _backend.EndFrame();
        }

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. _retainedFrame.HitTargets];
        }
        return ValueTask.CompletedTask;
    }

    internal bool TryGetCandidateActionIdAt(int x, int y, out string actionId)
    {
        if (_handoffCandidateHarness is null)
        {
            actionId = string.Empty;
            return false;
        }

        return _handoffCandidateHarness.TryGetActionIdAt(x, y, out actionId);
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

    private readonly record struct HandoffSelection(
        bool Selected,
        SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult,
        DrawingBackendCompositorHandoffResult Result)
    {
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

    public bool TryGetActionIdAt(int x, int y, out string actionId)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                // Check bounds
                if (x < hitTarget.Bounds.X
                    || y < hitTarget.Bounds.Y
                    || x >= hitTarget.Bounds.X + hitTarget.Bounds.Width
                    || y >= hitTarget.Bounds.Y + hitTarget.Bounds.Height)
                {
                    continue;
                }

                // Check clip bounds (if set): reject hits outside the clip region
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

        actionId = string.Empty;
        return false;
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
