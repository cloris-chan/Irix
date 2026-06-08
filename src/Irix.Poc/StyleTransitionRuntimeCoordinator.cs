using Irix.Rendering;

namespace Irix.Poc;

internal enum StyleTransitionRuntimeDecisionKind : byte
{
    None,
    Start,
    Cancel,
    Retarget,
    Commit,
}

internal enum StyleTransitionRuntimeResultKind : byte
{
    None,
    NoOp,
    Started,
    Canceled,
    Retargeted,
    Committed,
    Fallback,
}

internal enum StyleTransitionRuntimeFallbackReason : byte
{
    None,
    MissingRetainedSnapshot,
    CompileRejected,
}

internal interface IStyleTransitionRuntimeAdapter
{
    StyleTransitionRuntimeDecision ConsumeStyleTransitionDecision();

    void PublishStyleTransitionResult(in StyleTransitionRuntimeResult result);
}

internal interface IStyleTransitionCompositorAdapter
{
    ValueTask StartAsync(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask CancelAsync(NodeKey targetKey, CancellationToken cancellationToken = default);
}

internal interface IStyleTransitionAnimationTickCompositorAdapter
{
    ValueTask RenderAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default);
}

internal interface IStyleTransitionPresentationActivationCompositorAdapter
{
    CompositionAnimationPresentationSetActivationPreflightResult PreparePresentationActivation(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        RenderPipelineRetainedInputSnapshot? snapshot);

    void ActivatePresentationPlan(in CompositionAnimationPresentationSetPlan plan);
}

internal interface IStyleTransitionAnimationPresentationTickCompositorAdapter
{
    ValueTask RenderAnimationPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default);
}

internal interface IStyleTransitionPresentationClearCompositorAdapter
{
    void ClearActivePresentationTargets(ReadOnlySpan<NodeKey> targetKeys);
}

internal interface IStyleTransitionRetainedSnapshotProvider
{
    RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; }
}

internal readonly struct StyleTransitionRuntimeDecision(
    StyleTransitionRuntimeDecisionKind Kind,
    NodeKey TargetKey,
    ReadOnlyMemory<VirtualNodeProperty> PreviousProperties,
    ReadOnlyMemory<VirtualNodeProperty> NextProperties,
    CompositionTimestamp StartTimestamp,
    CompositionDuration Duration,
    CompositionAnimationEasing Easing = CompositionAnimationEasing.Linear,
    CompositionAnimationRepeatMode RepeatMode = CompositionAnimationRepeatMode.Once,
    CompositionAnimationInstanceId InstanceId = default,
    CompositionAnimationMarker[]? Markers = null) : IEquatable<StyleTransitionRuntimeDecision>
{
    private readonly CompositionAnimationMarker[]? _markers = Markers;

    public StyleTransitionRuntimeDecisionKind Kind { get; } = Kind;
    public NodeKey TargetKey { get; } = TargetKey;
    public ReadOnlyMemory<VirtualNodeProperty> PreviousProperties { get; } = PreviousProperties;
    public ReadOnlyMemory<VirtualNodeProperty> NextProperties { get; } = NextProperties;
    public CompositionTimestamp StartTimestamp { get; } = StartTimestamp;
    public CompositionDuration Duration { get; } = Duration;
    public CompositionAnimationEasing Easing { get; } = Easing;
    public CompositionAnimationRepeatMode RepeatMode { get; } = RepeatMode;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;
    internal CompositionAnimationMarker[]? MarkerArray => _markers;

    public bool RequiresCompilation => Kind is StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget;

    public static StyleTransitionRuntimeDecision Start(
        NodeKey targetKey,
        ReadOnlyMemory<VirtualNodeProperty> previousProperties,
        ReadOnlyMemory<VirtualNodeProperty> nextProperties,
        CompositionTimestamp startTimestamp,
        CompositionDuration duration,
        CompositionAnimationEasing easing = CompositionAnimationEasing.Linear,
        CompositionAnimationRepeatMode repeatMode = CompositionAnimationRepeatMode.Once,
        CompositionAnimationInstanceId instanceId = default,
        CompositionAnimationMarker[]? markers = null)
    {
        return new StyleTransitionRuntimeDecision(
            StyleTransitionRuntimeDecisionKind.Start,
            targetKey,
            previousProperties,
            nextProperties,
            startTimestamp,
            duration,
            easing,
            repeatMode,
            instanceId,
            markers);
    }

    public static StyleTransitionRuntimeDecision Retarget(
        NodeKey targetKey,
        ReadOnlyMemory<VirtualNodeProperty> previousProperties,
        ReadOnlyMemory<VirtualNodeProperty> nextProperties,
        CompositionTimestamp startTimestamp,
        CompositionDuration duration,
        CompositionAnimationEasing easing = CompositionAnimationEasing.Linear,
        CompositionAnimationRepeatMode repeatMode = CompositionAnimationRepeatMode.Once,
        CompositionAnimationInstanceId instanceId = default,
        CompositionAnimationMarker[]? markers = null)
    {
        return new StyleTransitionRuntimeDecision(
            StyleTransitionRuntimeDecisionKind.Retarget,
            targetKey,
            previousProperties,
            nextProperties,
            startTimestamp,
            duration,
            easing,
            repeatMode,
            instanceId,
            markers);
    }

    public static StyleTransitionRuntimeDecision Cancel(NodeKey targetKey) =>
        new(StyleTransitionRuntimeDecisionKind.Cancel, targetKey, default, default, default, default);

    public static StyleTransitionRuntimeDecision Commit(NodeKey targetKey) =>
        new(StyleTransitionRuntimeDecisionKind.Commit, targetKey, default, default, default, default);

    internal StyleTransitionRuntimeDecision WithStartTimestamp(CompositionTimestamp startTimestamp)
    {
        if (!RequiresCompilation || StartTimestamp == startTimestamp)
        {
            return this;
        }

        return new StyleTransitionRuntimeDecision(
            Kind,
            TargetKey,
            PreviousProperties,
            NextProperties,
            startTimestamp,
            Duration,
            Easing,
            RepeatMode,
            InstanceId,
            MarkerArray);
    }

    internal StyleTransitionCompileRequest ToCompileRequest()
    {
        return new StyleTransitionCompileRequest(
            TargetKey,
            PreviousProperties,
            NextProperties,
            StartTimestamp,
            Duration,
            Easing,
            InstanceId,
            MarkerArray,
            RepeatMode);
    }

    public bool Equals(StyleTransitionRuntimeDecision other)
    {
        return Kind == other.Kind
            && TargetKey == other.TargetKey
            && PreviousProperties.Equals(other.PreviousProperties)
            && NextProperties.Equals(other.NextProperties)
            && StartTimestamp == other.StartTimestamp
            && Duration == other.Duration
            && Easing == other.Easing
            && RepeatMode == other.RepeatMode
            && InstanceId == other.InstanceId
            && CompositionAnimationMarker.SequenceEqual(Markers, other.Markers);
    }

    public override bool Equals(object? obj) => obj is StyleTransitionRuntimeDecision other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(Kind);
        hashCode.Add(TargetKey);
        hashCode.Add(PreviousProperties);
        hashCode.Add(NextProperties);
        hashCode.Add(StartTimestamp);
        hashCode.Add(Duration);
        hashCode.Add(Easing);
        hashCode.Add(RepeatMode);
        hashCode.Add(InstanceId);
        CompositionAnimationMarker.AddHashCode(ref hashCode, Markers);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(StyleTransitionRuntimeDecision left, StyleTransitionRuntimeDecision right) => left.Equals(right);

    public static bool operator !=(StyleTransitionRuntimeDecision left, StyleTransitionRuntimeDecision right) => !left.Equals(right);
}

