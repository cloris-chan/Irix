using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class VirtualTextArenaTests
{
    [Fact]
    public void Snapshot_returns_cached_snapshot_until_text_changes()
    {
        var arena = new VirtualTextArena();
        arena.AddText("cached".AsSpan());

        var first = arena.Snapshot();
        var second = arena.GetOrCreateSnapshot();

        Assert.Equal(first, second);
        Assert.Same(first.Buffer, second.Buffer);

        arena.AddText(" updated".AsSpan());
        var third = arena.Snapshot();

        Assert.NotSame(first.Buffer, third.Buffer);
    }

    [Fact]
    public void ResolveRequired_throws_when_content_buffer_does_not_match_snapshot()
    {
        var arena = new VirtualTextArena();
        _ = arena.AddText("previous".AsSpan());
        var previousSnapshot = arena.Snapshot();

        arena.BeginFrame();
        var nextContent = arena.AddText("next".AsSpan());

        var exception = Assert.Throws<InvalidOperationException>(
            () => previousSnapshot.ResolveRequired(nextContent).ToString());
        Assert.Contains("does not match snapshot buffer id", exception.Message);
    }

    [Fact]
    public void NodesEqual_throws_on_mismatched_text_snapshot_instead_of_silent_empty()
    {
        var arena = new VirtualTextArena();
        var previous = VirtualNodeBuilder.Text(arena, "same", new NodeKey(1));
        var previousSnapshot = arena.Snapshot();

        arena.BeginFrame();
        var next = VirtualNodeBuilder.Text(arena, "same", new NodeKey(1));
        var nextSnapshot = arena.Snapshot();

        Assert.True(VirtualNodeDiffer.NodesEqual(previous, next, previousSnapshot, nextSnapshot));

        var exception = Assert.Throws<InvalidOperationException>(
            () => VirtualNodeDiffer.NodesEqual(previous, next, nextSnapshot, nextSnapshot));
        Assert.Contains("does not match snapshot buffer id", exception.Message);
    }

    [Fact]
    public void DrawCommandRecorder_throws_on_mismatched_text_snapshot_before_recording_text()
    {
        var arena = new VirtualTextArena();
        var previousContent = arena.AddText("stale".AsSpan());

        arena.BeginFrame();
        _ = arena.AddText("current".AsSpan());
        var currentSnapshot = arena.Snapshot();

        var recorder = new DrawCommandRecorder();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Text,
                new PixelRectangle(0, 0, 100, 32),
                Text: previousContent)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => recorder.Record(elements, currentSnapshot));
        Assert.Contains("does not match snapshot buffer id", exception.Message);
    }
}
