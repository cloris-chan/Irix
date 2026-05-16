namespace Irix.Rendering;

internal readonly struct StyleOnlyFastPathOptions(bool EnableStyleOnlySelectedRenderSource) : IEquatable<StyleOnlyFastPathOptions>
{

    public bool EnableStyleOnlySelectedRenderSource { get; } = EnableStyleOnlySelectedRenderSource;

    public static StyleOnlyFastPathOptions Disabled => default;

    public static StyleOnlyFastPathOptions Enabled => new(true);

    public RenderPipelineProductionOwnerOptions ProductionOwnerOptions => EnableStyleOnlySelectedRenderSource
        ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
        : RenderPipelineProductionOwnerOptions.Disabled;

    public DrawingBackendCompositorHandoffOptions HandoffOptions => EnableStyleOnlySelectedRenderSource
        ? DrawingBackendCompositorHandoffOptions.Enabled
        : DrawingBackendCompositorHandoffOptions.Disabled;

    public bool Equals(StyleOnlyFastPathOptions other)
    {
        return EnableStyleOnlySelectedRenderSource == other.EnableStyleOnlySelectedRenderSource;
    }

    public override bool Equals(object? obj) => obj is StyleOnlyFastPathOptions other && Equals(other);

    public override int GetHashCode() => EnableStyleOnlySelectedRenderSource.GetHashCode();

    public static bool operator ==(StyleOnlyFastPathOptions left, StyleOnlyFastPathOptions right) => left.Equals(right);

    public static bool operator !=(StyleOnlyFastPathOptions left, StyleOnlyFastPathOptions right) => !left.Equals(right);
}
