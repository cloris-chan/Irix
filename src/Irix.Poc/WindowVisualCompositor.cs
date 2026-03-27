using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowVisualCompositor(INativeWindow window) : ICompositor
{
    private const int HorizontalPadding = 16;
    private const int VerticalPadding = 16;
    private const int ItemSpacing = 12;
    private const int TextHeight = 32;
    private const int ButtonHeight = 40;

    private readonly Lock _hitTargetsLock = new();
    private ButtonHitTarget[] _hitTargets = [];

    public ValueTask RenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        if (patchBatch.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var root = patchBatch.Memory.Span[patchBatch.Count - 1].Node;
        var elements = new List<WindowContentElement>();
        var hitTargets = new List<ButtonHitTarget>();
        var availableWidth = Math.Max(window.Region.PhysicalBounds.Width - (HorizontalPadding * 2), 0);
        var cursorY = VerticalPadding;

        LayoutNode(root, availableWidth, ref cursorY, elements, hitTargets);
        window.SetContentElements(elements);

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. hitTargets];
        }

        return ValueTask.CompletedTask;
    }

    public bool TryGetActionAt(int x, int y, out string action)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (Contains(hitTarget.Bounds, x, y))
                {
                    action = hitTarget.Action;
                    return true;
                }
            }
        }

        action = string.Empty;
        return false;
    }

    private static void LayoutNode(
        VirtualNode node,
        int availableWidth,
        ref int cursorY,
        List<WindowContentElement> elements,
        List<ButtonHitTarget> hitTargets)
    {
        switch (node.Kind)
        {
            case VirtualNodeKind.ScrollContainer:
                foreach (var child in node.Children)
                {
                    LayoutNode(child, availableWidth, ref cursorY, elements, hitTargets);
                }
                break;
            case VirtualNodeKind.Text:
                var content = GetTextContent(node);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        new PixelRectangle(HorizontalPadding, cursorY, availableWidth, TextHeight),
                        content));
                    cursorY += TextHeight + ItemSpacing;
                }
                break;
            case VirtualNodeKind.Rectangle:
                var rectangleBounds = new PixelRectangle(
                    HorizontalPadding,
                    cursorY,
                    GetDimension(node, "Width", Math.Min(availableWidth, 160)),
                    GetDimension(node, "Height", 48));
                elements.Add(new WindowContentElement(WindowContentElementKind.Rectangle, rectangleBounds));
                cursorY += rectangleBounds.Height + ItemSpacing;
                break;
            case VirtualNodeKind.Button:
                var label = GetButtonLabel(node);
                var width = Math.Min(availableWidth, Math.Max(140, label.Length * 12 + 32));
                var buttonBounds = new PixelRectangle(HorizontalPadding, cursorY, width, ButtonHeight);
                elements.Add(new WindowContentElement(WindowContentElementKind.Button, buttonBounds, label));

                var action = GetTextAttribute(node, "Action");
                if (!string.IsNullOrWhiteSpace(action))
                {
                    hitTargets.Add(new ButtonHitTarget(buttonBounds, action));
                }

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

    private static bool Contains(PixelRectangle bounds, int x, int y)
    {
        return x >= bounds.X
            && y >= bounds.Y
            && x < bounds.X + bounds.Width
            && y < bounds.Y + bounds.Height;
    }

    private readonly record struct ButtonHitTarget(PixelRectangle Bounds, string Action);
}

