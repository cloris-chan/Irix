using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal enum ViewportDpiAwareness : byte
{
    ProcessDefault
}

internal enum ViewportScaleMode : byte
{
    PhysicalPixelsV0
}

internal readonly struct DeviceErrorDiagnostic(string Message) : IEquatable<DeviceErrorDiagnostic>
{
    private readonly string _message = Message;

    public static DeviceErrorDiagnostic None { get; } = new("(none)");

    public ReadOnlySpan<char> Message => _message;

    public static DeviceErrorDiagnostic FromNullable(string? message)
    {
        return string.IsNullOrWhiteSpace(message) ? None : new DeviceErrorDiagnostic(message);
    }

    public bool Equals(DeviceErrorDiagnostic other) => _message == other._message;

    public override bool Equals(object? obj) => obj is DeviceErrorDiagnostic other && Equals(other);

    public override int GetHashCode() => _message.GetHashCode();

    public override string ToString() => _message;

    public static bool operator ==(DeviceErrorDiagnostic left, DeviceErrorDiagnostic right) => left.Equals(right);

    public static bool operator !=(DeviceErrorDiagnostic left, DeviceErrorDiagnostic right) => !left.Equals(right);
}

internal readonly struct BackendClipTextDiagnosticSnapshot(
    DrawingBackendClipMode ClipMode,
    int ClippedCommandCount,
    int EmptyIntersectionSkippedCount,
    int ScissorStateChangeCount,
    EffectiveScissor LastEffectiveScissor,
    EffectiveScissor LastEffectiveTextClip,
    int TextClipSkippedCount,
    bool DeviceRemoved,
    DeviceErrorDiagnostic DeviceError) : IEquatable<BackendClipTextDiagnosticSnapshot>
{

    public DrawingBackendClipMode ClipMode { get; } = ClipMode;
    public int ClippedCommandCount { get; } = ClippedCommandCount;
    public int EmptyIntersectionSkippedCount { get; } = EmptyIntersectionSkippedCount;
    public int ScissorStateChangeCount { get; } = ScissorStateChangeCount;
    public EffectiveScissor LastEffectiveScissor { get; } = LastEffectiveScissor;
    public EffectiveScissor LastEffectiveTextClip { get; } = LastEffectiveTextClip;
    public int TextClipSkippedCount { get; } = TextClipSkippedCount;
    public bool DeviceRemoved { get; } = DeviceRemoved;
    public DeviceErrorDiagnostic DeviceError { get; } = DeviceError;

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
            DeviceErrorDiagnostic.FromNullable(renderer.DeviceErrorReason));
    }

    public bool Equals(BackendClipTextDiagnosticSnapshot other)
    {
        return ClipMode == other.ClipMode
            && ClippedCommandCount == other.ClippedCommandCount
            && EmptyIntersectionSkippedCount == other.EmptyIntersectionSkippedCount
            && ScissorStateChangeCount == other.ScissorStateChangeCount
            && LastEffectiveScissor == other.LastEffectiveScissor
            && LastEffectiveTextClip == other.LastEffectiveTextClip
            && TextClipSkippedCount == other.TextClipSkippedCount
            && DeviceRemoved == other.DeviceRemoved
            && DeviceError == other.DeviceError;
    }

    public override bool Equals(object? obj) => obj is BackendClipTextDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ClipMode);
        hash.Add(ClippedCommandCount);
        hash.Add(EmptyIntersectionSkippedCount);
        hash.Add(ScissorStateChangeCount);
        hash.Add(LastEffectiveScissor);
        hash.Add(LastEffectiveTextClip);
        hash.Add(TextClipSkippedCount);
        hash.Add(DeviceRemoved);
        hash.Add(DeviceError);
        return hash.ToHashCode();
    }

    public static bool operator ==(BackendClipTextDiagnosticSnapshot left, BackendClipTextDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(BackendClipTextDiagnosticSnapshot left, BackendClipTextDiagnosticSnapshot right) => !left.Equals(right);
}

