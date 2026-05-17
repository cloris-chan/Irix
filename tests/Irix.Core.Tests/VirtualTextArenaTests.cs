using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class VirtualTextArenaTests
{
    [Fact]
    public void GetOrCreateSnapshot_caches_until_text_changes()
    {
        var arena = new VirtualTextArena();
        arena.AddText("cached".AsSpan());

        var first = arena.GetOrCreateSnapshot();
        var second = arena.GetOrCreateSnapshot();

        Assert.Equal(first, second);
        Assert.Same(first.Buffer, second.Buffer);

        arena.AddText(" updated".AsSpan());
        var third = arena.GetOrCreateSnapshot();

        Assert.NotSame(first.Buffer, third.Buffer);
    }

    [Fact]
    public void ResolveRequired_throws_when_content_buffer_does_not_match_snapshot()
    {
        var arena = new VirtualTextArena();
        _ = arena.AddText("previous".AsSpan());
        var previousSnapshot = arena.GetOrCreateSnapshot();

        arena.BeginFrame();
        var nextContent = arena.AddText("next".AsSpan());

        var exception = Assert.Throws<InvalidOperationException>(
            () => previousSnapshot.ResolveRequired(nextContent).ToString());
        Assert.Contains("does not match snapshot buffer id", exception.Message);
    }

    [Fact]
    public void NodesEqual_returns_false_on_mismatched_text_snapshot_without_throwing()
    {
        var arena = new VirtualTextArena();
        var previous = VirtualNodeBuilder.Text(arena, "same", new NodeKey(1));
        var previousSnapshot = arena.GetOrCreateSnapshot();

        arena.BeginFrame();
        var next = VirtualNodeBuilder.Text(arena, "same", new NodeKey(1));
        var nextSnapshot = arena.GetOrCreateSnapshot();

        Assert.True(VirtualNodeStructuralComparer.Equals(previous, next, previousSnapshot, nextSnapshot));
        Assert.False(VirtualNodeStructuralComparer.Equals(previous, next, nextSnapshot, nextSnapshot));
        Assert.False(VirtualNodeDiffer.ContentEqual(previous.Content, next.Content, nextSnapshot, nextSnapshot));
    }

    [Fact]
    public void ContentEqual_returns_true_for_two_valid_empty_text_spans()
    {
        var snapshot = new TextBufferSnapshot(new TextBufferId(1), []);
        var emptyText = new TextNodeContent(new TextBufferId(1), new TextRange(0, 0));

        Assert.True(VirtualNodeDiffer.ContentEqual(
            NodeContent.FromText(emptyText),
            NodeContent.FromText(emptyText),
            snapshot,
            snapshot));
    }

    [Fact]
    public void DrawCommandRecorder_throws_on_mismatched_text_snapshot_before_recording_text()
    {
        var arena = new VirtualTextArena();
        var previousContent = arena.AddText("stale".AsSpan());

        arena.BeginFrame();
        _ = arena.AddText("current".AsSpan());
        var currentSnapshot = arena.GetOrCreateSnapshot();

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
