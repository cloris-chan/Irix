using Irix.Drawing;
using Xunit;

namespace Irix.Core.Tests;

public sealed class FrameTextArenaTests
{
    [Fact]
    public void Resolve_returns_frame_local_text_slices()
    {
        var arena = new FrameTextArena();
        var hello = arena.Add("Hello");
        var world = arena.Add("World");

        arena.Seal();

        Assert.Equal("Hello", arena.Resolve(hello).ToString());
        Assert.Equal("World", arena.Resolve(world).ToString());
    }

    [Fact]
    public void Resolve_returns_empty_for_invalid_slices()
    {
        var arena = new FrameTextArena();
        var text = arena.Add("Hello");

        arena.Seal();

        Assert.True(arena.Resolve(default).IsEmpty);
        Assert.True(arena.Resolve(new TextSlice(text.BufferId + 1, text.Start, text.Length)).IsEmpty);
        Assert.True(arena.Resolve(new TextSlice(text.BufferId, text.Start, text.Length + 1)).IsEmpty);
    }

    [Fact]
    public void Add_throws_after_seal()
    {
        var arena = new FrameTextArena();
        arena.Add("Hello");
        arena.Seal();

        Assert.Throws<InvalidOperationException>(() => arena.Add("World"));
    }
}
