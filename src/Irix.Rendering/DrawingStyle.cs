using Irix.Drawing;

namespace Irix.Rendering;

internal readonly record struct DrawingStyle(
    DrawColor TextColor,
    DrawColor RectangleFillColor,
    DrawColor ButtonFillColor,
    DrawColor ButtonHoverFillColor,
    DrawColor ButtonPressedFillColor,
    DrawColor ButtonFocusedFillColor,
    DrawColor ButtonTextColor,
    TextStyle TextStyle,
    TextStyle ButtonTextStyle)
{
    public static DrawingStyle Default => new(
        TextColor: DrawColor.Opaque(255, 255, 255),
        RectangleFillColor: DrawColor.Opaque(72, 72, 72),
        ButtonFillColor: DrawColor.Opaque(52, 120, 246),
        ButtonHoverFillColor: DrawColor.Opaque(72, 136, 255),
        ButtonPressedFillColor: DrawColor.Opaque(36, 92, 210),
        ButtonFocusedFillColor: DrawColor.Opaque(84, 160, 255),
        ButtonTextColor: DrawColor.Opaque(255, 255, 255),
        TextStyle: TextStyle.Default,
        ButtonTextStyle: TextStyle.Default);

    public DrawColor ResolveButtonFillColor(ButtonVisualState state)
    {
        if (state.IsPressed)
        {
            return ButtonPressedFillColor;
        }

        if (state.IsHovered)
        {
            return ButtonHoverFillColor;
        }

        return state.IsFocused ? ButtonFocusedFillColor : ButtonFillColor;
    }
}
