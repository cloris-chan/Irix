using Irix.Drawing;

namespace Irix.Rendering;

internal readonly record struct ControlVisualStateResolver
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
}