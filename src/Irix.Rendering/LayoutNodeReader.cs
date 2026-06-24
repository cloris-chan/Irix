namespace Irix.Rendering;

internal static class LayoutNodeReader
{
    public static TextContentResource GetTextContent(VirtualNode node)
    {
        return node.Content.TryGetText(out var textContent) ? textContent : default;
    }

    public static TextContentResource GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            if (!IsTextContent(child))
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

    public static bool IsTextContent(VirtualNode node) =>
        node.Kind == VirtualNodeKind.Content && node.Content.Kind == ContentResourceKind.Text;

    public static bool IsRectangleContent(VirtualNode node) =>
        node.Kind == VirtualNodeKind.Content && node.Content.Kind == ContentResourceKind.Rectangle;

    public static bool IsInteractiveContainer(VirtualNode node) =>
        node.Kind == VirtualNodeKind.Container
        && !new PropertyReader(node.Properties).GetActionId(VirtualPropertyKey.ActionId).IsNone;

    public static bool TryGetFirstRectangleContent(VirtualNode node, out VirtualNode rectangle)
    {
        foreach (var child in node.Children)
        {
            if (IsRectangleContent(child))
            {
                rectangle = child;
                return true;
            }
        }

        rectangle = default;
        return false;
    }

    public static bool TryGetFirstTextContent(VirtualNode node, out VirtualNode text)
    {
        foreach (var child in node.Children)
        {
            if (IsTextContent(child))
            {
                text = child;
                return true;
            }
        }

        text = default;
        return false;
    }

    public static StyleColorSlot ReadColor(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetColor(key, out var color) ? StyleColorSlot.Some(color) : StyleColorSlot.None;

    public static PaintSlot ReadPaint(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetPaint(key, out var paint) ? PaintSlot.Some(paint) : PaintSlot.None;

    public static BorderStrokeSlot ReadBorderStroke(PropertyReader reader, VirtualPropertyKey key) =>
        reader.TryGetBorderStroke(key, out var border) ? BorderStrokeSlot.Some(border) : BorderStrokeSlot.None;
}
