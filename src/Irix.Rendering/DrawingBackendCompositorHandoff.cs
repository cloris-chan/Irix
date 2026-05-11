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

internal readonly record struct DrawingBackendCompositorHandoffResult(
    DrawingBackendCompositorHandoffResultKind Kind,
    SegmentedRetainedFrameProductionOwnerFeedResult OwnerResult,
    RetainedRenderFrameHandoffHarnessResult CandidateResult)
{
    private static RetainedRenderFrameHandoffHarnessResult EmptyCandidateResult { get; } = RetainedRenderFrameHandoffHarnessResult.Disabled(new RetainedRenderFrameHandoffHarnessCounters(0, 0, 0, 0, [], false));

    public static DrawingBackendCompositorHandoffResult Disabled { get; } = new(
        DrawingBackendCompositorHandoffResultKind.Disabled,
        SegmentedRetainedFrameProductionOwnerFeedResult.Disabled,
        EmptyCandidateResult);

    public static DrawingBackendCompositorHandoffResult MissingOwner(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.MissingOwner,
            ownerResult,
            EmptyCandidateResult);
    }

    public static DrawingBackendCompositorHandoffResult Executed(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RetainedRenderFrameHandoffHarnessResult candidateResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.Executed,
            ownerResult,
            candidateResult);
    }

    public static DrawingBackendCompositorHandoffResult FallbackFull(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.FallbackFull,
            ownerResult,
            EmptyCandidateResult);
    }

    public static DrawingBackendCompositorHandoffResult Rejected(SegmentedRetainedFrameProductionOwnerFeedResult ownerResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            DrawingBackendCompositorHandoffResultKind.Rejected,
            ownerResult,
            EmptyCandidateResult);
    }
}