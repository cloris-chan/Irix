using Irix;
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class BackendClipTextSmokeDiagnostics
{
    internal static void RunPipelineScissorSmokeDiagnostic(DrawingBackendCompositor compositor, D3D12DrawingBackend backend, D3D12Renderer renderer)
    {        var arena = new VirtualTextArena();        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1000,
            attributes: [new VirtualNodeAttribute(VirtualAttributeKey.Height, AttributeValue.FromNumber(40))],
            children: [VirtualNodeFactory.Rectangle(160, 80, new NodeKey(1001))]);
        var viewport = new PixelRectangle(0, 0, renderer.Width, renderer.Height);
        using var batch = pipeline.Build(root, viewport);

        backend.SetClipMode(DrawingBackendClipMode.Scissor);
        compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
    }

    internal static void RunPipelineTextClipSmokeDiagnostic(DrawingBackendCompositor compositor, D3D12DrawingBackend backend, D3D12Renderer renderer)
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1100,
            attributes: [new VirtualNodeAttribute(VirtualAttributeKey.Height, AttributeValue.FromNumber(20))],
            children:
            [
                VirtualNodeBuilder.Button(arena, "PipelineClip", new NodeKey(1101), VirtualNodeAttribute.Action(new ActionId(100)))
            ]);
        var viewport = new PixelRectangle(0, 0, renderer.Width, renderer.Height);
        using var batch = pipeline.Build(root, viewport);

        backend.SetClipMode(DrawingBackendClipMode.Scissor);
        compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
    }

    internal static void RunClipScissorSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 16, 160, 80),
                ClipBounds: new DrawRect(32, 32, 80, 40),
                Color: DrawColor.Opaque(72, 136, 255))
        };

        backend.BeginFrame(default);
        backend.Execute(commands, FrameDrawingResources.Empty);
        backend.EndFrame();
    }

    internal static void RunEmptyScissorSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var commands = new[]
        {
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 16, 160, 80),
                ClipBounds: new DrawRect(2048, 2048, 80, 40),
                Color: DrawColor.Opaque(72, 136, 255))
        };

        backend.BeginFrame(default);
        backend.Execute(commands, FrameDrawingResources.Empty);
        backend.EndFrame();
    }

    internal static void RunTextClipSmokeDiagnostic(D3D12DrawingBackend backend)
    {
        var resources = FrameDrawingResources.Rent();
        try
        {
            var textStyle = resources.AddTextStyle(TextStyle.Default);
            var skippedText = resources.AddText("Skipped text clip smoke");
            var clippedText = resources.AddText("Clipped text clip smoke");
            resources.Seal();

            var commands = new[]
            {
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 160, 80),
                    Resource: textStyle,
                    Text: skippedText,
                    ClipBounds: new DrawRect(2048, 2048, 80, 40),
                    Color: DrawColor.Opaque(255, 255, 255)),
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 160, 80),
                    Resource: textStyle,
                    Text: clippedText,
                    ClipBounds: new DrawRect(32, 32, 80, 40),
                    Color: DrawColor.Opaque(255, 255, 255))
            };

            backend.BeginFrame(default);
            backend.Execute(commands, resources);
            backend.EndFrame();
        }
        finally
        {
            FrameDrawingResources.Return(resources);
        }
    }
}
