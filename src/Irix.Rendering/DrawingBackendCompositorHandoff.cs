namespace Irix.Rendering;

internal readonly struct DrawingBackendCompositorHandoffOptions(bool EnableSegmentedRenderSourceCandidate) : IEquatable<DrawingBackendCompositorHandoffOptions>
{

    public bool EnableSegmentedRenderSourceCandidate { get; } = EnableSegmentedRenderSourceCandidate;

    public static DrawingBackendCompositorHandoffOptions Disabled => default;

    public static DrawingBackendCompositorHandoffOptions Enabled => new(true);

    public bool Equals(DrawingBackendCompositorHandoffOptions other)
    {
        return EnableSegmentedRenderSourceCandidate == other.EnableSegmentedRenderSourceCandidate;
    }

    public override bool Equals(object? obj) => obj is DrawingBackendCompositorHandoffOptions other && Equals(other);

    public override int GetHashCode() => EnableSegmentedRenderSourceCandidate.GetHashCode();

    public static bool operator ==(DrawingBackendCompositorHandoffOptions left, DrawingBackendCompositorHandoffOptions right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCompositorHandoffOptions left, DrawingBackendCompositorHandoffOptions right) => !left.Equals(right);
}

internal enum DrawingBackendCompositorHandoffResultKind : byte
{
    Disabled,
    MissingOwner,
    Executed,
    FallbackFull,
    Rejected,
    RetainedFrameStaged
}

internal enum DrawingBackendCompositorHandoffReason : byte
{
    None,
    Disabled,
    MissingOwner,
    StaleOwner,
    OwnerRejected,
    OwnerFallbackFull,
    EmptySegmentRead,
    MalformedSegmentCoverage,
    DirtyRangeMismatch,
    BackendThrewBeforeCommit
}

internal readonly struct DrawingBackendCompositorHandoffResult(
    DrawingBackendCompositorHandoffResultKind Kind,
    SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult,
    RetainedRenderFrameHandoffHarnessResult CandidateResult,
    DrawingBackendCompositorHandoffReason Reason) : IEquatable<DrawingBackendCompositorHandoffResult>
{

    public DrawingBackendCompositorHandoffResultKind Kind { get; } = Kind;
    public SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult { get; } = OwnerResult;
    public RetainedRenderFrameHandoffHarnessResult CandidateResult { get; } = CandidateResult;
    public DrawingBackendCompositorHandoffReason Reason { get; } = Reason;

    private static RetainedRenderFrameHandoffHarnessResult EmptyCandidateResult { get; } = RetainedRenderFrameHandoffHarnessResult.Disabled(new RetainedRenderFrameHandoffHarnessCounters(0, 0, 0, 0, [], false));

    public static DrawingBackendCompositorHandoffResult Disabled { get; } = new(
        DrawingBackendCompositorHandoffResultKind.Disabled,
        SegmentedRetainedFrameProductionOwnerFeedResult.Disabled,
        EmptyCandidateResult,
        DrawingBackendCompositorHandoffReason.Disabled);

    public static DrawingBackendCompositorHandoffResult MissingOwner(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.MissingOwner,
            ownerResult,
            EmptyCandidateResult,
            DrawingBackendCompositorHandoffReason.MissingOwner);
    }

    public static DrawingBackendCompositorHandoffResult Executed(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RetainedRenderFrameHandoffHarnessResult candidateResult,
        DrawingBackendCompositorHandoffReason reason = DrawingBackendCompositorHandoffReason.None)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.Executed,
            ownerResult,
            candidateResult,
            reason);
    }

    public static DrawingBackendCompositorHandoffResult FallbackFull(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        DrawingBackendCompositorHandoffReason reason = DrawingBackendCompositorHandoffReason.OwnerFallbackFull)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.FallbackFull,
            ownerResult,
            EmptyCandidateResult,
            reason);
    }

    public static DrawingBackendCompositorHandoffResult Rejected(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        DrawingBackendCompositorHandoffReason reason = DrawingBackendCompositorHandoffReason.OwnerRejected)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.Rejected,
            ownerResult,
            EmptyCandidateResult,
            reason);
    }

    public static DrawingBackendCompositorHandoffResult RetainedFrameStaged(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.RetainedFrameStaged,
            ownerResult,
            EmptyCandidateResult,
            DrawingBackendCompositorHandoffReason.None);
    }

    public bool Equals(DrawingBackendCompositorHandoffResult other)
    {
        return Kind == other.Kind
            && OwnerResult == other.OwnerResult
            && CandidateResult == other.CandidateResult
            && Reason == other.Reason;
    }

    public override bool Equals(object? obj) => obj is DrawingBackendCompositorHandoffResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, OwnerResult, CandidateResult, Reason);

    public static bool operator ==(DrawingBackendCompositorHandoffResult left, DrawingBackendCompositorHandoffResult right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCompositorHandoffResult left, DrawingBackendCompositorHandoffResult right) => !left.Equals(right);
}
