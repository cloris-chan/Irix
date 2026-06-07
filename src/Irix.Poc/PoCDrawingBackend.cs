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
    private readonly FrameTextArena _pendingTextArena = new();

    public void BeginFrame(in FrameContext frameContext)
    {
        _pendingElements = [];
        _pendingTextArena.Reset();
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
    {
        _pendingElements ??= [];
        var outputMapping = ColorOutputMapping.SdrSrgb;
        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    _pendingElements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        ToPixelRectangle(command.Rect),
                        BackgroundColor: ToWindowColor(outputMapping.MapToSdr(command))));
                    break;
                case DrawCommandKind.DrawTextRun:
                    _pendingElements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        CopyText(resources, command.Text),
                        ForegroundColor: ToWindowColor(outputMapping.MapToSdr(command))));
                    break;
            }
        }
    }

    public void EndFrame()
    {
        _pendingTextArena.Seal();
        window.SetContentElements(_pendingElements ?? [], _pendingTextArena);
        _pendingElements = null;
    }

    public void Dispose()
    {
        _pendingTextArena.Dispose();
    }

    private TextSlice CopyText(IFrameResourceResolver resources, TextSlice text)
    {
        var span = resources.Resolve(text);
        return span.IsEmpty ? default : _pendingTextArena.Add(span);
    }

    private static PixelRectangle ToPixelRectangle(DrawRect rect) =>
        new((int)MathF.Round(rect.X), (int)MathF.Round(rect.Y), (int)MathF.Round(rect.Width), (int)MathF.Round(rect.Height));

    private static WindowColor ToWindowColor(DrawColor color) => new(color.A, color.R, color.G, color.B);
}
