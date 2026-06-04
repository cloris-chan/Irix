#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed partial class WindowDrawCommandTranslator
{
    private bool _captureAllocationAttribution;
    private long _allocationPhaseStart;

    internal WindowTranslateAllocationAttribution LastAllocationAttribution { get; private set; }

    internal RenderFrameBatch TranslateWithAllocationAttribution(PatchBatch patchBatch, out WindowTranslateAllocationAttribution attribution)
    {
        _captureAllocationAttribution = true;
        try
        {
            var batch = TranslateCoreWithAllocationAttribution(patchBatch);
            attribution = LastAllocationAttribution;
            return batch;
        }
        finally
        {
            _captureAllocationAttribution = false;
        }
    }

    private RenderFrameBatch TranslateCoreWithAllocationAttribution(PatchBatch patchBatch)
    {
        OnTranslateAllocationStarted();
        OnTranslateAllocationPhaseStarted();
        var retained = _translatorCore.Apply(in patchBatch);
        OnTranslateRetainedApplyAllocated();

        OnTranslateAllocationPhaseStarted();
        var input = CreateInput(in patchBatch);
        OnTranslateViewportAllocated();

        OnTranslateAllocationPhaseStarted();
        var output = _translatorCore.BuildOutputWithAllocationAttribution(in input, in retained, out _);
        OnTranslatePipelineBuildAllocated(_translatorCore);
        ApplyOutput(in output);

        OnTranslateAllocationPhaseStarted();
        _feedbackSink.Deliver(output.MaxScrollY, BuildScrollFeedback(output.LayoutResult));
        OnTranslateFeedbackAllocated();

        return output.Batch;
    }

    partial void OnTranslateAllocationStarted()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = default;
        }
    }

    partial void OnTranslateAllocationPhaseStarted()
    {
        if (_captureAllocationAttribution)
        {
            _allocationPhaseStart = GC.GetTotalAllocatedBytes(false);
        }
    }

    partial void OnTranslateRetainedApplyAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithRetainedApply(Delta());
        }
    }

    partial void OnTranslateViewportAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithViewport(Delta());
        }
    }

    partial void OnTranslatePipelineBuildAllocated(TranslatorCore translatorCore)
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution
                .WithPipelineBuild(Delta())
                .WithPipelineAttribution(translatorCore.LastPipelineAllocationAttribution);
        }
    }

    partial void OnTranslateFeedbackAllocated()
    {
        if (_captureAllocationAttribution)
        {
            LastAllocationAttribution = LastAllocationAttribution.WithFeedback(Delta());
        }
    }

    private long Delta() => GC.GetTotalAllocatedBytes(false) - _allocationPhaseStart;
}

internal readonly struct WindowTranslateAllocationAttribution(
    long RetainedApplyBytes,
    long ViewportBytes,
    long PipelineBuildBytes,
    long FeedbackBytes,
    RenderPipelineBuildAllocationAttribution PipelineAttribution = default) : IEquatable<WindowTranslateAllocationAttribution>
{
    public long RetainedApplyBytes { get; } = RetainedApplyBytes;
    public long ViewportBytes { get; } = ViewportBytes;
    public long PipelineBuildBytes { get; } = PipelineBuildBytes;
    public long FeedbackBytes { get; } = FeedbackBytes;
    public RenderPipelineBuildAllocationAttribution PipelineAttribution { get; } = PipelineAttribution;
    public long TotalBytes => RetainedApplyBytes + ViewportBytes + PipelineBuildBytes + FeedbackBytes;

    public WindowTranslateAllocationAttribution Add(WindowTranslateAllocationAttribution other) =>
        new(
            RetainedApplyBytes + other.RetainedApplyBytes,
            ViewportBytes + other.ViewportBytes,
            PipelineBuildBytes + other.PipelineBuildBytes,
            FeedbackBytes + other.FeedbackBytes,
            PipelineAttribution.Add(other.PipelineAttribution));

    public WindowTranslateAllocationAttribution WithRetainedApply(long bytes) => new(RetainedApplyBytes + bytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithViewport(long bytes) => new(RetainedApplyBytes, ViewportBytes + bytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithPipelineBuild(long bytes) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes + bytes, FeedbackBytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithFeedback(long bytes) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes + bytes, PipelineAttribution);

    public WindowTranslateAllocationAttribution WithPipelineAttribution(RenderPipelineBuildAllocationAttribution attribution) => new(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution.Add(attribution));

    public bool Equals(WindowTranslateAllocationAttribution other)
    {
        return RetainedApplyBytes == other.RetainedApplyBytes
            && ViewportBytes == other.ViewportBytes
            && PipelineBuildBytes == other.PipelineBuildBytes
            && FeedbackBytes == other.FeedbackBytes
            && PipelineAttribution.Equals(other.PipelineAttribution);
    }

    public override bool Equals(object? obj) => obj is WindowTranslateAllocationAttribution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RetainedApplyBytes, ViewportBytes, PipelineBuildBytes, FeedbackBytes, PipelineAttribution);
}
#endif
