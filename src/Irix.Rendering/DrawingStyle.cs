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
    public static DrawingStyle Default => RenderStylePreset.Default.Drawing;
}
