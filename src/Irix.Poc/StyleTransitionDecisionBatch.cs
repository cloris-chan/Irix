using Irix.Rendering;

namespace Irix.Poc;

internal enum StyleTransitionOwnerKind : byte
{
    None,
    ControlState,
}

internal enum StyleTransitionBatchResultKind : byte
{
    None,
    Empty,
    Accepted,
    Rejected,
    Mixed,
}

internal enum StyleTransitionOwnerValidationKind : byte
{
    None,
    Accepted,
    Rejected,
}

internal enum StyleTransitionOwnerRejectionReason : byte
{
    None,
    MissingOwner,
    MissingRetainedSnapshot,
    MissingRetainedTarget,
    CompileRejected,
    PresentationSetMissingRetainedFrame,
    PresentationSetCommandFrameMismatch,
    PresentationSetInvalidResolvedPlan,
    PresentationSetDuplicateLayerId,
    PresentationSetOverlappingCommandRange,
}

internal enum StyleTransitionBatchRuntimePreflightKind : byte
{
    None,
    Empty,
    Ready,
    Blocked,
    Mixed,
}

internal enum StyleTransitionOwnerRuntimePreflightKind : byte
{
    None,
    Ready,
    Blocked,
}

internal enum StyleTransitionOwnerRuntimeActionKind : byte
{
    None,
    Rejected,
    NoOp,
    StartPresentation,
    RetargetPresentation,
    CancelPresentation,
    CommitPresentation,
}

internal enum StyleTransitionOwnerRuntimeBlocker : byte
{
    None,
    ValidationRejected,
    OwnerTargetMismatch,
    MissingTrackedOwner,
    TrackedTargetMismatch,
}

internal enum StyleTransitionBatchPresentationActivationKind : byte
{
    None,
    Empty,
    Blocked,
    Activated,
}

internal enum StyleTransitionBatchPresentationActivationReason : byte
{
    None,
    EmptyBatch,
    RuntimePreflightNotReady,
    MissingRetainedSnapshot,
    MissingRetainedFrame,
    UnsupportedRuntimeAction,
    NoPresentationOwner,
    CompileRejected,
    PresentationPreflightRejected,
    MissingPresentationPlan,
    ActivationFailed,
}

internal readonly struct StyleTransitionOwnerKey(
    StyleTransitionOwnerKind Kind,
    ActionId ActionId,
    NodeKey TargetKey) : IEquatable<StyleTransitionOwnerKey>
{
    public StyleTransitionOwnerKind Kind { get; } = Kind;
    public ActionId ActionId { get; } = ActionId;
    public NodeKey TargetKey { get; } = TargetKey;

    public bool IsNone => Kind == StyleTransitionOwnerKind.None
        && ActionId.IsNone
        && TargetKey == NodeKey.None;

    public static StyleTransitionOwnerKey ControlState(ActionId actionId, NodeKey targetKey) =>
        new(StyleTransitionOwnerKind.ControlState, actionId, targetKey);

    public bool Equals(StyleTransitionOwnerKey other) =>
        Kind == other.Kind
        && ActionId == other.ActionId
        && TargetKey == other.TargetKey;

    public override bool Equals(object? obj) => obj is StyleTransitionOwnerKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, ActionId, TargetKey);

    public static bool operator ==(StyleTransitionOwnerKey left, StyleTransitionOwnerKey right) => left.Equals(right);

    public static bool operator !=(StyleTransitionOwnerKey left, StyleTransitionOwnerKey right) => !left.Equals(right);
}

internal readonly struct StyleTransitionDecisionBatchEntry(
    StyleTransitionOwnerKey OwnerKey,
    StyleTransitionRuntimeDecision Decision) : IEquatable<StyleTransitionDecisionBatchEntry>
{
    public StyleTransitionOwnerKey OwnerKey { get; } = OwnerKey;
    public StyleTransitionRuntimeDecision Decision { get; } = Decision;

    public bool Equals(StyleTransitionDecisionBatchEntry other) =>
        OwnerKey == other.OwnerKey
        && Decision == other.Decision;

    public override bool Equals(object? obj) => obj is StyleTransitionDecisionBatchEntry other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(OwnerKey, Decision);

    public static bool operator ==(StyleTransitionDecisionBatchEntry left, StyleTransitionDecisionBatchEntry right) => left.Equals(right);

    public static bool operator !=(StyleTransitionDecisionBatchEntry left, StyleTransitionDecisionBatchEntry right) => !left.Equals(right);
}

