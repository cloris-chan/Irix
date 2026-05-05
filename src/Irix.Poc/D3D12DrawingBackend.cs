using Irix.Drawing;
using Irix.Platform.Windows;

namespace Irix.Poc;

/// <summary>
/// Phase 1 D3D12 backend: clears the screen to the first FillRect color.
/// Validates the full pipeline: RenderFrameBatch → IDrawingBackend → D3D12 → GPU Present.
/// Text and complex geometry rendering will be added in Phase 2.
/// </summary>
internal sealed class D3D12DrawingBackend : IDrawingBackend
{
    private readonly D3D12Renderer _renderer;
    private float _clearR, _clearG, _clearB, _clearA = 1.0f;

    public D3D12DrawingBackend(D3D12Renderer renderer)
    {
        _renderer = renderer;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
        _renderer.BeginFrame();
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, ReadOnlySpan<TextRunEntry> textRuns)
    {
        // Extract the first FillRect color as the clear color
        foreach (var command in commands)
        {
            if (command.Kind == DrawCommandKind.FillRect)
            {
                _clearR = command.Color.R / 255f;
                _clearG = command.Color.G / 255f;
                _clearB = command.Color.B / 255f;
                _clearA = command.Color.A / 255f;
                return;
            }
        }
    }

    public void EndFrame()
    {
        _renderer.ClearAndPresent(_clearR, _clearG, _clearB, _clearA);
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
