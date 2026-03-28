using Irix.Platform;

namespace Irix.Poc;

internal enum WindowLayoutElementKind : byte
{
    Text,
    Rectangle,
    Button
}

internal readonly record struct WindowLayoutElement(
    WindowLayoutElementKind Kind,
    PixelRectangle Bounds,
    string? Text = null,
    string? Action = null);
