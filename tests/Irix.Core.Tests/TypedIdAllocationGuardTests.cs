using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public class TypedIdAllocationGuardTests
{
    private readonly VirtualTextArena _arena = new();

    [Fact]
    public void Render_pipeline_hot_path_does_not_allocate_string_collections()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2), VirtualNodeAttribute.Action(new ActionId(1))),
                VirtualNodeBuilder.Button(_arena, "Other", new NodeKey(3), VirtualNodeAttribute.Action(new ActionId(2))),
                .. Enumerable.Range(0, 20).Select(i => VirtualNodeBuilder.Text(_arena, $"Row {i}", new NodeKey((uint)(10 + i))))
            ]);

        pipeline.Build(root, viewport);

        var snapshot1 = pipeline.Build(root, viewport);

        Assert.NotEmpty(snapshot1.HitTargets);
        Assert.Equal(new ActionId(1), snapshot1.HitTargets[0].ActionId);
        Assert.Equal(new ActionId(2), snapshot1.HitTargets[1].ActionId);
    }

    [Fact]
    public void HitTestTarget_uses_typed_ActionId()
    {
        var target = new HitTestTarget(new PixelRectangle(0, 0, 100, 40), new ActionId(42));

        Assert.Equal(new ActionId(42), target.ActionId);
        Assert.False(target.ActionId.IsNone);
    }

    [Fact]
    public void LayoutElement_uses_typed_ActionId()
    {
        var element = new LayoutElement(LayoutElementKind.Button, new PixelRectangle(0, 0, 100, 40), ActionId: new ActionId(7));

        Assert.Equal(new ActionId(7), element.ActionId);
        Assert.False(element.ActionId.IsNone);
    }

    [Fact]
    public void ActionId_None_is_default_value()
    {
        var defaultId = default(ActionId);

        Assert.Equal(ActionId.None, defaultId);
        Assert.True(defaultId.IsNone);
        Assert.Equal(0u, defaultId.Value);
    }

    [Fact]
    public void VirtualNodeAttribute_uses_typed_key()
    {
        var attr = new VirtualNodeAttribute(VirtualAttributeKey.ActionId, AttributeValue.FromActionId(new ActionId(5)));

        Assert.Equal(VirtualAttributeKey.ActionId, attr.Key);
        Assert.Equal(AttributeValueKind.ActionId, attr.Value.Kind);
        Assert.Equal(new ActionId(5), attr.Value.ActionIdValue);
    }

    [Fact]
    public void VirtualNode_Key_is_NodeKey()
    {
        var node = VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(42));

        Assert.Equal(new NodeKey(42), node.Key);
    }

    [Fact]
    public void DrawingBackendCompositor_TryGetActionIdAt_returns_typed_ActionId()
    {
        var backend = new NullBackend();
        var compositor = new DrawingBackendCompositor(backend);

        var result = compositor.TryGetActionIdAt(0, 0, out var actionId);

        Assert.False(result);
        Assert.Equal(ActionId.None, actionId);
    }

    // ── R13-24: No managed refs in core IR types ──────────────────

    [Fact]
    public void ActionId_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ActionId>());
        Assert.True(Unsafe.SizeOf<ActionId>() == 4);
    }

    [Fact]
    public void NodeKey_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<NodeKey>());
        Assert.True(Unsafe.SizeOf<NodeKey>() == 4);
    }

    [Fact]
    public void TextNodeContent_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextNodeContent>());
    }

    [Fact]
    public void TextBufferId_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextBufferId>());
        Assert.True(Unsafe.SizeOf<TextBufferId>() == 4);
    }

    [Fact]
    public void TextRange_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextRange>());
    }

    [Fact]
    public void NodeContent_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<NodeContent>());
        Assert.Equal(24, Unsafe.SizeOf<NodeContent>());
    }

    [Fact]
    public void AttributeValue_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AttributeValue>());
        Assert.Equal(16, Unsafe.SizeOf<AttributeValue>());
    }

    [Fact]
    public void VirtualAttributeKey_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<VirtualAttributeKey>());
    }

    [Fact]
    public void AttributeChangeSet_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AttributeChangeSet>());
    }

    [Fact]
    public void ActionId_value_type_semantics()
    {
        var a = new ActionId(42);
        var b = new ActionId(42);
        var c = new ActionId(99);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void NodeContent_Number_roundtrip()
    {
        var nc = NodeContent.FromNumber(3.14);
        Assert.Equal(NodeContentKind.Number, nc.Kind);
        Assert.True(nc.TryGetNumber(out var value));
        Assert.Equal(3.14, value);
    }

    [Fact]
    public void NodeContent_Boolean_roundtrip()
    {
        var nc = NodeContent.FromBoolean(true);
        Assert.Equal(NodeContentKind.Boolean, nc.Kind);
        Assert.True(nc.TryGetBoolean(out var value));
        Assert.True(value);
    }

    [Fact]
    public void NodeContent_Text_roundtrip()
    {
        var arena = new VirtualTextArena();
        var textContent = arena.AddText("hello".AsSpan());
        var nc = NodeContent.FromText(textContent);
        Assert.Equal(NodeContentKind.Text, nc.Kind);
        Assert.True(nc.TryGetText(out var resolved));
        Assert.Equal(textContent, resolved);
    }

    [Fact]
    public void AttributeValue_Number_roundtrip()
    {
        var av = AttributeValue.FromNumber(42.5);
        Assert.Equal(AttributeValueKind.Number, av.Kind);
        Assert.True(av.TryGetNumber(out var value));
        Assert.Equal(42.5, value);
    }

    [Fact]
    public void AttributeValue_Boolean_roundtrip()
    {
        var av = AttributeValue.FromBoolean(true);
        Assert.Equal(AttributeValueKind.Boolean, av.Kind);
        Assert.True(av.TryGetBoolean(out var value));
        Assert.True(value);
    }

    [Fact]
    public void AttributeValue_ActionId_roundtrip()
    {
        var av = AttributeValue.FromActionId(new ActionId(7));
        Assert.Equal(AttributeValueKind.ActionId, av.Kind);
        Assert.True(av.TryGetActionId(out var value));
        Assert.Equal(new ActionId(7), value);
    }

    // ── R13-25: Source-level guards — no string API in core/rendering ──

    [Fact]
    public void Irix_Core_has_no_FromText_string_factory()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("FromText(string", content);
            Assert.DoesNotContain("string? Text", content);
        }
    }

    [Fact]
    public void Irix_Rendering_has_no_string_ActionId()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Rendering"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("string ActionId", content);
            Assert.DoesNotContain("string? ActionId", content);
            Assert.DoesNotContain("out string actionId", content);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Irix.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repo root (Irix.slnx)");
    }

    private sealed class NullBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
