namespace Irix;

public enum VirtualAttributeKey : ushort
{
    Unknown = 0,
    ActionId = 1,
    ScrollY = 2,
    Height = 3,
    Width = 4,
    IsHovered = 5,
    IsPressed = 6,
    IsFocused = 7,
    Text = 8,
    TextStyle = 9,
    FontFamily = 10,
    FontSize = 11,
    FontWeight = 12,
    Wrapping = 13,
    HorizontalPadding = 14,
    VerticalPadding = 15,
    ItemSpacing = 16,
    TextHeight = 17,
    ButtonHeight = 18,
    RectangleHeight = 19,
    MinimumButtonWidth = 20,
    ButtonTextWidthFactor = 21,
    ButtonHorizontalPadding = 22,
}

[Flags]
public enum ChangedAttributeMask : uint
{
    None = 0,
    ActionId = 1u << VirtualAttributeKey.ActionId,
    ScrollY = 1u << VirtualAttributeKey.ScrollY,
    Height = 1u << VirtualAttributeKey.Height,
    Width = 1u << VirtualAttributeKey.Width,
    IsHovered = 1u << VirtualAttributeKey.IsHovered,
    IsPressed = 1u << VirtualAttributeKey.IsPressed,
    IsFocused = 1u << VirtualAttributeKey.IsFocused,
    Text = 1u << VirtualAttributeKey.Text,
    TextStyle = 1u << VirtualAttributeKey.TextStyle,
    FontFamily = 1u << VirtualAttributeKey.FontFamily,
    FontSize = 1u << VirtualAttributeKey.FontSize,
    FontWeight = 1u << VirtualAttributeKey.FontWeight,
    Wrapping = 1u << VirtualAttributeKey.Wrapping,
    HorizontalPadding = 1u << VirtualAttributeKey.HorizontalPadding,
    VerticalPadding = 1u << VirtualAttributeKey.VerticalPadding,
    ItemSpacing = 1u << VirtualAttributeKey.ItemSpacing,
    TextHeight = 1u << VirtualAttributeKey.TextHeight,
    ButtonHeight = 1u << VirtualAttributeKey.ButtonHeight,
    RectangleHeight = 1u << VirtualAttributeKey.RectangleHeight,
    MinimumButtonWidth = 1u << VirtualAttributeKey.MinimumButtonWidth,
    ButtonTextWidthFactor = 1u << VirtualAttributeKey.ButtonTextWidthFactor,
    ButtonHorizontalPadding = 1u << VirtualAttributeKey.ButtonHorizontalPadding,
}

public static class ChangedAttributeMaskExtensions
{
    public static ChangedAttributeMask ToMask(this VirtualAttributeKey key) =>
        key == VirtualAttributeKey.Unknown ? ChangedAttributeMask.None : (ChangedAttributeMask)(1u << (int)key);

    public static bool IsControlMetadataKey(this VirtualAttributeKey key) =>
        key is VirtualAttributeKey.ActionId
            or VirtualAttributeKey.IsHovered
            or VirtualAttributeKey.IsPressed
            or VirtualAttributeKey.IsFocused;
}
