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
