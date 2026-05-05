using Irix.Drawing;
using Irix.Platform;

namespace Irix.Poc;

/// <summary>
/// PoC implementation of <see cref="IDrawingBackend"/> that translates DrawCommands
/// to WindowContentElements for the native window. Does not perform button detection —
/// that is a higher-level concern handled by the compositor/input layer.
/// </summary>
internal sealed class PoCDrawingBackend(INativeWindow window) : IDrawingBackend
{
    private List<WindowContentElement>? _pendingElements;
    private List<TextRunEntry>? _textRuns;

    public void BeginFrame(in FrameContext frameContext)
    {
        _pendingElements = [];
        _textRuns = [];
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, ReadOnlySpan<TextRunEntry> textRuns)
    {
        // Capture text runs for lookup
        _textRuns ??= [];
        _textRuns.Clear();
        foreach (var entry in textRuns)
        {
            _textRuns.Add(entry);
        }

        _pendingElements ??= [];
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    _pendingElements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        ToPixelRectangle(command.Rect),
                        BackgroundColor: ToWindowColor(command.Color)));
                    break;
                case DrawCommandKind.DrawTextRun:
                    var text = LookUpText(_textRuns, command.Resource);
                    _pendingElements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        text,
                        ForegroundColor: ToWindowColor(command.Color)));
                    break;
            }
        }
    }

    public void EndFrame()
    {
        window.SetContentElements(_pendingElements ?? []);
        _pendingElements = null;
        _textRuns = null;
    }

    public void Dispose()
    {
    }

    private static string? LookUpText(List<TextRunEntry> textRuns, ResourceHandle resource)
    {
        if (resource.Kind != DrawingResourceKind.TextStyle || resource.Id < 0)
        {
            return null;
        }

        foreach (var entry in textRuns)
        {
            if (entry.Id == resource.Id)
            {
                return entry.Text;
            }
        }

        return null;
    }

    private static PixelRectangle ToPixelRectangle(DrawRect rect) =>
        new((int)MathF.Round(rect.X), (int)MathF.Round(rect.Y), (int)MathF.Round(rect.Width), (int)MathF.Round(rect.Height));

    private static WindowColor ToWindowColor(DrawColor color) => new(color.A, color.R, color.G, color.B);
}
