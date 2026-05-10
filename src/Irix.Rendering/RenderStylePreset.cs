using Irix.Drawing;

namespace Irix.Rendering;

internal readonly record struct RenderStylePreset(
    LayoutStyle Layout,
    DrawingStyle Drawing,
    ControlVisualStateResolver VisualStates)
{
    public const string DefaultName = "RenderStylePreset.Default";

    public static RenderStylePreset Default { get; } = new(
        Layout: new LayoutStyle(
            HorizontalPadding: 16,
            VerticalPadding: 16,
            ItemSpacing: 12,
            TextHeight: 32,
            ButtonHeight: 40,
            RectangleHeight: 48,
            MinimumButtonWidth: 140,
            ButtonTextWidthFactor: 12,
            ButtonHorizontalPadding: 32),
        Drawing: new DrawingStyle(
            TextColor: DrawColor.Opaque(255, 255, 255),
            RectangleFillColor: DrawColor.Opaque(72, 72, 72),
            ButtonFillColor: DrawColor.Opaque(52, 120, 246),
            ButtonHoverFillColor: DrawColor.Opaque(72, 136, 255),
            ButtonPressedFillColor: DrawColor.Opaque(36, 92, 210),
            ButtonFocusedFillColor: DrawColor.Opaque(84, 160, 255),
            ButtonTextColor: DrawColor.Opaque(255, 255, 255),
            TextStyle: TextStyle.Default,
            ButtonTextStyle: TextStyle.Default),
        VisualStates: ControlVisualStateResolver.Default);
}