internal readonly struct RenderingPipelineDiagnosticSnapshot(
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
    IReadOnlyList<ScrollContainerDiag> ScrollContainerDiagnostics) : IEquatable<RenderingPipelineDiagnosticSnapshot>
{

    public long RenderCount { get; } = RenderCount;
    public long PartialApplyCount { get; } = PartialApplyCount;
    public long FullApplyCount { get; } = FullApplyCount;
    public long EmptyFrameCount { get; } = EmptyFrameCount;
    public IReadOnlyList<(int Start, int Count)> CompositorDirtyCommandRanges { get; } = CompositorDirtyCommandRanges;
    public IReadOnlyList<(int Start, int Count)> BackendDirtyCommandRanges { get; } = BackendDirtyCommandRanges;
    public int BackendClippedCommandCount { get; } = BackendClippedCommandCount;
    public int LayoutCommandCount { get; } = LayoutCommandCount;
    public int LayoutClippedCommandCount { get; } = LayoutClippedCommandCount;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LayoutRebuildReason { get; } = LayoutRebuildReason;
    public InvalidationKind LayoutInvalidationKind { get; } = LayoutInvalidationKind;
    public IReadOnlyList<LayoutDirtyClassification> LayoutDirtyClassifications { get; } = LayoutDirtyClassifications;
    public IReadOnlyList<HitTestTarget> HitTargets { get; } = HitTargets;
    public IReadOnlyList<ScrollContainerDiag> ScrollContainerDiagnostics { get; } = ScrollContainerDiagnostics;

    public double PartialHitRate => RenderCount > 0 ? 100.0 * PartialApplyCount / RenderCount : 0;

    public bool DirtyRangesAligned => CompositorDirtyCommandRanges.Count == BackendDirtyCommandRanges.Count &&
        CompositorDirtyCommandRanges.Zip(BackendDirtyCommandRanges).All(pair => pair.First == pair.Second);

    public bool Equals(RenderingPipelineDiagnosticSnapshot other)
    {
        return RenderCount == other.RenderCount
            && PartialApplyCount == other.PartialApplyCount
            && FullApplyCount == other.FullApplyCount
            && EmptyFrameCount == other.EmptyFrameCount
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(CompositorDirtyCommandRanges, other.CompositorDirtyCommandRanges)
            && EqualityComparer<IReadOnlyList<(int Start, int Count)>>.Default.Equals(BackendDirtyCommandRanges, other.BackendDirtyCommandRanges)
            && BackendClippedCommandCount == other.BackendClippedCommandCount
            && LayoutCommandCount == other.LayoutCommandCount
            && LayoutClippedCommandCount == other.LayoutClippedCommandCount
            && LayoutRebuildCount == other.LayoutRebuildCount
            && LayoutRebuildReason == other.LayoutRebuildReason
            && LayoutInvalidationKind == other.LayoutInvalidationKind
            && EqualityComparer<IReadOnlyList<LayoutDirtyClassification>>.Default.Equals(LayoutDirtyClassifications, other.LayoutDirtyClassifications)
            && EqualityComparer<IReadOnlyList<HitTestTarget>>.Default.Equals(HitTargets, other.HitTargets)
            && EqualityComparer<IReadOnlyList<ScrollContainerDiag>>.Default.Equals(ScrollContainerDiagnostics, other.ScrollContainerDiagnostics);
    }

    public override bool Equals(object? obj) => obj is RenderingPipelineDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RenderCount);
        hash.Add(PartialApplyCount);
        hash.Add(FullApplyCount);
        hash.Add(EmptyFrameCount);
        hash.Add(CompositorDirtyCommandRanges);
        hash.Add(BackendDirtyCommandRanges);
        hash.Add(BackendClippedCommandCount);
        hash.Add(LayoutCommandCount);
        hash.Add(LayoutClippedCommandCount);
        hash.Add(LayoutRebuildCount);
        hash.Add(LayoutRebuildReason);
        hash.Add(LayoutInvalidationKind);
        hash.Add(LayoutDirtyClassifications);
        hash.Add(HitTargets);
        hash.Add(ScrollContainerDiagnostics);
        return hash.ToHashCode();
    }

    public static bool operator ==(RenderingPipelineDiagnosticSnapshot left, RenderingPipelineDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(RenderingPipelineDiagnosticSnapshot left, RenderingPipelineDiagnosticSnapshot right) => !left.Equals(right);
}

