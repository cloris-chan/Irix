namespace Irix.Rendering;

internal static class PropertyChangeSetClassification
{
    public static InvalidationKind ClassifySet(this PropertyChangeSet changeSet)
    {
        return changeSet.Effects.ToInvalidationKind();
    }

    public static InvalidationKind ToInvalidationKind(this StyleEffect effects)
    {
        if (effects == StyleEffect.None)
            return InvalidationKind.None;

        if ((effects & StyleEffect.Layout) != 0)
            return InvalidationKind.Layout;

        if ((effects & StyleEffect.TextMeasure) != 0)
            return InvalidationKind.TextMeasure;

        if ((effects & StyleEffect.Composite) != 0)
            return InvalidationKind.CompositeOnly;

        if ((effects & (StyleEffect.Visual | StyleEffect.Interaction)) != 0)
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

    public static LayoutRebuildReason ClassifyMask(this PropertyChangeSet changeSet)
    {
        return changeSet.ClassifySet().ToLayoutRebuildReason();
    }

    public static bool IsControlMetadataKey(VirtualPropertyKey key) =>
        VirtualPropertyMetadata.TryGet(key, out var metadata)
        && (metadata.Effects & StyleEffect.Layout) == 0
        && (metadata.Effects & StyleEffect.TextMeasure) == 0
        && (metadata.Effects & (StyleEffect.Visual | StyleEffect.Composite | StyleEffect.Interaction)) != 0;
}
