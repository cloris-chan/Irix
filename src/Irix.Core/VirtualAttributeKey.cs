namespace Irix;

internal enum AttributeDomain : byte
{
    Layout = 1,
    Visual = 2,
    Interaction = 3,
    RuntimeState = 4,
}

[Flags]
public enum StyleEffect : byte
{
    None = 0,
    Layout = 1,
    TextMeasure = 2,
    Visual = 4,
    Composite = 8,
    Interaction = 16,
}

public enum AnimationChannel : byte
{
    None,
    Discrete,
    CpuStyle,
    Composite,
}

[Flags]
public enum VirtualNodeKindFlags : byte
{
    None = 0,
    Text = 1,
    Rectangle = 2,
    Button = 4,
    ScrollContainer = 8,
    All = Text | Rectangle | Button | ScrollContainer,
}

public enum StylePropertyScope : byte
{
    NodeLocal,
    RuntimeState,
}

public readonly struct VirtualAttributeKey : IEquatable<VirtualAttributeKey>
{
    private readonly ushort _id;

    private VirtualAttributeKey(AttributeDomain domain, ushort code)
    {
        _id = (ushort)(((ushort)domain << 8) | code);
    }

    internal AttributeDomain Domain => (AttributeDomain)(_id >> 8);
    internal ushort Code => (ushort)(_id & 0xFF);

    public bool Equals(VirtualAttributeKey other) => _id == other._id;

    public override bool Equals(object? obj) => obj is VirtualAttributeKey other && Equals(other);

    public override int GetHashCode() => _id;

    public static bool operator ==(VirtualAttributeKey left, VirtualAttributeKey right) => left.Equals(right);

    public static bool operator !=(VirtualAttributeKey left, VirtualAttributeKey right) => !left.Equals(right);

    public static readonly VirtualAttributeKey Width = new(AttributeDomain.Layout, 1);
    public static readonly VirtualAttributeKey Height = new(AttributeDomain.Layout, 2);
    public static readonly VirtualAttributeKey MinWidth = new(AttributeDomain.Layout, 3);
    public static readonly VirtualAttributeKey MaxWidth = new(AttributeDomain.Layout, 4);
    public static readonly VirtualAttributeKey MinHeight = new(AttributeDomain.Layout, 5);
    public static readonly VirtualAttributeKey MaxHeight = new(AttributeDomain.Layout, 6);
    public static readonly VirtualAttributeKey ScrollY = new(AttributeDomain.Layout, 7);

    public static readonly VirtualAttributeKey Opacity = new(AttributeDomain.Visual, 1);

    public static readonly VirtualAttributeKey ActionId = new(AttributeDomain.Interaction, 1);

    public static readonly VirtualAttributeKey IsHovered = new(AttributeDomain.RuntimeState, 1);
    public static readonly VirtualAttributeKey IsPressed = new(AttributeDomain.RuntimeState, 2);
    public static readonly VirtualAttributeKey IsFocused = new(AttributeDomain.RuntimeState, 3);
}

public readonly record struct StylePropertyMetadata(
    VirtualAttributeKey Key,
    AttributeValueKind ValueKind,
    StyleEffect Effects,
    AnimationChannel AnimationChannel,
    StylePropertyScope Scope,
    VirtualNodeKindFlags SupportedNodeKinds);

public static class VirtualAttributeMetadata
{
    private const VirtualNodeKindFlags SizedNodes =
        VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button | VirtualNodeKindFlags.ScrollContainer;

    public static StylePropertyMetadata Get(VirtualAttributeKey key)
    {
        if (TryGet(key, out var metadata))
        {
            return metadata;
        }

        throw new ArgumentOutOfRangeException(nameof(key), "Unknown virtual attribute key.");
    }