internal readonly struct StyleTransitionRuntimeResult(
    StyleTransitionRuntimeResultKind Kind,
    NodeKey TargetKey,
    StyleTransitionRuntimeFallbackReason FallbackReason = StyleTransitionRuntimeFallbackReason.None,
    StyleTransitionCompileStatus CompileStatus = StyleTransitionCompileStatus.None,
    StyleDeltaPlan DeltaPlan = default,
    bool HasDeclaration = false) : IEquatable<StyleTransitionRuntimeResult>
{
    public StyleTransitionRuntimeResultKind Kind { get; } = Kind;
    public NodeKey TargetKey { get; } = TargetKey;
    public StyleTransitionRuntimeFallbackReason FallbackReason { get; } = FallbackReason;
    public StyleTransitionCompileStatus CompileStatus { get; } = CompileStatus;
    public StyleDeltaPlan DeltaPlan { get; } = DeltaPlan;
    public bool HasDeclaration { get; } = HasDeclaration;

    public static StyleTransitionRuntimeResult NoOp() =>
        new(StyleTransitionRuntimeResultKind.NoOp, NodeKey.None);

    public static StyleTransitionRuntimeResult FromCompileResult(
        StyleTransitionRuntimeResultKind kind,
        NodeKey targetKey,
        in StyleTransitionCompileResult compileResult)
    {
        return new StyleTransitionRuntimeResult(
            kind,
            targetKey,
            StyleTransitionRuntimeFallbackReason.None,
            compileResult.Status,
            compileResult.DeltaPlan,
            compileResult.HasDeclaration);
    }

    public static StyleTransitionRuntimeResult Fallback(
        NodeKey targetKey,
        StyleTransitionRuntimeFallbackReason fallbackReason,
        StyleTransitionCompileStatus compileStatus = StyleTransitionCompileStatus.None,
        StyleDeltaPlan deltaPlan = default)
    {
        return new StyleTransitionRuntimeResult(
            StyleTransitionRuntimeResultKind.Fallback,
            targetKey,
            fallbackReason,
            compileStatus,
            deltaPlan);
    }

    public bool Equals(StyleTransitionRuntimeResult other)
    {
        return Kind == other.Kind
            && TargetKey == other.TargetKey
            && FallbackReason == other.FallbackReason
            && CompileStatus == other.CompileStatus
            && DeltaPlan == other.DeltaPlan
            && HasDeclaration == other.HasDeclaration;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionRuntimeResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetKey, FallbackReason, CompileStatus, DeltaPlan, HasDeclaration);

    public static bool operator ==(StyleTransitionRuntimeResult left, StyleTransitionRuntimeResult right) => left.Equals(right);

    public static bool operator !=(StyleTransitionRuntimeResult left, StyleTransitionRuntimeResult right) => !left.Equals(right);
}

