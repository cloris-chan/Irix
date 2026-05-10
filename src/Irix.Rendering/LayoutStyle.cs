namespace Irix.Rendering;

internal readonly record struct LayoutStyle(
    int HorizontalPadding,
    int VerticalPadding,
    int ItemSpacing,
    int TextHeight,
    int ButtonHeight,
    int RectangleHeight,
    int MinimumButtonWidth,
    int ButtonTextWidthFactor,
    int ButtonHorizontalPadding)
{
    public static LayoutStyle Default => RenderStylePreset.Default.Layout;
}
