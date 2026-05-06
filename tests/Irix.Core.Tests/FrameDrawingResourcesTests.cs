using Irix.Drawing;
using Xunit;

namespace Irix.Core.Tests;

public sealed class FrameDrawingResourcesTests
{
    [Fact]
    public void AddTextStyle_deduplicates_equal_styles()
    {
        var resources = new FrameDrawingResources();

        var first = resources.AddTextStyle(TextStyle.Default);
        var second = resources.AddTextStyle(TextStyle.Default);

        Assert.Equal(first, second);
        Assert.Equal(DrawingResourceKind.TextStyle, first.Kind);
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(first));
    }

    [Fact]
    public void ResolveTextStyle_returns_default_for_invalid_handles()
    {
        var resources = new FrameDrawingResources();

        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(ResourceHandle.None));
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(new ResourceHandle(42, DrawingResourceKind.TextStyle)));
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(new ResourceHandle(0, DrawingResourceKind.Brush)));
    }

    [Fact]
    public void AddTextStyle_throws_after_seal()
    {
        var resources = new FrameDrawingResources();
        resources.Seal();

        Assert.Throws<InvalidOperationException>(() => resources.AddTextStyle(TextStyle.Default));
    }
}
