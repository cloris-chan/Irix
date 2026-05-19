using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct DrawingBackendCompositorShadowProbeExecution(
    int CommandStart,
    int CommandCount,
    IFrameResourceResolver Resolver) : IEquatable<DrawingBackendCompositorShadowProbeExecution>
{

    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public IFrameResourceResolver Resolver { get; } = Resolver;

    public bool Equals(DrawingBackendCompositorShadowProbeExecution other)
    {
        return CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && EqualityComparer<IFrameResourceResolver>.Default.Equals(Resolver, other.Resolver);
    }

    public override bool Equals(object? obj) => obj is DrawingBackendCompositorShadowProbeExecution other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CommandStart, CommandCount, Resolver);

    public static bool operator ==(DrawingBackendCompositorShadowProbeExecution left, DrawingBackendCompositorShadowProbeExecution right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCompositorShadowProbeExecution left, DrawingBackendCompositorShadowProbeExecution right) => !left.Equals(right);
}

internal enum DrawingBackendCallKind : byte
{
    BeginFrame,
    Execute,
    EndFrame
}

internal readonly struct DrawingBackendCall(DrawingBackendCallKind Kind, int CommandCount) : IEquatable<DrawingBackendCall>
{
    public DrawingBackendCallKind Kind { get; } = Kind;

    public int CommandCount { get; } = CommandCount;

    public static DrawingBackendCall BeginFrame => new(DrawingBackendCallKind.BeginFrame, 0);

    public static DrawingBackendCall Execute(int commandCount) => new(DrawingBackendCallKind.Execute, commandCount);

    public static DrawingBackendCall EndFrame => new(DrawingBackendCallKind.EndFrame, 0);

    public bool Equals(DrawingBackendCall other) => Kind == other.Kind && CommandCount == other.CommandCount;

    public override bool Equals(object? obj) => obj is DrawingBackendCall other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, CommandCount);

    public static bool operator ==(DrawingBackendCall left, DrawingBackendCall right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCall left, DrawingBackendCall right) => !left.Equals(right);
}

internal readonly struct DrawingBackendCompositorShadowProbeHitTest(
    bool BeforeHit,
    ActionId BeforeActionId,
    bool AfterHit,
    ActionId AfterActionId) : IEquatable<DrawingBackendCompositorShadowProbeHitTest>
{

    public bool BeforeHit { get; } = BeforeHit;
    public ActionId BeforeActionId { get; } = BeforeActionId;
    public bool AfterHit { get; } = AfterHit;
    public ActionId AfterActionId { get; } = AfterActionId;

    public bool Unchanged => BeforeHit == AfterHit && BeforeActionId == AfterActionId;

    public bool Equals(DrawingBackendCompositorShadowProbeHitTest other)
    {
        return BeforeHit == other.BeforeHit
            && BeforeActionId.Equals(other.BeforeActionId)
            && AfterHit == other.AfterHit
            && AfterActionId.Equals(other.AfterActionId);
    }

    public override bool Equals(object? obj) => obj is DrawingBackendCompositorShadowProbeHitTest other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BeforeHit, BeforeActionId, AfterHit, AfterActionId);

    public static bool operator ==(DrawingBackendCompositorShadowProbeHitTest left, DrawingBackendCompositorShadowProbeHitTest right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCompositorShadowProbeHitTest left, DrawingBackendCompositorShadowProbeHitTest right) => !left.Equals(right);
}

internal readonly struct DrawingBackendCompositorShadowProbeResult(
    IReadOnlyList<DrawingBackendCompositorShadowProbeExecution> Executions,
    IReadOnlyList<DrawingBackendCall> Calls,
    DrawingBackendCompositorShadowProbeHitTest HitTest) : IEquatable<DrawingBackendCompositorShadowProbeResult>
{

    public IReadOnlyList<DrawingBackendCompositorShadowProbeExecution> Executions { get; } = Executions;
    public IReadOnlyList<DrawingBackendCall> Calls { get; } = Calls;
    public DrawingBackendCompositorShadowProbeHitTest HitTest { get; } = HitTest;

    public bool HitTestUnchanged => HitTest.Unchanged;

    public bool Equals(DrawingBackendCompositorShadowProbeResult other)
    {
        return EqualityComparer<IReadOnlyList<DrawingBackendCompositorShadowProbeExecution>>.Default.Equals(Executions, other.Executions)
            && EqualityComparer<IReadOnlyList<DrawingBackendCall>>.Default.Equals(Calls, other.Calls)
            && HitTest == other.HitTest;
    }

    public override bool Equals(object? obj) => obj is DrawingBackendCompositorShadowProbeResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Executions, Calls, HitTest);

    public static bool operator ==(DrawingBackendCompositorShadowProbeResult left, DrawingBackendCompositorShadowProbeResult right) => left.Equals(right);

    public static bool operator !=(DrawingBackendCompositorShadowProbeResult left, DrawingBackendCompositorShadowProbeResult right) => !left.Equals(right);
}

internal sealed class DrawingBackendCompositorShadowProbe(IDrawingBackend backend)
{
    public DrawingBackendCompositorShadowProbeResult Execute(
        in FrameContext frameContext,
        IReadOnlyList<SegmentedFrameRead> segments,
        DrawingBackendCompositor compositor,
        int hitTestX,
        int hitTestY)
    {
        var beforeHit = compositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var beforeActionId);
        var recordingBackend = new RecordingBackend(backend, segments);
        new SegmentedBackendExecutionAdapter(recordingBackend).Execute(frameContext, segments);
        var afterHit = compositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var afterActionId);

        return new DrawingBackendCompositorShadowProbeResult(
            recordingBackend.Executions,
            recordingBackend.Calls,
            new DrawingBackendCompositorShadowProbeHitTest(beforeHit, beforeActionId, afterHit, afterActionId));
    }

    private sealed class RecordingBackend(IDrawingBackend inner, IReadOnlyList<SegmentedFrameRead> segments) : IDrawingBackend
    {
        private readonly List<DrawingBackendCompositorShadowProbeExecution> _executions = [];
        private readonly List<DrawingBackendCall> _calls = [];

        public IReadOnlyList<DrawingBackendCompositorShadowProbeExecution> Executions => _executions;

        public IReadOnlyList<DrawingBackendCall> Calls => _calls;

        public void BeginFrame(in FrameContext frameContext)
        {
            _calls.Add(DrawingBackendCall.BeginFrame);
            inner.BeginFrame(frameContext);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            var index = _executions.Count;
            var commandStart = index < segments.Count ? segments[index].CommandStart : -1;
            _executions.Add(new DrawingBackendCompositorShadowProbeExecution(commandStart, commands.Length, resources));
            _calls.Add(DrawingBackendCall.Execute(commands.Length));
            inner.Execute(commands, resources);
        }

        public void EndFrame()
        {
            _calls.Add(DrawingBackendCall.EndFrame);
            inner.EndFrame();
        }

        public void Dispose()
        {
        }
    }
}