internal sealed class StyleTransitionRuntimeCoordinator
{
    internal StyleTransitionDecisionBatchValidationResult ValidateBatch<TSnapshotProvider>(
        in StyleTransitionDecisionBatch batch,
        TSnapshotProvider snapshotProvider,
        CancellationToken cancellationToken = default)
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StyleTransitionDecisionBatchPreflight.Validate(batch, snapshotProvider);
    }

    internal StyleTransitionBatchRuntimePreflightResult ValidateBatchRuntime<TSnapshotProvider>(
        in StyleTransitionDecisionBatch batch,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker,
        CancellationToken cancellationToken = default)
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StyleTransitionBatchRuntimePreflight.Validate(batch, snapshotProvider, completionTracker);
    }

    internal StyleTransitionBatchPresentationActivationResult ActivateBatchPresentation<TCompositor, TSnapshotProvider>(
        in StyleTransitionDecisionBatch batch,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionPresentationActivationCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = snapshotProvider.LastRetainedInputSnapshot;
        return StyleTransitionBatchPresentationActivation.Activate(
            batch,
            compositor,
            snapshot,
            completionTracker);
    }

    internal StyleTransitionBatchPresentationClearResult ClearBatchPresentation<TCompositor, TSnapshotProvider>(
        in StyleTransitionDecisionBatch batch,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionPresentationClearCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = snapshotProvider.LastRetainedInputSnapshot;
        return StyleTransitionBatchPresentationClear.Clear(
            batch,
            compositor,
            snapshot,
            completionTracker);
    }

    internal async ValueTask<StyleTransitionRuntimeResult> ApplyNextAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CancellationToken cancellationToken = default)
        where TRuntime : IStyleTransitionRuntimeAdapter
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        var decision = runtime.ConsumeStyleTransitionDecision();
        var result = await ApplyDecisionAsync(decision, compositor, snapshotProvider, cancellationToken);
        runtime.PublishStyleTransitionResult(result);
        return result;
    }

    internal static async ValueTask<StyleTransitionRuntimeResult> ApplyDecisionAsync<TCompositor, TSnapshotProvider>(
        StyleTransitionRuntimeDecision decision,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (decision.Kind == StyleTransitionRuntimeDecisionKind.None)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        if (decision.Kind == StyleTransitionRuntimeDecisionKind.Cancel)
        {
            await compositor.CancelAsync(decision.TargetKey, cancellationToken);
            return new StyleTransitionRuntimeResult(StyleTransitionRuntimeResultKind.Canceled, decision.TargetKey);
        }

        if (decision.Kind == StyleTransitionRuntimeDecisionKind.Commit)
        {
            await compositor.CancelAsync(decision.TargetKey, cancellationToken);
            return new StyleTransitionRuntimeResult(StyleTransitionRuntimeResultKind.Committed, decision.TargetKey);
        }

        if (!decision.RequiresCompilation)
        {
            return StyleTransitionRuntimeResult.NoOp();
        }

        var snapshot = snapshotProvider.LastRetainedInputSnapshot;
        if (snapshot is null)
        {
            return StyleTransitionRuntimeResult.Fallback(
                decision.TargetKey,
                StyleTransitionRuntimeFallbackReason.MissingRetainedSnapshot);
        }

        var compileRequest = decision.ToCompileRequest();
        var compileResult = StyleTransitionCompiler.Compile(compileRequest);
        if (!compileResult.HasDeclaration)
        {
            return StyleTransitionRuntimeResult.Fallback(
                decision.TargetKey,
                StyleTransitionRuntimeFallbackReason.CompileRejected,
                compileResult.Status,
                compileResult.DeltaPlan);
        }

        await compositor.StartAsync(compileResult.Declaration, snapshot, cancellationToken);
        var kind = decision.Kind == StyleTransitionRuntimeDecisionKind.Retarget
            ? StyleTransitionRuntimeResultKind.Retargeted
            : StyleTransitionRuntimeResultKind.Started;
        return StyleTransitionRuntimeResult.FromCompileResult(kind, decision.TargetKey, compileResult);
    }
}
