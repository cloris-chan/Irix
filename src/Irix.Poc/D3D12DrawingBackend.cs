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
    private readonly FrameRenderList<D3D12Renderer2D.RectData> _rects = new();
    private readonly FrameRenderList<D3D12TextRenderer.TextData> _texts = new();
    private IFrameResourceResolver? _resources;

    public D3D12DrawingBackend(D3D12Renderer renderer)
    {
        _renderer = renderer;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
        _renderer.BeginFrame();
        _rects.Reset();
        _texts.Reset();
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
    {
        _resources = resources;

        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
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
        var rects = _rects.Span;
        var texts = _texts.Span;
        var resources = _resources ?? FrameDrawingResources.Empty;
        _resources = null;

        if (rects.Length > 0 || texts.Length > 0)
        {
            _renderer.RenderFrame(rects, texts, resources, _bgR, _bgG, _bgB, _bgA);
        }
        else
        {
            _renderer.ClearAndPresent(_bgR, _bgG, _bgB, _bgA);
        }
    }

    public void Dispose()
    {
        _rects.Dispose();
        _texts.Dispose();
        _renderer.Dispose();
    }
}
