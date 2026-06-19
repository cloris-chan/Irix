namespace Irix;

internal enum PropertyDomain : byte
{
    Layout = 1,
    Visual = 2,
    Interaction = 3,
    RuntimeState = 4,
    Composition = 5,
}

[Flags]
internal enum StyleEffect : byte
{
    None = 0,
    Layout = 1,
    TextMeasure = 2,
    Visual = 4,
    Composite = 8,
    Interaction = 16,
}

internal enum AnimationChannel : byte
{
    None,
    Discrete,
    CpuStyle,
    Composite,
}

[Flags]
internal enum VirtualNodeKindFlags : byte
{
    None = 0,
    Text = 1,
    Rectangle = 2,
    Button = 4,
    ScrollContainer = 8,
    All = Text | Rectangle | Button | ScrollContainer,
}

internal enum StylePropertyScope : byte
{
    NodeLocal,
    RuntimeState,
}

public readonly struct VirtualPropertyKey : IEquatable<VirtualPropertyKey>
{
    private readonly ushort _id;

    private VirtualPropertyKey(PropertyDomain domain, ushort code)
    {
        _id = (ushort)(((ushort)domain << 8) | code);
    }

    internal PropertyDomain Domain => (PropertyDomain)(_id >> 8);

    internal ushort Code => (ushort)(_id & 0xFF);

    public bool Equals(VirtualPropertyKey other) => _id == other._id;

    public override bool Equals(object? obj) => obj is VirtualPropertyKey other && Equals(other);

    public override int GetHashCode() => _id;

    public static bool operator ==(VirtualPropertyKey left, VirtualPropertyKey right) => left.Equals(right);

    public static bool operator !=(VirtualPropertyKey left, VirtualPropertyKey right) => !left.Equals(right);

    public static readonly VirtualPropertyKey Width = new(PropertyDomain.Layout, 1);
    public static readonly VirtualPropertyKey Height = new(PropertyDomain.Layout, 2);
    public static readonly VirtualPropertyKey ScrollY = new(PropertyDomain.Layout, 3);

    public static readonly VirtualPropertyKey Background = new(PropertyDomain.Visual, 1);
    internal static readonly VirtualPropertyKey ForegroundColor = new(PropertyDomain.Visual, 2);
    public static readonly VirtualPropertyKey Border = new(PropertyDomain.Visual, 3);

    public static readonly VirtualPropertyKey ActionId = new(PropertyDomain.Interaction, 1);

    public static readonly VirtualPropertyKey IsHovered = new(PropertyDomain.RuntimeState, 1);
    public static readonly VirtualPropertyKey IsPressed = new(PropertyDomain.RuntimeState, 2);
    public static readonly VirtualPropertyKey IsFocused = new(PropertyDomain.RuntimeState, 3);

    internal static readonly VirtualPropertyKey LayerOpacity = new(PropertyDomain.Composition, 1);
    internal static readonly VirtualPropertyKey TranslateX = new(PropertyDomain.Composition, 2);
    internal static readonly VirtualPropertyKey TranslateY = new(PropertyDomain.Composition, 3);
}

internal readonly struct StylePropertyMetadata(
    VirtualPropertyKey key,
    PropertyValueKind valueKind,
    StyleEffect effects,
    AnimationChannel animationChannel,
    StylePropertyScope scope,
    VirtualNodeKindFlags supportedNodeKinds) : IEquatable<StylePropertyMetadata>
{

    public VirtualPropertyKey Key { get; } = key;
    public PropertyValueKind ValueKind { get; } = valueKind;
    public StyleEffect Effects { get; } = effects;
    public AnimationChannel AnimationChannel { get; } = animationChannel;
    public StylePropertyScope Scope { get; } = scope;
    public VirtualNodeKindFlags SupportedNodeKinds { get; } = supportedNodeKinds;

    public bool Equals(StylePropertyMetadata other) =>
        Key == other.Key
        && ValueKind == other.ValueKind
        && Effects == other.Effects
        && AnimationChannel == other.AnimationChannel
        && Scope == other.Scope
        && SupportedNodeKinds == other.SupportedNodeKinds;

    public override bool Equals(object? obj) => obj is StylePropertyMetadata other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, ValueKind, Effects, AnimationChannel, Scope, SupportedNodeKinds);

    public static bool operator ==(StylePropertyMetadata left, StylePropertyMetadata right) => left.Equals(right);

    public static bool operator !=(StylePropertyMetadata left, StylePropertyMetadata right) => !left.Equals(right);
}

