using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowVisualCompositor(INativeWindow window) : ICompositor
{
    private readonly Lock _hitTargetsLock = new();
    private ButtonHitTarget[] _hitTargets = [];

    public ValueTask RenderAsync(DrawCommandBatch drawCommandBatch, CancellationToken cancellationToken = default)
    {
        if (drawCommandBatch.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var elements = new List<WindowContentElement>();
        var hitTargets = new List<ButtonHitTarget>();
        var consumedTextIndices = new HashSet<int>();
        var commands = drawCommandBatch.Memory.Span;

        for (var index = 0; index < drawCommandBatch.Count; index++)
        {
            if (consumedTextIndices.Contains(index))
            {
                continue;
            }

            var command = commands[index];
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect when !string.IsNullOrWhiteSpace(command.Metadata):
                    var bounds = ToPixelRectangle(command.Rect);
                    var label = TryConsumeButtonLabel(commands, index + 1, command.Metadata, bounds, consumedTextIndices);
                    elements.Add(new WindowContentElement(WindowContentElementKind.Button, bounds, label));
                    hitTargets.Add(new ButtonHitTarget(bounds, command.Metadata));
                    break;
                case DrawCommandKind.FillRect:
                    elements.Add(new WindowContentElement(WindowContentElementKind.Rectangle, ToPixelRectangle(command.Rect)));
                    break;
                case DrawCommandKind.DrawTextRun:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        command.Text));
                    break;
            }
        }

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

    private static string TryConsumeButtonLabel(
        ReadOnlySpan<DrawCommand> commands,
        int startIndex,
        string? action,
        PixelRectangle bounds,
        HashSet<int> consumedTextIndices)
    {
        for (var index = startIndex; index < commands.Length; index++)
        {
            var candidate = commands[index];
            if (candidate.Kind == DrawCommandKind.DrawTextRun
                && candidate.Metadata == action
                && ToPixelRectangle(candidate.Rect) == bounds)
            {
                consumedTextIndices.Add(index);
                return string.IsNullOrWhiteSpace(candidate.Text) ? "Button" : candidate.Text;
            }
        }

        return "Button";
    }

    private static PixelRectangle ToPixelRectangle(DrawRect rect)
    {
        return new PixelRectangle(
            (int)MathF.Round(rect.X),
            (int)MathF.Round(rect.Y),
            (int)MathF.Round(rect.Width),
            (int)MathF.Round(rect.Height));
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

