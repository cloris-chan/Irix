namespace Irix.Rendering;

internal readonly struct LayoutStyle(
    int HorizontalPadding,
    int VerticalPadding,
    int ItemSpacing,
    int TextHeight,
    int ButtonHeight,
    int RectangleHeight,
    int MinimumButtonWidth,
    int ButtonTextWidthFactor,
    int ButtonHorizontalPadding) : IEquatable<LayoutStyle>
{

    public int HorizontalPadding { get; } = HorizontalPadding;
    public int VerticalPadding { get; } = VerticalPadding;
    public int ItemSpacing { get; } = ItemSpacing;
    public int TextHeight { get; } = TextHeight;
    public int ButtonHeight { get; } = ButtonHeight;
    public int RectangleHeight { get; } = RectangleHeight;
    public int MinimumButtonWidth { get; } = MinimumButtonWidth;
    public int ButtonTextWidthFactor { get; } = ButtonTextWidthFactor;
    public int ButtonHorizontalPadding { get; } = ButtonHorizontalPadding;

    public static LayoutStyle Default => RenderStylePreset.Default.Layout;

    public bool Equals(LayoutStyle other)
    {
        return HorizontalPadding == other.HorizontalPadding
            && VerticalPadding == other.VerticalPadding
            && ItemSpacing == other.ItemSpacing
            && TextHeight == other.TextHeight
            && ButtonHeight == other.ButtonHeight
            && RectangleHeight == other.RectangleHeight
            && MinimumButtonWidth == other.MinimumButtonWidth
            && ButtonTextWidthFactor == other.ButtonTextWidthFactor
            && ButtonHorizontalPadding == other.ButtonHorizontalPadding;
    }

    public override bool Equals(object? obj) => obj is LayoutStyle other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(HorizontalPadding);
        hash.Add(VerticalPadding);
        hash.Add(ItemSpacing);
        hash.Add(TextHeight);
        hash.Add(ButtonHeight);
        hash.Add(RectangleHeight);
        hash.Add(MinimumButtonWidth);
        hash.Add(ButtonTextWidthFactor);
        hash.Add(ButtonHorizontalPadding);
        return hash.ToHashCode();
    }

    public static bool operator ==(LayoutStyle left, LayoutStyle right) => left.Equals(right);

    public static bool operator !=(LayoutStyle left, LayoutStyle right) => !left.Equals(right);
}
