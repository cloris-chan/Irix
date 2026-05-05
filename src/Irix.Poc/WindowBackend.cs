using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowBackend
{
    public WindowBackendRenderResult Build(
        ReadOnlySpan<DrawCommand> commands,
        IReadOnlyList<HitTestTarget> hitTargets,
        IReadOnlyList<TextRunEntry> textRuns)
    {
        if (commands.Length == 0)
        {
            return new WindowBackendRenderResult([], []);
        }

        var elements = new List<WindowContentElement>();
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
                case DrawCommandKind.FillRect when TryGetHitTarget(hitTargets, ToPixelRectangle(command.Rect), out _):
                    var buttonBounds = ToPixelRectangle(command.Rect);
                    var button = TryConsumeButtonPresentation(commands, index + 1, buttonBounds, textRuns, consumedTextIndices);
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Button,
                        buttonBounds,
                        button.Label,
                        ForegroundColor: button.TextColor,
                        BackgroundColor: ToWindowColor(command.Color),
                        BorderColor: WindowColor.Opaque(24, 48, 96)));
                    break;
                case DrawCommandKind.FillRect:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        ToPixelRectangle(command.Rect),
                        BackgroundColor: ToWindowColor(command.Color)));
                    break;
                case DrawCommandKind.DrawTextRun:
                    var text = LookUpText(textRuns, command.Resource);
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        text,
                        ForegroundColor: ToWindowColor(command.Color)));
                    break;
            }
        }

        return new WindowBackendRenderResult([.. elements], [.. hitTargets]);
    }

    private static ButtonPresentation TryConsumeButtonPresentation(
        ReadOnlySpan<DrawCommand> commands,
        int startIndex,
        PixelRectangle bounds,
        IReadOnlyList<TextRunEntry> textRuns,
        HashSet<int> consumedTextIndices)
    {
        for (var index = startIndex; index < commands.Length; index++)
        {
            var candidate = commands[index];
            if (candidate.Kind == DrawCommandKind.DrawTextRun
                && ToPixelRectangle(candidate.Rect) == bounds)
            {
                consumedTextIndices.Add(index);
                var text = LookUpText(textRuns, candidate.Resource);
                return new ButtonPresentation(
                    string.IsNullOrWhiteSpace(text) ? "Button" : text,
                    ToWindowColor(candidate.Color));
            }
        }

        return new ButtonPresentation("Button", WindowColor.Opaque(255, 255, 255));
    }

    private static string? LookUpText(IReadOnlyList<TextRunEntry> textRuns, ResourceHandle resource)
    {
        if (resource.Kind != DrawingResourceKind.TextStyle || resource.Id < 0 || textRuns.Count == 0)
        {
            return null;
        }

        foreach (var entry in textRuns)
        {
            if (entry.Id == resource.Id)
            {
                return entry.Text;
            }
        }

        return null;
    }

    private static bool TryGetHitTarget(IReadOnlyList<HitTestTarget> hitTargets, PixelRectangle bounds, out HitTestTarget hitTarget)
    {
        for (var index = 0; index < hitTargets.Count; index++)
        {
            var candidate = hitTargets[index];
            if (candidate.Bounds == bounds)
            {
                hitTarget = candidate;
                return true;
            }
        }

        hitTarget = default;
        return false;
    }

    private static PixelRectangle ToPixelRectangle(DrawRect rect)
    {
        return new PixelRectangle(
            (int)MathF.Round(rect.X),
            (int)MathF.Round(rect.Y),
            (int)MathF.Round(rect.Width),
            (int)MathF.Round(rect.Height));
    }

    private static WindowColor ToWindowColor(DrawColor color) => new(color.A, color.R, color.G, color.B);

    private readonly record struct ButtonPresentation(string Label, WindowColor TextColor);
}
