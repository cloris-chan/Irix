namespace Irix.Rendering;

internal enum CompositionAnimationPresentationSetResultKind : byte
{
    None,
    Empty,
    Accepted,
    Rejected,
    Mixed,
}

internal enum CompositionAnimationPresentationSetEntryKind : byte
{
    None,
    Accepted,
    Rejected,
}

internal enum CompositionAnimationPresentationSetRejectionReason : byte
{
    None,
    MissingRetainedSnapshot,
    MissingRetainedFrame,
    CommandFrameMismatch,
    MissingRetainedTarget,
    InvalidResolvedPlan,
    DuplicateLayerId,
    OverlappingCommandRange,
}

internal readonly struct CompositionAnimationPresentationSetEntryValidationResult(
    CompositionAnimationPresentationSetEntryKind Kind,
    int Index,
    NodeKey TargetKey,
    CompositionAnimationPresentationSetRejectionReason RejectionReason = CompositionAnimationPresentationSetRejectionReason.None,
    CompositionLayerId LayerId = default,
    int CommandStart = -1,
    int CommandCount = 0,
    int ConflictingIndex = -1) : IEquatable<CompositionAnimationPresentationSetEntryValidationResult>
{
    public CompositionAnimationPresentationSetEntryKind Kind { get; } = Kind;
    public int Index { get; } = Index;
    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionAnimationPresentationSetRejectionReason RejectionReason { get; } = RejectionReason;
    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public int ConflictingIndex { get; } = ConflictingIndex;
    public bool IsAccepted => Kind == CompositionAnimationPresentationSetEntryKind.Accepted;
    public bool IsRejected => Kind == CompositionAnimationPresentationSetEntryKind.Rejected;

    internal static CompositionAnimationPresentationSetEntryValidationResult Accepted(
        int index,
        NodeKey targetKey,
        in CompositionLayerAnimation animation)
    {
        return new CompositionAnimationPresentationSetEntryValidationResult(
            CompositionAnimationPresentationSetEntryKind.Accepted,
            index,
            targetKey,
            LayerId: animation.LayerId,
            CommandStart: animation.CommandStart,
            CommandCount: animation.CommandCount);
    }

    internal static CompositionAnimationPresentationSetEntryValidationResult Rejected(
        int index,
        NodeKey targetKey,
        CompositionAnimationPresentationSetRejectionReason reason,
        CompositionLayerId layerId = default,
        int commandStart = -1,
        int commandCount = 0,
        int conflictingIndex = -1)
    {
        return new CompositionAnimationPresentationSetEntryValidationResult(
            CompositionAnimationPresentationSetEntryKind.Rejected,
            index,
            targetKey,
            reason,
            layerId,
            commandStart,
            commandCount,
            conflictingIndex);
    }

    public bool Equals(CompositionAnimationPresentationSetEntryValidationResult other)
    {
        return Kind == other.Kind
            && Index == other.Index
            && TargetKey == other.TargetKey
            && RejectionReason == other.RejectionReason
            && LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && ConflictingIndex == other.ConflictingIndex;
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationPresentationSetEntryValidationResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Index, TargetKey, RejectionReason, LayerId, CommandStart, CommandCount, ConflictingIndex);

    public static bool operator ==(CompositionAnimationPresentationSetEntryValidationResult left, CompositionAnimationPresentationSetEntryValidationResult right) =>
        left.Equals(right);

    public static bool operator !=(CompositionAnimationPresentationSetEntryValidationResult left, CompositionAnimationPresentationSetEntryValidationResult right) =>
        !left.Equals(right);
}