internal readonly struct ViewportDiagnosticsSnapshot(
    PixelRectangle WindowPhysicalBounds,
    PixelRectangle RendererSwapchainBounds,
    PixelRectangle TranslatorViewport,
    PixelRectangle LayoutViewport,
    PixelRectangle LastAppliedPendingResize,
    long RenderCount,
    long LayoutRebuildCount,
    LayoutRebuildReason LayoutRebuildReason,
    float ScreenScale,
    ViewportDpiAwareness DpiAwareness,
    ViewportScaleMode ScaleMode,
    DisplayScale Scale = default,
    PixelRectangle LogicalViewport = default) : IEquatable<ViewportDiagnosticsSnapshot>
{

    public PixelRectangle WindowPhysicalBounds { get; } = WindowPhysicalBounds;
    public PixelRectangle RendererSwapchainBounds { get; } = RendererSwapchainBounds;
    public PixelRectangle TranslatorViewport { get; } = TranslatorViewport;
    public PixelRectangle LayoutViewport { get; } = LayoutViewport;
    public PixelRectangle LastAppliedPendingResize { get; } = LastAppliedPendingResize;
    public long RenderCount { get; } = RenderCount;
    public long LayoutRebuildCount { get; } = LayoutRebuildCount;
    public LayoutRebuildReason LayoutRebuildReason { get; } = LayoutRebuildReason;
    public float ScreenScale { get; } = ScreenScale;
    public ViewportDpiAwareness DpiAwareness { get; } = DpiAwareness;
    public ViewportScaleMode ScaleMode { get; } = ScaleMode;
    public DisplayScale Scale { get; } = Scale;
    public PixelRectangle LogicalViewport { get; } = LogicalViewport;

    public bool ViewportMatchesRenderer => TranslatorViewport.Width == RendererSwapchainBounds.Width && TranslatorViewport.Height == RendererSwapchainBounds.Height;

    public bool LayoutUsesRendererSize => LayoutViewport.Width == RendererSwapchainBounds.Width && LayoutViewport.Height == RendererSwapchainBounds.Height;

    public bool Equals(ViewportDiagnosticsSnapshot other)
    {
        return WindowPhysicalBounds == other.WindowPhysicalBounds
            && RendererSwapchainBounds == other.RendererSwapchainBounds
            && TranslatorViewport == other.TranslatorViewport
            && LayoutViewport == other.LayoutViewport
            && LastAppliedPendingResize == other.LastAppliedPendingResize
            && RenderCount == other.RenderCount
            && LayoutRebuildCount == other.LayoutRebuildCount
            && LayoutRebuildReason == other.LayoutRebuildReason
            && ScreenScale.Equals(other.ScreenScale)
            && DpiAwareness == other.DpiAwareness
            && ScaleMode == other.ScaleMode
            && Scale == other.Scale
            && LogicalViewport == other.LogicalViewport;
    }

    public override bool Equals(object? obj) => obj is ViewportDiagnosticsSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(WindowPhysicalBounds);
        hash.Add(RendererSwapchainBounds);
        hash.Add(TranslatorViewport);
        hash.Add(LayoutViewport);
        hash.Add(LastAppliedPendingResize);
        hash.Add(RenderCount);
        hash.Add(LayoutRebuildCount);
        hash.Add(LayoutRebuildReason);
        hash.Add(ScreenScale);
        hash.Add(DpiAwareness);
        hash.Add(ScaleMode);
        hash.Add(Scale);
        hash.Add(LogicalViewport);
        return hash.ToHashCode();
    }

    public static bool operator ==(ViewportDiagnosticsSnapshot left, ViewportDiagnosticsSnapshot right) => left.Equals(right);

    public static bool operator !=(ViewportDiagnosticsSnapshot left, ViewportDiagnosticsSnapshot right) => !left.Equals(right);
}

