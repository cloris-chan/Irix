using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator : IPatchBatchTranslator
{
    private readonly INativeWindow _window;
    private readonly Action? _prepareFrame;
    private readonly Func<PixelRectangle>? _viewportProvider;
    private readonly Irix.Rendering.RenderPipeline _renderPipeline = new(
        LayoutStyle.Default,
        new DrawingStyle(
        TextColor: DrawColor.Opaque(32, 32, 32),
        RectangleFillColor: DrawColor.Opaque(72, 72, 72),
        ButtonFillColor: DrawColor.Opaque(52, 120, 246),
        ButtonTextColor: DrawColor.Opaque(255, 255, 255),
        TextStyle: TextStyle.Default,
        ButtonTextStyle: TextStyle.Default));

    private VirtualNodeTree _lastTree;

    public WindowDrawCommandTranslator(INativeWindow window)
        : this(window, prepareFrame: null, viewportProvider: null)
    {
    }

    public WindowDrawCommandTranslator(
        INativeWindow window,
        Action? prepareFrame,
        Func<PixelRectangle>? viewportProvider)
    {
        _window = window;
        _prepareFrame = prepareFrame;
        _viewportProvider = viewportProvider;
    }

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
        if (patchBatch.Count > 0)
        {
            _lastTree = new VirtualNodeTree(patchBatch.Root);
        }

        _prepareFrame?.Invoke();
        var viewport = _viewportProvider?.Invoke() ?? _window.Region.PhysicalBounds;
        return _renderPipeline.Build(_lastTree.Root, viewport);
    }
}
