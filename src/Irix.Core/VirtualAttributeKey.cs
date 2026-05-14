namespace Irix;

public enum AttributeDomain : byte
{
    Layout = 1,
    Visual = 2,
    Interaction = 3,
    RuntimeState = 4,
    Composite = 5,
    Style = 6,
}

public readonly struct VirtualAttributeKey : IEquatable<VirtualAttributeKey>
{
    public AttributeDomain Domain { get; }
    public ushort Code { get; }

    public VirtualAttributeKey(AttributeDomain domain, ushort code)
    {
        Domain = domain;
        Code = code;
    }

    public bool Equals(VirtualAttributeKey other) => Domain == other.Domain && Code == other.Code;

    public override bool Equals(object? obj) => obj is VirtualAttributeKey other && Equals(other);

    public override int GetHashCode() => ((ushort)Domain << 16) | Code;

    public static bool operator ==(VirtualAttributeKey left, VirtualAttributeKey right) => left.Equals(right);

    public static bool operator !=(VirtualAttributeKey left, VirtualAttributeKey right) => !left.Equals(right);

    public override string ToString() => $"{Domain}:{Code}";

    // ── Layout keys (R13-11) ──────────────────────────────────────
    public static readonly VirtualAttributeKey Width = new(AttributeDomain.Layout, 1);
    public static readonly VirtualAttributeKey Height = new(AttributeDomain.Layout, 2);
    public static readonly VirtualAttributeKey MinWidth = new(AttributeDomain.Layout, 3);
    public static readonly VirtualAttributeKey MaxWidth = new(AttributeDomain.Layout, 4);
    public static readonly VirtualAttributeKey MinHeight = new(AttributeDomain.Layout, 5);
    public static readonly VirtualAttributeKey MaxHeight = new(AttributeDomain.Layout, 6);
    public static readonly VirtualAttributeKey HorizontalPadding = new(AttributeDomain.Layout, 7);
    public static readonly VirtualAttributeKey VerticalPadding = new(AttributeDomain.Layout, 8);
    public static readonly VirtualAttributeKey ItemSpacing = new(AttributeDomain.Layout, 9);
    public static readonly VirtualAttributeKey TextHeight = new(AttributeDomain.Layout, 10);
    public static readonly VirtualAttributeKey ScrollY = new(AttributeDomain.Layout, 11);

    // ── Style keys (R13-10: pure text/style, not layout-affecting) ──
    public static readonly VirtualAttributeKey TextStyle = new(AttributeDomain.Style, 1);
    public static readonly VirtualAttributeKey FontFamily = new(AttributeDomain.Style, 2);
    public static readonly VirtualAttributeKey FontSize = new(AttributeDomain.Style, 3);
    public static readonly VirtualAttributeKey FontWeight = new(AttributeDomain.Style, 4);
    public static readonly VirtualAttributeKey Wrapping = new(AttributeDomain.Style, 5);

    // ── Visual keys (R13-12) ──────────────────────────────────────
    public static readonly VirtualAttributeKey Opacity = new(AttributeDomain.Visual, 1);
    public static readonly VirtualAttributeKey FillColor = new(AttributeDomain.Visual, 2);
    public static readonly VirtualAttributeKey TextColor = new(AttributeDomain.Visual, 3);

    // ── Interaction keys (R13-14) ─────────────────────────────────
    public static readonly VirtualAttributeKey ActionId = new(AttributeDomain.Interaction, 1);

    // ── RuntimeState keys (R13-14) ────────────────────────────────
    public static readonly VirtualAttributeKey IsHovered = new(AttributeDomain.RuntimeState, 1);
    public static readonly VirtualAttributeKey IsPressed = new(AttributeDomain.RuntimeState, 2);
    public static readonly VirtualAttributeKey IsFocused = new(AttributeDomain.RuntimeState, 3);
}

// ── AttributeChangeSet (R13-15): domain-separated masks ──────────

public readonly struct AttributeChangeSet : IEquatable<AttributeChangeSet>
{
    public ulong LayoutMask { get; }
    public ulong StyleMask { get; }
    public ulong VisualMask { get; }
    public ulong InteractionMask { get; }
    public ulong RuntimeStateMask { get; }
    public ulong CompositeMask { get; }

    public AttributeChangeSet(
        ulong layoutMask = 0, ulong styleMask = 0, ulong visualMask = 0,
        ulong interactionMask = 0, ulong runtimeStateMask = 0, ulong compositeMask = 0)
    {
        LayoutMask = layoutMask;
        StyleMask = styleMask;
        VisualMask = visualMask;
        InteractionMask = interactionMask;
        RuntimeStateMask = runtimeStateMask;
        CompositeMask = compositeMask;
    }

    public bool IsEmpty => LayoutMask == 0 && StyleMask == 0 && VisualMask == 0
        && InteractionMask == 0 && RuntimeStateMask == 0 && CompositeMask == 0;

    public bool HasLayout => LayoutMask != 0;
    public bool HasStyle => StyleMask != 0;
    public bool HasVisual => VisualMask != 0;
    public bool HasInteraction => InteractionMask != 0;
    public bool HasRuntimeState => RuntimeStateMask != 0;
    public bool HasComposite => CompositeMask != 0;

    public static AttributeChangeSet AddKey(AttributeChangeSet set, VirtualAttributeKey key)
    {
        var bit = 1ul << (key.Code - 1);
        return key.Domain switch
        {
            AttributeDomain.Layout => new AttributeChangeSet(set.LayoutMask | bit, set.StyleMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositeMask),
            AttributeDomain.Style => new AttributeChangeSet(set.LayoutMask, set.StyleMask | bit, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositeMask),
            AttributeDomain.Visual => new AttributeChangeSet(set.LayoutMask, set.StyleMask, set.VisualMask | bit, set.InteractionMask, set.RuntimeStateMask, set.CompositeMask),
            AttributeDomain.Interaction => new AttributeChangeSet(set.LayoutMask, set.StyleMask, set.VisualMask, set.InteractionMask | bit, set.RuntimeStateMask, set.CompositeMask),
            AttributeDomain.RuntimeState => new AttributeChangeSet(set.LayoutMask, set.StyleMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask | bit, set.CompositeMask),
            AttributeDomain.Composite => new AttributeChangeSet(set.LayoutMask, set.StyleMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositeMask | bit),
            _ => set,
        };
    }

    public bool Equals(AttributeChangeSet other) => LayoutMask == other.LayoutMask && StyleMask == other.StyleMask
        && VisualMask == other.VisualMask && InteractionMask == other.InteractionMask
        && RuntimeStateMask == other.RuntimeStateMask && CompositeMask == other.CompositeMask;

    public override bool Equals(object? obj) => obj is AttributeChangeSet other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayoutMask, StyleMask, VisualMask, InteractionMask, RuntimeStateMask, CompositeMask);

    public static bool operator ==(AttributeChangeSet left, AttributeChangeSet right) => left.Equals(right);
    public static bool operator !=(AttributeChangeSet left, AttributeChangeSet right) => !left.Equals(right);
}