internal static class VirtualPropertyMetadata
{
    private const VirtualNodeKindFlags WidthNodes =
        VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button;

    private const VirtualNodeKindFlags HeightNodes =
        VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button | VirtualNodeKindFlags.ScrollContainer;

    private const VirtualNodeKindFlags CompositionNodes =
        VirtualNodeKindFlags.Text | VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button | VirtualNodeKindFlags.ScrollContainer;

    public static StylePropertyMetadata Get(VirtualPropertyKey key)
    {
        if (TryGet(key, out var metadata))
        {
            return metadata;
        }

        throw new ArgumentOutOfRangeException(nameof(key), "Unknown virtual property key.");
    }

    public static bool TryGet(VirtualPropertyKey key, out StylePropertyMetadata metadata)
    {
        if (key == VirtualPropertyKey.Width)
        {
            metadata = Number(key, StyleEffect.Layout, AnimationChannel.CpuStyle, StylePropertyScope.NodeLocal, WidthNodes);
            return true;
        }

        if (key == VirtualPropertyKey.Height)
        {
            metadata = Number(key, StyleEffect.Layout, AnimationChannel.CpuStyle, StylePropertyScope.NodeLocal, HeightNodes);
            return true;
        }

        if (key == VirtualPropertyKey.ScrollY)
        {
            metadata = Number(
                key,
                StyleEffect.Layout,
                AnimationChannel.CpuStyle,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.ScrollContainer);
            return true;
        }

        if (key == VirtualPropertyKey.Background)
        {
            metadata = new StylePropertyMetadata(
                key,
                PropertyValueKind.Paint,
                StyleEffect.Visual,
                AnimationChannel.CpuStyle,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualPropertyKey.ForegroundColor)
        {
            metadata = new StylePropertyMetadata(
                key,
                PropertyValueKind.Color,
                StyleEffect.Visual,
                AnimationChannel.CpuStyle,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.Text | VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualPropertyKey.Border)
        {
            metadata = new StylePropertyMetadata(
                key,
                PropertyValueKind.BorderStroke,
                StyleEffect.Visual,
                AnimationChannel.CpuStyle,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualPropertyKey.ActionId)
        {
            metadata = new StylePropertyMetadata(
                key,
                PropertyValueKind.ActionId,
                StyleEffect.Interaction,
                AnimationChannel.None,
                StylePropertyScope.NodeLocal,
                VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualPropertyKey.IsHovered || key == VirtualPropertyKey.IsPressed || key == VirtualPropertyKey.IsFocused)
        {
            metadata = new StylePropertyMetadata(
                key,
                PropertyValueKind.Boolean,
                StyleEffect.Interaction | StyleEffect.Visual,
                AnimationChannel.Discrete,
                StylePropertyScope.RuntimeState,
                VirtualNodeKindFlags.Button);
            return true;
        }

        if (key == VirtualPropertyKey.LayerOpacity)
        {
            metadata = Number(
                key,
                StyleEffect.Composite,
                AnimationChannel.Composite,
                StylePropertyScope.NodeLocal,
                CompositionNodes);
            return true;
        }

        if (key == VirtualPropertyKey.TranslateX || key == VirtualPropertyKey.TranslateY)
        {
            metadata = Number(
                key,
                StyleEffect.Composite,
                AnimationChannel.Composite,
                StylePropertyScope.NodeLocal,
                CompositionNodes);
            return true;
        }

        metadata = default;
        return false;
    }

    private static StylePropertyMetadata Number(
        VirtualPropertyKey key,
        StyleEffect effects,
        AnimationChannel animationChannel,
        StylePropertyScope scope,
        VirtualNodeKindFlags supportedNodeKinds) =>
        new(key, PropertyValueKind.Number, effects, animationChannel, scope, supportedNodeKinds);
}

internal static class VirtualPropertyDiagnostics
{
    public static string Format(VirtualPropertyKey key)
    {
        if (key == VirtualPropertyKey.Width) return nameof(VirtualPropertyKey.Width);
        if (key == VirtualPropertyKey.Height) return nameof(VirtualPropertyKey.Height);
        if (key == VirtualPropertyKey.ScrollY) return nameof(VirtualPropertyKey.ScrollY);
        if (key == VirtualPropertyKey.Background) return nameof(VirtualPropertyKey.Background);
        if (key == VirtualPropertyKey.ForegroundColor) return nameof(VirtualPropertyKey.ForegroundColor);
        if (key == VirtualPropertyKey.Border) return nameof(VirtualPropertyKey.Border);
        if (key == VirtualPropertyKey.ActionId) return nameof(VirtualPropertyKey.ActionId);
        if (key == VirtualPropertyKey.IsHovered) return nameof(VirtualPropertyKey.IsHovered);
        if (key == VirtualPropertyKey.IsPressed) return nameof(VirtualPropertyKey.IsPressed);
        if (key == VirtualPropertyKey.IsFocused) return nameof(VirtualPropertyKey.IsFocused);
        if (key == VirtualPropertyKey.LayerOpacity) return nameof(VirtualPropertyKey.LayerOpacity);
        if (key == VirtualPropertyKey.TranslateX) return nameof(VirtualPropertyKey.TranslateX);
        if (key == VirtualPropertyKey.TranslateY) return nameof(VirtualPropertyKey.TranslateY);

        return $"Unknown({(byte)key.Domain},{key.Code})";
    }
}

internal static class VirtualNodePropertySupport
{
    public static bool Supports(VirtualNodeKind kind, VirtualPropertyKey key)
    {
        if (!VirtualPropertyMetadata.TryGet(key, out var metadata))
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

internal readonly struct PropertyChangeSet : IEquatable<PropertyChangeSet>
{
    public PropertyChangeSet()
    {
    }

    private PropertyChangeSet(
        ulong layoutMask,
        ulong visualMask,
        ulong interactionMask,
        ulong runtimeStateMask,
        ulong compositionMask,
        StyleEffect effects)
    {
        LayoutMask = layoutMask;
        VisualMask = visualMask;
        InteractionMask = interactionMask;
        RuntimeStateMask = runtimeStateMask;
        CompositionMask = compositionMask;
        Effects = effects;
    }

    public ulong LayoutMask { get; }
    public ulong VisualMask { get; }
    public ulong InteractionMask { get; }
    public ulong RuntimeStateMask { get; }
    public ulong CompositionMask { get; }
    public StyleEffect Effects { get; }

    public bool IsEmpty =>
        LayoutMask == 0
        && VisualMask == 0
        && InteractionMask == 0
        && RuntimeStateMask == 0
        && CompositionMask == 0
        && Effects == StyleEffect.None;

    public static PropertyChangeSet AddKey(PropertyChangeSet set, VirtualPropertyKey key)
    {
        if (!VirtualPropertyMetadata.TryGet(key, out var metadata))
        {
            return set;
        }

        var bit = key.Code is 0 or > 64 ? 0 : 1ul << (key.Code - 1);
        return key.Domain switch
        {
            PropertyDomain.Layout => new PropertyChangeSet(set.LayoutMask | bit, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositionMask, set.Effects | metadata.Effects),
            PropertyDomain.Visual => new PropertyChangeSet(set.LayoutMask, set.VisualMask | bit, set.InteractionMask, set.RuntimeStateMask, set.CompositionMask, set.Effects | metadata.Effects),
            PropertyDomain.Interaction => new PropertyChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask | bit, set.RuntimeStateMask, set.CompositionMask, set.Effects | metadata.Effects),
            PropertyDomain.RuntimeState => new PropertyChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask | bit, set.CompositionMask, set.Effects | metadata.Effects),
            PropertyDomain.Composition => new PropertyChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositionMask | bit, set.Effects | metadata.Effects),
            _ => new PropertyChangeSet(set.LayoutMask, set.VisualMask, set.InteractionMask, set.RuntimeStateMask, set.CompositionMask, set.Effects | metadata.Effects),
        };
    }

    public bool Equals(PropertyChangeSet other) => LayoutMask == other.LayoutMask
        && VisualMask == other.VisualMask
        && InteractionMask == other.InteractionMask
        && RuntimeStateMask == other.RuntimeStateMask
        && CompositionMask == other.CompositionMask
        && Effects == other.Effects;

    public override bool Equals(object? obj) => obj is PropertyChangeSet other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayoutMask, VisualMask, InteractionMask, RuntimeStateMask, CompositionMask, Effects);

    public static bool operator ==(PropertyChangeSet left, PropertyChangeSet right) => left.Equals(right);

    public static bool operator !=(PropertyChangeSet left, PropertyChangeSet right) => !left.Equals(right);
}
