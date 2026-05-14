namespace Irix.Rendering;

internal static class AttributeChangeSetClassification
{
    public static InvalidationKind ClassifySet(this AttributeChangeSet changeSet)
    {
        if (changeSet.IsEmpty)
            return InvalidationKind.None;

        if (changeSet.HasLayout)
            return InvalidationKind.Layout;

        if (changeSet.HasStyle)
            return InvalidationKind.TextMeasure;

        if (changeSet.HasComposite)
            return InvalidationKind.CompositeOnly;

        if (changeSet.HasVisual)
            return InvalidationKind.VisualOnly;

        if (changeSet.HasInteraction || changeSet.HasRuntimeState)
            return InvalidationKind.VisualOnly;

        return InvalidationKind.None;
    }

    public static LayoutRebuildReason ToLayoutRebuildReason(this InvalidationKind kind)
    {
        return kind switch
        {
            InvalidationKind.None => LayoutRebuildReason.None,
            InvalidationKind.CompositeOnly => LayoutRebuildReason.StyleOnly,
            InvalidationKind.VisualOnly => LayoutRebuildReason.StyleOnly,
            InvalidationKind.TextMeasure => LayoutRebuildReason.TextSizeAffecting,
            InvalidationKind.Layout => LayoutRebuildReason.LayoutAffecting,
            InvalidationKind.TreeStructure => LayoutRebuildReason.TreeStructure,
            InvalidationKind.ViewportChanged => LayoutRebuildReason.ViewportChanged,
            _ => LayoutRebuildReason.None,
        };
    }

    public static LayoutRebuildReason ClassifyMask(this AttributeChangeSet changeSet)
    {
        return changeSet.ClassifySet().ToLayoutRebuildReason();
    }

    public static bool IsControlMetadataKey(VirtualAttributeKey key) =>
        key == VirtualAttributeKey.ActionId
        || key == VirtualAttributeKey.IsHovered
        || key == VirtualAttributeKey.IsPressed
        || key == VirtualAttributeKey.IsFocused;
}