internal readonly struct StyleTransitionDecisionBatch : IEquatable<StyleTransitionDecisionBatch>
{
    private readonly StyleTransitionDecisionBatchEntry[]? _entries;

    public StyleTransitionDecisionBatch(ReadOnlySpan<StyleTransitionDecisionBatchEntry> entries)
    {
        _entries = entries.IsEmpty ? null : entries.ToArray();
    }

    public ReadOnlySpan<StyleTransitionDecisionBatchEntry> Entries => _entries;
    public int Count => _entries?.Length ?? 0;
    public bool IsEmpty => Count == 0;

    public static StyleTransitionDecisionBatch Empty => default;

    public static StyleTransitionDecisionBatch Create(ReadOnlySpan<StyleTransitionDecisionBatchEntry> entries) =>
        new(entries);

    public bool Equals(StyleTransitionDecisionBatch other)
    {
        var entries = Entries;
        var otherEntries = other.Entries;
        if (entries.Length != otherEntries.Length)
        {
            return false;
        }

        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i] != otherEntries[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionDecisionBatch other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (ref readonly var entry in Entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionDecisionBatch left, StyleTransitionDecisionBatch right) => left.Equals(right);

    public static bool operator !=(StyleTransitionDecisionBatch left, StyleTransitionDecisionBatch right) => !left.Equals(right);
}

internal readonly struct StyleTransitionOwnerValidationResult(
    StyleTransitionOwnerValidationKind Kind,
    StyleTransitionOwnerKey OwnerKey,
    NodeKey TargetKey,
    StyleTransitionRuntimeDecisionKind DecisionKind,
    StyleTransitionOwnerRejectionReason RejectionReason = StyleTransitionOwnerRejectionReason.None,
    StyleTransitionCompileStatus CompileStatus = StyleTransitionCompileStatus.None,
    StyleDeltaPlan DeltaPlan = default,
    bool HasDeclaration = false,
    CompositionLayerId LayerId = default,
    int CommandStart = -1,
    int CommandCount = 0,
    int ConflictingOwnerIndex = -1) : IEquatable<StyleTransitionOwnerValidationResult>
{
    public StyleTransitionOwnerValidationKind Kind { get; } = Kind;
    public StyleTransitionOwnerKey OwnerKey { get; } = OwnerKey;
    public NodeKey TargetKey { get; } = TargetKey;
    public StyleTransitionRuntimeDecisionKind DecisionKind { get; } = DecisionKind;
    public StyleTransitionOwnerRejectionReason RejectionReason { get; } = RejectionReason;
    public StyleTransitionCompileStatus CompileStatus { get; } = CompileStatus;
    public StyleDeltaPlan DeltaPlan { get; } = DeltaPlan;
    public bool HasDeclaration { get; } = HasDeclaration;
    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public int ConflictingOwnerIndex { get; } = ConflictingOwnerIndex;
    public bool IsAccepted => Kind == StyleTransitionOwnerValidationKind.Accepted;
    public bool IsRejected => Kind == StyleTransitionOwnerValidationKind.Rejected;

    internal static StyleTransitionOwnerValidationResult Accepted(
        StyleTransitionOwnerKey ownerKey,
        StyleTransitionRuntimeDecision decision,
        in StyleTransitionCompileResult compileResult)
    {
        return new StyleTransitionOwnerValidationResult(
            StyleTransitionOwnerValidationKind.Accepted,
            ownerKey,
            decision.TargetKey,
            decision.Kind,
            CompileStatus: compileResult.Status,
            DeltaPlan: compileResult.DeltaPlan,
            HasDeclaration: compileResult.HasDeclaration);
    }

    internal StyleTransitionOwnerValidationResult WithPresentationSetValidation(
        in CompositionAnimationPresentationSetEntryValidationResult presentationResult,
        int conflictingOwnerIndex = -1)
    {
        if (presentationResult.IsAccepted)
        {
            return new StyleTransitionOwnerValidationResult(
                Kind,
                OwnerKey,
                TargetKey,
                DecisionKind,
                RejectionReason,
                CompileStatus,
                DeltaPlan,
                HasDeclaration,
                presentationResult.LayerId,
                presentationResult.CommandStart,
                presentationResult.CommandCount,
                ConflictingOwnerIndex);
        }

        return new StyleTransitionOwnerValidationResult(
            StyleTransitionOwnerValidationKind.Rejected,
            OwnerKey,
            TargetKey,
            DecisionKind,
            MapPresentationSetRejection(presentationResult.RejectionReason),
            CompileStatus,
            DeltaPlan,
            HasDeclaration,
            presentationResult.LayerId,
            presentationResult.CommandStart,
            presentationResult.CommandCount,
            conflictingOwnerIndex);
    }

    internal static StyleTransitionOwnerValidationResult Rejected(
        StyleTransitionOwnerKey ownerKey,
        StyleTransitionRuntimeDecision decision,
        StyleTransitionOwnerRejectionReason reason,
        StyleTransitionCompileStatus compileStatus = StyleTransitionCompileStatus.None,
        StyleDeltaPlan deltaPlan = default)
    {
        return new StyleTransitionOwnerValidationResult(
            StyleTransitionOwnerValidationKind.Rejected,
            ownerKey,
            decision.TargetKey,
            decision.Kind,
            reason,
            compileStatus,
            deltaPlan);
    }

    public bool Equals(StyleTransitionOwnerValidationResult other)
    {
        return Kind == other.Kind
            && OwnerKey == other.OwnerKey
            && TargetKey == other.TargetKey
            && DecisionKind == other.DecisionKind
            && RejectionReason == other.RejectionReason
            && CompileStatus == other.CompileStatus
            && DeltaPlan == other.DeltaPlan
            && HasDeclaration == other.HasDeclaration
            && LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && ConflictingOwnerIndex == other.ConflictingOwnerIndex;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionOwnerValidationResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(OwnerKey);
        hash.Add(TargetKey);
        hash.Add(DecisionKind);
        hash.Add(RejectionReason);
        hash.Add(CompileStatus);
        hash.Add(DeltaPlan);
        hash.Add(HasDeclaration);
        hash.Add(LayerId);
        hash.Add(CommandStart);
        hash.Add(CommandCount);
        hash.Add(ConflictingOwnerIndex);
        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionOwnerValidationResult left, StyleTransitionOwnerValidationResult right) => left.Equals(right);

    public static bool operator !=(StyleTransitionOwnerValidationResult left, StyleTransitionOwnerValidationResult right) => !left.Equals(right);

    private static StyleTransitionOwnerRejectionReason MapPresentationSetRejection(
        CompositionAnimationPresentationSetRejectionReason reason)
    {
        return reason switch
        {
            CompositionAnimationPresentationSetRejectionReason.MissingRetainedSnapshot => StyleTransitionOwnerRejectionReason.MissingRetainedSnapshot,
            CompositionAnimationPresentationSetRejectionReason.MissingRetainedFrame => StyleTransitionOwnerRejectionReason.PresentationSetMissingRetainedFrame,
            CompositionAnimationPresentationSetRejectionReason.CommandFrameMismatch => StyleTransitionOwnerRejectionReason.PresentationSetCommandFrameMismatch,
            CompositionAnimationPresentationSetRejectionReason.MissingRetainedTarget => StyleTransitionOwnerRejectionReason.MissingRetainedTarget,
            CompositionAnimationPresentationSetRejectionReason.InvalidResolvedPlan => StyleTransitionOwnerRejectionReason.PresentationSetInvalidResolvedPlan,
            CompositionAnimationPresentationSetRejectionReason.DuplicateLayerId => StyleTransitionOwnerRejectionReason.PresentationSetDuplicateLayerId,
            CompositionAnimationPresentationSetRejectionReason.OverlappingCommandRange => StyleTransitionOwnerRejectionReason.PresentationSetOverlappingCommandRange,
            _ => StyleTransitionOwnerRejectionReason.PresentationSetInvalidResolvedPlan,
        };
    }
}

internal readonly struct StyleTransitionDecisionBatchValidationResult : IEquatable<StyleTransitionDecisionBatchValidationResult>
{
    private readonly StyleTransitionOwnerValidationResult[]? _ownerResults;

    internal StyleTransitionDecisionBatchValidationResult(
        StyleTransitionBatchResultKind Kind,
        ReadOnlySpan<StyleTransitionOwnerValidationResult> ownerResults,
        bool PresentationStateChanged)
    {
        this.Kind = Kind;
        _ownerResults = ownerResults.IsEmpty ? null : ownerResults.ToArray();
        this.PresentationStateChanged = PresentationStateChanged;
    }

    public StyleTransitionBatchResultKind Kind { get; }
    public ReadOnlySpan<StyleTransitionOwnerValidationResult> OwnerResults => _ownerResults;
    public bool PresentationStateChanged { get; }
    public int AcceptedCount => Count(StyleTransitionOwnerValidationKind.Accepted);
    public int RejectedCount => Count(StyleTransitionOwnerValidationKind.Rejected);

    public bool Equals(StyleTransitionDecisionBatchValidationResult other)
    {
        if (Kind != other.Kind || PresentationStateChanged != other.PresentationStateChanged)
        {
            return false;
        }

        var results = OwnerResults;
        var otherResults = other.OwnerResults;
        if (results.Length != otherResults.Length)
        {
            return false;
        }

        for (var i = 0; i < results.Length; i++)
        {
            if (results[i] != otherResults[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionDecisionBatchValidationResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(PresentationStateChanged);
        foreach (ref readonly var result in OwnerResults)
        {
            hash.Add(result);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionDecisionBatchValidationResult left, StyleTransitionDecisionBatchValidationResult right) =>
        left.Equals(right);

    public static bool operator !=(StyleTransitionDecisionBatchValidationResult left, StyleTransitionDecisionBatchValidationResult right) =>
        !left.Equals(right);

    private int Count(StyleTransitionOwnerValidationKind kind)
    {
        var count = 0;
        foreach (ref readonly var result in OwnerResults)
        {
            if (result.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }
}

internal readonly struct StyleTransitionOwnerRuntimePreflightResult(
    StyleTransitionOwnerRuntimePreflightKind Kind,
    StyleTransitionOwnerKey OwnerKey,
    NodeKey TargetKey,
    StyleTransitionRuntimeDecisionKind DecisionKind,
    StyleTransitionOwnerRuntimeActionKind ActionKind,
    StyleTransitionOwnerRuntimeBlocker Blocker = StyleTransitionOwnerRuntimeBlocker.None,
    StyleTransitionOwnerValidationResult ValidationResult = default,
    bool RequiresPresentationSetInstall = false,
    bool RequiresCompletionTracking = false,
    bool WouldAttachCompletionMarker = false,
    bool ClearsTrackedOwner = false,
    bool HasTrackedOwner = false,
    CompositionAnimationInstanceId TrackedInstanceId = default) : IEquatable<StyleTransitionOwnerRuntimePreflightResult>
{
    public StyleTransitionOwnerRuntimePreflightKind Kind { get; } = Kind;
    public StyleTransitionOwnerKey OwnerKey { get; } = OwnerKey;
    public NodeKey TargetKey { get; } = TargetKey;
    public StyleTransitionRuntimeDecisionKind DecisionKind { get; } = DecisionKind;
    public StyleTransitionOwnerRuntimeActionKind ActionKind { get; } = ActionKind;
    public StyleTransitionOwnerRuntimeBlocker Blocker { get; } = Blocker;
    public StyleTransitionOwnerValidationResult ValidationResult { get; } = ValidationResult;
    public bool RequiresPresentationSetInstall { get; } = RequiresPresentationSetInstall;
    public bool RequiresCompletionTracking { get; } = RequiresCompletionTracking;
    public bool WouldAttachCompletionMarker { get; } = WouldAttachCompletionMarker;
    public bool ClearsTrackedOwner { get; } = ClearsTrackedOwner;
    public bool HasTrackedOwner { get; } = HasTrackedOwner;
    public CompositionAnimationInstanceId TrackedInstanceId { get; } = TrackedInstanceId;
    public bool IsReady => Kind == StyleTransitionOwnerRuntimePreflightKind.Ready;
    public bool IsBlocked => Kind == StyleTransitionOwnerRuntimePreflightKind.Blocked;

    internal static StyleTransitionOwnerRuntimePreflightResult Ready(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        StyleTransitionRuntimeDecisionKind decisionKind,
        StyleTransitionOwnerRuntimeActionKind actionKind,
        in StyleTransitionOwnerValidationResult validationResult,
        bool requiresPresentationSetInstall = false,
        bool requiresCompletionTracking = false,
        bool wouldAttachCompletionMarker = false,
        bool clearsTrackedOwner = false,
        bool hasTrackedOwner = false,
        CompositionAnimationInstanceId trackedInstanceId = default)
    {
        return new StyleTransitionOwnerRuntimePreflightResult(
            StyleTransitionOwnerRuntimePreflightKind.Ready,
            ownerKey,
            targetKey,
            decisionKind,
            actionKind,
            ValidationResult: validationResult,
            RequiresPresentationSetInstall: requiresPresentationSetInstall,
            RequiresCompletionTracking: requiresCompletionTracking,
            WouldAttachCompletionMarker: wouldAttachCompletionMarker,
            ClearsTrackedOwner: clearsTrackedOwner,
            HasTrackedOwner: hasTrackedOwner,
            TrackedInstanceId: trackedInstanceId);
    }

    internal static StyleTransitionOwnerRuntimePreflightResult Blocked(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        StyleTransitionRuntimeDecisionKind decisionKind,
        StyleTransitionOwnerRuntimeActionKind actionKind,
        StyleTransitionOwnerRuntimeBlocker blocker,
        in StyleTransitionOwnerValidationResult validationResult,
        bool hasTrackedOwner = false,
        CompositionAnimationInstanceId trackedInstanceId = default)
    {
        return new StyleTransitionOwnerRuntimePreflightResult(
            StyleTransitionOwnerRuntimePreflightKind.Blocked,
            ownerKey,
            targetKey,
            decisionKind,
            actionKind,
            blocker,
            validationResult,
            HasTrackedOwner: hasTrackedOwner,
            TrackedInstanceId: trackedInstanceId);
    }

    public bool Equals(StyleTransitionOwnerRuntimePreflightResult other)
    {
        return Kind == other.Kind
            && OwnerKey == other.OwnerKey
            && TargetKey == other.TargetKey
            && DecisionKind == other.DecisionKind
            && ActionKind == other.ActionKind
            && Blocker == other.Blocker
            && ValidationResult == other.ValidationResult
            && RequiresPresentationSetInstall == other.RequiresPresentationSetInstall
            && RequiresCompletionTracking == other.RequiresCompletionTracking
            && WouldAttachCompletionMarker == other.WouldAttachCompletionMarker
            && ClearsTrackedOwner == other.ClearsTrackedOwner
            && HasTrackedOwner == other.HasTrackedOwner
            && TrackedInstanceId == other.TrackedInstanceId;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionOwnerRuntimePreflightResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(OwnerKey);
        hash.Add(TargetKey);
        hash.Add(DecisionKind);
        hash.Add(ActionKind);
        hash.Add(Blocker);
        hash.Add(ValidationResult);
        hash.Add(RequiresPresentationSetInstall);
        hash.Add(RequiresCompletionTracking);
        hash.Add(WouldAttachCompletionMarker);
        hash.Add(ClearsTrackedOwner);
        hash.Add(HasTrackedOwner);
        hash.Add(TrackedInstanceId);
        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionOwnerRuntimePreflightResult left, StyleTransitionOwnerRuntimePreflightResult right) =>
        left.Equals(right);

    public static bool operator !=(StyleTransitionOwnerRuntimePreflightResult left, StyleTransitionOwnerRuntimePreflightResult right) =>
        !left.Equals(right);
}

internal readonly struct StyleTransitionBatchRuntimePreflightResult : IEquatable<StyleTransitionBatchRuntimePreflightResult>
{
    private readonly StyleTransitionOwnerRuntimePreflightResult[]? _ownerResults;

    internal StyleTransitionBatchRuntimePreflightResult(
        StyleTransitionBatchRuntimePreflightKind Kind,
        StyleTransitionDecisionBatchValidationResult Validation,
        ReadOnlySpan<StyleTransitionOwnerRuntimePreflightResult> ownerResults,
        bool PresentationStateChanged)
    {
        this.Kind = Kind;
        this.Validation = Validation;
        _ownerResults = ownerResults.IsEmpty ? null : ownerResults.ToArray();
        this.PresentationStateChanged = PresentationStateChanged;
    }

    public StyleTransitionBatchRuntimePreflightKind Kind { get; }
    public StyleTransitionDecisionBatchValidationResult Validation { get; }
    public ReadOnlySpan<StyleTransitionOwnerRuntimePreflightResult> OwnerResults => _ownerResults;
    public bool PresentationStateChanged { get; }
    public int ReadyCount => Count(StyleTransitionOwnerRuntimePreflightKind.Ready);
    public int BlockedCount => Count(StyleTransitionOwnerRuntimePreflightKind.Blocked);
    public bool RequiresPresentationSetInstall => Any(result => result.RequiresPresentationSetInstall);
    public bool RequiresCompletionTracking => Any(result => result.RequiresCompletionTracking);
    public bool RequiresTrackedOwnerClear => Any(result => result.ClearsTrackedOwner);

    public bool Equals(StyleTransitionBatchRuntimePreflightResult other)
    {
        if (Kind != other.Kind
            || Validation != other.Validation
            || PresentationStateChanged != other.PresentationStateChanged)
        {
            return false;
        }

        var results = OwnerResults;
        var otherResults = other.OwnerResults;
        if (results.Length != otherResults.Length)
        {
            return false;
        }

        for (var i = 0; i < results.Length; i++)
        {
            if (results[i] != otherResults[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionBatchRuntimePreflightResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Validation);
        hash.Add(PresentationStateChanged);
        foreach (ref readonly var result in OwnerResults)
        {
            hash.Add(result);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(StyleTransitionBatchRuntimePreflightResult left, StyleTransitionBatchRuntimePreflightResult right) =>
        left.Equals(right);

    public static bool operator !=(StyleTransitionBatchRuntimePreflightResult left, StyleTransitionBatchRuntimePreflightResult right) =>
        !left.Equals(right);

    private int Count(StyleTransitionOwnerRuntimePreflightKind kind)
    {
        var count = 0;
        foreach (ref readonly var result in OwnerResults)
        {
            if (result.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private bool Any(Func<StyleTransitionOwnerRuntimePreflightResult, bool> predicate)
    {
        foreach (ref readonly var result in OwnerResults)
        {
            if (predicate(result))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly struct StyleTransitionBatchPresentationActivationResult(
    StyleTransitionBatchPresentationActivationKind Kind,
    StyleTransitionBatchPresentationActivationReason Reason,
    StyleTransitionBatchRuntimePreflightResult RuntimePreflight = default,
    CompositionAnimationPresentationSetActivationPreflightResult ActivationPreflight = default,
    int DeclarationCount = 0,
    int TrackedOwnerCount = 0,
    bool PresentationStateChanged = false) : IEquatable<StyleTransitionBatchPresentationActivationResult>
{
    public StyleTransitionBatchPresentationActivationKind Kind { get; } = Kind;
    public StyleTransitionBatchPresentationActivationReason Reason { get; } = Reason;
    public StyleTransitionBatchRuntimePreflightResult RuntimePreflight { get; } = RuntimePreflight;
    public CompositionAnimationPresentationSetActivationPreflightResult ActivationPreflight { get; } = ActivationPreflight;
    public int DeclarationCount { get; } = DeclarationCount;
    public int TrackedOwnerCount { get; } = TrackedOwnerCount;
    public bool PresentationStateChanged { get; } = PresentationStateChanged;
    public bool IsActivated => Kind == StyleTransitionBatchPresentationActivationKind.Activated;

    internal static StyleTransitionBatchPresentationActivationResult Empty(
        in StyleTransitionBatchRuntimePreflightResult runtimePreflight) =>
        new(
            StyleTransitionBatchPresentationActivationKind.Empty,
            StyleTransitionBatchPresentationActivationReason.EmptyBatch,
            runtimePreflight);

    internal static StyleTransitionBatchPresentationActivationResult Blocked(
        StyleTransitionBatchPresentationActivationReason reason,
        in StyleTransitionBatchRuntimePreflightResult runtimePreflight,
        in CompositionAnimationPresentationSetActivationPreflightResult activationPreflight = default,
        int declarationCount = 0) =>
        new(
            StyleTransitionBatchPresentationActivationKind.Blocked,
            reason,
            runtimePreflight,
            activationPreflight,
            declarationCount);

    internal static StyleTransitionBatchPresentationActivationResult Activated(
        in StyleTransitionBatchRuntimePreflightResult runtimePreflight,
        in CompositionAnimationPresentationSetActivationPreflightResult activationPreflight,
        int declarationCount,
        int trackedOwnerCount) =>
        new(
            StyleTransitionBatchPresentationActivationKind.Activated,
            StyleTransitionBatchPresentationActivationReason.None,
            runtimePreflight,
            activationPreflight,
            declarationCount,
            trackedOwnerCount,
            PresentationStateChanged: true);

    public bool Equals(StyleTransitionBatchPresentationActivationResult other) =>
        Kind == other.Kind
        && Reason == other.Reason
        && RuntimePreflight == other.RuntimePreflight
        && ActivationPreflight == other.ActivationPreflight
        && DeclarationCount == other.DeclarationCount
        && TrackedOwnerCount == other.TrackedOwnerCount
        && PresentationStateChanged == other.PresentationStateChanged;

    public override bool Equals(object? obj) => obj is StyleTransitionBatchPresentationActivationResult other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Reason, RuntimePreflight, ActivationPreflight, DeclarationCount, TrackedOwnerCount, PresentationStateChanged);

    public static bool operator ==(StyleTransitionBatchPresentationActivationResult left, StyleTransitionBatchPresentationActivationResult right) =>
        left.Equals(right);

    public static bool operator !=(StyleTransitionBatchPresentationActivationResult left, StyleTransitionBatchPresentationActivationResult right) =>
        !left.Equals(right);
}

internal static class StyleTransitionDecisionBatchPreflight
{
    internal static StyleTransitionDecisionBatchValidationResult Validate(
        in StyleTransitionDecisionBatch batch,
        IStyleTransitionRetainedSnapshotProvider snapshotProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);

        if (batch.IsEmpty)
        {
            return new StyleTransitionDecisionBatchValidationResult(
                StyleTransitionBatchResultKind.Empty,
                [],
                PresentationStateChanged: false);
        }

        var entries = batch.Entries;
        var snapshot = snapshotProvider.LastRetainedInputSnapshot;
        var results = new StyleTransitionOwnerValidationResult[entries.Length];
        var declarations = new CompositionAnimationDeclaration[entries.Length];
        var declarationOwnerIndices = new int[entries.Length];
        var declarationCount = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var result = ValidateEntry(entry, snapshot, out var declaration);
            results[i] = result;

            if (result.IsAccepted && result.HasDeclaration)
            {
                declarations[declarationCount] = declaration;
                declarationOwnerIndices[declarationCount] = i;
                declarationCount++;
            }
        }

        if (declarationCount > 0)
        {
            var presentationSetValidation = CompositionAnimationPresentationSetValidator.Validate(
                declarations.AsSpan(0, declarationCount),
                snapshot,
                snapshot?.CommandCount ?? 0);
            var presentationResults = presentationSetValidation.EntryResults;
            for (var i = 0; i < presentationResults.Length; i++)
            {
                var ownerIndex = declarationOwnerIndices[i];
                var conflictingOwnerIndex = presentationResults[i].ConflictingIndex >= 0
                    ? declarationOwnerIndices[presentationResults[i].ConflictingIndex]
                    : -1;
                results[ownerIndex] = results[ownerIndex].WithPresentationSetValidation(
                    presentationResults[i],
                    conflictingOwnerIndex);
            }
        }

        return new StyleTransitionDecisionBatchValidationResult(
            ResolveKind(results),
            results,
            PresentationStateChanged: false);
    }

    private static StyleTransitionOwnerValidationResult ValidateEntry(
        in StyleTransitionDecisionBatchEntry entry,
        RenderPipelineRetainedInputSnapshot? snapshot,
        out CompositionAnimationDeclaration declaration)
    {
        declaration = default;

        if (entry.OwnerKey.IsNone)
        {
            return StyleTransitionOwnerValidationResult.Rejected(
                entry.OwnerKey,
                entry.Decision,
                StyleTransitionOwnerRejectionReason.MissingOwner);
        }

        if (snapshot is null)
        {
            return StyleTransitionOwnerValidationResult.Rejected(
                entry.OwnerKey,
                entry.Decision,
                StyleTransitionOwnerRejectionReason.MissingRetainedSnapshot);
        }

        if (!snapshot.TryGetCompositionTarget(entry.Decision.TargetKey, out _))
        {
            return StyleTransitionOwnerValidationResult.Rejected(
                entry.OwnerKey,
                entry.Decision,
                StyleTransitionOwnerRejectionReason.MissingRetainedTarget);
        }

        if (!entry.Decision.RequiresCompilation)
        {
            return StyleTransitionOwnerValidationResult.Accepted(
                entry.OwnerKey,
                entry.Decision,
                default);
        }

        var compileResult = StyleTransitionCompiler.Compile(entry.Decision.ToCompileRequest());
        declaration = compileResult.Declaration;
        if (!compileResult.HasDeclaration)
        {
            return StyleTransitionOwnerValidationResult.Rejected(
                entry.OwnerKey,
                entry.Decision,
                StyleTransitionOwnerRejectionReason.CompileRejected,
                compileResult.Status,
                compileResult.DeltaPlan);
        }

        return StyleTransitionOwnerValidationResult.Accepted(entry.OwnerKey, entry.Decision, compileResult);
    }

    private static StyleTransitionBatchResultKind ResolveKind(ReadOnlySpan<StyleTransitionOwnerValidationResult> results)
    {
        var acceptedCount = 0;
        var rejectedCount = 0;
        foreach (ref readonly var result in results)
        {
            if (result.IsAccepted)
            {
                acceptedCount++;
            }
            else if (result.IsRejected)
            {
                rejectedCount++;
            }
        }

        return acceptedCount == results.Length
            ? StyleTransitionBatchResultKind.Accepted
            : rejectedCount == results.Length
                ? StyleTransitionBatchResultKind.Rejected
                : StyleTransitionBatchResultKind.Mixed;
    }
}

internal static class StyleTransitionBatchRuntimePreflight
{
    internal static StyleTransitionBatchRuntimePreflightResult Validate(
        in StyleTransitionDecisionBatch batch,
        IStyleTransitionRetainedSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker)
    {
        ArgumentNullException.ThrowIfNull(snapshotProvider);
        ArgumentNullException.ThrowIfNull(completionTracker);

        var validation = StyleTransitionDecisionBatchPreflight.Validate(batch, snapshotProvider);
        if (batch.IsEmpty)
        {
            return new StyleTransitionBatchRuntimePreflightResult(
                StyleTransitionBatchRuntimePreflightKind.Empty,
                validation,
                [],
                PresentationStateChanged: false);
        }

        var entries = batch.Entries;
        var validationResults = validation.OwnerResults;
        var runtimeResults = new StyleTransitionOwnerRuntimePreflightResult[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            runtimeResults[i] = ValidateOwner(entries[i], validationResults[i], completionTracker);
        }

        return new StyleTransitionBatchRuntimePreflightResult(
            ResolveKind(runtimeResults),
            validation,
            runtimeResults,
            PresentationStateChanged: false);
    }

    private static StyleTransitionOwnerRuntimePreflightResult ValidateOwner(
        in StyleTransitionDecisionBatchEntry entry,
        in StyleTransitionOwnerValidationResult validationResult,
        StyleTransitionCompletionTracker completionTracker)
    {
        if (validationResult.IsRejected)
        {
            return StyleTransitionOwnerRuntimePreflightResult.Blocked(
                validationResult.OwnerKey,
                validationResult.TargetKey,
                validationResult.DecisionKind,
                StyleTransitionOwnerRuntimeActionKind.Rejected,
                StyleTransitionOwnerRuntimeBlocker.ValidationRejected,
                validationResult);
        }

        if (entry.OwnerKey.TargetKey != entry.Decision.TargetKey)
        {
            return StyleTransitionOwnerRuntimePreflightResult.Blocked(
                entry.OwnerKey,
                entry.Decision.TargetKey,
                entry.Decision.Kind,
                ResolveActionKind(entry.Decision.Kind),
                StyleTransitionOwnerRuntimeBlocker.OwnerTargetMismatch,
                validationResult);
        }

        return entry.Decision.Kind switch
        {
            StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget =>
                ValidateStartOrRetarget(entry, validationResult),
            StyleTransitionRuntimeDecisionKind.Cancel or StyleTransitionRuntimeDecisionKind.Commit =>
                ValidateClear(entry, validationResult, completionTracker),
            StyleTransitionRuntimeDecisionKind.None =>
                StyleTransitionOwnerRuntimePreflightResult.Ready(
                    entry.OwnerKey,
                    entry.Decision.TargetKey,
                    entry.Decision.Kind,
                    StyleTransitionOwnerRuntimeActionKind.NoOp,
                    validationResult),
            _ => StyleTransitionOwnerRuntimePreflightResult.Blocked(
                entry.OwnerKey,
                entry.Decision.TargetKey,
                entry.Decision.Kind,
                StyleTransitionOwnerRuntimeActionKind.NoOp,
                StyleTransitionOwnerRuntimeBlocker.ValidationRejected,
                validationResult),
        };
    }

    private static StyleTransitionOwnerRuntimePreflightResult ValidateStartOrRetarget(
        in StyleTransitionDecisionBatchEntry entry,
        in StyleTransitionOwnerValidationResult validationResult)
    {
        var requiresCompletionTracking = CanTrackCompletion(entry.OwnerKey, entry.Decision);
        return StyleTransitionOwnerRuntimePreflightResult.Ready(
            entry.OwnerKey,
            entry.Decision.TargetKey,
            entry.Decision.Kind,
            ResolveActionKind(entry.Decision.Kind),
            validationResult,
            requiresPresentationSetInstall: validationResult.HasDeclaration,
            requiresCompletionTracking: requiresCompletionTracking,
            wouldAttachCompletionMarker: requiresCompletionTracking && !HasCompletionMarker(entry.Decision.Markers));
    }

    private static StyleTransitionOwnerRuntimePreflightResult ValidateClear(
        in StyleTransitionDecisionBatchEntry entry,
        in StyleTransitionOwnerValidationResult validationResult,
        StyleTransitionCompletionTracker completionTracker)
    {
        if (!completionTracker.TryGetActiveTransition(entry.OwnerKey, out var trackedTarget, out var trackedInstance))
        {
            return StyleTransitionOwnerRuntimePreflightResult.Blocked(
                entry.OwnerKey,
                entry.Decision.TargetKey,
                entry.Decision.Kind,
                ResolveActionKind(entry.Decision.Kind),
                StyleTransitionOwnerRuntimeBlocker.MissingTrackedOwner,
                validationResult);
        }

        if (trackedTarget != entry.Decision.TargetKey)
        {
            return StyleTransitionOwnerRuntimePreflightResult.Blocked(
                entry.OwnerKey,
                entry.Decision.TargetKey,
                entry.Decision.Kind,
                ResolveActionKind(entry.Decision.Kind),
                StyleTransitionOwnerRuntimeBlocker.TrackedTargetMismatch,
                validationResult,
                hasTrackedOwner: true,
                trackedInstanceId: trackedInstance);
        }

        return StyleTransitionOwnerRuntimePreflightResult.Ready(
            entry.OwnerKey,
            entry.Decision.TargetKey,
            entry.Decision.Kind,
            ResolveActionKind(entry.Decision.Kind),
            validationResult,
            clearsTrackedOwner: true,
            hasTrackedOwner: true,
            trackedInstanceId: trackedInstance);
    }

    private static StyleTransitionBatchRuntimePreflightKind ResolveKind(
        ReadOnlySpan<StyleTransitionOwnerRuntimePreflightResult> results)
    {
        var readyCount = 0;
        var blockedCount = 0;
        foreach (ref readonly var result in results)
        {
            if (result.IsReady)
            {
                readyCount++;
            }
            else if (result.IsBlocked)
            {
                blockedCount++;
            }
        }

        return readyCount == results.Length
            ? StyleTransitionBatchRuntimePreflightKind.Ready
            : blockedCount == results.Length
                ? StyleTransitionBatchRuntimePreflightKind.Blocked
                : StyleTransitionBatchRuntimePreflightKind.Mixed;
    }

    private static StyleTransitionOwnerRuntimeActionKind ResolveActionKind(
        StyleTransitionRuntimeDecisionKind decisionKind)
    {
        return decisionKind switch
        {
            StyleTransitionRuntimeDecisionKind.Start => StyleTransitionOwnerRuntimeActionKind.StartPresentation,
            StyleTransitionRuntimeDecisionKind.Retarget => StyleTransitionOwnerRuntimeActionKind.RetargetPresentation,
            StyleTransitionRuntimeDecisionKind.Cancel => StyleTransitionOwnerRuntimeActionKind.CancelPresentation,
            StyleTransitionRuntimeDecisionKind.Commit => StyleTransitionOwnerRuntimeActionKind.CommitPresentation,
            StyleTransitionRuntimeDecisionKind.None => StyleTransitionOwnerRuntimeActionKind.NoOp,
            _ => StyleTransitionOwnerRuntimeActionKind.None,
        };
    }

    private static bool CanTrackCompletion(
        StyleTransitionOwnerKey ownerKey,
        in StyleTransitionRuntimeDecision decision)
    {
        return !ownerKey.IsNone
            && ownerKey.TargetKey == decision.TargetKey
            && decision.Kind is StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget
            && decision.TargetKey != NodeKey.None
            && decision.InstanceId.IsValid
            && decision.RepeatMode == CompositionAnimationRepeatMode.Once;
    }

    private static bool HasCompletionMarker(ReadOnlySpan<CompositionAnimationMarker> markers)
    {
        for (var i = 0; i < markers.Length; i++)
        {
            if (markers[i].Id == StyleTransitionCompletionTracker.CompletionMarkerId
                && markers[i].RuntimeEventId == StyleTransitionCompletionTracker.CompletionRuntimeEventId)
            {
                return true;
            }
        }

        return false;
    }
}

internal static class StyleTransitionBatchPresentationActivation
{
    internal static StyleTransitionBatchPresentationActivationResult Activate<TCompositor>(
        in StyleTransitionDecisionBatch batch,
        TCompositor compositor,
        RenderPipelineRetainedInputSnapshot? snapshot,
        StyleTransitionCompletionTracker completionTracker)
        where TCompositor : IStyleTransitionPresentationActivationCompositorAdapter
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(completionTracker);

        var runtimePreflight = StyleTransitionBatchRuntimePreflight.Validate(
            batch,
            new FixedStyleTransitionRetainedSnapshotProvider(snapshot),
            completionTracker);
        if (runtimePreflight.Kind == StyleTransitionBatchRuntimePreflightKind.Empty)
        {
            return StyleTransitionBatchPresentationActivationResult.Empty(runtimePreflight);
        }

        if (snapshot is null)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.MissingRetainedSnapshot,
                runtimePreflight);
        }

        if (runtimePreflight.Kind != StyleTransitionBatchRuntimePreflightKind.Ready)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.RuntimePreflightNotReady,
                runtimePreflight);
        }

        var entries = batch.Entries;
        var ownerResults = runtimePreflight.OwnerResults;
        var declarations = new CompositionAnimationDeclaration[ownerResults.Length];
        var trackedDecisions = new StyleTransitionRuntimeDecision[ownerResults.Length];
        var trackedOwnerKeys = new StyleTransitionOwnerKey[ownerResults.Length];
        var trackedResults = new StyleTransitionRuntimeResult[ownerResults.Length];
        var declarationCount = 0;
        var trackedCount = 0;

        for (var i = 0; i < ownerResults.Length; i++)
        {
            var ownerResult = ownerResults[i];
            if (ownerResult.ActionKind is StyleTransitionOwnerRuntimeActionKind.CancelPresentation
                or StyleTransitionOwnerRuntimeActionKind.CommitPresentation)
            {
                return StyleTransitionBatchPresentationActivationResult.Blocked(
                    StyleTransitionBatchPresentationActivationReason.UnsupportedRuntimeAction,
                    runtimePreflight);
            }

            if (ownerResult.ActionKind == StyleTransitionOwnerRuntimeActionKind.NoOp)
            {
                return StyleTransitionBatchPresentationActivationResult.Blocked(
                    StyleTransitionBatchPresentationActivationReason.NoPresentationOwner,
                    runtimePreflight);
            }

            if (ownerResult.ActionKind is not (StyleTransitionOwnerRuntimeActionKind.StartPresentation
                or StyleTransitionOwnerRuntimeActionKind.RetargetPresentation))
            {
                return StyleTransitionBatchPresentationActivationResult.Blocked(
                    StyleTransitionBatchPresentationActivationReason.RuntimePreflightNotReady,
                    runtimePreflight);
            }

            if (!ownerResult.RequiresPresentationSetInstall)
            {
                continue;
            }

            var entry = entries[i];
            var decision = ownerResult.RequiresCompletionTracking
                ? completionTracker.AttachCompletionMarker(entry.OwnerKey, entry.Decision)
                : entry.Decision;
            var compileResult = StyleTransitionCompiler.Compile(decision.ToCompileRequest());
            if (!compileResult.HasDeclaration)
            {
                return StyleTransitionBatchPresentationActivationResult.Blocked(
                    StyleTransitionBatchPresentationActivationReason.CompileRejected,
                    runtimePreflight,
                    declarationCount: declarationCount);
            }

            declarations[declarationCount++] = compileResult.Declaration;
            if (ownerResult.RequiresCompletionTracking)
            {
                trackedOwnerKeys[trackedCount] = entry.OwnerKey;
                trackedDecisions[trackedCount] = decision;
                trackedResults[trackedCount] = StyleTransitionRuntimeResult.FromCompileResult(
                    decision.Kind == StyleTransitionRuntimeDecisionKind.Retarget
                        ? StyleTransitionRuntimeResultKind.Retargeted
                        : StyleTransitionRuntimeResultKind.Started,
                    decision.TargetKey,
                    compileResult);
                trackedCount++;
            }
        }

        if (declarationCount == 0)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.NoPresentationOwner,
                runtimePreflight);
        }

        var activationPreflight = compositor.PreparePresentationActivation(
            declarations.AsSpan(0, declarationCount),
            snapshot);
        if (activationPreflight.Kind != CompositionAnimationPresentationSetResultKind.Accepted)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                MapActivationRejection(activationPreflight),
                runtimePreflight,
                activationPreflight,
                declarationCount);
        }

        if (!activationPreflight.HasPlan)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.MissingPresentationPlan,
                runtimePreflight,
                activationPreflight,
                declarationCount);
        }

        try
        {
            compositor.ActivatePresentationPlan(activationPreflight.Plan);
        }
        catch (ArgumentException)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.ActivationFailed,
                runtimePreflight,
                activationPreflight,
                declarationCount);
        }
        catch (InvalidOperationException)
        {
            return StyleTransitionBatchPresentationActivationResult.Blocked(
                StyleTransitionBatchPresentationActivationReason.ActivationFailed,
                runtimePreflight,
                activationPreflight,
                declarationCount);
        }

        var trackedOwnerCount = 0;
        for (var i = 0; i < trackedCount; i++)
        {
            var tracking = completionTracker.PublishRuntimeResult(
                trackedOwnerKeys[i],
                trackedDecisions[i],
                trackedResults[i]);
            if (tracking.Kind is StyleTransitionCompletionResultKind.TrackingStarted
                or StyleTransitionCompletionResultKind.TrackingRetargeted)
            {
                trackedOwnerCount++;
            }
        }

        return StyleTransitionBatchPresentationActivationResult.Activated(
            runtimePreflight,
            activationPreflight,
            declarationCount,
            trackedOwnerCount);
    }

    private static StyleTransitionBatchPresentationActivationReason MapActivationRejection(
        in CompositionAnimationPresentationSetActivationPreflightResult activationPreflight)
    {
        var entries = activationPreflight.Entries;
        for (var i = 0; i < entries.Length; i++)
        {
            var reason = entries[i].Validation.RejectionReason;
            if (reason == CompositionAnimationPresentationSetRejectionReason.MissingRetainedFrame)
            {
                return StyleTransitionBatchPresentationActivationReason.MissingRetainedFrame;
            }

            if (reason != CompositionAnimationPresentationSetRejectionReason.None)
            {
                return StyleTransitionBatchPresentationActivationReason.PresentationPreflightRejected;
            }
        }

        return StyleTransitionBatchPresentationActivationReason.PresentationPreflightRejected;
    }
}
