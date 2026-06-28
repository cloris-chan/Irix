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
public sealed partial class DrawingBackendCompositor(IDrawingBackend backend) : ICompositor, IRetainedFrameStagingCompositor, ICompositionScrollPresentationCompositor, ICompositionAnimationCompositor, ICompositionFramePacingProvider, IDisposable
{
    private readonly IDrawingBackend _backend = backend;
    private readonly DrawingBackendCompositorHandoffOptions _handoffOptions;
    private readonly ICompositionClockSource _clockSource = new SystemCompositionClockSource();
    private readonly Lock _frameGate = new();
    private readonly Lock _hitTargetsLock = new();
    private readonly Lock _compositionStateLock = new();
    private readonly Lock _compositionMarkerLock = new();
    private readonly RetainedRenderFrame _retainedFrame = new();
    private RetainedRenderFrameHandoffHarness? _handoffCandidateHarness;
    private HitTestTarget[] _hitTargets = [];
    private CompositorHitTestSnapshot _hitTestSnapshot;
    private ulong _lastAppliedFrameId;
    private long _renderCount;
    private long _partialApplyCount;
    private long _fullApplyCount;
    private long _emptyFrameCount;
    private long _retainedStageCount;
    private DisplayScale _displayScale = DisplayScale.Identity;
    private PixelRectangle _physicalViewport;
    private long _lastFrameTimeTicks;
    private long _totalFrameTimeTicks;
    private long _frameTimeSampleCount;
    private long _maxFrameTimeTicks;
    private readonly CompositionPresentationState _compositionPresentationState = new();
    private CompositionFrame _lastCompositionFrame;
    private CompositionBackendExecutionResult _lastCompositionExecutionResult;
    private readonly List<CompositionAnimationMarkerEvent> _compositionMarkerEvents = [];
    private IndexRangeList _lastDirtyCommandRangeList;
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
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges => _lastDirtyCommandRangeList;

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

    /// <summary>Number of retained frames staged without a regular backend present.</summary>
    public long RetainedStageCount => Volatile.Read(ref _retainedStageCount);

    /// <summary>Number of compositor-only animation ticks executed over the retained frame.</summary>
    public long CompositionTickCount => Volatile.Read(ref _compositionTickCount);

    public DrawingBackendClipMode BackendClipMode => _backend is IClipScissorCapability capability
        ? capability.ClipMode
        : DrawingBackendClipMode.None;

    internal CompositionFramePacing FramePacing => _backend is ICompositionDrawingBackend compositionBackend
        ? compositionBackend.FramePacing
        : CompositionFramePacing.SoftwareTimer;

    CompositionFramePacing ICompositionFramePacingProvider.FramePacing => FramePacing;

    internal DrawingBackendCompositor(
        IDrawingBackend backend,
        DrawingBackendCompositorHandoffOptions handoffOptions)
        : this(backend, handoffOptions, new SystemCompositionClockSource())
    {
    }

    internal DrawingBackendCompositor(
        IDrawingBackend backend,
        ICompositionClockSource clockSource)
        : this(backend)
    {
        ArgumentNullException.ThrowIfNull(clockSource);

        _clockSource = clockSource;
    }

    internal DrawingBackendCompositor(
        IDrawingBackend backend,
        DrawingBackendCompositorHandoffOptions handoffOptions,
        ICompositionClockSource clockSource)
        : this(backend, clockSource)
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

    internal CompositionFrame LastCompositionFrame
    {
        get
        {
            lock (_compositionStateLock)
            {
                return _lastCompositionFrame;
            }
        }
    }

    internal CompositionBackendExecutionResult LastCompositionExecutionResult
    {
        get
        {
            lock (_compositionStateLock)
            {
                return _lastCompositionExecutionResult;
            }
        }
    }

    internal CompositionAnimationPlan? CompositionAnimationPlan => _compositionPresentationState.AnimationPlan;

    internal CompositionAnimationPresentationSetPlan? CompositionAnimationPresentationPlan => _compositionPresentationState.AnimationPresentationPlan;

    internal CompositionScrollPresentationPlan? CompositionScrollPresentationPlan => _compositionPresentationState.ScrollPresentationPlan;

    internal bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
    {
        presentedScrollY = 0;
        if (targetKey == NodeKey.None || _compositionPresentationState.ScrollPresentationPlan is not { } plan)
        {
            return false;
        }

        var layerAnimation = plan.LayerAnimation;
        if (layerAnimation.TargetKey != targetKey)
        {
            return false;
        }

        CompositionFrame activeFrame;
        lock (_compositionStateLock)
        {
            activeFrame = _lastCompositionFrame;
        }

        for (var i = 0; i < activeFrame.LayerCount; i++)
        {
            var layer = activeFrame.GetLayer(i);
            if (layer.Id == layerAnimation.LayerId)
            {
                presentedScrollY = layerAnimation.RetainedScrollY - layer.Transform.TranslateY;
                return double.IsFinite(presentedScrollY);
            }
        }

        return false;
    }

