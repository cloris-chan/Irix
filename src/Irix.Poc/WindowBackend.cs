using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowBackend
{
    public WindowBackendRenderResult Build(
        ReadOnlySpan<DrawCommand> commands,
        IReadOnlyList<HitTestTarget> hitTargets,
        IFrameResourceResolver resources)
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
                    var button = TryConsumeButtonPresentation(commands, index + 1, buttonBounds, resources, consumedTextIndices);
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
                    var text = ResolveText(resources, command.Text);
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
        IFrameResourceResolver resources,
        HashSet<int> consumedTextIndices)
    {
        for (var index = startIndex; index < commands.Length; index++)
        {
            var candidate = commands[index];
            if (candidate.Kind == DrawCommandKind.DrawTextRun
                && ToPixelRectangle(candidate.Rect) == bounds)
            {
                consumedTextIndices.Add(index);
                var text = ResolveText(resources, candidate.Text);
                return new ButtonPresentation(
                    string.IsNullOrWhiteSpace(text) ? "Button" : text,
                    ToWindowColor(candidate.Color));
            }
        }

        return new ButtonPresentation("Button", WindowColor.Opaque(255, 255, 255));
    }

    private static string ResolveText(IFrameResourceResolver resources, TextSlice text)
    {
        var span = resources.Resolve(text);
        return span.IsEmpty ? string.Empty : span.ToString();
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

    private readonly struct ButtonPresentation(string Label, WindowColor TextColor) : IEquatable<ButtonPresentation>
    {
        public string Label { get; } = Label;
        public WindowColor TextColor { get; } = TextColor;

        public bool Equals(ButtonPresentation other)
        {
            return Label == other.Label
                && TextColor == other.TextColor;
        }

        public override bool Equals(object? obj) => obj is ButtonPresentation other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Label, TextColor);

        public static bool operator ==(ButtonPresentation left, ButtonPresentation right) => left.Equals(right);

        public static bool operator !=(ButtonPresentation left, ButtonPresentation right) => !left.Equals(right);
    }
}
