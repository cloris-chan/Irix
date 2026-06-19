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
        var consumedIndices = new HashSet<int>();
        var outputMapping = ColorOutputMapping.SdrSrgb;

        for (var index = 0; index < commands.Length; index++)
        {
            if (consumedIndices.Contains(index))
            {
                continue;
            }

            var command = commands[index];
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect when TryGetHitTarget(hitTargets, ToPixelRectangle(command.Rect), out _):
                    var buttonBounds = ToPixelRectangle(command.Rect);
                    var button = TryConsumeButtonPresentation(commands, index + 1, buttonBounds, resources, consumedIndices);
                    var buttonBorder = TryConsumeBorderPresentation(
                        commands,
                        index + 1,
                        buttonBounds,
                        consumedIndices,
                        new BorderPresentation(WindowColor.Opaque(24, 48, 96), 1));
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Button,
                        buttonBounds,
                        button.Text,
                        ForegroundColor: button.TextColor,
                        BackgroundColor: ToWindowColor(outputMapping.MapToSdr(command)),
                        BorderColor: buttonBorder.Color,
                        BorderThickness: buttonBorder.Thickness));
                    break;
                case DrawCommandKind.FillRect:
                    var rectangleBounds = ToPixelRectangle(command.Rect);
                    var rectangleBorder = TryConsumeBorderPresentation(
                        commands,
                        index + 1,
                        rectangleBounds,
                        consumedIndices,
                        default);
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        rectangleBounds,
                        BackgroundColor: ToWindowColor(outputMapping.MapToSdr(command)),
                        BorderColor: rectangleBorder.Color,
                        BorderThickness: rectangleBorder.Thickness));
                    break;
                case DrawCommandKind.StrokeRect:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Rectangle,
                        ToPixelRectangle(command.Rect),
                        BorderColor: ToWindowColor(outputMapping.MapToSdr(command)),
                        BorderThickness: ToBorderThickness(command.StrokeWidth)));
                    break;
                case DrawCommandKind.DrawTextRun:
                    elements.Add(new WindowContentElement(
                        WindowContentElementKind.Text,
                        ToPixelRectangle(command.Rect),
                        command.Text,
                        ForegroundColor: ToWindowColor(outputMapping.MapToSdr(command))));
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
        HashSet<int> consumedIndices)
    {
        var outputMapping = ColorOutputMapping.SdrSrgb;
        for (var index = startIndex; index < commands.Length; index++)
        {
            var candidate = commands[index];
            if (candidate.Kind == DrawCommandKind.DrawTextRun
                && ToPixelRectangle(candidate.Rect) == bounds)
            {
                consumedIndices.Add(index);
                return new ButtonPresentation(candidate.Text, ToWindowColor(outputMapping.MapToSdr(candidate)));
            }
        }

        return new ButtonPresentation(default, WindowColor.Opaque(255, 255, 255));
    }

    private static BorderPresentation TryConsumeBorderPresentation(
        ReadOnlySpan<DrawCommand> commands,
        int startIndex,
        PixelRectangle bounds,
        HashSet<int> consumedIndices,
        BorderPresentation defaultValue)
    {
        var outputMapping = ColorOutputMapping.SdrSrgb;
        for (var index = startIndex; index < commands.Length; index++)
        {
            var candidate = commands[index];
            if (candidate.Kind == DrawCommandKind.StrokeRect
                && ToPixelRectangle(candidate.Rect) == bounds)
            {
                consumedIndices.Add(index);
                return new BorderPresentation(
                    ToWindowColor(outputMapping.MapToSdr(candidate)),
                    ToBorderThickness(candidate.StrokeWidth));
            }
        }

        return defaultValue;
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

    private static int ToBorderThickness(float thickness) =>
        float.IsFinite(thickness) && thickness > 0f ? Math.Max((int)MathF.Round(thickness), 1) : 0;

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

    private readonly struct BorderPresentation(WindowColor Color, int Thickness) : IEquatable<BorderPresentation>
    {
        public WindowColor Color { get; } = Color;
        public int Thickness { get; } = Thickness;

        public bool Equals(BorderPresentation other) => Color == other.Color && Thickness == other.Thickness;

        public override bool Equals(object? obj) => obj is BorderPresentation other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Color, Thickness);

        public static bool operator ==(BorderPresentation left, BorderPresentation right) => left.Equals(right);

        public static bool operator !=(BorderPresentation left, BorderPresentation right) => !left.Equals(right);
    }
}
