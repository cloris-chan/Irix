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
            return new WindowBackendRenderResult([], [], resources);
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
                        button.Text,
                        ForegroundColor: button.TextColor,
                        BackgroundColor: ToWindowColor(command.ToSdrColor()),
                        BorderColor: WindowColor.Opaque(24, 48, 96)));
                    break;
                case DrawCommandKind.FillRect:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        ToPixelRectangle(command.Rect),
                        BackgroundColor: ToWindowColor(command.ToSdrColor())));
                    break;
                case DrawCommandKind.DrawTextRun:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        command.Text,
                        ForegroundColor: ToWindowColor(command.ToSdrColor())));
                    break;
            }
        }

        return new WindowBackendRenderResult([.. elements], [.. hitTargets], resources);
    }

    private ButtonPresentation TryConsumeButtonPresentation(
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
                return new ButtonPresentation(candidate.Text, ToWindowColor(candidate.ToSdrColor()));
            }
        }

        return new ButtonPresentation(default, WindowColor.Opaque(255, 255, 255));
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

    private readonly struct ButtonPresentation(TextSlice Text, WindowColor TextColor) : IEquatable<ButtonPresentation>
    {
        public TextSlice Text { get; } = Text;
        public WindowColor TextColor { get; } = TextColor;

        public bool Equals(ButtonPresentation other)
        {
            return Text == other.Text
                && TextColor == other.TextColor;
        }

        public override bool Equals(object? obj) => obj is ButtonPresentation other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Text, TextColor);

        public static bool operator ==(ButtonPresentation left, ButtonPresentation right) => left.Equals(right);

        public static bool operator !=(ButtonPresentation left, ButtonPresentation right) => !left.Equals(right);
    }
}