    public static bool TryGet(VirtualAttributeKey key, out StylePropertyMetadata metadata)
    {
        if (key == VirtualAttributeKey.Width)
        {
            metadata = Number(key, StyleEffect.Layout, AnimationChannel.CpuStyle, StylePropertyScope.NodeLocal, SizedNodes);
            return true;
        }

        if (key == VirtualAttributeKey.Height)
        {
            metadata = Number(key, StyleEffect.Layout, AnimationChannel.CpuStyle, StylePropertyScope.NodeLocal, SizedNodes);
            return true;
        }

        if (key == VirtualAttributeKey.MinWidth || key == VirtualAttributeKey.MaxWidth
            || key == VirtualAttributeKey.MinHeight || key == VirtualAttributeKey.MaxHeight)
        {
            metadata = Number(key, StyleEffect.Layout, AnimationChannel.Discrete, StylePropertyScope.NodeLocal, SizedNodes);
            return true;
        }

        if (key == VirtualAttributeKey.ScrollY)
        {
            metadata = Number(
                key,
                StyleEffect.Layout,
                AnimationChannel.CpuStyle,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.ScrollContainer);
            return true;
        }

        if (key == VirtualAttributeKey.Opacity)
        {
            metadata = Number(
                key,
                StyleEffect.Composite | StyleEffect.Visual,
                AnimationChannel.Composite,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.All);
            return true;
        }

        if (key == VirtualAttributeKey.ActionId)
        {
            metadata = new StylePropertyMetadata(
                key,
                AttributeValueKind.ActionId,
                StyleEffect.Interaction,
                AnimationChannel.None,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualAttributeKey.IsHovered || key == VirtualAttributeKey.IsPressed || key == VirtualAttributeKey.IsFocused)
        {
            metadata = new StylePropertyMetadata(
                key,
                AttributeValueKind.Boolean,
                StyleEffect.Interaction | StyleEffect.Visual,
                AnimationChannel.Discrete,
                StylePropertyScope.RuntimeState,
                VirtualNodeKindFlags.Button);
            return true;
        }

        metadata = default;
        return false;
    }

    private static StylePropertyMetadata Number(
        VirtualAttributeKey key,
        StyleEffect effects,
        AnimationChannel animationChannel,
        StylePropertyScope scope,
        VirtualNodeKindFlags supportedNodeKinds) =>
        new(key, AttributeValueKind.Number, effects, animationChannel, scope, supportedNodeKinds);
}

public static class VirtualAttributeDiagnostics
{
    public static string Format(VirtualAttributeKey key)
    {
        if (key == VirtualAttributeKey.Width) return nameof(VirtualAttributeKey.Width);
        if (key == VirtualAttributeKey.Height) return nameof(VirtualAttributeKey.Height);
        if (key == VirtualAttributeKey.MinWidth) return nameof(VirtualAttributeKey.MinWidth);
        if (key == VirtualAttributeKey.MaxWidth) return nameof(VirtualAttributeKey.MaxWidth);
        if (key == VirtualAttributeKey.MinHeight) return nameof(VirtualAttributeKey.MinHeight);
        if (key == VirtualAttributeKey.MaxHeight) return nameof(VirtualAttributeKey.MaxHeight);
        if (key == VirtualAttributeKey.ScrollY) return nameof(VirtualAttributeKey.ScrollY);
        if (key == VirtualAttributeKey.Opacity) return nameof(VirtualAttributeKey.Opacity);
        if (key == VirtualAttributeKey.ActionId) return nameof(VirtualAttributeKey.ActionId);
        if (key == VirtualAttributeKey.IsHovered) return nameof(VirtualAttributeKey.IsHovered);
        if (key == VirtualAttributeKey.IsPressed) return nameof(VirtualAttributeKey.IsPressed);
        if (key == VirtualAttributeKey.IsFocused) return nameof(VirtualAttributeKey.IsFocused);

        return $"Unknown({(byte)key.Domain},{key.Code})";
    }
}

public static class VirtualNodePropertySupport
{
    public static bool Supports(VirtualNodeKind kind, VirtualAttributeKey key)
    {
        if (!VirtualAttributeMetadata.TryGet(key, out var metadata))
        {
            return false;
        }

        return (metadata.SupportedNodeKinds & ToFlag(kind)) != 0;
    }

    private static VirtualNodeKindFlags ToFlag(VirtualNodeKind kind)
    {
        return kind switch
        {
            VirtualNodeKind.Text => VirtualNodeKindFlags.Text,
            VirtualNodeKind.Rectangle => VirtualNodeKindFlags.Rectangle,
            VirtualNodeKind.Button => VirtualNodeKindFlags.Button,
            VirtualNodeKind.ScrollContainer => VirtualNodeKindFlags.ScrollContainer,
            _ => VirtualNodeKindFlags.None,
        };
    }
}

public readonly struct AttributeChangeSet : IEquatable<AttributeChangeSet>
{
    public AttributeChangeSet()
    {
    }

    private AttributeChangeSet(
        ulong layoutMask,
        ulong visualMask,
        ulong interactionMask,
        ulong runtimeStateMask,
        StyleEffect effects)
    {
        LayoutMask = layoutMask;
        VisualMask = visualMask;
        InteractionMask = interactionMask;
        RuntimeStateMask = runtimeStateMask;
        Effects = effects;
    }

    public ulong LayoutMask { get; }
    public ulong VisualMask { get; }
    public ulong InteractionMask { get; }
    public ulong RuntimeStateMask { get; }
    public StyleEffect Effects { get; }

    public bool IsEmpty => LayoutMask == 0 && VisualMask == 0 && InteractionMask == 0 && RuntimeStateMask == 0 && Effects == StyleEffect.None;

    public static AttributeChangeSet AddKey(AttributeChangeSet set, VirtualAttributeKey key)
    {
        if (!VirtualAttributeMetadata.TryGet(key, out var metadata))
        {
            return set;
        }

        var bit = key.Code is 0 or > 64 ? 0 : 1ul << (key.Code - 1);
        return key.Domain switch
        {
            AttributeDomain.Layout => new AttributeChangeSet(set.LayoutMask | bit, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.Effects | metadata.Effects),
            AttributeDomain.Visual => new AttributeChangeSet(set.LayoutMask, set.VisualMask | bit, set.InteractionMask, set.RuntimeStateMask, set.Effects | metadata.Effects),
            AttributeDomain.Interaction => new AttributeChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask | bit, set.RuntimeStateMask, set.Effects | metadata.Effects),
            AttributeDomain.RuntimeState => new AttributeChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask | bit, set.Effects | metadata.Effects),
            _ => new AttributeChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.Effects | metadata.Effects),
        };
    }

    public bool Equals(AttributeChangeSet other) => LayoutMask == other.LayoutMask
        && VisualMask == other.VisualMask
        && InteractionMask == other.InteractionMask
        && RuntimeStateMask == other.RuntimeStateMask
        && Effects == other.Effects;

    public override bool Equals(object? obj) => obj is AttributeChangeSet other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayoutMask, VisualMask, InteractionMask, RuntimeStateMask, Effects);

    public static bool operator ==(AttributeChangeSet left, AttributeChangeSet right) => left.Equals(right);

    public static bool operator !=(AttributeChangeSet left, AttributeChangeSet right) => !left.Equals(right);
}
