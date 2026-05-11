using Irix.Drawing;

namespace Irix.Rendering;

internal readonly record struct DrawingBackendCompositorShadowProbeExecution(
    int CommandStart,
    int CommandCount,
    IFrameResourceResolver Resolver);

internal readonly record struct DrawingBackendCompositorShadowProbeHitTest(
    bool BeforeHit,
    string BeforeActionId,
    bool AfterHit,
    string AfterActionId)
{
    public bool Unchanged => BeforeHit == AfterHit && BeforeActionId == AfterActionId;
}

internal readonly record struct DrawingBackendCompositorShadowProbeResult(
    IReadOnlyList<DrawingBackendCompositorShadowProbeExecution> Executions,
    IReadOnlyList<string> Calls,
    DrawingBackendCompositorShadowProbeHitTest HitTest)
{
    public bool HitTestUnchanged => HitTest.Unchanged;
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
        var beforeHit = compositor.TryGetActionIdAt(hitTestX, hitTestY, out var beforeActionId);
        var recordingBackend = new RecordingBackend(backend, segments);
        new SegmentedBackendExecutionAdapter(recordingBackend).Execute(frameContext, segments);
        var afterHit = compositor.TryGetActionIdAt(hitTestX, hitTestY, out var afterActionId);

        return new DrawingBackendCompositorShadowProbeResult(
            recordingBackend.Executions,
            recordingBackend.Calls,
            new DrawingBackendCompositorShadowProbeHitTest(beforeHit, beforeActionId, afterHit, afterActionId));
    }

    private sealed class RecordingBackend(IDrawingBackend inner, IReadOnlyList<SegmentedFrameRead> segments) : IDrawingBackend
    {
        private readonly List<DrawingBackendCompositorShadowProbeExecution> _executions = [];
        private readonly List<string> _calls = [];

        public IReadOnlyList<DrawingBackendCompositorShadowProbeExecution> Executions => _executions;

        public IReadOnlyList<string> Calls => _calls;

        public void BeginFrame(in FrameContext frameContext)
        {
            _calls.Add("BeginFrame");
            inner.BeginFrame(frameContext);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            var index = _executions.Count;
            var commandStart = index < segments.Count ? segments[index].CommandStart : -1;
            _executions.Add(new DrawingBackendCompositorShadowProbeExecution(commandStart, commands.Length, resources));
            _calls.Add($"Execute:{commands.Length}");
            inner.Execute(commands, resources);
        }

        public void EndFrame()
        {
            _calls.Add("EndFrame");
            inner.EndFrame();
        }

        public void Dispose()
        {
        }
    }
}