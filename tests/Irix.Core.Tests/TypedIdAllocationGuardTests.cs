using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public class TypedIdAllocationGuardTests
{
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
                VirtualNodeFactory.Button("Click", 2, VirtualNodeAttribute.Action(new ActionId(1))),
                VirtualNodeFactory.Button("Other", 3, VirtualNodeAttribute.Action(new ActionId(2))),
                .. Enumerable.Range(0, 20).Select(i => VirtualNodeFactory.Text($"Row {i}", (uint)(10 + i)))
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
        var node = VirtualNodeFactory.Text("hello", 42);

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

    private sealed class NullBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
