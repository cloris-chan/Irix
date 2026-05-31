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
        using var transformBackend = new PlainBackend();
        using var transformCompositor = new DrawingBackendCompositor(transformBackend);
        using var transformFrame = CreateFrame(commandCount: 1);
        transformCompositor.RenderAsync(transformFrame).GetAwaiter().GetResult();
        transformCompositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = TryRenderTransformTick(transformCompositor);
        var transformBackendSkip = transformCompositor.LastCompositionExecutionStatus;

        using var scrollBackend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var scrollCompositor = new DrawingBackendCompositor(scrollBackend);
        using var scrollFrame = CreateFrame(commandCount: 2);
        scrollCompositor.RenderAsync(scrollFrame).GetAwaiter().GetResult();
        scrollCompositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 2));
        _ = TryRenderScrollTick(scrollCompositor);
        var scrollCapabilitySkip = scrollCompositor.LastCompositionExecutionStatus;

        using var retainedBackend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity);
        using var retainedCompositor = new DrawingBackendCompositor(retainedBackend);
        using var retainedFrame = CreateFrame(commandCount: 2);
        retainedCompositor.RenderAsync(retainedFrame).GetAwaiter().GetResult();
        retainedCompositor.SetCompositionScrollPresentationPlan(CreateScrollPlan(commandCount: 2));
        using var nextRetainedFrame = CreateFrame(commandCount: 2);
        retainedCompositor.RenderAsync(nextRetainedFrame).GetAwaiter().GetResult();
        var retainedUpdateSkip = retainedCompositor.LastCompositionExecutionStatus;

        using var successBackend = new CapabilityBackend(CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer);
        using var successCompositor = new DrawingBackendCompositor(successBackend);
        using var successFrame = CreateFrame(commandCount: 1);
        successCompositor.RenderAsync(successFrame).GetAwaiter().GetResult();
        successCompositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));
        _ = successCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1)).GetAwaiter().GetResult();
        var executed = successCompositor.LastCompositionExecutionStatus;

        return new CompositionSkipDiagnostics(
            transformBackendSkip,
            scrollCapabilitySkip,
            retainedUpdateSkip,
            executed,
            successBackend.ExecuteCompositionCount,
            retainedBackend.ExecuteCompositionCount);
    }

    internal static string Format(in CompositionSkipDiagnostics diagnostics)
    {
        return $"composition-skip actual transform={FormatStatus(diagnostics.TransformBackendSkip)} scroll={FormatStatus(diagnostics.ScrollCapabilitySkip)} retainedUpdate={FormatStatus(diagnostics.RetainedUpdateSkip)} executed={FormatStatus(diagnostics.ExecutedStatus)} executedCompositionCount={diagnostics.ExecutedCompositionCount} skippedCompositionCount={diagnostics.SkippedCompositionCount}";
    }

    internal static string FormatStatus(in CompositionExecutionStatus status)
    {
        return $"{status.Kind}:{status.SkipReason}:required={FormatCapabilities(status.RequiredCapabilities)}:backend={FormatCapabilities(status.BackendCapabilities)}:pacing={status.FramePacing}:layers={status.LayerCount}:commands={status.CommandCount}";
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
}

internal readonly struct CompositionSkipDiagnostics(
    CompositionExecutionStatus TransformBackendSkip,
    CompositionExecutionStatus ScrollCapabilitySkip,
    CompositionExecutionStatus RetainedUpdateSkip,
    CompositionExecutionStatus ExecutedStatus,
    int ExecutedCompositionCount,
    int SkippedCompositionCount) : IEquatable<CompositionSkipDiagnostics>
{
    public CompositionExecutionStatus TransformBackendSkip { get; } = TransformBackendSkip;
    public CompositionExecutionStatus ScrollCapabilitySkip { get; } = ScrollCapabilitySkip;
    public CompositionExecutionStatus RetainedUpdateSkip { get; } = RetainedUpdateSkip;
    public CompositionExecutionStatus ExecutedStatus { get; } = ExecutedStatus;
    public int ExecutedCompositionCount { get; } = ExecutedCompositionCount;
    public int SkippedCompositionCount { get; } = SkippedCompositionCount;

    public bool Equals(CompositionSkipDiagnostics other)
    {
        return TransformBackendSkip == other.TransformBackendSkip
            && ScrollCapabilitySkip == other.ScrollCapabilitySkip
            && RetainedUpdateSkip == other.RetainedUpdateSkip
            && ExecutedStatus == other.ExecutedStatus
            && ExecutedCompositionCount == other.ExecutedCompositionCount
            && SkippedCompositionCount == other.SkippedCompositionCount;
    }

    public override bool Equals(object? obj) => obj is CompositionSkipDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TransformBackendSkip, ScrollCapabilitySkip, RetainedUpdateSkip, ExecutedStatus, ExecutedCompositionCount, SkippedCompositionCount);
}
#endif
