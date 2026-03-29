using Irix.Drawing;
using Irix.Platform;

namespace Irix.Poc;

internal sealed class WindowBackend
{
    public WindowBackendRenderResult Build(ReadOnlySpan<DrawCommand> commands)
    {
        if (commands.Length == 0)
        {
            return new WindowBackendRenderResult([], []);
        }

        var elements = new List<WindowContentElement>();
        var hitTargets = new List<WindowHitTarget>();
        var consumedTextIndices = new HashSet<int>();

        for (var index = 0; index < commands.Length; index++)
        {
            if (consumedTextIndices.Contains(index))
            {
                continue;
            }

            var command = commands[index];
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect when !string.IsNullOrWhiteSpace(command.Metadata):
                    var buttonBounds = ToPixelRectangle(command.Rect);
                    var label = TryConsumeButtonLabel(commands, index + 1, command.Metadata, buttonBounds, consumedTextIndices);
                    elements.Add(new WindowContentElement(WindowContentElementKind.Button, buttonBounds, label));
                    hitTargets.Add(new WindowHitTarget(buttonBounds, command.Metadata));
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

        return new WindowBackendRenderResult([.. elements], [.. hitTargets]);
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
}
