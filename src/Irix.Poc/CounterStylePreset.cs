using Irix.Drawing;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CounterStylePreset
{
    public static RenderStylePreset Default { get; } = CreateDefault();

    private static RenderStylePreset CreateDefault()
    {
        var preset = RenderStylePreset.Default;
        var drawing = preset.Drawing;
        return new RenderStylePreset(
            preset.Layout,
            new DrawingStyle(
                DrawColor.Opaque(32, 32, 32),
                drawing.RectangleFillColor,
                drawing.ButtonFillColor,
                drawing.ButtonHoverFillColor,
                drawing.ButtonPressedFillColor,
                drawing.ButtonFocusedFillColor,
                drawing.ButtonTextColor,
                drawing.TextStyle,
                drawing.ButtonTextStyle),
            preset.VisualStates);
    }
}