internal readonly struct ScrollDiagnosticsSnapshot(
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
    bool IsAnimating = false) : IEquatable<ScrollDiagnosticsSnapshot>
{

    public long DispatchedFrameCount { get; } = DispatchedFrameCount;
    public double RenderWaitMs { get; } = RenderWaitMs;
    public double LastDt { get; } = LastDt;
    public double DrainedPixels { get; } = DrainedPixels;
    public double LastFrameDrainedPixels { get; } = LastFrameDrainedPixels;
    public double PendingPixels { get; } = PendingPixels;
    public bool FrameQueued { get; } = FrameQueued;
    public bool TickLoopRunning { get; } = TickLoopRunning;
    public int AppliedScrollY { get; } = AppliedScrollY;
    public double TargetPosition { get; } = TargetPosition;
    public double MaxScrollY { get; } = MaxScrollY;
    public bool HasMaxScrollY { get; } = HasMaxScrollY;
    public double Position { get; } = Position;
    public double Accumulator { get; } = Accumulator;
    public bool IsAnimating { get; } = IsAnimating;

    public bool Equals(ScrollDiagnosticsSnapshot other)
    {
        return DispatchedFrameCount == other.DispatchedFrameCount
            && RenderWaitMs.Equals(other.RenderWaitMs)
            && LastDt.Equals(other.LastDt)
            && DrainedPixels.Equals(other.DrainedPixels)
            && LastFrameDrainedPixels.Equals(other.LastFrameDrainedPixels)
            && PendingPixels.Equals(other.PendingPixels)
            && FrameQueued == other.FrameQueued
            && TickLoopRunning == other.TickLoopRunning
            && AppliedScrollY == other.AppliedScrollY
            && TargetPosition.Equals(other.TargetPosition)
            && MaxScrollY.Equals(other.MaxScrollY)
            && HasMaxScrollY == other.HasMaxScrollY
            && Position.Equals(other.Position)
            && Accumulator.Equals(other.Accumulator)
            && IsAnimating == other.IsAnimating;
    }

    public override bool Equals(object? obj) => obj is ScrollDiagnosticsSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DispatchedFrameCount);
        hash.Add(RenderWaitMs);
        hash.Add(LastDt);
        hash.Add(DrainedPixels);
        hash.Add(LastFrameDrainedPixels);
        hash.Add(PendingPixels);
        hash.Add(FrameQueued);
        hash.Add(TickLoopRunning);
        hash.Add(AppliedScrollY);
        hash.Add(TargetPosition);
        hash.Add(MaxScrollY);
        hash.Add(HasMaxScrollY);
        hash.Add(Position);
        hash.Add(Accumulator);
        hash.Add(IsAnimating);
        return hash.ToHashCode();
    }

    public static bool operator ==(ScrollDiagnosticsSnapshot left, ScrollDiagnosticsSnapshot right) => left.Equals(right);

    public static bool operator !=(ScrollDiagnosticsSnapshot left, ScrollDiagnosticsSnapshot right) => !left.Equals(right);
}

internal readonly struct InputDiagnosticsSnapshot(
    OwnershipSnapshot Ownership,
    IReadOnlyList<InputDiagnosticButtonState> ButtonStates,
    IReadOnlyList<InputDiagnosticOwnershipStep> OwnershipSteps,
    IReadOnlyList<InputOwnershipEvent> Events,
    IReadOnlyList<InputDirtyReasonDiagnostic> DirtyReasons) : IEquatable<InputDiagnosticsSnapshot>
{

    public OwnershipSnapshot Ownership { get; } = Ownership;
    public IReadOnlyList<InputDiagnosticButtonState> ButtonStates { get; } = ButtonStates;
    public IReadOnlyList<InputDiagnosticOwnershipStep> OwnershipSteps { get; } = OwnershipSteps;
    public IReadOnlyList<InputOwnershipEvent> Events { get; } = Events;
    public IReadOnlyList<InputDirtyReasonDiagnostic> DirtyReasons { get; } = DirtyReasons;

    public bool Equals(InputDiagnosticsSnapshot other)
    {
        return Ownership == other.Ownership
            && EqualityComparer<IReadOnlyList<InputDiagnosticButtonState>>.Default.Equals(ButtonStates, other.ButtonStates)
            && EqualityComparer<IReadOnlyList<InputDiagnosticOwnershipStep>>.Default.Equals(OwnershipSteps, other.OwnershipSteps)
            && EqualityComparer<IReadOnlyList<InputOwnershipEvent>>.Default.Equals(Events, other.Events)
            && EqualityComparer<IReadOnlyList<InputDirtyReasonDiagnostic>>.Default.Equals(DirtyReasons, other.DirtyReasons);
    }

    public override bool Equals(object? obj) => obj is InputDiagnosticsSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Ownership, ButtonStates, OwnershipSteps, Events, DirtyReasons);

    public static bool operator ==(InputDiagnosticsSnapshot left, InputDiagnosticsSnapshot right) => left.Equals(right);

    public static bool operator !=(InputDiagnosticsSnapshot left, InputDiagnosticsSnapshot right) => !left.Equals(right);
}

