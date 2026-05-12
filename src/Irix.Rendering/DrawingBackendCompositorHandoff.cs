namespace Irix.Rendering;

internal readonly record struct DrawingBackendCompositorHandoffOptions
{
    public bool EnableSegmentedRenderSourceCandidate { get; init; }

    public static DrawingBackendCompositorHandoffOptions Disabled => default;

    public static DrawingBackendCompositorHandoffOptions Enabled => new() { EnableSegmentedRenderSourceCandidate = true };
}

internal enum DrawingBackendCompositorHandoffResultKind : byte
{
    Disabled,
    MissingOwner,
    Executed,
    FallbackFull,
    Rejected
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

internal readonly record struct DrawingBackendCompositorHandoffResult(
    DrawingBackendCompositorHandoffResultKind Kind,
    SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult,
    RetainedRenderFrameHandoffHarnessResult CandidateResult,
    DrawingBackendCompositorHandoffReason Reason)
{
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
}
