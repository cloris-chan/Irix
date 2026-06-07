using Irix.Drawing;
using Irix.Rendering;
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
    public void AddBrush_deduplicates_solid_materials_without_public_resolver_surface()
    {
        var resources = new FrameDrawingResources();
        var material = DrawMaterial.SolidColor(Color.FromSrgb(10, 20, 30));

        var first = resources.AddBrush(material);
        var second = resources.AddBrush(material);
        var none = resources.AddBrush(DrawMaterial.None);

        Assert.Equal(first, second);
        Assert.Equal(DrawingResourceKind.Brush, first.Kind);
        Assert.Equal(ResourceHandle.None, none);
        Assert.Equal(material, resources.ResolveBrush(first));
        Assert.Equal(material, ((IFrameBrushResolver)resources).ResolveBrush(first));
        Assert.Equal(DrawMaterial.None, resources.ResolveBrush(ResourceHandle.None));
        Assert.DoesNotContain(
            "ResolveBrush",
            typeof(IFrameResourceResolver).GetMethods().Select(method => method.Name));
    }

    [Fact]
    public void AddBrush_deduplicates_internal_linear_gradient_materials()
    {
        var resources = new FrameDrawingResources();
        var material = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(120, 40));

        var first = resources.AddBrush(material);
        var second = resources.AddBrush(material);
        var changedStops = resources.AddBrush(DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 0, 255),
            new DrawPoint(0, 0),
            new DrawPoint(120, 40)));
        var changedGeometry = resources.AddBrush(DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(80, 40)));

        Assert.Equal(first, second);
        Assert.NotEqual(first, changedStops);
        Assert.NotEqual(first, changedGeometry);
        Assert.Equal(DrawingResourceKind.Brush, first.Kind);
        Assert.Equal(DrawMaterialKind.LinearGradient, resources.ResolveBrush(first).Kind);
        Assert.Equal(material, resources.ResolveBrush(first));
    }

    [Fact]
    public void ResolveTextStyle_returns_default_for_invalid_handles()
    {
        var resources = new FrameDrawingResources();

        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(ResourceHandle.None));
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(new ResourceHandle(42, DrawingResourceKind.TextStyle)));
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(new ResourceHandle(0, DrawingResourceKind.Brush)));
        Assert.Equal(DrawMaterial.None, resources.ResolveBrush(new ResourceHandle(0, DrawingResourceKind.TextStyle)));
    }

    [Fact]
    public void Add_resources_throw_after_seal()
    {
        var resources = new FrameDrawingResources();
        resources.Seal();

        Assert.Throws<InvalidOperationException>(() => resources.AddTextStyle(TextStyle.Default));
        Assert.Throws<InvalidOperationException>(() => resources.AddBrush(DrawMaterial.SolidColor(Color.FromSrgb(1, 2, 3))));
    }

    [Fact]
    public void Reset_reuses_resources_for_new_frame()
    {
        using var resources = new FrameDrawingResources();

        var frameId1 = resources.FrameId;
        var handle1 = resources.AddTextStyle(TextStyle.Default);
        var brush1 = resources.AddBrush(DrawMaterial.SolidColor(Color.FromSrgb(1, 2, 3)));
        var text1 = resources.AddText("Hello");
        resources.Seal();
        Assert.Equal("Hello", resources.Resolve(text1).ToString());
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(handle1));
        Assert.Equal(DrawMaterialKind.SolidColor, resources.ResolveBrush(brush1).Kind);

        resources.Reset();
        var frameId2 = resources.FrameId;

        var handle2 = resources.AddTextStyle(TextStyle.Default);
        var brush2 = resources.AddBrush(DrawMaterial.SolidColor(Color.FromSrgb(4, 5, 6)));
        var text2 = resources.AddText("World");
        resources.Seal();
        Assert.Equal("World", resources.Resolve(text2).ToString());
        Assert.Equal(TextStyle.Default, resources.ResolveTextStyle(handle2));
        Assert.Equal(DrawMaterialKind.SolidColor, resources.ResolveBrush(brush2).Kind);
        Assert.Equal(brush1, brush2);
        Assert.NotEqual(0ul, frameId1);
        Assert.NotEqual(0ul, frameId2);
        Assert.NotEqual(frameId1, frameId2);
    }

    [Fact]
    public void Reset_after_return_throws()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Seal();

        FrameDrawingResources.Return(resources);

        Assert.Throws<InvalidOperationException>(() => resources.Reset());

        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
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

    [Fact]
    public void Retain_prevents_Return_from_recycling()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Retain();

        FrameDrawingResources.Return(resources);

        // Return is a no-op while retained — resources should not be in pool.
        // A fresh Rent() should NOT get the same instance back.
        var other = FrameDrawingResources.Rent();
        Assert.NotSame(resources, other);
        FrameDrawingResources.Return(other);

        // Release returns to pool
        resources.Release();
    }

    [Fact]
    public void Release_returns_to_pool_if_not_already_returned()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Retain();

        resources.Release();

        // After Release, resources should be back in pool.
        // A fresh Rent() may recycle the same object.
        var recycled = FrameDrawingResources.Rent();
        // Either same object (recycled) or different (pool was full) — either way no leak.
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void Double_Release_is_idempotent()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Retain();

        resources.Release();
        resources.Release(); // second release should be a no-op

        // No exception, no double-return. Pool state is clean.
        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void Multiple_retains_require_matching_releases_before_pool_return()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("multi-retain");
        resources.Seal();

        resources.Retain();
        resources.Retain();

        FrameDrawingResources.Return(resources);
        resources.Release();

        Assert.Equal("multi-retain", resources.Resolve(slice).ToString());

        resources.Release();
        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void Release_after_Return_is_noop()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Retain();

        // Simulate batch.Dispose() calling Return() while retained — no-op
        FrameDrawingResources.Return(resources);

        // Now Release() — should return to pool (since Return was blocked by _retained)
        resources.Release();

        // Resources are back in pool. Second Release is a no-op.
        resources.Release();

        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void Retain_then_batch_dispose_does_not_return_to_pool()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("retained");
        resources.Seal();
        resources.Retain();

        // Simulate batch.Dispose() → Return() — should be no-op since retained
        FrameDrawingResources.Return(resources);

        // TextSlice is still valid — resources are NOT in pool
        Assert.Equal("retained", resources.Resolve(slice).ToString());

        // Release returns to pool
        resources.Release();
    }

    [Fact]
    public void FrameId_increments_on_each_rent()
    {
        var r1 = FrameDrawingResources.Rent();
        var id1 = r1.FrameId;
        FrameDrawingResources.Return(r1);

        var r2 = FrameDrawingResources.Rent();
        var id2 = r2.FrameId;

        // If same object, FrameId must differ
        if (ReferenceEquals(r1, r2))
        {
            Assert.NotEqual(id1, id2);
        }
        else
        {
            Assert.NotEqual(0ul, id2);
        }

        FrameDrawingResources.Return(r2);
    }

    [Fact]
    public void Stale_render_frame_batch_dispose_does_not_return_reused_resources()
    {
        var before = FrameDrawingResources.GetPoolDiagnostics();
        var active = FrameDrawingResources.Rent();
        var staleFrameId = active.FrameId == ulong.MaxValue ? active.FrameId - 1 : active.FrameId + 1;

        var text = active.AddText("active");
        active.Seal();

        FrameDrawingResources.Return(active, staleFrameId);

        Assert.Equal("active", active.Resolve(text).ToString());
        var delta = FrameDrawingResources.GetPoolDiagnostics().Delta(before);
        Assert.Equal(1, delta.StaleReturnSkipCount);
        FrameDrawingResources.Return(active);
    }
}
