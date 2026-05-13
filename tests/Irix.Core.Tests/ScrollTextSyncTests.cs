using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Regression tests for text-overlay synchronization during scrolling.
/// Ensures rect and text commands arrive in the same frame batch with consistent
/// positions, which is the prerequisite for GPU-level sync (D3D12Renderer.SyncTextOverlay).
/// </summary>
public sealed class ScrollTextSyncTests
{
    [Fact]
    public async Task Continuous_scroll_rect_and_text_same_frame_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new SyncTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Simulate 60 continuous scroll frames (like scrolling at 60Hz for 1 second)
        for (var scrollY = 0; scrollY < 600; scrollY += 10)
        {
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("Button A", 2,
                        VirtualNodeAttribute.Action(new ActionId(100))),
                    VirtualNodeFactory.Button("Button B", 3,
                        VirtualNodeAttribute.Action(new ActionId(101))),
                    VirtualNodeFactory.Button("Button C", 4,
                        VirtualNodeAttribute.Action(new ActionId(102))),
                ]);

            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);

            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        // Every frame that had rects must also have had text in the same Execute call.
        // A frame with rects but no text would indicate the D3D12 rect pass and D2D text
        // pass are split across frames — the exact condition that causes text-lag.
        Assert.NotEmpty(backend.FrameSnapshots);
        foreach (var snapshot in backend.FrameSnapshots)
        {
            if (snapshot.RectCount > 0)
            {
                Assert.True(snapshot.TextCount > 0,
                    $"Frame {snapshot.FrameIndex}: {snapshot.RectCount} rects but 0 texts. " +
                    "Rect and text must arrive in the same Execute call for sync to work.");
            }
        }
    }

    [Fact]
    public async Task Continuous_scroll_text_positions_track_rect_positions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new SyncTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Scroll with larger steps to make position differences visible
        for (var scrollY = 0; scrollY < 300; scrollY += 20)
        {
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("ScrollBtn", 2,
                        VirtualNodeAttribute.Action(new ActionId(100))),
                ]);

            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);

            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        // For each frame, the button's rect Y and text Y must be identical.
        // A mismatch would mean the layout produced inconsistent coordinates
        // (which shouldn't happen, but guards against layout bugs during scroll).
        foreach (var snapshot in backend.FrameSnapshots)
        {
            if (snapshot.FirstRectY.HasValue && snapshot.FirstTextY.HasValue)
            {
                Assert.Equal(snapshot.FirstRectY.Value, snapshot.FirstTextY.Value);
            }
        }
    }

    [Fact]
    public async Task Rapid_scroll_no_frame_skips_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new SyncTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Simulate rapid scroll: 200 frames with small increments
        // This mimics fast mouse-wheel scrolling at high Hz
        for (var i = 0; i < 200; i++)
        {
            var scrollY = i * 3; // 3px per frame = fast scroll

            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("Fast", 2,
                        VirtualNodeAttribute.Action(new ActionId(100))),
                    VirtualNodeFactory.Button("Scroll", 3,
                        VirtualNodeAttribute.Action(new ActionId(101))),
                ]);

            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);

            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        Assert.Equal(200, backend.FrameSnapshots.Count);

        // No frame should have rects without text during rapid scroll
        var framesWithRectsNoText = backend.FrameSnapshots
            .Where(f => f.RectCount > 0 && f.TextCount == 0)
            .ToList();

        Assert.Empty(framesWithRectsNoText);
    }

    [Fact]
    public async Task Scroll_stop_resume_text_stays_synchronized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new SyncTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Phase 1: scroll down
        for (var scrollY = 0; scrollY < 100; scrollY += 10)
        {
            var root = CreateScrollFrame(scrollY);
            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);
            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        // Phase 2: pause (same scroll position, like scroll stop)
        for (var i = 0; i < 30; i++)
        {
            var root = CreateScrollFrame(100);
            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);
            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        // Phase 3: resume scroll
        for (var scrollY = 100; scrollY < 200; scrollY += 10)
        {
            var root = CreateScrollFrame(scrollY);
            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);
            await compositor.RenderAsync(batch, cancellationToken);
            batch.Dispose();
        }

        // All frames across all phases must have consistent rect/text pairing
        foreach (var snapshot in backend.FrameSnapshots)
        {
            if (snapshot.RectCount > 0)
            {
                Assert.True(snapshot.TextCount > 0,
                    $"Frame {snapshot.FrameIndex} (phase boundary): rect/text mismatch");
            }
        }

        // Verify no stale positions during pause: all pause frames should have same Y
        var pauseFrames = backend.FrameSnapshots.Skip(10).Take(30).ToList();
        var firstPauseY = pauseFrames[0].FirstRectY;
        foreach (var frame in pauseFrames)
        {
            if (frame.FirstRectY.HasValue && firstPauseY.HasValue)
            {
                Assert.Equal(firstPauseY.Value, frame.FirstRectY.Value);
            }
        }
    }

    private static VirtualNode CreateScrollFrame(double scrollY)
    {
        return new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(scrollY))],
            children:
            [
                VirtualNodeFactory.Button("Btn", 2,
                    VirtualNodeAttribute.Action(new ActionId(100))),
            ]);
    }

    private sealed class SyncTrackingBackend : IDrawingBackend
    {
        public List<FrameSnapshot> FrameSnapshots { get; } = [];
        private int _frameIndex;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            var rectCount = 0;
            var textCount = 0;
            double? firstRectY = null;
            double? firstTextY = null;

            foreach (var cmd in commands)
            {
                switch (cmd.Kind)
                {
                    case DrawCommandKind.FillRect:
                        rectCount++;
                        firstRectY ??= cmd.Rect.Y;
                        break;
                    case DrawCommandKind.DrawTextRun:
                        textCount++;
                        firstTextY ??= cmd.Rect.Y;
                        break;
                }
            }

            FrameSnapshots.Add(new FrameSnapshot(_frameIndex++, rectCount, textCount, firstRectY, firstTextY));
        }

        public void EndFrame() { }
        public void Dispose() { }
    }

    private readonly record struct FrameSnapshot(
        int FrameIndex,
        int RectCount,
        int TextCount,
        double? FirstRectY,
        double? FirstTextY);
}
