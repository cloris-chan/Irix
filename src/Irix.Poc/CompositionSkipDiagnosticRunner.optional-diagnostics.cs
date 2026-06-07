#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CompositionSkipDiagnosticRunner
{
    internal static void Run(TextWriter output)
    {
        var diagnostics = RunCore();
        output.WriteLine("=== Composition Skip Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== composition skip diagnostic complete ===");
    }

    internal static CompositionSkipDiagnostics RunCore()
    {
        var transformNoPlanSkip = CaptureTransformNoPlanSkip();
        var transformBackendSkip = CaptureTransformBackendSkip();
        var transformMissingFrameSkip = CaptureTransformMissingRetainedFrameSkip();
        var transformInvalidFrameSkip = CaptureTransformInvalidRetainedFrameSkip();
        var scrollNoPlanSkip = CaptureScrollNoPlanSkip();
        var scrollCapabilitySkip = CaptureScrollMissingCapabilitySkip();
        var scrollMissingFrameSkip = CaptureScrollMissingRetainedFrameSkip();
        var scrollInvalidFrameSkip = CaptureScrollInvalidRetainedFrameSkip();
        var retainedUpdateNoPlanSkip = CaptureRetainedUpdateNoPlanSkip();
        var retainedUpdateSkip = CaptureRetainedUpdateMissingCapabilitySkip();
        var presentationNoPlanSkip = CapturePresentationNoPlanSkip();
        var presentationCapabilitySkip = CapturePresentationMissingCapabilitySkip();
        var presentationExecuted = CapturePresentationExecutedStatus(out var presentationExecutedCompositionCount);
        var deviceLostRecoveredSkip = CaptureDeviceLostRecoveredSkip(out var recoveredBackendExecuteCount);
        var executed = CaptureExecutedStatus(out var executedCompositionCount);

        return new CompositionSkipDiagnostics(
            transformNoPlanSkip,
            transformBackendSkip,
            transformMissingFrameSkip,
            transformInvalidFrameSkip,
            scrollNoPlanSkip,
            scrollCapabilitySkip,
            scrollMissingFrameSkip,
            scrollInvalidFrameSkip,
            retainedUpdateNoPlanSkip,
            retainedUpdateSkip,
            presentationNoPlanSkip,
            presentationCapabilitySkip,
            presentationExecuted,
            deviceLostRecoveredSkip,
            executed,
            presentationExecutedCompositionCount,
            executedCompositionCount,
            recoveredBackendExecuteCount);
    }

    internal static string Format(in CompositionSkipDiagnostics diagnostics)
    {
        return $"composition-skip actual transformNoPlan={FormatStatus(diagnostics.TransformNoPlanSkip)} transformBackend={FormatStatus(diagnostics.TransformBackendSkip)} transformMissingFrame={FormatStatus(diagnostics.TransformMissingFrameSkip)} transformInvalidFrame={FormatStatus(diagnostics.TransformInvalidFrameSkip)} scrollNoPlan={FormatStatus(diagnostics.ScrollNoPlanSkip)} scrollCapability={FormatStatus(diagnostics.ScrollCapabilitySkip)} scrollMissingFrame={FormatStatus(diagnostics.ScrollMissingFrameSkip)} scrollInvalidFrame={FormatStatus(diagnostics.ScrollInvalidFrameSkip)} retainedUpdateNoPlan={FormatStatus(diagnostics.RetainedUpdateNoPlanSkip)} retainedUpdateCapability={FormatStatus(diagnostics.RetainedUpdateSkip)} presentationNoPlan={FormatStatus(diagnostics.PresentationNoPlanSkip)} presentationCapability={FormatStatus(diagnostics.PresentationCapabilitySkip)} presentationExecuted={FormatStatus(diagnostics.PresentationExecutedStatus)} deviceLostRecovered={FormatStatus(diagnostics.DeviceLostRecoveredSkip)} executed={FormatStatus(diagnostics.ExecutedStatus)} presentationExecutedCompositionCount={diagnostics.PresentationExecutedCompositionCount} executedCompositionCount={diagnostics.ExecutedCompositionCount} recoveredCompositionCount={diagnostics.RecoveredCompositionCount}";
    }

    internal static string FormatStatus(in CompositionExecutionStatus status)
    {
        return $"{status.Kind}:{status.SkipReason}:isSkipped={status.IsSkipped}:required={FormatCapabilities(status.RequiredCapabilities)}:backend={FormatCapabilities(status.BackendCapabilities)}:pacing={status.FramePacing}:layers={status.LayerCount}:commands={status.CommandCount}";
    }

    internal static string FormatCapabilities(CompositionBackendCapabilities capabilities)
    {
        if (capabilities == CompositionBackendCapabilities.None)
        {
            return nameof(CompositionBackendCapabilities.None);
        }

        var parts = new string[7];
        var count = 0;
        var represented = CompositionBackendCapabilities.None;
        if ((capabilities & CompositionBackendCapabilities.TransformOpacity) == CompositionBackendCapabilities.TransformOpacity)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.TransformOpacity);
            represented |= CompositionBackendCapabilities.TransformOpacity;
        }

        if ((capabilities & CompositionBackendCapabilities.ScrollPresentation) == CompositionBackendCapabilities.ScrollPresentation)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.ScrollPresentation);
            represented |= CompositionBackendCapabilities.ScrollPresentation;
        }

        if ((capabilities & CompositionBackendCapabilities.MultiLayer) == CompositionBackendCapabilities.MultiLayer)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.MultiLayer);
            represented |= CompositionBackendCapabilities.MultiLayer;
        }

        if ((capabilities & CompositionBackendCapabilities.LayerContentCache) == CompositionBackendCapabilities.LayerContentCache)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.LayerContentCache);
            represented |= CompositionBackendCapabilities.LayerContentCache;
        }

        var remaining = capabilities & ~represented;
        if ((remaining & CompositionBackendCapabilities.Transform) == CompositionBackendCapabilities.Transform)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.Transform);
        }

        if ((remaining & CompositionBackendCapabilities.Opacity) == CompositionBackendCapabilities.Opacity)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.Opacity);
        }

        if ((remaining & CompositionBackendCapabilities.FixedClip) == CompositionBackendCapabilities.FixedClip)
        {
            parts[count++] = nameof(CompositionBackendCapabilities.FixedClip);
        }

        return string.Join("|", parts.AsSpan(0, count).ToArray());
    }

    private static CompositionExecutionStatus CaptureTransformNoPlanSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        _ = TryRenderTransformTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureTransformBackendSkip()
    {
        using var backend = new PlainBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = TryRenderTransformTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureTransformMissingRetainedFrameSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = TryRenderTransformTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureTransformInvalidRetainedFrameSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 2));
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        _ = TryRenderTransformTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureScrollNoPlanSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.ScrollPresentation);
        using var compositor = new DrawingBackendCompositor(backend);
        _ = TryRenderScrollTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureScrollMissingCapabilitySkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 2);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 2));
        _ = TryRenderScrollTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureScrollMissingRetainedFrameSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.ScrollPresentation);
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 1));
        _ = TryRenderScrollTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureScrollInvalidRetainedFrameSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.ScrollPresentation);
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 2));
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        _ = TryRenderScrollTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureRetainedUpdateNoPlanSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.ScrollPresentation);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureRetainedUpdateMissingCapabilitySkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 2);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 2));
        using var nextFrame = CreateFrame(commandCount: 2);
        compositor.RenderAsync(nextFrame).GetAwaiter().GetResult();
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CapturePresentationNoPlanSkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer);
        using var compositor = new DrawingBackendCompositor(backend);
        _ = TryRenderPresentationTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CapturePresentationMissingCapabilitySkip()
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 2);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.ActivateCompositionAnimationPresentationPlan(CreatePresentationPlanSet(commandCount: 2));
        _ = TryRenderPresentationTick(compositor);
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CapturePresentationExecutedStatus(out int executeCompositionCount)
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 2);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.ActivateCompositionAnimationPresentationPlan(CreatePresentationPlanSet(commandCount: 2));
        _ = compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
        executeCompositionCount = backend.ExecuteCompositionCount;
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureDeviceLostRecoveredSkip(out int executeCompositionCount)
    {
        using var backend = new RecoveringCompositionBackend(throwOnComposition: true);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
        executeCompositionCount = backend.ExecuteCompositionCount;
        return compositor.LastCompositionExecutionStatus;
    }

    private static CompositionExecutionStatus CaptureExecutedStatus(out int executeCompositionCount)
    {
        using var backend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateFrame(commandCount: 1);
        compositor.RenderAsync(frame).GetAwaiter().GetResult();
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
        executeCompositionCount = backend.ExecuteCompositionCount;
        return compositor.LastCompositionExecutionStatus;
    }

    private static bool TryRenderTransformTick(DrawingBackendCompositor compositor)
    {
        try
        {
            _ = compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryRenderScrollTick(DrawingBackendCompositor compositor)
    {
        try
        {
            _ = compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryRenderPresentationTick(DrawingBackendCompositor compositor)
    {
        try
        {
            _ = compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static RenderFrameBatch CreateFrame(int commandCount)
    {
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[commandCount];
        for (var i = 0; i < commandCount; i++)
        {
            commands[i] = new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(i * 4, i * 4, 16, 16), Color: DrawColor.Opaque((byte)(10 + i), 20, 30));
        }

        return new RenderFrameBatch(new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commandCount), [], resources);
    }

    private static CompositionAnimationPlan CreateAnimationPlan(int commandCount)
    {
        return new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: commandCount,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f)));
    }

    private static CompositionAnimationPresentationSetPlan CreatePresentationPlanSet(int commandCount)
    {
        return new CompositionAnimationPresentationSetPlan(
            [
                CreateAnimationPlan(layerId: new CompositionLayerId(1), commandStart: 0, commandCount: 1),
                CreateAnimationPlan(layerId: new CompositionLayerId(2), commandStart: 1, commandCount: commandCount - 1)
            ]);
    }

    private static CompositionAnimationPlan CreateAnimationPlan(
        CompositionLayerId layerId,
        int commandStart,
        int commandCount)
    {
        return new CompositionAnimationPlan(new CompositionLayerAnimation(
            layerId,
            CommandStart: commandStart,
            CommandCount: commandCount,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f)));
    }

    private static CompositionScrollPresentationPlan CreateScrollPlan(int commandCount)
    {
        return new CompositionScrollPresentationPlan(new CompositionScrollLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: commandCount,
            new PixelRectangle(0, 0, 64, 64),
            RetainedScrollY: 0,
            MaxScrollY: 64,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(1)),
            new CompositionScalarAnimation(0, 16)));
    }

    private sealed class PlainBackend : IDrawingBackend
    {
        public int ExecuteCount { get; private set; }

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public void EndFrame() { }
        public void Dispose() { }
    }

    private sealed class CapabilityBackend(CompositionBackendCapabilities capabilities) : IDrawingBackend, ICompositionDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities { get; } = capabilities;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            return new CompositionBackendExecutionResult(
                D3D12Backed: false,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: 0,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }
        public void Dispose() { }
    }

    private sealed class RecoveringCompositionBackend(bool throwOnComposition) : IDrawingBackend, ICompositionDrawingBackend, IDeviceRecovery
    {
        private bool _deviceRemoved;
        private bool _throwOnComposition = throwOnComposition;

        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public bool IsDeviceRemoved => _deviceRemoved;
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            if (_throwOnComposition)
            {
                _deviceRemoved = true;
                throw new InvalidOperationException("Device removed during composition diagnostic.");
            }

            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: 0,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }

        public bool TryRecover()
        {
            _deviceRemoved = false;
            _throwOnComposition = false;
            return true;
        }

        public void Dispose() { }
    }
}

