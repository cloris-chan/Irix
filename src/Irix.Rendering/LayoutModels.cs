using Irix.Platform;

namespace Irix.Rendering;

internal enum LayoutElementKind : byte
{
    Text,
    Rectangle,
    Button
}

internal readonly record struct LayoutElement(
    LayoutElementKind Kind,
    PixelRectangle Bounds,
    string? Text = null,
    string? Action = null);
