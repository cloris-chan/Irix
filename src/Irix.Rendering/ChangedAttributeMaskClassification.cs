namespace Irix.Rendering;

internal static class ChangedAttributeMaskClassification
{
    private static readonly ChangedAttributeMask StyleOnlyKeys =
        VirtualAttributeKey.IsHovered.ToMask()
        | VirtualAttributeKey.IsPressed.ToMask()
        | VirtualAttributeKey.IsFocused.ToMask()
        | VirtualAttributeKey.ActionId.ToMask();

    private static readonly ChangedAttributeMask TextSizeAffectingKeys =
        VirtualAttributeKey.Text.ToMask()
        | VirtualAttributeKey.TextStyle.ToMask()
        | VirtualAttributeKey.FontFamily.ToMask()
        | VirtualAttributeKey.FontSize.ToMask()
        | VirtualAttributeKey.FontWeight.ToMask()
        | VirtualAttributeKey.Wrapping.ToMask();

    private static readonly ChangedAttributeMask LayoutAffectingKeys =
        VirtualAttributeKey.ScrollY.ToMask()
        | VirtualAttributeKey.Width.ToMask()
        | VirtualAttributeKey.Height.ToMask()
        | VirtualAttributeKey.HorizontalPadding.ToMask()
        | VirtualAttributeKey.VerticalPadding.ToMask()
        | VirtualAttributeKey.ItemSpacing.ToMask()
        | VirtualAttributeKey.TextHeight.ToMask()
        | VirtualAttributeKey.ButtonHeight.ToMask()
        | VirtualAttributeKey.RectangleHeight.ToMask()
        | VirtualAttributeKey.MinimumButtonWidth.ToMask()
        | VirtualAttributeKey.ButtonTextWidthFactor.ToMask()
        | VirtualAttributeKey.ButtonHorizontalPadding.ToMask();

    public static LayoutRebuildReason ClassifyMask(this ChangedAttributeMask mask)
    {
        if (mask == ChangedAttributeMask.None)
            return LayoutRebuildReason.None;

        var reason = LayoutRebuildReason.None;

        if ((mask & StyleOnlyKeys) != 0)
            reason = LayoutRebuildReason.StyleOnly;

        if ((mask & TextSizeAffectingKeys) != 0)
            reason = LayoutRebuildReason.TextSizeAffecting;

        if ((mask & LayoutAffectingKeys) != 0 || (mask & ~(StyleOnlyKeys | TextSizeAffectingKeys)) != 0)
            reason = LayoutRebuildReason.LayoutAffecting;

        return reason;
    }
}