internal readonly struct CompositionSkipDiagnostics(
    CompositionExecutionStatus TransformNoPlanSkip,
    CompositionExecutionStatus TransformBackendSkip,
    CompositionExecutionStatus TransformMissingFrameSkip,
    CompositionExecutionStatus TransformInvalidFrameSkip,
    CompositionExecutionStatus ScrollNoPlanSkip,
    CompositionExecutionStatus ScrollCapabilitySkip,
    CompositionExecutionStatus ScrollMissingFrameSkip,
    CompositionExecutionStatus ScrollInvalidFrameSkip,
    CompositionExecutionStatus RetainedUpdateNoPlanSkip,
    CompositionExecutionStatus RetainedUpdateSkip,
    CompositionExecutionStatus PresentationNoPlanSkip,
    CompositionExecutionStatus PresentationCapabilitySkip,
    CompositionExecutionStatus PresentationExecutedStatus,
    CompositionExecutionStatus DeviceLostRecoveredSkip,
    CompositionExecutionStatus ExecutedStatus,
    int PresentationExecutedCompositionCount,
    int ExecutedCompositionCount,
    int RecoveredCompositionCount) : IEquatable<CompositionSkipDiagnostics>
{
    public CompositionExecutionStatus TransformNoPlanSkip { get; } = TransformNoPlanSkip;
    public CompositionExecutionStatus TransformBackendSkip { get; } = TransformBackendSkip;
    public CompositionExecutionStatus TransformMissingFrameSkip { get; } = TransformMissingFrameSkip;
    public CompositionExecutionStatus TransformInvalidFrameSkip { get; } = TransformInvalidFrameSkip;
    public CompositionExecutionStatus ScrollNoPlanSkip { get; } = ScrollNoPlanSkip;
    public CompositionExecutionStatus ScrollCapabilitySkip { get; } = ScrollCapabilitySkip;
    public CompositionExecutionStatus ScrollMissingFrameSkip { get; } = ScrollMissingFrameSkip;
    public CompositionExecutionStatus ScrollInvalidFrameSkip { get; } = ScrollInvalidFrameSkip;
    public CompositionExecutionStatus RetainedUpdateNoPlanSkip { get; } = RetainedUpdateNoPlanSkip;
    public CompositionExecutionStatus RetainedUpdateSkip { get; } = RetainedUpdateSkip;
    public CompositionExecutionStatus PresentationNoPlanSkip { get; } = PresentationNoPlanSkip;
    public CompositionExecutionStatus PresentationCapabilitySkip { get; } = PresentationCapabilitySkip;
    public CompositionExecutionStatus PresentationExecutedStatus { get; } = PresentationExecutedStatus;
    public CompositionExecutionStatus DeviceLostRecoveredSkip { get; } = DeviceLostRecoveredSkip;
    public CompositionExecutionStatus ExecutedStatus { get; } = ExecutedStatus;
    public int PresentationExecutedCompositionCount { get; } = PresentationExecutedCompositionCount;
    public int ExecutedCompositionCount { get; } = ExecutedCompositionCount;
    public int RecoveredCompositionCount { get; } = RecoveredCompositionCount;

    public bool Equals(CompositionSkipDiagnostics other)
    {
        return TransformNoPlanSkip == other.TransformNoPlanSkip
            && TransformBackendSkip == other.TransformBackendSkip
            && TransformMissingFrameSkip == other.TransformMissingFrameSkip
            && TransformInvalidFrameSkip == other.TransformInvalidFrameSkip
            && ScrollNoPlanSkip == other.ScrollNoPlanSkip
            && ScrollCapabilitySkip == other.ScrollCapabilitySkip
            && ScrollMissingFrameSkip == other.ScrollMissingFrameSkip
            && ScrollInvalidFrameSkip == other.ScrollInvalidFrameSkip
            && RetainedUpdateNoPlanSkip == other.RetainedUpdateNoPlanSkip
            && RetainedUpdateSkip == other.RetainedUpdateSkip
            && PresentationNoPlanSkip == other.PresentationNoPlanSkip
            && PresentationCapabilitySkip == other.PresentationCapabilitySkip
            && PresentationExecutedStatus == other.PresentationExecutedStatus
            && DeviceLostRecoveredSkip == other.DeviceLostRecoveredSkip
            && ExecutedStatus == other.ExecutedStatus
            && PresentationExecutedCompositionCount == other.PresentationExecutedCompositionCount
            && ExecutedCompositionCount == other.ExecutedCompositionCount
            && RecoveredCompositionCount == other.RecoveredCompositionCount;
    }

    public override bool Equals(object? obj) => obj is CompositionSkipDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TransformNoPlanSkip);
        hash.Add(TransformBackendSkip);
        hash.Add(TransformMissingFrameSkip);
        hash.Add(TransformInvalidFrameSkip);
        hash.Add(ScrollNoPlanSkip);
        hash.Add(ScrollCapabilitySkip);
        hash.Add(ScrollMissingFrameSkip);
        hash.Add(ScrollInvalidFrameSkip);
        hash.Add(RetainedUpdateNoPlanSkip);
        hash.Add(RetainedUpdateSkip);
        hash.Add(PresentationNoPlanSkip);
        hash.Add(PresentationCapabilitySkip);
        hash.Add(PresentationExecutedStatus);
        hash.Add(DeviceLostRecoveredSkip);
        hash.Add(ExecutedStatus);
        hash.Add(PresentationExecutedCompositionCount);
        hash.Add(ExecutedCompositionCount);
        hash.Add(RecoveredCompositionCount);
        return hash.ToHashCode();
    }
}
#endif
