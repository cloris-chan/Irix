namespace Irix.Rendering;

internal readonly record struct StyleOnlyFastPathOptions
{
    public bool EnableStyleOnlySelectedRenderSource { get; init; }

    public static StyleOnlyFastPathOptions Disabled => default;

    public static StyleOnlyFastPathOptions Enabled => new() { EnableStyleOnlySelectedRenderSource = true };

    public RenderPipelineProductionOwnerOptions ProductionOwnerOptions => EnableStyleOnlySelectedRenderSource
        ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
        : RenderPipelineProductionOwnerOptions.Disabled;

    public DrawingBackendCompositorHandoffOptions HandoffOptions => EnableStyleOnlySelectedRenderSource
        ? DrawingBackendCompositorHandoffOptions.Enabled
        : DrawingBackendCompositorHandoffOptions.Disabled;
}
