using Irix.Rendering;

namespace Irix.Poc;

internal readonly struct StyleTransitionBatchActivationDiagnosticSnapshot(
    StyleTransitionBatchPresentationActivationResult ActivationResult,
    StyleTransitionRuntimeResult CleanupResult,
    bool HasActiveTransitionAfterCleanup,
    int ActiveOwnerCountAfterCleanup,
    bool HasPresentationPlanAfterCleanup) : IEquatable<StyleTransitionBatchActivationDiagnosticSnapshot>
{
    public StyleTransitionBatchPresentationActivationResult ActivationResult { get; } = ActivationResult;
    public StyleTransitionRuntimeResult CleanupResult { get; } = CleanupResult;
    public bool HasActiveTransitionAfterCleanup { get; } = HasActiveTransitionAfterCleanup;
    public int ActiveOwnerCountAfterCleanup { get; } = ActiveOwnerCountAfterCleanup;
    public bool HasPresentationPlanAfterCleanup { get; } = HasPresentationPlanAfterCleanup;
    public StyleTransitionBatchPresentationActivationKind ActivationKind => ActivationResult.Kind;
    public StyleTransitionBatchPresentationActivationReason ActivationReason => ActivationResult.Reason;
    public StyleTransitionBatchRuntimePreflightKind RuntimePreflightKind => ActivationResult.RuntimePreflight.Kind;
    public CompositionAnimationPresentationSetResultKind PresentationPreflightKind => ActivationResult.ActivationPreflight.Kind;
    public int RuntimeReadyCount => ActivationResult.RuntimePreflight.ReadyCount;
    public int RuntimeBlockedCount => ActivationResult.RuntimePreflight.BlockedCount;
    public int PresentationAcceptedCount => ActivationResult.ActivationPreflight.AcceptedCount;
    public int PresentationRejectedCount => ActivationResult.ActivationPreflight.RejectedCount;
    public int DeclarationCount => ActivationResult.DeclarationCount;
    public int TrackedOwnerCount => ActivationResult.TrackedOwnerCount;
    public bool PresentationStateChanged => ActivationResult.PresentationStateChanged;
    public StyleTransitionRuntimeResultKind CleanupKind => CleanupResult.Kind;
    public NodeKey CleanupTargetKey => CleanupResult.TargetKey;
    public bool CleanupApplied => CleanupResult.Kind is StyleTransitionRuntimeResultKind.Canceled or StyleTransitionRuntimeResultKind.Committed;

    public static StyleTransitionBatchActivationDiagnosticSnapshot Capture(
        in StyleTransitionBatchPresentationActivationResult activationResult,
        in StyleTransitionRuntimeResult cleanupResult,
        StyleTransitionCompletionTracker completionTracker,
        DrawingBackendCompositor compositor)
    {
        ArgumentNullException.ThrowIfNull(completionTracker);
        ArgumentNullException.ThrowIfNull(compositor);
        return new StyleTransitionBatchActivationDiagnosticSnapshot(
            activationResult,
            cleanupResult,
            completionTracker.HasActiveTransition,
            completionTracker.ActiveOwnerCount,
            compositor.CompositionAnimationPresentationPlan is not null);
    }

    public bool Equals(StyleTransitionBatchActivationDiagnosticSnapshot other) =>
        ActivationResult == other.ActivationResult
        && CleanupResult == other.CleanupResult
        && HasActiveTransitionAfterCleanup == other.HasActiveTransitionAfterCleanup
        && ActiveOwnerCountAfterCleanup == other.ActiveOwnerCountAfterCleanup
        && HasPresentationPlanAfterCleanup == other.HasPresentationPlanAfterCleanup;

    public override bool Equals(object? obj) => obj is StyleTransitionBatchActivationDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(ActivationResult, CleanupResult, HasActiveTransitionAfterCleanup, ActiveOwnerCountAfterCleanup, HasPresentationPlanAfterCleanup);

    public static bool operator ==(StyleTransitionBatchActivationDiagnosticSnapshot left, StyleTransitionBatchActivationDiagnosticSnapshot right) =>
        left.Equals(right);

    public static bool operator !=(StyleTransitionBatchActivationDiagnosticSnapshot left, StyleTransitionBatchActivationDiagnosticSnapshot right) =>
        !left.Equals(right);
}
