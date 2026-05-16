using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct ControlVisualStateResolver : IEquatable<ControlVisualStateResolver>
{
    public static ControlVisualStateResolver Default { get; } = new();

    public DrawColor ResolveButtonFillColor(DrawingStyle drawing, ButtonVisualState state)
    {
        if (state.IsPressed)
        {
            return drawing.ButtonPressedFillColor;
        }

        if (state.IsHovered)
        {
            return drawing.ButtonHoverFillColor;
        }

        return state.IsFocused ? drawing.ButtonFocusedFillColor : drawing.ButtonFillColor;
    }

    public bool Equals(ControlVisualStateResolver other) => true;

    public override bool Equals(object? obj) => obj is ControlVisualStateResolver;

    public override int GetHashCode() => 0;

    public static bool operator ==(ControlVisualStateResolver left, ControlVisualStateResolver right) => left.Equals(right);

    public static bool operator !=(ControlVisualStateResolver left, ControlVisualStateResolver right) => !left.Equals(right);
}
