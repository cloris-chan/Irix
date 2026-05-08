using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator(
    INativeWindow window,
    Action? prepareFrame,
    Func<PixelRectangle>? viewportProvider) : IPatchBatchTranslator
{
    private readonly INativeWindow _window = window;
    private readonly Action? _prepareFrame = prepareFrame;
    private readonly Func<PixelRectangle>? _viewportProvider = viewportProvider;
    private readonly Irix.Rendering.RenderPipeline _renderPipeline = new(
        LayoutStyle.Default,
        new DrawingStyle(
        TextColor: DrawColor.Opaque(32, 32, 32),
        RectangleFillColor: DrawColor.Opaque(72, 72, 72),
        ButtonFillColor: DrawColor.Opaque(52, 120, 246),
        ButtonTextColor: DrawColor.Opaque(255, 255, 255),
        TextStyle: TextStyle.Default,
        ButtonTextStyle: TextStyle.Default));

    private readonly RetainedTree _retainedTree = new(default);

    public WindowDrawCommandTranslator(INativeWindow window)
        : this(window, prepareFrame: null, viewportProvider: null)
    {
    }

    public RenderFrameBatch Translate(PatchBatch patchBatch)
    {
        IReadOnlyList<int>? dirty = null;
        if (patchBatch.Kind == PatchBatchKind.RenderRequest)
        {
            // Render request: reuse retained tree, no dirty nodes
        }
        else if (patchBatch.Count > 0)
        {
            // Diff batch: apply patches to retained tree, get dirty set
            dirty = _retainedTree.Apply(patchBatch);
        }
        else
        {
            // Empty diff with new root (e.g. initial frame): apply to update retained tree
            _retainedTree.Apply(patchBatch);
        }

        _prepareFrame?.Invoke();
        var viewport = _viewportProvider?.Invoke() ?? _window.Region.PhysicalBounds;
        return _renderPipeline.Build(_retainedTree.Tree.Root, viewport, dirty);
    }
}
