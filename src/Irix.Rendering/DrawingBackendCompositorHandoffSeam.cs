using Irix.Drawing;

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
    public static DrawingBackendCompositorHandoffResult Disabled { get; } = new(
        DrawingBackendCompositorHandoffResultKind.Disabled,
        SegmentedRetainedFrameProductionOwnerFeedResult.Disabled,
        RetainedRenderFrameHandoffHarnessResult.Disabled(new RetainedRenderFrameHandoffHarnessCounters(0, 0, 0, 0, [], false)));
}

internal sealed class DrawingBackendCompositorHandoffSeam(
    DrawingBackendCompositor compositor,
    DrawingBackendCompositorHandoffOptions options = default,
    Func<IDrawingBackend>? candidateBackendFactory = null) : IDisposable
{
    private RetainedRenderFrameHandoffHarness? _candidateHarness;
    private bool _disposed;

    public DrawingBackendCompositorHandoffResult LastResult { get; private set; } = DrawingBackendCompositorHandoffResult.Disabled;

    public bool HasCandidateHarness => _candidateHarness is not null;

    public ValueTask RenderAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        FrameContext frameContext,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var renderTask = compositor.RenderAsync(renderFrameBatch, cancellationToken);
        if (!renderTask.IsCompletedSuccessfully)
        {
            return RenderAfterAsync(renderTask, ownership, frameContext);
        }

        renderTask.GetAwaiter().GetResult();
        LastResult = ExecuteCandidateFrame(ownership, frameContext);
        return ValueTask.CompletedTask;
    }

    private async ValueTask RenderAfterAsync(
        ValueTask renderTask,
        RetainedRenderFrameSegmentOwnership? ownership,
        FrameContext frameContext)
    {
        await renderTask;
        LastResult = ExecuteCandidateFrame(ownership, frameContext);
    }

    public DrawingBackendCompositorHandoffResult ExecuteCandidateFrame(
        RetainedRenderFrameSegmentOwnership? ownership,
        FrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!options.EnableSegmentedRenderSourceCandidate)
        {
            LastResult = DrawingBackendCompositorHandoffResult.Disabled;
            return LastResult;
        }

        if (ownership?.RuntimeOwner is null)
        {
            LastResult = new DrawingBackendCompositorHandoffResult(
                DrawingBackendCompositorHandoffResultKind.MissingOwner,
                ownership?.LastResult ?? SegmentedRetainedFrameProductionOwnerFeedResult.Disabled,
                RetainedRenderFrameHandoffHarnessResult.Disabled(new RetainedRenderFrameHandoffHarnessCounters(0, 0, 0, 0, [], false)));
            return LastResult;
        }

        var harness = _candidateHarness ??= new RetainedRenderFrameHandoffHarness(candidateBackendFactory!(), RetainedRenderFrameHandoffHarnessOptions.Enabled);
        try
        {
            var candidateResult = harness.ExecuteCandidateFrame(ownership, frameContext, compositor.LastDirtyCommandRanges);
            LastResult = CreateResult(ownership.LastResult, candidateResult);
            return LastResult;
        }
        catch
        {
            LastResult = CreateResult(ownership.LastResult, harness.LastResult);
            throw;
        }
    }

    public bool TryGetCandidateActionIdAt(int x, int y, out string actionId)
    {
        if (_candidateHarness is null)
        {
            actionId = string.Empty;
            return false;
        }

        return _candidateHarness.TryGetActionIdAt(x, y, out actionId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _candidateHarness?.Dispose();
        _disposed = true;
    }

    private static DrawingBackendCompositorHandoffResult CreateResult(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RetainedRenderFrameHandoffHarnessResult candidateResult)
    {
        return new DrawingBackendCompositorHandoffResult(
            MapKind(ownerResult, candidateResult),
            ownerResult,
            candidateResult);
    }

    private static DrawingBackendCompositorHandoffResultKind MapKind(
        SegmentedRetainedFrameProductionOwnerFeedResult ownerResult,
        RetainedRenderFrameHandoffHarnessResult candidateResult)
    {
        if (candidateResult.Kind == RetainedRenderFrameHandoffHarnessResultKind.Disabled)
        {
            return DrawingBackendCompositorHandoffResultKind.Disabled;
        }

        if (candidateResult.Kind == RetainedRenderFrameHandoffHarnessResultKind.MissingSegmentedOwner)
        {
            return DrawingBackendCompositorHandoffResultKind.MissingOwner;
        }

        if (ownerResult.Kind == SegmentedRetainedFrameShadowResultKind.ShadowRejected
            || ownerResult.ShadowResult.PlanKind == RetainedPartialApplyResultKind.Rejected)
        {
            return DrawingBackendCompositorHandoffResultKind.Rejected;
        }

        if (ownerResult.Kind == SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull)
        {
            return DrawingBackendCompositorHandoffResultKind.FallbackFull;
        }

        return DrawingBackendCompositorHandoffResultKind.Executed;
    }
}