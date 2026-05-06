using System.Buffers;
using Irix.Drawing;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class BatchOwnershipTests
{
    [Fact]
    public void PatchBatch_dispose_releases_owner_memory()
    {
        var owner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeFactory.Text("Count: 0", 1))
        ]);
        var batch = new PatchBatch(owner, 1);

        batch.Dispose();

        Assert.Equal(1, owner.DisposeCallCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = batch.Memory);
    }

    [Fact]
    public void DrawCommandBatch_dispose_releases_owner_memory()
    {
        var owner = new TrackingMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32))
        ]);
        var batch = new DrawCommandBatch(owner, 1);

        batch.Dispose();

        Assert.Equal(1, owner.DisposeCallCount);
        Assert.Equal(0, batch.Memory.Length);
    }

    [Fact]
    public async Task CompositorLoop_disposes_patch_and_draw_batches_after_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeFactory.Text("Count: 0", 1))
        ]);
        var drawOwner = new TrackingMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32))
        ]);
        var translator = new FakeTranslator(drawOwner);
        var compositor = new RecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var patchBatch = new PatchBatch(patchOwner, 1);

        await loop.PublishAsync(patchBatch, cancellationToken);
        await compositor.WaitForRenderAsync(cancellationToken);
        await WaitForConditionAsync(() => patchOwner.DisposeCallCount == 1 && drawOwner.DisposeCallCount == 1, cancellationToken);

        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, patchOwner.DisposeCallCount);
        Assert.Equal(1, drawOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_skips_translation_and_rendering_when_patch_count_is_zero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var drawOwner = new TrackingMemoryOwner<DrawCommand>([]);
        var translator = new FakeTranslator(drawOwner);
        var compositor = new RecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        // Publish a no-change PatchBatch (Count = 0)
        var emptyOwner = new TrackingMemoryOwner<VirtualNodePatch>([]);
        var emptyBatch = new PatchBatch(emptyOwner, 0);
        await loop.PublishAsync(emptyBatch, cancellationToken);

        // Wait briefly to let the processing loop run
        await Task.Delay(100, cancellationToken);

        Assert.Equal(0, translator.TranslateCallCount);
        Assert.Equal(0, compositor.RenderCallCount);
        Assert.Equal(1, emptyOwner.DisposeCallCount);
    }

    private sealed class TrackingMemoryOwner<T>(T[] buffer) : IMemoryOwner<T>
    {
        private T[]? _buffer = buffer;

        public int DisposeCallCount { get; private set; }

        public Memory<T> Memory => _buffer ?? Memory<T>.Empty;

        public void Dispose()
        {
            DisposeCallCount++;
            _buffer = null;
        }
    }

    private sealed class FakeTranslator(TrackingMemoryOwner<DrawCommand> drawOwner) : IPatchBatchTranslator
    {
        public int TranslateCallCount { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            return new RenderFrameBatch(new DrawCommandBatch(drawOwner, 1), []);
        }
    }

    private sealed class RecordingCompositor : ICompositor
    {
        private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RenderCallCount { get; private set; }

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            _rendered.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public Task WaitForRenderAsync(CancellationToken cancellationToken)
        {
            return _rendered.Task.WaitAsync(cancellationToken);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
