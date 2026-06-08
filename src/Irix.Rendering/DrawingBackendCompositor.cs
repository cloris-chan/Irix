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
    private CompositionAnimationPlan? _compositionAnimationPlan;
    private CompositionAnimationPresentationSetPlan? _compositionAnimationPresentationPlan;
    private CompositionScrollPresentationPlan? _compositionScrollPresentationPlan;
    private CompositionFrame _lastCompositionFrame;
    private CompositionBackendExecutionResult _lastCompositionExecutionResult;
    private readonly List<CompositionAnimationMarkerEvent> _compositionMarkerEvents = [];
    private CompositionAnimationMarkerPlaybackState[] _compositionAnimationMarkerStates = [];
    private CompositionAnimationMarkerPlaybackState[][] _compositionAnimationPresentationMarkerStates = [];
    private CompositionAnimationMarkerPlaybackState[] _compositionScrollMarkerStates = [];
    private CompositionTimelineSample _lastCompositionAnimationSample;
    private CompositionTimelineSample[] _lastCompositionAnimationPresentationSamples = [];
    private CompositionTimelineSample _lastCompositionScrollSample;
    private bool _hasLastCompositionAnimationSample;
    private bool[] _hasLastCompositionAnimationPresentationSamples = [];
    private bool _hasLastCompositionScrollSample;
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

    internal CompositionAnimationPlan? CompositionAnimationPlan => _compositionAnimationPlan;

    internal CompositionAnimationPresentationSetPlan? CompositionAnimationPresentationPlan => _compositionAnimationPresentationPlan;

    internal CompositionScrollPresentationPlan? CompositionScrollPresentationPlan => _compositionScrollPresentationPlan;

    internal bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
    {
        presentedScrollY = 0;
        if (targetKey == NodeKey.None || _compositionScrollPresentationPlan is not { } plan)
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

    internal void SetCompositionAnimationPlan(in CompositionAnimationPlan plan)
    {
        lock (_frameGate)
        {
            if (_retainedFrame.CommandCount > 0 && !plan.IsValidForCommandCount(_retainedFrame.CommandCount))
            {
                throw new ArgumentException("Composition animation plan layer range must fit the retained command frame.", nameof(plan));
            }

            _compositionScrollPresentationPlan = null;
            _compositionAnimationPresentationPlan = null;
            _compositionAnimationPlan = plan;
            _compositionAnimationMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
            ClearCompositionAnimationPresentationPlaybackState();
            _compositionScrollMarkerStates = [];
            ClearCompositionPresentationState();
        }
    }

    internal void SetCompositionAnimationDeclaration(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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

            _compositionScrollPresentationPlan = null;
            _compositionAnimationPresentationPlan = null;
            _compositionAnimationPlan = plan;
            _compositionAnimationMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
            ClearCompositionAnimationPresentationPlaybackState();
            _compositionScrollMarkerStates = [];
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

            _compositionAnimationPlan = null;
            _compositionScrollPresentationPlan = null;
            _compositionAnimationPresentationPlan = plan;
            _compositionAnimationMarkerStates = [];
            _compositionScrollMarkerStates = [];
            _compositionAnimationPresentationMarkerStates = CreatePresentationMarkerPlaybackStates(plan);
            _lastCompositionAnimationPresentationSamples = new CompositionTimelineSample[plan.Count];
            _hasLastCompositionAnimationPresentationSamples = new bool[plan.Count];
            ClearCompositionPresentationState();
        }
    }

    internal void ClearCompositionAnimation()
    {
        lock (_frameGate)
        {
            if (_compositionAnimationPlan is null && _compositionAnimationPresentationPlan is null)
            {
                return;
            }

            _compositionAnimationPlan = null;
            _compositionAnimationPresentationPlan = null;
            _compositionAnimationMarkerStates = [];
            ClearCompositionAnimationPresentationPlaybackState();
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
            if (_compositionScrollPresentationPlan is null)
            {
                return;
            }

            _compositionScrollPresentationPlan = null;
            _compositionScrollMarkerStates = [];
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
        _compositionAnimationPlan = null;
        _compositionAnimationPresentationPlan = null;
        _compositionScrollPresentationPlan = null;
        _compositionAnimationMarkerStates = [];
        ClearCompositionAnimationPresentationPlaybackState();
        _compositionScrollMarkerStates = [];
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

            _compositionAnimationPlan = null;
            _compositionAnimationPresentationPlan = null;
            _compositionScrollPresentationPlan = plan;
            _compositionAnimationMarkerStates = [];
            ClearCompositionAnimationPresentationPlaybackState();
            _compositionScrollMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
            ClearCompositionPresentationState();
        }
    }

    internal void SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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

            _compositionAnimationPlan = null;
            _compositionAnimationPresentationPlan = null;
            _compositionScrollPresentationPlan = plan;
            _compositionAnimationMarkerStates = [];
            ClearCompositionAnimationPresentationPlaybackState();
            _compositionScrollMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
            ClearCompositionPresentationState();
        }
    }

    void ICompositionScrollPresentationCompositor.SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot)
    {
        SetCompositionScrollPresentationDeclaration(declaration, snapshot);
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

        if (TryRenderActiveScrollPresentationAfterRetainedUpdate(out _))
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
        CancellationToken cancellationToken)
    {
        return StageRetainedFrameAsync(renderFrameBatch, ownership, cancellationToken);
    }

    internal ValueTask StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        lock (_frameGate)
        {
            return StageRetainedFrameCore(renderFrameBatch, ownership);
        }
    }

    private ValueTask StageRetainedFrameCore(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership)
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
            LastDirtyCommandRanges = renderFrameBatch.DirtyCommandRanges;
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
        if (isSameFrameScope && renderFrameBatch.DirtyCommandRanges.Count > 0)
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
        LastDirtyCommandRanges = _retainedFrame.DirtyCommandRanges;
        ClearCompositionPresentationState();
        return new RetainedFrameStageResult(true, retainedPartialApplySucceeded, ResolveHandoffSelection(renderFrameBatch, ownership));
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
        if (_compositionAnimationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                1,
                1,
                CompositionBackendCapabilities.TransformOpacity,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition animation plan must be set before compositor-only animation ticks can be rendered.");
        }

        if (_backend is not ICompositionDrawingBackend compositionBackend)
        {
            RecordCompositionExecutionSkipped(
                1,
                2,
                CompositionBackendCapabilities.TransformOpacity,
                CompositionBackendCapabilities.None,
                CompositionFramePacing.SoftwareTimer,
                plan.LayerAnimation.LayerId.IsValid ? 1 : 0,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose transform/opacity composition execution for compositor-only animation ticks.");
        }

        var backendCapabilities = compositionBackend.CompositionCapabilities;
        if ((backendCapabilities & CompositionBackendCapabilities.TransformOpacity) != CompositionBackendCapabilities.TransformOpacity)
        {
            RecordCompositionExecutionSkipped(
                1,
                3,
                CompositionBackendCapabilities.TransformOpacity,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerAnimation.LayerId.IsValid ? 1 : 0,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose transform/opacity composition execution for compositor-only animation ticks.");
        }

        if (!_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            RecordCompositionExecutionSkipped(
                1,
                4,
                CompositionBackendCapabilities.TransformOpacity,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerAnimation.LayerId.IsValid ? 1 : 0,
                0);
            throw new InvalidOperationException("A retained render frame must exist before compositor-only animation ticks can be rendered.");
        }

        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordCompositionExecutionSkipped(
                1,
                5,
                CompositionBackendCapabilities.TransformOpacity,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerAnimation.LayerId.IsValid ? 1 : 0,
                commands.Length);
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
            1,
            CompositionBackendCapabilities.TransformOpacity,
            static (DrawingBackendCompositor compositor, in CompositionMarkerEventContext context) =>
            {
                var plan = context.AnimationPlan.GetValueOrDefault();
                var sample = context.Sample;
                CompositionAnimationMarkerEvaluator.EvaluateTransformOpacity(
                    plan.LayerAnimation,
                    compositor._hasLastCompositionAnimationSample,
                    plan.LayerAnimation.Timeline.StartTimestamp,
                    compositor._lastCompositionAnimationSample,
                    sample,
                    compositor._compositionAnimationMarkerStates,
                    compositor._compositionMarkerEvents);
                compositor._lastCompositionAnimationSample = sample;
                compositor._hasLastCompositionAnimationSample = true;
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
        if (_compositionAnimationPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                4,
                1,
                CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition animation presentation plan must be active before compositor-only presentation ticks can be rendered.");
        }

        var requiredCapabilities = RequiredCapabilitiesForAnimationPresentationPlan(plan.Count);
        if (_backend is not ICompositionDrawingBackend compositionBackend)
        {
            RecordCompositionExecutionSkipped(
                4,
                2,
                requiredCapabilities,
                CompositionBackendCapabilities.None,
                CompositionFramePacing.SoftwareTimer,
                plan.Count,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose transform/opacity composition execution for compositor-only presentation ticks.");
        }

        var backendCapabilities = compositionBackend.CompositionCapabilities;
        if ((backendCapabilities & requiredCapabilities) != requiredCapabilities)
        {
            RecordCompositionExecutionSkipped(
                4,
                3,
                requiredCapabilities,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.Count,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose the required composition capabilities for compositor-only presentation ticks.");
        }

        if (!_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            RecordCompositionExecutionSkipped(
                4,
                4,
                requiredCapabilities,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.Count,
                0);
            throw new InvalidOperationException("A retained render frame must exist before compositor-only presentation ticks can be rendered.");
        }

        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordCompositionExecutionSkipped(
                4,
                5,
                requiredCapabilities,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.Count,
                commands.Length);
            throw new InvalidOperationException("Composition animation presentation plan ranges must fit the retained command frame.");
        }

        var compositionFrame = plan.Evaluate(commands.Length, timestamp);
        return RenderCompositionFrameAtAsync(
            compositionBackend,
            commands,
            resources,
            compositionFrame,
            timestamp,
            4,
            requiredCapabilities,
            static (DrawingBackendCompositor compositor, in CompositionMarkerEventContext context) =>
            {
                var plan = context.AnimationPresentationPlan.GetValueOrDefault();
                for (var i = 0; i < plan.Count; i++)
                {
                    var layerAnimation = plan.GetPlan(i).LayerAnimation;
                    var sample = layerAnimation.Timeline.SampleAt(context.Timestamp);
                    var markerStates = i < compositor._compositionAnimationPresentationMarkerStates.Length
                        ? compositor._compositionAnimationPresentationMarkerStates[i]
                        : [];
                    var hasPreviousSample = i < compositor._hasLastCompositionAnimationPresentationSamples.Length
                        && compositor._hasLastCompositionAnimationPresentationSamples[i];
                    var previousSample = i < compositor._lastCompositionAnimationPresentationSamples.Length
                        ? compositor._lastCompositionAnimationPresentationSamples[i]
                        : default;

                    CompositionAnimationMarkerEvaluator.EvaluateTransformOpacity(
                        layerAnimation,
                        hasPreviousSample,
                        layerAnimation.Timeline.StartTimestamp,
                        previousSample,
                        sample,
                        markerStates,
                        compositor._compositionMarkerEvents);

                    if (i < compositor._lastCompositionAnimationPresentationSamples.Length)
                    {
                        compositor._lastCompositionAnimationPresentationSamples[i] = sample;
                    }

                    if (i < compositor._hasLastCompositionAnimationPresentationSamples.Length)
                    {
                        compositor._hasLastCompositionAnimationPresentationSamples[i] = true;
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
        if (_compositionScrollPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                2,
                1,
                CompositionBackendCapabilities.ScrollPresentation,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                0);
            throw new InvalidOperationException("A composition scroll presentation plan must be set before compositor-only scroll ticks can be rendered.");
        }

        if (_backend is not ICompositionDrawingBackend compositionBackend)
        {
            RecordCompositionExecutionSkipped(
                2,
                2,
                CompositionBackendCapabilities.ScrollPresentation,
                CompositionBackendCapabilities.None,
                CompositionFramePacing.SoftwareTimer,
                plan.LayerCount,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose fixed-clip scroll presentation execution for compositor-only scroll ticks.");
        }

        var backendCapabilities = compositionBackend.CompositionCapabilities;
        if ((backendCapabilities & CompositionBackendCapabilities.ScrollPresentation) != CompositionBackendCapabilities.ScrollPresentation)
        {
            RecordCompositionExecutionSkipped(
                2,
                3,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                _retainedFrame.CommandCount);
            throw new InvalidOperationException("The drawing backend must expose fixed-clip scroll presentation execution for compositor-only scroll ticks.");
        }

        if (!_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            RecordCompositionExecutionSkipped(
                2,
                4,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                0);
            throw new InvalidOperationException("A retained render frame must exist before compositor-only scroll ticks can be rendered.");
        }

        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordCompositionExecutionSkipped(
                2,
                5,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                commands.Length);
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
            2);
    }

    private bool TryRenderActiveScrollPresentationAfterRetainedUpdate(out CompositionBackendExecutionResult result)
    {
        result = default;
        if (_compositionScrollPresentationPlan is not { } plan)
        {
            RecordCompositionExecutionSkipped(
                3,
                1,
                CompositionBackendCapabilities.ScrollPresentation,
                _backend is ICompositionDrawingBackend existingBackend ? existingBackend.CompositionCapabilities : CompositionBackendCapabilities.None,
                FramePacing,
                0,
                _retainedFrame.CommandCount);
            return false;
        }

        if (_backend is not ICompositionDrawingBackend compositionBackend)
        {
            RecordCompositionExecutionSkipped(
                3,
                2,
                CompositionBackendCapabilities.ScrollPresentation,
                CompositionBackendCapabilities.None,
                CompositionFramePacing.SoftwareTimer,
                plan.LayerCount,
                _retainedFrame.CommandCount);
            return false;
        }

        var backendCapabilities = compositionBackend.CompositionCapabilities;
        if ((backendCapabilities & CompositionBackendCapabilities.ScrollPresentation) != CompositionBackendCapabilities.ScrollPresentation)
        {
            RecordCompositionExecutionSkipped(
                3,
                3,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                _retainedFrame.CommandCount);
            return false;
        }

        if (!_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            RecordCompositionExecutionSkipped(
                3,
                4,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                0);
            return false;
        }

        if (!plan.IsValidForCommandCount(commands.Length))
        {
            RecordCompositionExecutionSkipped(
                3,
                5,
                CompositionBackendCapabilities.ScrollPresentation,
                backendCapabilities,
                compositionBackend.FramePacing,
                plan.LayerCount,
                commands.Length);
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
            3).GetAwaiter().GetResult();
        return true;
    }

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
                    compositor._hasLastCompositionScrollSample,
                    plan.LayerAnimation.Timeline.StartTimestamp,
                    compositor._lastCompositionScrollSample,
                    sample,
                    compositor._compositionScrollMarkerStates,
                    compositor._compositionMarkerEvents);
                compositor._lastCompositionScrollSample = sample;
                compositor._hasLastCompositionScrollSample = true;
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

    private static CompositionAnimationMarkerPlaybackState[] CreateMarkerPlaybackStates(ReadOnlySpan<CompositionAnimationMarker> markers)
    {
        if (markers.Length == 0)
        {
            return [];
        }

        var states = new CompositionAnimationMarkerPlaybackState[markers.Length];
        for (var i = 0; i < markers.Length; i++)
        {
            states[i] = new CompositionAnimationMarkerPlaybackState(markers[i].Id);
        }

        return states;
    }

    private static CompositionAnimationMarkerPlaybackState[][] CreatePresentationMarkerPlaybackStates(
        in CompositionAnimationPresentationSetPlan plan)
    {
        if (plan.IsEmpty)
        {
            return [];
        }

        var states = new CompositionAnimationMarkerPlaybackState[plan.Count][];
        for (var i = 0; i < plan.Count; i++)
        {
            states[i] = CreateMarkerPlaybackStates(plan.GetPlan(i).LayerAnimation.Markers);
        }

        return states;
    }

    private static CompositionBackendCapabilities RequiredCapabilitiesForAnimationPresentationPlan(int layerCount)
    {
        return layerCount > 1
            ? CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer
            : CompositionBackendCapabilities.TransformOpacity;
    }

    private void ClearCompositionAnimationPresentationPlaybackState()
    {
        _compositionAnimationPresentationMarkerStates = [];
        _lastCompositionAnimationPresentationSamples = [];
        _hasLastCompositionAnimationPresentationSamples = [];
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
        lock (_compositionStateLock)
        {
            _lastCompositionFrame = default;
            _lastCompositionExecutionResult = default;
        }

        RefreshHitTestSnapshot(default);

        _hasLastCompositionAnimationSample = false;
        Array.Clear(_hasLastCompositionAnimationPresentationSamples);
        _hasLastCompositionScrollSample = false;
    }

    private void ClearCompositionMarkerEvents()
    {
        lock (_compositionMarkerLock)
        {
            _compositionMarkerEvents.Clear();
        }
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
