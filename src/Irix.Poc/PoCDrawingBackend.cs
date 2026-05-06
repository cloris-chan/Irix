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

    public void BeginFrame(in FrameContext frameContext)
    {
        _pendingElements = [];
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, ITextResolver textResolver)
    {
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
                    var text = ResolveText(textResolver, command.Text);
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
    }

    public void Dispose()
    {
    }

    private static string ResolveText(ITextResolver textResolver, TextSlice text)
    {
        var span = textResolver.Resolve(text);
        return span.IsEmpty ? string.Empty : span.ToString();
    }

    private static PixelRectangle ToPixelRectangle(DrawRect rect) =>
        new((int)MathF.Round(rect.X), (int)MathF.Round(rect.Y), (int)MathF.Round(rect.Width), (int)MathF.Round(rect.Height));

    private static WindowColor ToWindowColor(DrawColor color) => new(color.A, color.R, color.G, color.B);
}