internal readonly struct CompositionAnimationPresentationSetValidationResult : IEquatable<CompositionAnimationPresentationSetValidationResult>
{
    private readonly CompositionAnimationPresentationSetEntryValidationResult[]? _entryResults;

    internal CompositionAnimationPresentationSetValidationResult(
        CompositionAnimationPresentationSetResultKind Kind,
        ReadOnlySpan<CompositionAnimationPresentationSetEntryValidationResult> entryResults,
        bool PresentationStateChanged)
    {
        this.Kind = Kind;
        _entryResults = entryResults.IsEmpty ? null : entryResults.ToArray();
        this.PresentationStateChanged = PresentationStateChanged;
    }

    public CompositionAnimationPresentationSetResultKind Kind { get; }
    public ReadOnlySpan<CompositionAnimationPresentationSetEntryValidationResult> EntryResults => _entryResults;
    public bool PresentationStateChanged { get; }
    public int AcceptedCount => Count(CompositionAnimationPresentationSetEntryKind.Accepted);
    public int RejectedCount => Count(CompositionAnimationPresentationSetEntryKind.Rejected);

    public bool Equals(CompositionAnimationPresentationSetValidationResult other)
    {
        if (Kind != other.Kind || PresentationStateChanged != other.PresentationStateChanged)
        {
            return false;
        }

        var results = EntryResults;
        var otherResults = other.EntryResults;
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

    public override bool Equals(object? obj) => obj is CompositionAnimationPresentationSetValidationResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(PresentationStateChanged);
        foreach (ref readonly var result in EntryResults)
        {
            hash.Add(result);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(CompositionAnimationPresentationSetValidationResult left, CompositionAnimationPresentationSetValidationResult right) =>
        left.Equals(right);

    public static bool operator !=(CompositionAnimationPresentationSetValidationResult left, CompositionAnimationPresentationSetValidationResult right) =>
        !left.Equals(right);

    private int Count(CompositionAnimationPresentationSetEntryKind kind)
    {
        var count = 0;
        foreach (ref readonly var result in EntryResults)
        {
            if (result.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }
}

internal static class CompositionAnimationPresentationSetValidator
{
    internal static CompositionAnimationPresentationSetValidationResult Validate(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        RenderPipelineRetainedInputSnapshot? snapshot,
        int commandCount)
    {
        if (declarations.IsEmpty)
        {
            return new CompositionAnimationPresentationSetValidationResult(
                CompositionAnimationPresentationSetResultKind.Empty,
                [],
                PresentationStateChanged: false);
        }

        if (snapshot is null)
        {
            return RejectAll(declarations, CompositionAnimationPresentationSetRejectionReason.MissingRetainedSnapshot);
        }

        if (commandCount <= 0)
        {
            return RejectAll(declarations, CompositionAnimationPresentationSetRejectionReason.MissingRetainedFrame);
        }

        if (snapshot.CommandCount != commandCount)
        {
            return RejectAll(declarations, CompositionAnimationPresentationSetRejectionReason.CommandFrameMismatch);
        }

        var results = new CompositionAnimationPresentationSetEntryValidationResult[declarations.Length];
        for (var i = 0; i < declarations.Length; i++)
        {
            var declaration = declarations[i];
            if (!declaration.TryResolve(snapshot, commandCount, out var plan))
            {
                results[i] = CompositionAnimationPresentationSetEntryValidationResult.Rejected(
                    i,
                    declaration.TargetKey,
                    CompositionAnimationPresentationSetRejectionReason.MissingRetainedTarget);
                continue;
            }

            if (!plan.IsValidForCommandCount(commandCount))
            {
                results[i] = CompositionAnimationPresentationSetEntryValidationResult.Rejected(
                    i,
                    declaration.TargetKey,
                    CompositionAnimationPresentationSetRejectionReason.InvalidResolvedPlan);
                continue;
            }

            var animation = plan.LayerAnimation;
            if (TryFindLayerConflict(results, i, animation.LayerId, out var layerConflictIndex))
            {
                results[i] = CompositionAnimationPresentationSetEntryValidationResult.Rejected(
                    i,
                    declaration.TargetKey,
                    CompositionAnimationPresentationSetRejectionReason.DuplicateLayerId,
                    animation.LayerId,
                    animation.CommandStart,
                    animation.CommandCount,
                    layerConflictIndex);
                continue;
            }

            if (TryFindCommandRangeConflict(results, i, animation.CommandStart, animation.CommandCount, out var rangeConflictIndex))
            {
                results[i] = CompositionAnimationPresentationSetEntryValidationResult.Rejected(
                    i,
                    declaration.TargetKey,
                    CompositionAnimationPresentationSetRejectionReason.OverlappingCommandRange,
                    animation.LayerId,
                    animation.CommandStart,
                    animation.CommandCount,
                    rangeConflictIndex);
                continue;
            }

            results[i] = CompositionAnimationPresentationSetEntryValidationResult.Accepted(
                i,
                declaration.TargetKey,
                animation);
        }

        return new CompositionAnimationPresentationSetValidationResult(
            ResolveKind(results),
            results,
            PresentationStateChanged: false);
    }

    private static CompositionAnimationPresentationSetValidationResult RejectAll(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        CompositionAnimationPresentationSetRejectionReason reason)
    {
        var results = new CompositionAnimationPresentationSetEntryValidationResult[declarations.Length];
        for (var i = 0; i < declarations.Length; i++)
        {
            results[i] = CompositionAnimationPresentationSetEntryValidationResult.Rejected(
                i,
                declarations[i].TargetKey,
                reason);
        }

        return new CompositionAnimationPresentationSetValidationResult(
            CompositionAnimationPresentationSetResultKind.Rejected,
            results,
            PresentationStateChanged: false);
    }

    private static CompositionAnimationPresentationSetResultKind ResolveKind(
        ReadOnlySpan<CompositionAnimationPresentationSetEntryValidationResult> results)
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
            ? CompositionAnimationPresentationSetResultKind.Accepted
            : rejectedCount == results.Length
                ? CompositionAnimationPresentationSetResultKind.Rejected
                : CompositionAnimationPresentationSetResultKind.Mixed;
    }

    private static bool TryFindLayerConflict(
        ReadOnlySpan<CompositionAnimationPresentationSetEntryValidationResult> results,
        int count,
        CompositionLayerId layerId,
        out int conflictingIndex)
    {
        for (var i = 0; i < count; i++)
        {
            if (results[i].IsAccepted && results[i].LayerId == layerId)
            {
                conflictingIndex = i;
                return true;
            }
        }

        conflictingIndex = -1;
        return false;
    }

    private static bool TryFindCommandRangeConflict(
        ReadOnlySpan<CompositionAnimationPresentationSetEntryValidationResult> results,
        int count,
        int commandStart,
        int commandCount,
        out int conflictingIndex)
    {
        var commandEnd = commandStart + commandCount;
        for (var i = 0; i < count; i++)
        {
            if (!results[i].IsAccepted)
            {
                continue;
            }

            var acceptedStart = results[i].CommandStart;
            var acceptedEnd = acceptedStart + results[i].CommandCount;
            if (commandStart < acceptedEnd && acceptedStart < commandEnd)
            {
                conflictingIndex = i;
                return true;
            }
        }

        conflictingIndex = -1;
        return false;
    }
}
