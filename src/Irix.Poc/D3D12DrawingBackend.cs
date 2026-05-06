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
    private float _bgR, _bgG, _bgB, _bgA = 1.0f;
    private List<D3D12Renderer2D.RectData>? _rects;
    private List<D3D12TextRenderer.TextData>? _texts;
    private D3D12Renderer2D.RectData[]? _rectArray;
    private D3D12TextRenderer.TextData[]? _textArray;
    private IFrameResourceResolver? _resources;

    public D3D12DrawingBackend(D3D12Renderer renderer)
    {
        _renderer = renderer;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
        _renderer.BeginFrame();
        _rects = [];
        _texts = [];
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
    {
        if (_rects == null || _texts == null) return;
        _resources = resources;

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
                case DrawCommandKind.DrawTextRun:
                    if (resources.Resolve(command.Text).IsEmpty)
                    {
                        break;
                    }

                    _texts.Add(new D3D12TextRenderer.TextData(
                        command.Rect.X,
                        command.Rect.Y,
                        command.Rect.Width,
                        command.Rect.Height,
                        command.Color.R / 255f,
                        command.Color.G / 255f,
                        command.Color.B / 255f,
                        command.Color.A / 255f,
                        command.Text,
                        command.Resource));
                    break;
            }
        }
    }

    public void EndFrame()
    {
        var rects = _rects ?? [];
        var texts = _texts ?? [];
        var resources = _resources ?? FrameDrawingResources.Empty;
        _rects = null;
        _texts = null;
        _resources = null;
        _rectArray = rects.Count > 0 ? rects.ToArray() : null;
        _textArray = texts.Count > 0 ? texts.ToArray() : null;

        if (_rectArray != null || _textArray != null)
        {
            _renderer.RenderFrame(
                _rectArray ?? [],
                _textArray ?? [],
                resources,
                _bgR,
                _bgG,
                _bgB,
                _bgA);
        }
        else
        {
            // Fallback: just clear with background color
            _renderer.ClearAndPresent(_bgR, _bgG, _bgB, _bgA);
        }
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