    bool ICompositionScrollPresentationCompositor.TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY) => TryGetPresentedScrollY(targetKey, out presentedScrollY);

    internal int PendingCompositionMarkerEventCount
    {
        get
        {
            lock (_compositionMarkerLock)
            {
                return _compositionMarkerEvents.Count;
            }
        }
    }

    internal int DrainCompositionMarkerEvents(Span<CompositionAnimationMarkerEvent> destination)
    {
        lock (_compositionMarkerLock)
        {
            var count = Math.Min(destination.Length, _compositionMarkerEvents.Count);
            for (var i = 0; i < count; i++)
            {
                destination[i] = _compositionMarkerEvents[i];
            }

            if (count == _compositionMarkerEvents.Count)
            {
                _compositionMarkerEvents.Clear();
            }
            else if (count > 0)
            {
                _compositionMarkerEvents.RemoveRange(0, count);
            }

            return count;
        }
    }

    partial void RecordCompositionExecutionSkipped(
        byte kind,
        byte reason,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount,
        int commandCount);

    partial void RecordCompositionExecutionCompleted(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount,
        int commandCount);

    private const byte CompositionExecutionKindTransformOpacityTick = 1;
    private const byte CompositionExecutionKindScrollPresentationTick = 2;
    private const byte CompositionExecutionKindRetainedUpdateScrollPresentation = 3;
    private const byte CompositionExecutionKindAnimationPresentationTick = 4;

    private const byte CompositionExecutionSkipReasonNoActivePlan = 1;
    private const byte CompositionExecutionSkipReasonBackendDoesNotImplementComposition = 2;
    private const byte CompositionExecutionSkipReasonMissingBackendCapability = 3;
    private const byte CompositionExecutionSkipReasonMissingRetainedFrame = 4;
    private const byte CompositionExecutionSkipReasonInvalidPlanForRetainedFrame = 5;
    private const byte CompositionExecutionSkipReasonDeviceLostRecovered = 6;

    internal void SetCompositionAnimationPlan(in CompositionAnimationPlan plan)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount > 0 && !plan.IsValidForCommandCount(_retainedFrame.CommandCount))
            {
                throw new ArgumentException("Composition animation plan layer range must fit the retained command frame.", nameof(plan));
            }

            _compositionPresentationState.SetAnimationPlan(plan);
            ClearCompositionPresentationState();
        }
    }

    internal void SetCompositionAnimationDeclaration(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount <= 0)
            {
                throw new InvalidOperationException("A retained render frame must exist before installing a composition animation declaration.");
            }

            if (snapshot.CommandCount != _retainedFrame.CommandCount)
            {
                throw new ArgumentException("Composition animation declaration snapshot must match the retained command frame.", nameof(snapshot));
            }

            if (!declaration.TryResolve(snapshot, _retainedFrame.CommandCount, out var plan))
            {
                throw new ArgumentException("Composition animation declaration target must resolve to a retained command range.", nameof(declaration));
            }

            _compositionPresentationState.SetAnimationPlan(plan);
            ClearCompositionPresentationState();
        }
    }

    void ICompositionAnimationCompositor.SetCompositionAnimationDeclaration(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        SetCompositionAnimationDeclaration(declaration, snapshot);
    }

    internal CompositionAnimationPresentationSetValidationResult ValidateCompositionAnimationPresentationSet(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        RenderPipelineRetainedInputSnapshot? snapshot)
    {
        lock (_frameGate)
        {
            return CompositionAnimationPresentationSetValidator.Validate(
                declarations,
                snapshot,
                _retainedFrame.CommandCount);
        }
    }

    internal CompositionAnimationPresentationSetActivationPreflightResult PrepareCompositionAnimationPresentationSetActivation(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        RenderPipelineRetainedInputSnapshot? snapshot)
    {
        lock (_frameGate)
        {
            return CompositionAnimationPresentationSetActivationPreflight.Prepare(
                declarations,
                snapshot,
                _retainedFrame.CommandCount);
        }
    }

    internal void ActivateCompositionAnimationPresentationPlan(in CompositionAnimationPresentationSetPlan plan)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount <= 0)
            {
                throw new InvalidOperationException("A retained render frame must exist before activating a composition animation presentation plan.");
            }

            if (plan.IsEmpty || !plan.IsValidForCommandCount(_retainedFrame.CommandCount))
            {
                throw new ArgumentException("Composition animation presentation plan ranges must fit the retained command frame and must not overlap.", nameof(plan));
            }

            _compositionPresentationState.SetAnimationPresentationPlan(plan);
            ClearCompositionPresentationState();
        }
    }

    internal int ClearCompositionAnimationPresentationTargets(ReadOnlySpan<NodeKey> targetKeys)
    {
        lock (_frameGate)
        {
            if (targetKeys.IsEmpty || _compositionPresentationState.AnimationPresentationPlan is not { } plan)
            {
                return 0;
            }

            if (!plan.TryRemoveTargets(targetKeys, out var remainingPlan, out var removedCount))
            {
                return 0;
            }

            if (remainingPlan.IsEmpty)
            {
                _compositionPresentationState.ClearAnimationPresentationPlan();
                ClearCompositionPresentationState();
                ClearCompositionMarkerEvents();
                return removedCount;
            }

            _compositionPresentationState.RemoveAnimationPresentationTargets(plan, targetKeys, remainingPlan);
            ClearCompositionPresentationFrameState();
            ClearCompositionMarkerEvents(targetKeys);
            return removedCount;
        }
    }

    internal void ClearCompositionAnimation()
    {
        lock (_frameGate)
        {
            if (!_compositionPresentationState.HasAnimation)
            {
                return;
            }

            _compositionPresentationState.ClearAnimation();
            ClearCompositionPresentationState();
            ClearCompositionMarkerEvents();
        }
    }

    void ICompositionAnimationCompositor.ClearCompositionAnimation()
    {
        ClearCompositionAnimation();
    }

    ValueTask<CompositionBackendExecutionResult> ICompositionAnimationCompositor.RenderCompositionAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken)
    {
        return RenderCompositionAnimationTickAtAsync(timestamp, cancellationToken);
    }

    internal void ClearCompositionPlan()
    {
        lock (_frameGate)
        {
            ClearCompositionPlanCore();
        }
    }

    internal void ClearCompositionScrollPresentation()
    {
        lock (_frameGate)
        {
            if (_compositionPresentationState.ScrollPresentationPlan is null)
            {
                return;
            }

            _compositionPresentationState.ClearScrollPresentation();
            ClearCompositionPresentationState();
            ClearCompositionMarkerEvents();
        }
    }

    void ICompositionScrollPresentationCompositor.ClearCompositionScrollPresentation()
    {
        ClearCompositionScrollPresentation();
    }

    private void ClearCompositionPlanCore()
    {
        _compositionPresentationState.ClearAll();
        ClearCompositionPresentationState();
        ClearCompositionMarkerEvents();
    }

    internal void SetCompositionScrollPresentationPlan(in CompositionScrollPresentationPlan plan)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount > 0 && !plan.IsValidForCommandCount(_retainedFrame.CommandCount))
            {
                throw new ArgumentException("Composition scroll presentation plan layer range must fit the retained command frame.", nameof(plan));
            }

            _compositionPresentationState.SetScrollPresentationPlan(plan);
            ClearCompositionPresentationState();
        }
    }

    internal void SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount <= 0)
            {
                throw new InvalidOperationException("A retained render frame must exist before installing a composition scroll presentation declaration.");
            }

            if (snapshot.CommandCount != _retainedFrame.CommandCount)
            {
                throw new ArgumentException("Composition scroll presentation declaration snapshot must match the retained command frame.", nameof(snapshot));
            }

            if (!declaration.TryResolve(snapshot, _retainedFrame.CommandCount, out var plan))
            {
                throw new ArgumentException("Composition scroll presentation declaration target must resolve to a retained scroll command range.", nameof(declaration));
            }

            _compositionPresentationState.SetScrollPresentationPlan(plan);
            ClearCompositionPresentationState();
        }
    }

    void ICompositionScrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        SetCompositionScrollPresentationDeclaration(declaration, snapshot);
    }

    internal bool TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        lock (_frameGate)
        {
            if (_compositionPresentationState.ScrollPresentationPlan is null
                || _retainedFrame.CommandCount <= 0
                || !declaration.TryResolve(snapshot, snapshot.CommandCount, out var nextPlan))
            {
                _compositionPresentationState.ClearPendingScrollPresentationRetainedFrameUpdate();
                return false;
            }

            _compositionPresentationState.PrepareScrollPresentationRetainedFrameUpdate(nextPlan, snapshot.CommandCount);
            return true;
        }
    }

    bool ICompositionScrollPresentationCompositor.TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        return TryPrepareCompositionScrollPresentationRetainedFrameUpdate(declaration, snapshot);
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
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        lock (_frameGate)
        {
            return RenderCore(renderFrameBatch, ownership, frameContext);
        }
    }

    private ValueTask RenderCore(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        in FrameContext frameContext)
    {
        var sw = Stopwatch.StartNew();
        var stage = ApplyRetainedFrame(renderFrameBatch, ownership);
        if (!stage.HasCommands)
        {
            return ValueTask.CompletedTask;
        }

        if (HasActiveRetainedRefreshScrollPresentationPlan()
            && TryRefreshActiveScrollPresentationAfterRetainedUpdate(out _))
        {
            LastHandoffResult = stage.HandoffSelection.Selected
                ? DrawingBackendCompositorHandoffResult.RetainedFrameStaged(stage.HandoffSelection.OwnerResult)
                : stage.HandoffSelection.Result;
            LastPartialApplySucceeded = stage.RetainedPartialApplySucceeded;
            RecordRenderedApply(LastPartialApplySucceeded);
            RecordFrameTime(sw);

            PublishHitTargets([.. _retainedFrame.HitTargets]);

            return ValueTask.CompletedTask;
        }

        if (stage.HandoffSelection.Selected)
        {
            LastHandoffResult = ExecuteSelectedHandoffFrame(ownership!, stage.HandoffSelection.OwnerResult, frameContext);
            LastPartialApplySucceeded = LastHandoffResult.CandidateResult.Counters.LastPartialApplySucceeded;
            RecordRenderedApply(LastPartialApplySucceeded);
            RecordFrameTime(sw);

            PublishHitTargets(ownership!.RuntimeOwner!.HitTargets.ToArray());

            return ValueTask.CompletedTask;
        }

        LastHandoffResult = stage.HandoffSelection.Result;
        LastPartialApplySucceeded = stage.RetainedPartialApplySucceeded;
        RecordRenderedApply(LastPartialApplySucceeded);

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

        PublishHitTargets([.. _retainedFrame.HitTargets]);
        return ValueTask.CompletedTask;
    }

    ValueTask IRetainedFrameStagingCompositor.StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken,
        RetainedFrameStageCompositionMode compositionMode)
    {
        return StageRetainedFrameAsync(renderFrameBatch, ownership, cancellationToken, compositionMode);
    }

    internal ValueTask StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken = default,
        RetainedFrameStageCompositionMode compositionMode = RetainedFrameStageCompositionMode.RefreshActiveCompositionAfterStage)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        lock (_frameGate)
        {
            return StageRetainedFrameCore(renderFrameBatch, ownership, compositionMode);
        }
    }

    private ValueTask StageRetainedFrameCore(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        RetainedFrameStageCompositionMode compositionMode)
    {
        var stage = ApplyRetainedFrame(renderFrameBatch, ownership);
        Interlocked.Increment(ref _retainedStageCount);
        if (!stage.HasCommands)
        {
            return ValueTask.CompletedTask;
        }

        LastHandoffResult = stage.HandoffSelection.Selected
            ? DrawingBackendCompositorHandoffResult.RetainedFrameStaged(stage.HandoffSelection.OwnerResult)
            : stage.HandoffSelection.Result;
        LastPartialApplySucceeded = stage.RetainedPartialApplySucceeded;
        if (compositionMode == RetainedFrameStageCompositionMode.RefreshActiveCompositionAfterStage
            && HasActiveRetainedRefreshScrollPresentationPlan())
        {
            _ = TryRefreshActiveScrollPresentationAfterRetainedUpdate(out _);
        }

        PublishHitTargets([.. _retainedFrame.HitTargets]);

        return ValueTask.CompletedTask;
    }

    private RetainedFrameStageResult ApplyRetainedFrame(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            PublishHitTargets([]);

            Interlocked.Increment(ref _emptyFrameCount);
            _retainedFrame.ReleaseResources();
            _retainedFrame.Invalidate();
            _lastAppliedFrameId = 0;
            ClearCompositionPlanCore();
            _lastDirtyCommandRangeList = renderFrameBatch.DirtyCommandRangeList;
            LastPartialApplySucceeded = false;
            LastHandoffResult = ResolveHandoffSelection(renderFrameBatch, ownership).Result;
            return default;
        }

        // Cross-frame partial apply guard: only allow partial apply when the batch's
        // resources are the same FrameDrawingResources instance AND same rental cycle
        // (FrameId) as the last apply. This prevents a recycled pooled instance from
        // being misidentified as "same frame scope".
        var batchFrameId = renderFrameBatch.Resources is FrameDrawingResources fdr ? fdr.FrameId : 0ul;
        var isSameFrameScope = batchFrameId != 0 && batchFrameId == _lastAppliedFrameId;

        var retainedPartialApplySucceeded = false;
        if (isSameFrameScope && renderFrameBatch.DirtyCommandRangeList.Count > 0)
        {
            retainedPartialApplySucceeded = _retainedFrame.TryApplyPartial(renderFrameBatch);
        }

        if (!retainedPartialApplySucceeded)
        {
            // Release old retained resources before taking new ones.
            _retainedFrame.ReleaseResources();
            _retainedFrame.ApplyFull(renderFrameBatch);
            _retainedFrame.RetainResources();
        }

        _lastAppliedFrameId = batchFrameId;
        _lastDirtyCommandRangeList = _retainedFrame.DirtyCommandRanges;
        TryApplyPreparedScrollPresentationRetainedFrameUpdate();
        ClearCompositionPresentationState();
        return new RetainedFrameStageResult(true, retainedPartialApplySucceeded, ResolveHandoffSelection(renderFrameBatch, ownership));
    }

    private void TryApplyPreparedScrollPresentationRetainedFrameUpdate()
    {
        if (!_compositionPresentationState.TryTakePendingScrollPresentationRetainedFrameUpdate(out var pending))
        {
            return;
        }

        if (pending.CommandCount == _retainedFrame.CommandCount)
        {
            _compositionPresentationState.ApplyPreparedScrollPresentationRetainedFrameUpdate(pending.Plan);
        }
        else
        {
            _compositionPresentationState.DiscardPreparedScrollPresentationRetainedFrameUpdate();
            ClearCompositionMarkerEvents();
        }
    }

    private void RecordRenderedApply(bool partialApplySucceeded)
    {
        Interlocked.Increment(ref _renderCount);
        if (partialApplySucceeded)
        {
            Interlocked.Increment(ref _partialApplyCount);
        }
        else
        {
            Interlocked.Increment(ref _fullApplyCount);
        }
    }

    private bool TryGetCompositionBackendForExecution(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        int layerCount,
        out ICompositionDrawingBackend compositionBackend,
        out byte failureReason)
    {
        if (_backend is not ICompositionDrawingBackend backend)
        {
            failureReason = CompositionExecutionSkipReasonBackendDoesNotImplementComposition;
            RecordCompositionExecutionSkipped(
                kind,
                failureReason,
                requiredCapabilities,
                CompositionBackendCapabilities.None,
                CompositionFramePacing.SoftwareTimer,
                layerCount,
                _retainedFrame.CommandCount);
            compositionBackend = null!;
            return false;
        }

        var backendCapabilities = backend.CompositionCapabilities;
        if ((backendCapabilities & requiredCapabilities) != requiredCapabilities)
        {
            failureReason = CompositionExecutionSkipReasonMissingBackendCapability;
            RecordCompositionExecutionSkipped(
                kind,
                failureReason,
                requiredCapabilities,
                backendCapabilities,
                backend.FramePacing,
                layerCount,
                _retainedFrame.CommandCount);
            compositionBackend = null!;
            return false;
        }

        compositionBackend = backend;
        failureReason = 0;
        return true;
    }

    private ICompositionDrawingBackend GetCompositionBackendForExecutionOrThrow(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        int layerCount,
        string missingBackendMessage,
        string missingCapabilityMessage)
    {
        if (TryGetCompositionBackendForExecution(
            kind,
            requiredCapabilities,
            layerCount,
            out var compositionBackend,
            out var failureReason))
        {
            return compositionBackend;
        }

        throw new InvalidOperationException(
            failureReason == CompositionExecutionSkipReasonBackendDoesNotImplementComposition
                ? missingBackendMessage
                : missingCapabilityMessage);
    }

    private bool TryReadRetainedCompositionFrameForExecution(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        ICompositionDrawingBackend compositionBackend,
        int layerCount,
        out ReadOnlySpan<DrawCommand> commands,
        out IFrameResourceResolver resources)
    {
        if (!_retainedFrame.TryReadFrame(out commands, out resources))
        {
            RecordCompositionExecutionSkipped(
                kind,
                CompositionExecutionSkipReasonMissingRetainedFrame,
                requiredCapabilities,
                compositionBackend.CompositionCapabilities,
                compositionBackend.FramePacing,
                layerCount,
                0);
            return false;
        }

        return true;
    }

    private ReadOnlySpan<DrawCommand> ReadRetainedCompositionFrameForExecutionOrThrow(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        ICompositionDrawingBackend compositionBackend,
        int layerCount,
        string missingFrameMessage,
        out IFrameResourceResolver resources)
    {
        if (TryReadRetainedCompositionFrameForExecution(
            kind,
            requiredCapabilities,
            compositionBackend,
            layerCount,
            out var commands,
            out resources))
        {
            return commands;
        }

        throw new InvalidOperationException(missingFrameMessage);
    }

    private void RecordInvalidCompositionPlanForExecution(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        ICompositionDrawingBackend compositionBackend,
        int layerCount,
        int commandCount)
    {
        RecordCompositionExecutionSkipped(
            kind,
            CompositionExecutionSkipReasonInvalidPlanForRetainedFrame,
            requiredCapabilities,
            compositionBackend.CompositionCapabilities,
            compositionBackend.FramePacing,
            layerCount,
            commandCount);
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAsync(
        CancellationToken cancellationToken = default)
    {
        return RenderCompositionAnimationTickAtAsync(_clockSource.TimestampNow(), cancellationToken);
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<CompositionBackendExecutionResult>(cancellationToken);
        }

        lock (_frameGate)
        {
            return RenderCompositionAnimationTickAtCore(timestamp);
        }
    }

    private ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAtCore(CompositionTimestamp timestamp)
    {
        const byte kind = CompositionExecutionKindTransformOpacityTick;
        const CompositionBackendCapabilities requiredCapabilities = CompositionBackendCapabilities.TransformOpacity;
        if (_compositionPresentationState.AnimationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                kind,
                CompositionExecutionSkipReasonNoActivePlan,
                requiredCapabilities,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition animation plan must be set before compositor-only animation ticks can be rendered.");
        }

        var layerCount = plan.LayerAnimation.LayerId.IsValid ? 1 : 0;
        var compositionBackend = GetCompositionBackendForExecutionOrThrow(
            kind,
            requiredCapabilities,
            layerCount,
            "The drawing backend must expose transform/opacity composition execution for compositor-only animation ticks.",
            "The drawing backend must expose transform/opacity composition execution for compositor-only animation ticks.");
        var commands = ReadRetainedCompositionFrameForExecutionOrThrow(
            kind,
            requiredCapabilities,
            compositionBackend,
            layerCount,
            "A retained render frame must exist before compositor-only animation ticks can be rendered.",
            out var resources);
        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordInvalidCompositionPlanForExecution(kind, requiredCapabilities, compositionBackend, layerCount, commands.Length);
            throw new InvalidOperationException("Composition animation plan layer range must fit the retained command frame.");
        }

        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        var sample = plan.LayerAnimation.Timeline.SampleAt(timestamp);
        return RenderCompositionFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            compositionFrame,
            timestamp,
            kind,
            requiredCapabilities,
            static (DrawingBackendCompositor compositor, in CompositionMarkerEventContext context) =>
            {
                var plan = context.AnimationPlan.GetValueOrDefault();
                var sample = context.Sample;
                CompositionAnimationMarkerEvaluator.EvaluateTransformOpacity(
                    plan.LayerAnimation,
                    compositor._compositionPresentationState.HasLastAnimationSample,
                    plan.LayerAnimation.Timeline.StartTimestamp,
                    compositor._compositionPresentationState.LastAnimationSample,
                    sample,
                    compositor._compositionPresentationState.AnimationMarkerStates,
                    compositor._compositionMarkerEvents);
                compositor._compositionPresentationState.SetLastAnimationSample(sample);
            },
            new CompositionMarkerEventContext(
                plan,
                default,
                default,
                sample,
                timestamp));
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationPresentationTickAsync(
        CancellationToken cancellationToken = default)
    {
        return RenderCompositionAnimationPresentationTickAtAsync(_clockSource.TimestampNow(), cancellationToken);
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<CompositionBackendExecutionResult>(cancellationToken);
        }

        lock (_frameGate)
        {
            return RenderCompositionAnimationPresentationTickAtCore(timestamp);
        }
    }

    private ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationPresentationTickAtCore(CompositionTimestamp timestamp)
    {
        const byte kind = CompositionExecutionKindAnimationPresentationTick;
        if (_compositionPresentationState.AnimationPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                kind,
                CompositionExecutionSkipReasonNoActivePlan,
                CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition animation presentation plan must be active before compositor-only presentation ticks can be rendered.");
        }

        var requiredCapabilities = RequiredCapabilitiesForAnimationPresentationPlan(plan.Count);
        var compositionBackend = GetCompositionBackendForExecutionOrThrow(
            kind,
            requiredCapabilities,
            plan.Count,
            "The drawing backend must expose transform/opacity composition execution for compositor-only presentation ticks.",
            "The drawing backend must expose the required composition capabilities for compositor-only presentation ticks.");
        var commands = ReadRetainedCompositionFrameForExecutionOrThrow(
            kind,
            requiredCapabilities,
            compositionBackend,
            plan.Count,
            "A retained render frame must exist before compositor-only presentation ticks can be rendered.",
            out var resources);
        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordInvalidCompositionPlanForExecution(kind, requiredCapabilities, compositionBackend, plan.Count, commands.Length);
            throw new InvalidOperationException("Composition animation presentation plan ranges must fit the retained command frame.");
        }

        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        return RenderCompositionFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            compositionFrame,
            timestamp,
            kind,
            requiredCapabilities,
            static (DrawingBackendCompositor compositor, in CompositionMarkerEventContext context) =>
            {
                var plan = context.AnimationPresentationPlan.GetValueOrDefault();
                for (var i = 0; i < plan.Count; i++)
                {
                    var layerAnimation = plan.GetPlan(i).LayerAnimation;
                    var sample = layerAnimation.Timeline.SampleAt(context.Timestamp);
                    var markerStates = i < compositor._compositionPresentationState.AnimationPresentationMarkerStates.Length
                        ? compositor._compositionPresentationState.AnimationPresentationMarkerStates[i]
                        : [];
                    var hasPreviousSample = i < compositor._compositionPresentationState.HasLastAnimationPresentationSamples.Length
                        && compositor._compositionPresentationState.HasLastAnimationPresentationSamples[i];
                    var previousSample = i < compositor._compositionPresentationState.LastAnimationPresentationSamples.Length
                        ? compositor._compositionPresentationState.LastAnimationPresentationSamples[i]
                        : default;

                    CompositionAnimationMarkerEvaluator.EvaluateTransformOpacity(
                        layerAnimation,
                        hasPreviousSample,
                        layerAnimation.Timeline.StartTimestamp,
                        previousSample,
                        sample,
                        markerStates,
                        compositor._compositionMarkerEvents);

                    if (i < compositor._compositionPresentationState.LastAnimationPresentationSamples.Length)
                    {
                        compositor._compositionPresentationState.LastAnimationPresentationSamples[i] = sample;
                    }

                    if (i < compositor._compositionPresentationState.HasLastAnimationPresentationSamples.Length)
                    {
                        compositor._compositionPresentationState.HasLastAnimationPresentationSamples[i] = true;
                    }
                }
            },
            new CompositionMarkerEventContext(
                default,
                plan,
                default,
                default,
                timestamp));
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAsync(
        CancellationToken cancellationToken = default)
    {
        return RenderCompositionScrollPresentationTickAtAsync(_clockSource.TimestampNow(), cancellationToken);
    }

    internal ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<CompositionBackendExecutionResult>(cancellationToken);
        }

        lock (_frameGate)
        {
            return RenderCompositionScrollPresentationTickAtCore(timestamp);
        }
    }

    ValueTask<CompositionBackendExecutionResult> ICompositionScrollPresentationCompositor.RenderCompositionScrollPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken)
    {
        return RenderCompositionScrollPresentationTickAtAsync(timestamp, cancellationToken);
    }

    private ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtCore(CompositionTimestamp timestamp)
    {
        const byte kind = CompositionExecutionKindScrollPresentationTick;
        const CompositionBackendCapabilities requiredCapabilities = CompositionBackendCapabilities.ScrollPresentation;
        if (_compositionPresentationState.ScrollPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                kind,
                CompositionExecutionSkipReasonNoActivePlan,
                requiredCapabilities,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition scroll presentation plan must be set before compositor-only scroll ticks can be rendered.");
        }

        var compositionBackend = GetCompositionBackendForExecutionOrThrow(
            kind,
            requiredCapabilities,
            plan.LayerCount,
            "The drawing backend must expose fixed-clip scroll presentation execution for compositor-only scroll ticks.",
            "The drawing backend must expose fixed-clip scroll presentation execution for compositor-only scroll ticks.");
        var commands = ReadRetainedCompositionFrameForExecutionOrThrow(
            kind,
            requiredCapabilities,
            compositionBackend,
            plan.LayerCount,
            "A retained render frame must exist before compositor-only scroll ticks can be rendered.",
            out var resources);
        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordInvalidCompositionPlanForExecution(kind, requiredCapabilities, compositionBackend, plan.LayerCount, commands.Length);
            throw new InvalidOperationException("Composition scroll presentation plan layer range must fit the retained command frame.");
        }

        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        var sample = plan.LayerAnimation.Timeline.SampleAt(timestamp);
        return RenderCompositionScrollPresentationFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            plan,
            compositionFrame,
            sample,
            timestamp,
            kind);
    }

    private bool TryRefreshActiveScrollPresentationAfterRetainedUpdate(out CompositionBackendExecutionResult result)
    {
        result = default;
        const byte kind = CompositionExecutionKindRetainedUpdateScrollPresentation;
        const CompositionBackendCapabilities requiredCapabilities = CompositionBackendCapabilities.ScrollPresentation;
        if (_compositionPresentationState.ScrollPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                kind,
                CompositionExecutionSkipReasonNoActivePlan,
                requiredCapabilities,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                _retainedFrame.CommandCount);
            return false;
        }

        if (!TryGetCompositionBackendForExecution(
                kind,
                requiredCapabilities,
                plan.LayerCount,
                out var compositionBackend,
                out _)
            || !TryReadRetainedCompositionFrameForExecution(
                kind,
                requiredCapabilities,
                compositionBackend,
                plan.LayerCount,
                out var commands,
                out var resources))
        {
            return false;
        }

        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordInvalidCompositionPlanForExecution(kind, requiredCapabilities, compositionBackend, plan.LayerCount, commands.Length);
            return false;
        }

        var timestamp = _clockSource.TimestampNow();
        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        var sample = plan.LayerAnimation.Timeline.SampleAt(timestamp);
        result = RenderCompositionScrollPresentationFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            plan,
            compositionFrame,
            sample,
            timestamp,
            kind).GetAwaiter().GetResult();
        return true;
    }

    private bool HasActiveRetainedRefreshScrollPresentationPlan() => _compositionPresentationState.HasActiveRetainedRefreshScrollPresentationPlan;

    private ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationFrameAtAsync(
        ICompositionDrawingBackend compositionBackend,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionScrollPresentationPlan plan,
        in CompositionFrame compositionFrame,
        in CompositionTimelineSample sample,
        CompositionTimestamp timestamp,
        byte kind)
    {
        return RenderCompositionFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            compositionFrame,
            timestamp,
            kind,
            CompositionBackendCapabilities.ScrollPresentation,
            static (DrawingBackendCompositor compositor, in CompositionMarkerEventContext context) =>
            {
                var plan = context.ScrollPresentationPlan.GetValueOrDefault();
                var sample = context.Sample;
                CompositionAnimationMarkerEvaluator.EvaluateScrollPresentation(
                    plan.LayerAnimation,
                    compositor._compositionPresentationState.HasLastScrollPresentationSample,
                    plan.LayerAnimation.Timeline.StartTimestamp,
                    compositor._compositionPresentationState.LastScrollPresentationSample,
                    sample,
                    compositor._compositionPresentationState.ScrollPresentationMarkerStates,
                    compositor._compositionMarkerEvents);
                compositor._compositionPresentationState.SetLastScrollPresentationSample(sample);
            },
            new CompositionMarkerEventContext(
                default,
                default,
                plan,
                sample,
                timestamp));
    }

    private ValueTask<CompositionBackendExecutionResult> RenderCompositionFrameAtAsync(
        ICompositionDrawingBackend compositionBackend,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame,
        CompositionTimestamp timestamp,
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionMarkerEventPublisher publishMarkerEvents,
        in CompositionMarkerEventContext markerEventContext)
    {
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
                RecordCompositionExecutionSkipped(
                    kind,
                    6,
                    requiredCapabilities,
                    compositionBackend.CompositionCapabilities,
                    compositionBackend.FramePacing,
                    compositionFrame.LayerCount,
                    commands.Length);
                return ValueTask.FromResult(result);
            }

            throw;
        }

        RecordCompositionExecutionCompleted(
            kind,
            requiredCapabilities,
            compositionBackend.CompositionCapabilities,
            compositionBackend.FramePacing,
            compositionFrame.LayerCount,
            commands.Length);
        lock (_compositionMarkerLock)
        {
            publishMarkerEvents.Invoke(this, markerEventContext);
        }
        SetCompositionPresentationState(compositionFrame, result);
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

        if (!RangeUtils.TryNormalizeStrict(_lastDirtyCommandRangeList, renderFrameBatch.Commands.Count, out _))
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
            return MapCandidateResult(ownerResult, harness.ExecuteCandidateFrame(ownership, frameContext, _lastDirtyCommandRangeList));
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

    private readonly struct RetainedFrameStageResult(
        bool HasCommands,
        bool RetainedPartialApplySucceeded,
        HandoffSelection HandoffSelection) : IEquatable<RetainedFrameStageResult>
    {
        public bool HasCommands { get; } = HasCommands;
        public bool RetainedPartialApplySucceeded { get; } = RetainedPartialApplySucceeded;
        public HandoffSelection HandoffSelection { get; } = HandoffSelection;

        public bool Equals(RetainedFrameStageResult other)
        {
            return HasCommands == other.HasCommands
                && RetainedPartialApplySucceeded == other.RetainedPartialApplySucceeded
                && HandoffSelection.Equals(other.HandoffSelection);
        }

        public override bool Equals(object? obj) => obj is RetainedFrameStageResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(HasCommands, RetainedPartialApplySucceeded, HandoffSelection);

        public static bool operator ==(RetainedFrameStageResult left, RetainedFrameStageResult right) => left.Equals(right);

        public static bool operator !=(RetainedFrameStageResult left, RetainedFrameStageResult right) => !left.Equals(right);
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
            && ownerResult.BatchCommandGeneration == renderFrameBatch.Commands.OwnerGeneration
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
        if (TryHitTestLogicalPixel(x, y, out var result))
        {
            actionId = result.ActionId;
            return true;
        }

        actionId = ActionId.None;
        return false;
    }

    internal bool TryHitTestLogicalPixel(int x, int y, out CompositorHitTestResult result)
    {
        CompositorHitTestSnapshot snapshot;
        lock (_hitTargetsLock)
        {
            snapshot = _hitTestSnapshot;
        }

        return snapshot.TryHitTestLogicalPixel(x, y, out result);
    }

    private CompositionFrame GetActiveCompositionFrame()
    {
        lock (_compositionStateLock)
        {
            return _lastCompositionFrame;
        }
    }

    private void PublishHitTargets(HitTestTarget[] hitTargets)
    {
        var activeFrame = GetActiveCompositionFrame();
        lock (_hitTargetsLock)
        {
            _hitTargets = hitTargets;
            _hitTestSnapshot = CompositorHitTestSnapshot.Create(
                _hitTargets,
                _retainedFrame.CommandCount,
                activeFrame);
        }
    }

    private void RefreshHitTestSnapshot(in CompositionFrame compositionFrame)
    {
        lock (_hitTargetsLock)
        {
            _hitTestSnapshot = CompositorHitTestSnapshot.Create(
                _hitTargets,
                _retainedFrame.CommandCount,
                compositionFrame);
        }
    }

    private static CompositionBackendCapabilities RequiredCapabilitiesForAnimationPresentationPlan(int layerCount)
    {
        return layerCount > 1
            ? CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer
            : CompositionBackendCapabilities.TransformOpacity;
    }

    private void SetCompositionPresentationState(
        in CompositionFrame frame,
        in CompositionBackendExecutionResult result)
    {
        lock (_compositionStateLock)
        {
            _lastCompositionFrame = frame;
            _lastCompositionExecutionResult = result;
        }

        RefreshHitTestSnapshot(frame);
    }

    private void ClearCompositionPresentationState()
    {
        ClearCompositionPresentationFrameState();
        _compositionPresentationState.ClearSamples();
    }

    private void ClearCompositionPresentationFrameState()
    {
        lock (_compositionStateLock)
        {
            _lastCompositionFrame = default;
            _lastCompositionExecutionResult = default;
        }

        RefreshHitTestSnapshot(default);
    }

    private void ClearCompositionMarkerEvents()
    {
        lock (_compositionMarkerLock)
        {
            _compositionMarkerEvents.Clear();
        }
    }

    private void ClearCompositionMarkerEvents(ReadOnlySpan<NodeKey> targetKeys)
    {
        if (targetKeys.IsEmpty)
        {
            return;
        }

        lock (_compositionMarkerLock)
        {
            for (var i = _compositionMarkerEvents.Count - 1; i >= 0; i--)
            {
                if (ContainsTargetKey(targetKeys, _compositionMarkerEvents[i].TargetKey))
                {
                    _compositionMarkerEvents.RemoveAt(i);
                }
            }
        }
    }

    private static bool ContainsTargetKey(ReadOnlySpan<NodeKey> targetKeys, NodeKey targetKey)
    {
        if (targetKey == NodeKey.None)
        {
            return false;
        }

        for (var i = 0; i < targetKeys.Length; i++)
        {
            if (targetKeys[i] == targetKey)
            {
                return true;
            }
        }

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

    private delegate void CompositionMarkerEventPublisher(
        DrawingBackendCompositor compositor,
        in CompositionMarkerEventContext context);

    private readonly struct PendingCompositionScrollPresentationRetainedFrameUpdate(
        CompositionScrollPresentationPlan Plan,
        int CommandCount)
    {
        public CompositionScrollPresentationPlan Plan { get; } = Plan;
        public int CommandCount { get; } = CommandCount;
        public bool HasValue => CommandCount > 0;
    }

    private readonly struct CompositionMarkerEventContext(
        CompositionAnimationPlan? AnimationPlan,
        CompositionAnimationPresentationSetPlan? AnimationPresentationPlan,
        CompositionScrollPresentationPlan? ScrollPresentationPlan,
        CompositionTimelineSample Sample,
        CompositionTimestamp Timestamp)
    {
        public CompositionAnimationPlan? AnimationPlan { get; } = AnimationPlan;
        public CompositionAnimationPresentationSetPlan? AnimationPresentationPlan { get; } = AnimationPresentationPlan;
        public CompositionScrollPresentationPlan? ScrollPresentationPlan { get; } = ScrollPresentationPlan;
        public CompositionTimelineSample Sample { get; } = Sample;
        public CompositionTimestamp Timestamp { get; } = Timestamp;
    }
}
