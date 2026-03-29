using Irix.Platform;

namespace Irix.Rendering;

internal sealed class LayoutTreeBuilder
{
    private readonly LayoutStyle _style;

    public LayoutTreeBuilder()
        : this(LayoutStyle.Default)
    {
    }

    public LayoutTreeBuilder(LayoutStyle style)
    {
        _style = style;
    }

    public IReadOnlyList<LayoutElement> Build(VirtualNode root, PixelRectangle viewportBounds)
    {
        var elements = new List<LayoutElement>();
        var availableWidth = Math.Max(viewportBounds.Width - (_style.HorizontalPadding * 2), 0);
        var cursorY = _style.VerticalPadding;

        LayoutNode(root, availableWidth, ref cursorY, elements, _style);
        return elements;
    }

    private static void LayoutNode(
        VirtualNode node,
        int availableWidth,
        ref int cursorY,
        List<LayoutElement> elements,
        LayoutStyle style)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
                foreach (var child in node.Children)
                {
                    LayoutNode(child, availableWidth, ref cursorY, elements, style);
                }
                break;
            case VirtualNodeKind.Text:
                var content = GetTextContent(node);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    elements.Add(new LayoutElement(
                        LayoutElementKind.Text,
                        new PixelRectangle(style.HorizontalPadding, cursorY, availableWidth, style.TextHeight),
                        Text: content));
                    cursorY += style.TextHeight + style.ItemSpacing;
                }
                break;
            case VirtualNodeKind.Rectangle:
                var rectangleBounds = new PixelRectangle(
                    style.HorizontalPadding,
                    cursorY,
                    GetDimension(node, "Width", Math.Min(availableWidth, 160)),
                    GetDimension(node, "Height", 48));
                elements.Add(new LayoutElement(LayoutElementKind.Rectangle, rectangleBounds));
                cursorY += rectangleBounds.Height + style.ItemSpacing;
                break;
            case VirtualNodeKind.Button:
                var label = GetButtonLabel(node);
                var width = Math.Min(availableWidth, Math.Max(
                    style.MinimumButtonWidth,
                    label.Length * style.ButtonTextWidthFactor + style.ButtonHorizontalPadding));
                var bounds = new PixelRectangle(style.HorizontalPadding, cursorY, width, style.ButtonHeight);
                var action = GetTextAttribute(node, "Action");
                elements.Add(new LayoutElement(
                    LayoutElementKind.Button,
                    bounds,
                    Text: label,
                    Action: action));
                cursorY += style.ButtonHeight + style.ItemSpacing;
                break;
        }
    }

    private static int GetDimension(VirtualNode node, string attributeName, int defaultValue)
    {
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == attributeName && attribute.Value.Kind == AttributeValueKind.Number)
            {
                return (int)attribute.Value.Number;
            }
        }

        return defaultValue;
    }

    private static string GetButtonLabel(VirtualNode node)
    {
        foreach (var child in node.Children)
        {
            var content = GetTextContent(child);
            if (child.Kind == VirtualNodeKind.Text && !string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return "Button";
    }

    private static string? GetTextAttribute(VirtualNode node, string attributeName)
    {
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name == attributeName && attribute.Value.Kind == AttributeValueKind.Text)
            {
                return attribute.Value.Text;
            }
        }

        return null;
    }

    private static string? GetTextContent(VirtualNode node)
    {
        return node.Content.Kind == NodeContentKind.Text ? node.Content.Text : null;
    }
}
