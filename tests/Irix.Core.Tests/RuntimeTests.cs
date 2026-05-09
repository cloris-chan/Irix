using Xunit;

namespace Irix.Core.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public async Task StartAsync_and_Dispatch_publish_virtual_node_patches()
    {
        var patchSink = new RecordingPatchSink();
        await using var runtime = new Runtime<TestModel, TestMessage>(new TestApplication(), patchSink);

        await runtime.StartAsync(TestContext.Current.CancellationToken);
        await patchSink.WaitForBatchCountAsync(1);

        runtime.Dispatch(new TestMessage.Increment());
        await patchSink.WaitForBatchCountAsync(2);

        Assert.Equal(1, runtime.CurrentModel.Count);
        Assert.Equal(2, patchSink.PublishedBatches.Count);
        Assert.All(patchSink.PublishedBatches, batch => Assert.Single(batch));
    }

    [Fact]
    public async Task DispatchAndWaitAsync_completes_after_patch_render_completion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var patchSink = new BlockingRenderPatchSink();
        await using var runtime = new Runtime<TestModel, TestMessage>(new TestApplication(), patchSink);

        await runtime.StartAsync(cancellationToken);

        var dispatchTask = runtime.DispatchAndWaitAsync(new TestMessage.Increment(), cancellationToken);
        await patchSink.WaitForWaitedPatchAsync(cancellationToken);

        Assert.False(dispatchTask.IsCompleted);
        Assert.Equal(1, runtime.CurrentModel.Count);

        patchSink.CompleteRender();
        await dispatchTask.WaitAsync(cancellationToken);
    }

    private sealed record TestModel(int Count);

    private abstract record TestMessage
    {
        public sealed record Increment : TestMessage;
    }

    private sealed class TestApplication : IApplication<TestModel, TestMessage>
    {
        public TestModel Initialize() => new(0);

        public UpdateResult<TestModel, TestMessage> Update(TestModel model, TestMessage message) =>
            message switch
            {
                TestMessage.Increment => new UpdateResult<TestModel, TestMessage>(model with { Count = model.Count + 1 }),
                _ => throw new NotSupportedException()
            };

        public VirtualNodeTree BuildView(TestModel model)
        {
            return new VirtualNodeTree(VirtualNodeFactory.Text($"Count: {model.Count}", 1));
        }
    }

    private sealed class RecordingPatchSink : IVirtualNodePatchSink
    {
        private readonly TaskCompletionSource _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<VirtualNodePatch[]> PublishedBatches { get; } = [];

        public ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            try
            {
                PublishedBatches.Add(patchBatch.Memory[..patchBatch.Count].ToArray());
            }
            finally
            {
                patchBatch.Dispose();
                _taskCompletionSource.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            return PublishAsync(patchBatch, cancellationToken);
        }

        public async Task WaitForBatchCountAsync(int expectedCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            while (PublishedBatches.Count < expectedCount)
            {
                await Task.WhenAny(_taskCompletionSource.Task, Task.Delay(25, timeout.Token));
            }
        }
    }

    private sealed class BlockingRenderPatchSink : IVirtualNodePatchSink
    {
        private readonly TaskCompletionSource _waitedPatchPublished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _renderCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            patchBatch.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            patchBatch.Dispose();
            _waitedPatchPublished.TrySetResult();
            return new ValueTask(_renderCompleted.Task.WaitAsync(cancellationToken));
        }

        public Task WaitForWaitedPatchAsync(CancellationToken cancellationToken)
        {
            return _waitedPatchPublished.Task.WaitAsync(cancellationToken);
        }

        public void CompleteRender()
        {
            _renderCompleted.TrySetResult();
        }
    }
}
