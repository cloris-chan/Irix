namespace Irix.Rendering;

internal static class LayoutNodeReader
{
    public static TextContentResource GetTextContent(VirtualNodeReader node)
    {
        return node.Content.TryGetText(out var textContent) ? textContent : default;
    }

    public static TextContentResource GetButtonLabel(VirtualNodeReader node)
    {
        var childDfsIndex = node.DfsIndex + 1;
        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i, childDfsIndex);
            if (child.Kind != VirtualNodeKind.Content || child.Content.Kind != ContentResourceKind.Text)
            {
                childDfsIndex += child.CountSubtreeNodes();
                continue;
            }

            if (child.Content.TryGetText(out var textContent))
            {
                return textContent;
            }

            childDfsIndex += child.CountSubtreeNodes();
        }

        return default;
    }

    public static bool IsTextContent(VirtualNodeReader node) =>
        node.Kind == VirtualNodeKind.Content && node.Content.Kind == ContentResourceKind.Text;

    public static bool IsRectangleContent(VirtualNodeReader node) =>
        node.Kind == VirtualNodeKind.Content && node.Content.Kind == ContentResourceKind.Rectangle;

    public static bool IsInteractiveContainer(VirtualNodeReader node) =>
        node.Kind == VirtualNodeKind.Container
        && !new PropertyReader(node.Properties).GetActionId(VirtualPropertyKey.ActionId).IsNone;

    public static bool TryGetFirstRectangleContent(VirtualNodeReader node, out VirtualNodeReader rectangle)
    {
        var childDfsIndex = node.DfsIndex + 1;
        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i, childDfsIndex);
            if (child.Kind == VirtualNodeKind.Content && child.Content.Kind == ContentResourceKind.Rectangle)
            {
                rectangle = child;
                return true;
            }

            childDfsIndex += child.CountSubtreeNodes();
        }

        rectangle = default;
        return false;
    }

    public static bool TryGetFirstTextContent(VirtualNodeReader node, out VirtualNodeReader text)
    {
        var childDfsIndex = node.DfsIndex + 1;
        for (var i = 0; i < node.ChildCount; i++)
        {
            var child = node.GetChild(i, childDfsIndex);
            if (child.Kind == VirtualNodeKind.Content && child.Content.Kind == ContentResourceKind.Text)
            {
                text = child;
                return true;
            }

            childDfsIndex += child.CountSubtreeNodes();
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