internal enum InputDiagnosticButtonStateKind : byte
{
    Normal,
    Hovered,
    Pressed,
    Focused,
    AfterMove,
    AfterPress,
    DuringCaptureMove,
    ReleaseOutside,
    FocusLost
}

internal readonly struct InputDiagnosticButtonState(
    InputDiagnosticButtonStateKind Kind,
    ActionId ActionId,
    ButtonVisualState State) : IEquatable<InputDiagnosticButtonState>
{
    public InputDiagnosticButtonStateKind Kind { get; } = Kind;
    public ActionId ActionId { get; } = ActionId;
    public ButtonVisualState State { get; } = State;

    public bool Equals(InputDiagnosticButtonState other)
    {
        return Kind == other.Kind
            && ActionId == other.ActionId
            && State == other.State;
    }

    public override bool Equals(object? obj) => obj is InputDiagnosticButtonState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, ActionId, State);

    public static bool operator ==(InputDiagnosticButtonState left, InputDiagnosticButtonState right) => left.Equals(right);

    public static bool operator !=(InputDiagnosticButtonState left, InputDiagnosticButtonState right) => !left.Equals(right);
}

internal enum InputDiagnosticOwnershipStepKind : byte
{
    AfterMove,
    AfterPress,
    DuringCaptureMove,
    ReleaseOutside,
    KeyboardEnter,
    KeyboardSpace,
    PressEmpty,
    ReleaseAfterEmptyPress,
    FocusLost
}

internal readonly struct InputDiagnosticOwnershipStep(
    InputDiagnosticOwnershipStepKind Kind,
    OwnershipSnapshot Ownership,
    bool HasMappedResult = false,
    bool Mapped = false,
    CounterMessage? Message = null) : IEquatable<InputDiagnosticOwnershipStep>
{
    public InputDiagnosticOwnershipStepKind Kind { get; } = Kind;
    public OwnershipSnapshot Ownership { get; } = Ownership;
    public bool HasMappedResult { get; } = HasMappedResult;
    public bool Mapped { get; } = Mapped;
    public CounterMessage? Message { get; } = Message;

    public bool Equals(InputDiagnosticOwnershipStep other)
    {
        return Kind == other.Kind
            && Ownership == other.Ownership
            && HasMappedResult == other.HasMappedResult
            && Mapped == other.Mapped
            && EqualityComparer<CounterMessage?>.Default.Equals(Message, other.Message);
    }

    public override bool Equals(object? obj) => obj is InputDiagnosticOwnershipStep other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Ownership, HasMappedResult, Mapped, Message);

    public static bool operator ==(InputDiagnosticOwnershipStep left, InputDiagnosticOwnershipStep right) => left.Equals(right);

    public static bool operator !=(InputDiagnosticOwnershipStep left, InputDiagnosticOwnershipStep right) => !left.Equals(right);
}

internal enum InputDirtyReasonCase : byte
{
    HoverOnly,
    Press,
    Release
}

internal readonly struct InputDirtyReasonDiagnostic(
    InputDirtyReasonCase Case,
    LayoutRebuildReason Reason,
    IReadOnlyList<LayoutDirtyClassification> Classifications) : IEquatable<InputDirtyReasonDiagnostic>
{
    public InputDirtyReasonCase Case { get; } = Case;
    public LayoutRebuildReason Reason { get; } = Reason;
    public IReadOnlyList<LayoutDirtyClassification> Classifications { get; } = Classifications;

    public bool Equals(InputDirtyReasonDiagnostic other)
    {
        return Case == other.Case
            && Reason == other.Reason
            && EqualityComparer<IReadOnlyList<LayoutDirtyClassification>>.Default.Equals(Classifications, other.Classifications);
    }

    public override bool Equals(object? obj) => obj is InputDirtyReasonDiagnostic other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Case, Reason, Classifications);

    public static bool operator ==(InputDirtyReasonDiagnostic left, InputDirtyReasonDiagnostic right) => left.Equals(right);

    public static bool operator !=(InputDirtyReasonDiagnostic left, InputDirtyReasonDiagnostic right) => !left.Equals(right);
}
