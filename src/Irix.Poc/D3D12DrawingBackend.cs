using Irix.Drawing;
using Irix.Platform.Windows;

namespace Irix.Poc;

/// <summary>
/// D3D12 backend: renders FillRect commands as colored rectangles via D3D12Renderer2D.
/// Falls back to clear color for the background.
/// </summary>
internal sealed class D3D12DrawingBackend : IDrawingBackend
{
    private readonly D3D12Renderer _renderer;
    private readonly D3D12Renderer2D _renderer2D;
    private float _bgR, _bgG, _bgB, _bgA = 1.0f;
    private List<D3D12Renderer2D.RectData>? _rects;
    private D3D12Renderer2D.RectData[]? _rectArray;

    public D3D12DrawingBackend(D3D12Renderer renderer, D3D12Renderer2D renderer2D)
    {
        _renderer = renderer;
        _renderer2D = renderer2D;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
        _renderer.BeginFrame();
        _rects = [];
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, ReadOnlySpan<TextRunEntry> textRuns)
    {
        if (_rects == null) return;

        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    // First FillRect with ZIndex 0 is the background
                    if (_rects.Count == 0)
                    {
                        _bgR = command.Color.R / 255f;
                        _bgG = command.Color.G / 255f;
                        _bgB = command.Color.B / 255f;
                        _bgA = command.Color.A / 255f;
                    }
                    _rects.Add(new D3D12Renderer2D.RectData(
                        command.Rect.X, command.Rect.Y,
                        command.Rect.Width, command.Rect.Height,
                        command.Color.R / 255f, command.Color.G / 255f,
                        command.Color.B / 255f, command.Color.A / 255f));
                    break;
            }
        }
    }

    public void EndFrame()
    {
        var rects = _rects ?? [];
        _rects = null;
        _rectArray = rects.Count > 0 ? rects.ToArray() : null;

        // Render rectangles via D3D12Renderer2D
        if (_rectArray != null)
        {
            _renderer.RenderRectangles(_rectArray);
        }
        else
        {
            // Fallback: just clear with background color
            _renderer.ClearAndPresent(_bgR, _bgG, _bgB, _bgA);
        }
    }

    public void Dispose()
    {
        _renderer2D.Dispose();
        _renderer.Dispose();
    }
}
