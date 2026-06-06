namespace Irix.Rendering;

[Flags]
internal enum StyleDeltaWork : byte
{
    None = 0,
    Layout = 1,
    TextMeasure = 2,
    Draw = 4,
    Composition = 8,
    ControlStateProjection = 16,
}

internal readonly struct StyleDeltaPlan(
    PropertyChangeSet Changes,
    StyleDeltaWork Work,
    InvalidationKind InvalidationKind,
    LayoutRebuildReason LayoutRebuildReason) : IEquatable<StyleDeltaPlan>
{
    public PropertyChangeSet Changes { get; } = Changes;
    public StyleDeltaWork Work { get; } = Work;
    public InvalidationKind InvalidationKind { get; } = InvalidationKind;
    public LayoutRebuildReason LayoutRebuildReason { get; } = LayoutRebuildReason;

    public bool RequiresLayout => (Work & StyleDeltaWork.Layout) != 0;
    public bool RequiresTextMeasure => (Work & StyleDeltaWork.TextMeasure) != 0;
    public bool RequiresDrawUpdate => (Work & StyleDeltaWork.Draw) != 0;
    public bool RequiresCompositionUpdate => (Work & StyleDeltaWork.Composition) != 0;
    public bool RequiresControlStateProjection => (Work & StyleDeltaWork.ControlStateProjection) != 0;
    public bool CanReuseLayout => !RequiresLayout && !RequiresTextMeasure;
    public bool IsCompositorOnlyTransitionCandidate => RequiresCompositionUpdate && CanReuseLayout && !RequiresDrawUpdate;

    public bool Equals(StyleDeltaPlan other)
    {
        return Changes == other.Changes
            && Work == other.Work
            && InvalidationKind == other.InvalidationKind
            && LayoutRebuildReason == other.LayoutRebuildReason;
    }

    public override bool Equals(object? obj) => obj is StyleDeltaPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Changes, Work, InvalidationKind, LayoutRebuildReason);

    public static bool operator ==(StyleDeltaPlan left, StyleDeltaPlan right) => left.Equals(right);

    public static bool operator !=(StyleDeltaPlan left, StyleDeltaPlan right) => !left.Equals(right);
}

internal static class StyleDeltaPlanner
{
    public static StyleDeltaPlan Plan(ReadOnlySpan<VirtualNodeProperty> previousProperties, ReadOnlySpan<VirtualNodeProperty> nextProperties)
    {
        return Plan(BuildChangeSet(previousProperties, nextProperties));
    }

    public static StyleDeltaPlan Plan(PropertyChangeSet changes)
    {
        var effects = changes.Effects;
        var work = StyleDeltaWork.None;

        if ((effects & StyleEffect.Layout) != 0)
        {
            work |= StyleDeltaWork.Layout | StyleDeltaWork.Draw;
        }

        if ((effects & StyleEffect.TextMeasure) != 0)
        {
            work |= StyleDeltaWork.TextMeasure | StyleDeltaWork.Layout | StyleDeltaWork.Draw;
        }

        if ((effects & (StyleEffect.Visual | StyleEffect.Interaction)) != 0)
        {
            work |= StyleDeltaWork.Draw;
        }

        if ((effects & StyleEffect.Composite) != 0)
        {
            work |= StyleDeltaWork.Composition;
        }

        if ((effects & StyleEffect.Interaction) != 0)
        {
            work |= StyleDeltaWork.ControlStateProjection;
        }

        var invalidationKind = ResolveInvalidationKind(effects, work);
        return new StyleDeltaPlan(changes, work, invalidationKind, invalidationKind.ToLayoutRebuildReason());
    }

    internal static PropertyChangeSet BuildChangeSet(ReadOnlySpan<VirtualNodeProperty> previousProperties, ReadOnlySpan<VirtualNodeProperty> nextProperties)
    {
        var changeSet = default(PropertyChangeSet);

        foreach (var property in previousProperties)
        {
            if (!TryFindProperty(nextProperties, property.Key, out var nextProperty)
                || property.Value != nextProperty.Value)
            {
                changeSet = PropertyChangeSet.AddKey(changeSet, property.Key);
            }
        }

        foreach (var property in nextProperties)
        {
            if (!TryFindProperty(previousProperties, property.Key, out _))
            {
                changeSet = PropertyChangeSet.AddKey(changeSet, property.Key);
            }
        }

        return changeSet;
    }

    private static InvalidationKind ResolveInvalidationKind(StyleEffect effects, StyleDeltaWork work)
    {
        if ((effects & StyleEffect.Layout) != 0)
        {
            return InvalidationKind.Layout;
        }

        if ((effects & StyleEffect.TextMeasure) != 0)
        {
            return InvalidationKind.TextMeasure;
        }

        if ((work & StyleDeltaWork.Draw) != 0)
        {
            return InvalidationKind.VisualOnly;
        }

        if ((work & StyleDeltaWork.Composition) != 0)
        {
            return InvalidationKind.CompositeOnly;
        }

        return InvalidationKind.None;
    }

    private static bool TryFindProperty(ReadOnlySpan<VirtualNodeProperty> properties, VirtualPropertyKey key, out VirtualNodeProperty property)
    {
        foreach (var candidate in properties)
        {
            if (candidate.Key == key)
            {
                property = candidate;
                return true;
            }
        }

        property = default;
        return false;
    }
}
