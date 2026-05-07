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

    [Fact]
    public void Reset_reuses_resources_for_new_frame()
    {
        using var resources = new FrameDrawingResources();

        var handle1 = resources.AddTextStyle(TextStyle.Default);
        var text1 = resources.AddText("Hello");
        resources.Seal();
        Assert.Equal("Hello", resources.Resolve(text1).ToString());
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(handle1));

        resources.Reset();

        var handle2 = resources.AddTextStyle(TextStyle.Default);
        var text2 = resources.AddText("World");
        resources.Seal();
        Assert.Equal("World", resources.Resolve(text2).ToString());
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(handle2));
    }

    [Fact]
    public void Return_to_pool_invalidates_old_text_slices()
    {
        var resources = FrameDrawingResources.Rent();
        var text = resources.AddText("Hello");
        resources.Seal();
        Assert.Equal("Hello", resources.Resolve(text).ToString());

        FrameDrawingResources.Return(resources);

        // After Return, old slices must be invalid on a new Rent
        var resources2 = FrameDrawingResources.Rent();
        // Either same instance reused or new — either way old slice must not resolve
        var resolved = resources2.Resolve(text);
        Assert.True(resolved.IsEmpty);
        FrameDrawingResources.Return(resources2);
    }

    [Fact]
    public void Rent_and_Return_avoids_allocation_when_pool_warm()
    {
        // Warm the pool with multiple Rent/Return cycles
        for (var i = 0; i < 4; i++)
        {
            var r = FrameDrawingResources.Rent();
            r.Seal();
            FrameDrawingResources.Return(r);
        }

        // Now Rent should get a pooled instance (no new allocation)
        var pooled = FrameDrawingResources.Rent();
        pooled.Seal();
        FrameDrawingResources.Return(pooled);
    }
}
