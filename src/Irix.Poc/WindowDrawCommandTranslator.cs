using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowDrawCommandTranslator(INativeWindow window) : IPatchBatchTranslator
{
    private const int HorizontalPadding = 16;
    private const int VerticalPadding = 16;
    private const int ItemSpacing = 12;
    private const int TextHeight = 32;
    private const int ButtonHeight = 40;

    public DrawCommandBatch Translate(PatchBatch patchBatch)
    {
        if (patchBatch.Count == 0)
        {
            return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0);
        }

        var root = patchBatch.Memory.Span[patchBatch.Count - 1].Node;
        var commands = new List<DrawCommand>();
        var availableWidth = Math.Max(window.Region.PhysicalBounds.Width - (HorizontalPadding * 2), 0);
        var cursorY = VerticalPadding;

        RecordNode(root, availableWidth, ref cursorY, commands);

        return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([.. commands]), commands.Count);
    }

    private static void RecordNode(
        VirtualNode node,
        int availableWidth,
        ref int cursorY,
        List<DrawCommand> commands)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
                foreach (var child in node.Children)
                {
                    RecordNode(child, availableWidth, ref cursorY, commands);
                }
                break;
            case VirtualNodeKind.Text:
                var content = GetTextContent(node);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    commands.Add(new DrawCommand(
                        DrawCommandKind.DrawTextRun,
                        Rect: new DrawRect(HorizontalPadding, cursorY, availableWidth, TextHeight),
                        Text: content,
                        Color: DrawColor.Opaque(255, 255, 255)));
                    cursorY += TextHeight + ItemSpacing;
                }
                break;
            case VirtualNodeKind.Rectangle:
                var rectangle = new DrawRect(
                    HorizontalPadding,
                    cursorY,
                    GetDimension(node, "Width", Math.Min(availableWidth, 160)),
                    GetDimension(node, "Height", 48));
                commands.Add(new DrawCommand(
                    DrawCommandKind.FillRect,
                    Rect: rectangle,
                    Color: DrawColor.Opaque(72, 72, 72)));
                cursorY += (int)rectangle.Height + ItemSpacing;
                break;
            case VirtualNodeKind.Button:
                var label = GetButtonLabel(node);
                var width = Math.Min(availableWidth, Math.Max(140, label.Length * 12 + 32));
                var bounds = new DrawRect(HorizontalPadding, cursorY, width, ButtonHeight);
                var action = GetTextAttribute(node, "Action");

                commands.Add(new DrawCommand(
                    DrawCommandKind.FillRect,
                    Rect: bounds,
                    Color: DrawColor.Opaque(52, 120, 246),
                    Metadata: action));
                commands.Add(new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: bounds,
                    Text: label,
                    Color: DrawColor.Opaque(255, 255, 255),
                    Metadata: action));
                cursorY += ButtonHeight + ItemSpacing;
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
