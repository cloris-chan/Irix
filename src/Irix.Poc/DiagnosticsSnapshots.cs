using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct BackendClipTextDiagnosticSnapshot(
    DrawingBackendClipMode ClipMode,
    int ClippedCommandCount,
    int EmptyIntersectionSkippedCount,
    int ScissorStateChangeCount,
    EffectiveScissor LastEffectiveScissor,
    EffectiveScissor LastEffectiveTextClip,
    int TextClipSkippedCount,
    bool DeviceRemoved,
    string DeviceErrorReason)
{
    public bool GpuScissor => ClipMode == DrawingBackendClipMode.Scissor;

    public static BackendClipTextDiagnosticSnapshot FromBackend(D3D12DrawingBackend backend, D3D12Renderer renderer)
    {
        return new BackendClipTextDiagnosticSnapshot(
            backend.ClipMode,
            backend.ClippedCommandCount,
            backend.EmptyIntersectionSkippedCount,
            backend.ScissorStateChangeCount,
            backend.LastEffectiveScissor,
            backend.LastEffectiveTextClip,
            backend.TextClipSkippedCount,
            renderer.IsDeviceRemoved,
            renderer.DeviceErrorReason ?? "(none)");
    }
}

internal readonly record struct RenderingPipelineDiagnosticSnapshot(
    long RenderCount,
    long PartialApplyCount,
    long FullApplyCount,
    long EmptyFrameCount,
    IReadOnlyList<(int Start, int Count)> CompositorDirtyCommandRanges,
    IReadOnlyList<(int Start, int Count)> BackendDirtyCommandRanges,
    int BackendClippedCommandCount,
    int LayoutCommandCount,
    int LayoutClippedCommandCount,
    long LayoutRebuildCount,
    LayoutRebuildReason LayoutRebuildReason,
    InvalidationKind LayoutInvalidationKind,
    IReadOnlyList<LayoutDirtyClassification> LayoutDirtyClassifications,
    IReadOnlyList<HitTestTarget> HitTargets,
    IReadOnlyList<ScrollContainerDiag> ScrollContainerDiagnostics)
{
    public double PartialHitRate => RenderCount > 0 ? 100.0 * PartialApplyCount / RenderCount : 0;

    public bool DirtyRangesAligned => CompositorDirtyCommandRanges.Count == BackendDirtyCommandRanges.Count &&
        CompositorDirtyCommandRanges.Zip(BackendDirtyCommandRanges).All(pair => pair.First == pair.Second);
}

internal readonly record struct ViewportDiagnosticsSnapshot(
    PixelRectangle WindowPhysicalBounds,
    PixelRectangle RendererSwapchainBounds,
    PixelRectangle TranslatorViewport,
    PixelRectangle LayoutViewport,
    PixelRectangle LastAppliedPendingResize,
    long RenderCount,
    long LayoutRebuildCount,
    string LayoutRebuildReason,
    float ScreenScale,
    string DpiAwareness,
    string ScaleMode,
    DisplayScale Scale = default,
    PixelRectangle LogicalViewport = default)
{
    public bool ViewportMatchesRenderer => TranslatorViewport.Width == RendererSwapchainBounds.Width && TranslatorViewport.Height == RendererSwapchainBounds.Height;

    public bool LayoutUsesRendererSize => LayoutViewport.Width == RendererSwapchainBounds.Width && LayoutViewport.Height == RendererSwapchainBounds.Height;
}

internal readonly record struct ScrollDiagnosticsSnapshot(
    long DispatchedFrameCount,
    double RenderWaitMs,
    double LastDt,
    double DrainedPixels,
    double LastFrameDrainedPixels,
    double PendingPixels,
    bool FrameQueued,
    bool TickLoopRunning,
    int AppliedScrollY,
    double TargetPosition,
    double MaxScrollY,
    bool HasMaxScrollY,
    double Position = 0,
    double Accumulator = 0,
    bool IsAnimating = false);

internal readonly record struct InputDiagnosticsSnapshot(
    OwnershipSnapshot Ownership,
    IReadOnlyList<string> OrderedDiagnosticLines,
    IReadOnlyList<string> OwnershipLines,
    IReadOnlyList<string> ButtonVisualStateLines,
    IReadOnlyList<string> EventLines,
    IReadOnlyList<string> DirtyReasonLines);
