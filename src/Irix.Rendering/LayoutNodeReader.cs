namespace Irix.Rendering;

internal static class LayoutNodeReader
{
    public static TextNodeContent GetTextContent(VirtualNode node)
    {
        return node.Content.TryGetText(out var textContent) ? textContent : default;
    }

    public static TextNodeContent GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind != VirtualNodeKind.Text)
            {
                continue;
            }

            var content = GetTextContent(child);
            if (!content.IsNone)
            {
                return content;
            }
        }

        return default;
    }

    public static StyleColorSlot ReadColor(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetColor(key, out var color) ? StyleColorSlot.Some(color) : StyleColorSlot.None;

    public static PaintSlot ReadPaint(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetPaint(key, out var paint) ? PaintSlot.Some(paint) : PaintSlot.None;

    public static BorderStrokeSlot ReadBorderStroke(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetBorderStroke(key, out var border) ? BorderStrokeSlot.Some(border) : BorderStrokeSlot.None;
}
