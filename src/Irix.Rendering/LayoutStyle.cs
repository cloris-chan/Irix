namespace Irix.Rendering;

internal readonly record struct LayoutStyle(
    int HorizontalPadding = 16,
    int VerticalPadding = 16,
    int ItemSpacing = 12,
    int TextHeight = 32,
    int ButtonHeight = 40,
    int MinimumButtonWidth = 140,
    int ButtonTextWidthFactor = 12,
    int ButtonHorizontalPadding = 32)
{
    public static LayoutStyle Default => new(
        HorizontalPadding: 16,
        VerticalPadding: 16,
        ItemSpacing: 12,
        TextHeight: 32,
        ButtonHeight: 40,
        MinimumButtonWidth: 140,
        ButtonTextWidthFactor: 12,
        ButtonHorizontalPadding: 32);
}
