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

    [Fact]
    public void Reset_reuses_arena_for_new_frame()
    {
        using var arena = new FrameTextArena();

        var hello = arena.Add("Hello");
        arena.Seal();
        Assert.Equal("Hello", arena.Resolve(hello).ToString());

        arena.Reset();

        var world = arena.Add("World");
        arena.Seal();
        Assert.Equal("World", arena.Resolve(world).ToString());
        Assert.True(arena.Resolve(hello).IsEmpty);
    }

    [Fact]
    public void Dispose_invalidates_all_slices()
    {
        var arena = new FrameTextArena();
        var text = arena.Add("Hello");
        arena.Seal();
        Assert.Equal("Hello", arena.Resolve(text).ToString());

        arena.Dispose();

        Assert.True(arena.Resolve(text).IsEmpty);
    }

    [Fact]
    public void Add_empty_text_resolves_empty()
    {
        using var arena = new FrameTextArena();
        var slice = arena.Add("");
        arena.Seal();

        Assert.True(arena.Resolve(slice).IsEmpty);
    }

    [Fact]
    public void Add_multilingual_text_roundtrips()
    {
        using var arena = new FrameTextArena();
        var chinese = arena.Add("你好世界");
        var emoji = arena.Add("🎯🔥");
        var mixed = arena.Add("Hello你好🎯");
        arena.Seal();

        Assert.Equal("你好世界", arena.Resolve(chinese).ToString());
        Assert.Equal("🎯🔥", arena.Resolve(emoji).ToString());
        Assert.Equal("Hello你好🎯", arena.Resolve(mixed).ToString());
    }

    [Fact]
    public void Add_long_text_roundtrips()
    {
        using var arena = new FrameTextArena();
        var longText = new string('A', 10_000);
        var slice = arena.Add(longText);
        arena.Seal();

        Assert.Equal(longText, arena.Resolve(slice).ToString());
    }
}
