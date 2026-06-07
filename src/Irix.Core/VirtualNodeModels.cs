using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Irix;

public enum VirtualNodeKind
{
    None,
    Text,
    Rectangle,
    Button,
    ScrollContainer
}

public enum VirtualNodePatchOperation
{
    ReplaceRoot,
    Add,
    Remove,
    Update,
    Move
}

public enum PropertyValueKind : byte
{
    None,
    Number,
    Boolean,
    ActionId,
    Color
}

public enum NodeContentKind : byte
{
    None,
    Text,
    Number,
    Boolean
}

// ── InvalidationKind (R13-17) ────────────────────────────────────

public enum InvalidationKind : byte
{
    None,
    CompositeOnly,
    VisualOnly,
    TextMeasure,
    Layout,
    TreeStructure,
    ViewportChanged,
}

// ── NodeContent: 24-byte pure value union (R13-3) ────────────────

[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct NodeContent : IEquatable<NodeContent>
{
    [FieldOffset(0)] private readonly NodeContentKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _padding2;
    [FieldOffset(8)] private readonly ulong _data0;
    [FieldOffset(16)] private readonly ulong _data1;

    private NodeContent(NodeContentKind kind, ulong data0, ulong data1)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _padding2 = 0;
        _data0 = data0;
        _data1 = data1;
    }

    public NodeContentKind Kind => _kind;

    public static NodeContent None => default;

    public static NodeContent FromText(TextNodeContent textContent) =>
        new(NodeContentKind.Text, textContent.BufferId.Value, (ulong)(uint)textContent.Range.Start | ((ulong)(uint)textContent.Range.Length << 32));

    public static NodeContent FromNumber(double value) =>
        new(NodeContentKind.Number, BitConverter.DoubleToUInt64Bits(value), 0);

    public static NodeContent FromBoolean(bool value) =>
        new(NodeContentKind.Boolean, value ? 1ul : 0ul, 0);

    public bool TryGetText(out TextNodeContent textContent)
    {
        if (_kind != NodeContentKind.Text) { textContent = default; return false; }
        textContent = new TextNodeContent(new TextBufferId((uint)_data0), new TextRange((int)(_data1 & 0xFFFFFFFF), (int)(_data1 >> 32)));
        return true;
    }

    public bool TryGetNumber(out double value)
    {
        if (_kind != NodeContentKind.Number) { value = 0; return false; }
        value = BitConverter.UInt64BitsToDouble(_data0);
        return true;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (_kind != NodeContentKind.Boolean) { value = false; return false; }
        value = _data0 != 0;
        return true;
    }

    public bool Equals(NodeContent other) => _kind == other._kind && _data0 == other._data0 && _data1 == other._data1;

    public override bool Equals(object? obj) => obj is NodeContent other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, _data0, _data1);

    public static bool operator ==(NodeContent left, NodeContent right) => left.Equals(right);

    public static bool operator !=(NodeContent left, NodeContent right) => !left.Equals(right);
}

// ── PropertyValue: pure value union (R13-7) ─────────────────────

[StructLayout(LayoutKind.Explicit, Size = 24)]
public readonly struct PropertyValue : IEquatable<PropertyValue>
{
    [FieldOffset(0)] private readonly PropertyValueKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _uintValue;
    [FieldOffset(8)] private readonly ulong _data0;
    [FieldOffset(8)] private readonly Color _colorValue;

    private PropertyValue(PropertyValueKind kind, uint uintValue, ulong data0)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _uintValue = uintValue;
        _colorValue = default;
        _data0 = data0;
    }

    private PropertyValue(Color color)
    {
        _kind = PropertyValueKind.Color;
        _padding0 = 0;
        _padding1 = 0;
        _uintValue = 0;
        _data0 = 0;
        _colorValue = color;
    }

    public PropertyValueKind Kind => _kind;

    public static PropertyValue None => default;

    public static PropertyValue FromNumber(double value) =>
        new(PropertyValueKind.Number, 0, BitConverter.DoubleToUInt64Bits(value));

    public static PropertyValue FromBoolean(bool value) =>
        new(PropertyValueKind.Boolean, value ? 1u : 0u, 0);

    public static PropertyValue FromActionId(ActionId value) =>
        new(PropertyValueKind.ActionId, value.Value, 0);

    internal static PropertyValue FromColor(StyleColor value) =>
        new(value.Value);

    public bool TryGetNumber(out double value)
    {
        if (_kind != PropertyValueKind.Number) { value = 0; return false; }
        value = BitConverter.UInt64BitsToDouble(_data0);
        return true;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (_kind != PropertyValueKind.Boolean) { value = false; return false; }
        value = _uintValue != 0;
        return true;
    }

    public bool TryGetActionId(out ActionId value)
    {
        if (_kind != PropertyValueKind.ActionId) { value = ActionId.None; return false; }
        value = new ActionId(_uintValue);
        return true;
    }

    internal bool TryGetColor(out StyleColor value)
    {
        if (_kind != PropertyValueKind.Color) { value = default; return false; }
        value = new StyleColor(_colorValue);
        return true;
    }

    public double GetRequiredNumber()
    {
        if (TryGetNumber(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.Number}.");
    }

    public bool GetRequiredBoolean()
    {
        if (TryGetBoolean(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.Boolean}.");
    }

    public ActionId GetRequiredActionId()
    {
        if (TryGetActionId(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.ActionId}.");
    }

    internal StyleColor GetRequiredColor()
    {
        if (TryGetColor(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.Color}.");
    }

    public bool Equals(PropertyValue other) => _kind == other._kind && _uintValue == other._uintValue && _data0 == other._data0 && _colorValue == other._colorValue;

    public override bool Equals(object? obj) => obj is PropertyValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, _uintValue, _data0, _colorValue);

    public static bool operator ==(PropertyValue left, PropertyValue right) => left.Equals(right);

    public static bool operator !=(PropertyValue left, PropertyValue right) => !left.Equals(right);
}

internal readonly struct StyleColor(Color Value) : IEquatable<StyleColor>
{
    public Color Value { get; } = Value;

    public uint Argb => Value.ToSrgb().Argb;

    public byte A => Value.ToSrgb().A;
    public byte R => Value.ToSrgb().R;
    public byte G => Value.ToSrgb().G;
    public byte B => Value.ToSrgb().B;

    public static StyleColor Transparent => default;

    public static StyleColor Opaque(byte r, byte g, byte b) => FromArgb(255, r, g, b);

    public static StyleColor FromArgb(byte a, byte r, byte g, byte b) =>
        new(Color.FromSrgb(a, r, g, b));

    public static StyleColor FromArgb(uint argb) =>
        FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public SrgbColor ToSrgb() => Value.ToSrgb();

    public bool Equals(StyleColor other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is StyleColor other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(StyleColor left, StyleColor right) => left.Equals(right);

    public static bool operator !=(StyleColor left, StyleColor right) => !left.Equals(right);
}

internal readonly struct StyleColorSlot(StyleColor Value, bool HasValue) : IEquatable<StyleColorSlot>
{
    public StyleColor Value { get; } = Value;
    public bool HasValue { get; } = HasValue;

    public static StyleColorSlot None => default;

    public static StyleColorSlot Some(StyleColor value) => new(value, true);

    public bool Equals(StyleColorSlot other) => Value == other.Value && HasValue == other.HasValue;

    public override bool Equals(object? obj) => obj is StyleColorSlot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasValue);

    public static bool operator ==(StyleColorSlot left, StyleColorSlot right) => left.Equals(right);

    public static bool operator !=(StyleColorSlot left, StyleColorSlot right) => !left.Equals(right);
}

// ── VirtualNodeTree / VirtualNode (R13-6: factory key -> NodeKey) ─

public readonly struct VirtualNodeTree(VirtualNode root, TextBufferSnapshot textSnapshot = default)
{
    public VirtualNode Root { get; } = root;
    public TextBufferSnapshot TextSnapshot { get; } = textSnapshot;

}

public readonly struct VirtualNode
{
    private readonly VirtualNodeProperty[]? _properties;
    private readonly VirtualNode[]? _children;

    public VirtualNode(
        VirtualNodeKind kind,
        NodeKey key = default,
        NodeContent content = default,
        scoped ReadOnlySpan<VirtualNodeProperty> properties = default,
        scoped ReadOnlySpan<VirtualNode> children = default)
        : this(kind, key, content, VirtualNodePropertySet.Create(kind, properties), CreateChildren(children))
    {
    }

    private VirtualNode(
        VirtualNodeKind kind,
        NodeKey key,
        NodeContent content,
        VirtualNodeProperty[] properties,
        VirtualNode[] children)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(children);

        Kind = kind;
        Key = key;
        Content = content;
        _properties = properties.Length == 0 ? null : properties;
        _children = children.Length == 0 ? null : children;
        ValidateNodeShape(kind, key, content, properties, children);
    }

    public VirtualNodeKind Kind { get; }
    public NodeKey Key { get; }
    public NodeContent Content { get; }
    public ReadOnlySpan<VirtualNodeProperty> Properties => _properties is null ? ReadOnlySpan<VirtualNodeProperty>.Empty : _properties;
    public ReadOnlySpan<VirtualNode> Children => _children is null ? ReadOnlySpan<VirtualNode>.Empty : _children;

    /// <summary>
    /// Takes ownership of already validated/frozen arrays without copying them again.
    /// Callers must not mutate the arrays after this call.
    /// </summary>
    internal static VirtualNode CreateFromOwnedArraysUnsafe(
        VirtualNodeKind kind,
        NodeKey key,
        NodeContent content,
        VirtualNodeProperty[] properties,
        VirtualNode[] children) =>
        new(kind, key, content, properties, children);

    private static VirtualNode[] CreateChildren(scoped ReadOnlySpan<VirtualNode> children) =>
        children.IsEmpty ? [] : children.ToArray();

    private static void ValidateNodeShape(
        VirtualNodeKind kind,
        NodeKey key,
        NodeContent content,
        VirtualNodeProperty[] properties,
        VirtualNode[] children)
    {
        switch (kind)
        {
            case VirtualNodeKind.None:
                if (key != NodeKey.None
                    || content != NodeContent.None
                    || properties.Length != 0
                    || children.Length != 0)
                {
                    throw new ArgumentException("None nodes must be empty.");
                }

                return;

            case VirtualNodeKind.Text:
                if (children.Length != 0)
                {
                    throw new ArgumentException($"{kind} nodes cannot have children.", nameof(children));
                }

                if (content.Kind != NodeContentKind.Text)
                {
                    throw new ArgumentException("Text nodes require text content.", nameof(content));
                }

                return;

            case VirtualNodeKind.Rectangle:
                if (children.Length != 0)
                {
                    throw new ArgumentException($"{kind} nodes cannot have children.", nameof(children));
                }

                if (content != NodeContent.None)
                {
                    throw new ArgumentException("Rectangle nodes cannot have content.", nameof(content));
                }

                return;

            case VirtualNodeKind.Button:
                if (content != NodeContent.None)
                {
                    throw new ArgumentException("Button nodes cannot have content.", nameof(content));
                }

                if (children.Length != 1)
                {
                    throw new ArgumentException("Button nodes require exactly one leaf text label child.", nameof(children));
                }

                var child = children[0];
                if (child.Kind == VirtualNodeKind.Text
                    && child.Children.IsEmpty
                    && child.Content.TryGetText(out var label)
                    && !label.IsNone)
                {
                    return;
                }

                throw new ArgumentException("Button nodes require exactly one leaf text label child.", nameof(children));

            case VirtualNodeKind.ScrollContainer:
                if (content != NodeContent.None)
                {
                    throw new ArgumentException("ScrollContainer nodes cannot have content.", nameof(content));
                }

                return;

            default:
                return;
        }
    }

}

public ref struct VirtualNodePropertyListBuilder
{
    private Span<VirtualNodeProperty> _properties;
    private int _count;

    public VirtualNodePropertyListBuilder(Span<VirtualNodeProperty> properties)
    {
        _properties = properties;
        _count = 0;
    }

    public readonly int Count => _count;

    public readonly ReadOnlySpan<VirtualNodeProperty> Written => _properties[.._count];

    public void AddWidth(double value) => Add(VirtualNodeProperty.Width(value));

    public void AddHeight(double value) => Add(VirtualNodeProperty.Height(value));

    public void AddAction(ActionId actionId) => Add(VirtualNodeProperty.Action(actionId));

    public void AddState(bool hovered, bool pressed, bool focused)
    {
        AddHovered(hovered);
        AddPressed(pressed);
        AddFocused(focused);
    }

    public void AddHovered(bool value) => Add(VirtualNodeProperty.Hovered(value));

    public void AddPressed(bool value) => Add(VirtualNodeProperty.Pressed(value));

    public void AddFocused(bool value) => Add(VirtualNodeProperty.Focused(value));

    public void AddScrollY(double value) => Add(VirtualNodeProperty.ScrollY(value));

    internal void AddBackgroundColor(StyleColor value) => Add(VirtualNodeProperty.BackgroundColor(value));

    internal void AddForegroundColor(StyleColor value) => Add(VirtualNodeProperty.ForegroundColor(value));

    public void Add(VirtualNodeProperty property)
    {
        for (var i = 0; i < _count; i++)
        {
            if (_properties[i].Key == property.Key)
            {
                throw new ArgumentException(
                    $"Duplicate property {VirtualPropertyDiagnostics.Format(property.Key)}.",
                    nameof(property));
            }
        }

        if (_count >= _properties.Length)
        {
            throw new InvalidOperationException("The virtual node property builder storage is full.");
        }

        _properties[_count++] = property;
    }
}

public ref struct VirtualNodeChildrenBuilder
{
    private const int InlineCapacity = 4;

    private InlineChildBuffer _inlineChildren;
    private VirtualNode[]? _overflow;
    private int _count;

    public VirtualNodeChildrenBuilder()
    {
        _overflow = null;
        _count = 0;
    }

    public readonly int Count => _count;

    public void Add(VirtualNode child)
    {
        if (_overflow is not null)
        {
            EnsureOverflowCapacity(_count + 1);
            _overflow[_count++] = child;
            return;
        }

        if (_count < InlineCapacity)
        {
            _inlineChildren[_count++] = child;
            return;
        }

        PromoteToOverflow();
        _overflow![_count++] = child;
    }

    internal VirtualNode[] ToArray()
    {
        if (_count == 0)
        {
            return [];
        }

        var result = new VirtualNode[_count];
        if (_overflow is not null)
        {
            Array.Copy(_overflow, result, _count);
            return result;
        }

        _inlineChildren[.._count].CopyTo(result);
        return result;
    }

    private void PromoteToOverflow()
    {
        _overflow = new VirtualNode[InlineCapacity * 2];
        _inlineChildren[..InlineCapacity].CopyTo(_overflow);
    }

    private void EnsureOverflowCapacity(int requiredCapacity)
    {
        if (_overflow is null || requiredCapacity <= _overflow.Length)
        {
            return;
        }

        var newCapacity = Math.Max(requiredCapacity, _overflow.Length * 2);
        Array.Resize(ref _overflow, newCapacity);
    }

    [InlineArray(InlineCapacity)]
    private struct InlineChildBuffer
    {
        private VirtualNode _element0;
    }
}

internal static class VirtualNodePropertySet
{
    public static VirtualNodeProperty[] Create(VirtualNodeKind kind, scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        if (properties.IsEmpty)
        {
            return [];
        }

        var copy = properties.ToArray();
        Validate(kind, copy);
        return copy;
    }

    private static void Validate(VirtualNodeKind kind, VirtualNodeProperty[] properties)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!VirtualNodePropertySupport.Supports(kind, property.Key))
            {
                throw new ArgumentException(
                    $"Property {VirtualPropertyDiagnostics.Format(property.Key)} is not supported on {kind}.",
                    nameof(properties));
            }

            for (var j = i + 1; j < properties.Length; j++)
            {
                if (property.Key == properties[j].Key)
                {
                    throw new ArgumentException(
                        $"Duplicate property {VirtualPropertyDiagnostics.Format(property.Key)} on {kind}.",
                        nameof(properties));
                }
            }
        }
    }
}

// ── VirtualNodeProperty (R13-9: domain-scoped key, R13-18: helpers) ─

public readonly struct VirtualNodeProperty : IEquatable<VirtualNodeProperty>
{
    private VirtualNodeProperty(VirtualPropertyKey key, PropertyValue value)
    {
        Validate(key, value);
        Key = key;
        Value = value;
    }

    public VirtualPropertyKey Key { get; }
    public PropertyValue Value { get; }

    public static VirtualNodeProperty Action(ActionId actionId) =>
        new(VirtualPropertyKey.ActionId, PropertyValue.FromActionId(actionId));

    public static VirtualNodeProperty Width(double value) =>
        new(VirtualPropertyKey.Width, PropertyValue.FromNumber(value));

    public static VirtualNodeProperty Height(double value) =>
        new(VirtualPropertyKey.Height, PropertyValue.FromNumber(value));

    public static VirtualNodeProperty ScrollY(double value) =>
        new(VirtualPropertyKey.ScrollY, PropertyValue.FromNumber(value));

    public static VirtualNodeProperty Hovered(bool value) =>
        new(VirtualPropertyKey.IsHovered, PropertyValue.FromBoolean(value));

    public static VirtualNodeProperty Pressed(bool value) =>
        new(VirtualPropertyKey.IsPressed, PropertyValue.FromBoolean(value));

    public static VirtualNodeProperty Focused(bool value) =>
        new(VirtualPropertyKey.IsFocused, PropertyValue.FromBoolean(value));

    internal static VirtualNodeProperty BackgroundColor(StyleColor value) =>
        new(VirtualPropertyKey.BackgroundColor, PropertyValue.FromColor(value));

    internal static VirtualNodeProperty ForegroundColor(StyleColor value) =>
        new(VirtualPropertyKey.ForegroundColor, PropertyValue.FromColor(value));

    internal static VirtualNodeProperty LayerOpacity(double value) =>
        new(VirtualPropertyKey.LayerOpacity, PropertyValue.FromNumber(value));

    internal static VirtualNodeProperty TranslateX(double value) =>
        new(VirtualPropertyKey.TranslateX, PropertyValue.FromNumber(value));

    internal static VirtualNodeProperty TranslateY(double value) =>
        new(VirtualPropertyKey.TranslateY, PropertyValue.FromNumber(value));

    private static void Validate(VirtualPropertyKey key, PropertyValue value)
    {
        var metadata = VirtualPropertyMetadata.Get(key);
        if (value.Kind != metadata.ValueKind)
        {
            throw new ArgumentException(
                $"Property {VirtualPropertyDiagnostics.Format(key)} expects {metadata.ValueKind} but got {value.Kind}.",
                nameof(value));
        }
    }

    public bool Equals(VirtualNodeProperty other) => Key == other.Key && Value == other.Value;

    public override bool Equals(object? obj) => obj is VirtualNodeProperty other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, Value);

    public static bool operator ==(VirtualNodeProperty left, VirtualNodeProperty right) => left.Equals(right);

    public static bool operator !=(VirtualNodeProperty left, VirtualNodeProperty right) => !left.Equals(right);
}

// ── VirtualNodePatch ─────────────────────────────────────────────

public readonly struct VirtualNodePatch(VirtualNodePatchOperation operation, int nodeIndex, VirtualNode node, int screenId = 0)
{
    public VirtualNodePatchOperation Operation { get; } = operation;
    public int NodeIndex { get; } = nodeIndex;
    public VirtualNode Node { get; } = node;
    public int ScreenId { get; } = screenId;

    public VirtualNodePatch WithScreenId(int screenId) => new(Operation, NodeIndex, Node, screenId);

}

// ── VirtualNodeFactory (R13-6: Text accepts TextNodeContent, R13-19: NodeKey) ──

public static class VirtualNodeFactory
{
    public static VirtualNode Create(
        VirtualNodeKind kind,
        NodeKey key,
        NodeContent content,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ReadOnlySpan<VirtualNode> children) =>
        new(kind, key, content, properties, children);

    public static VirtualNode Create(
        VirtualNodeKind kind,
        NodeKey key,
        NodeContent content,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ref VirtualNodeChildrenBuilder children)
    {
        var childArray = children.ToArray();
        var propertyArray = VirtualNodePropertySet.Create(kind, properties);
        return VirtualNode.CreateFromOwnedArraysUnsafe(kind, key, content, propertyArray, childArray);
    }

    public static VirtualNode Text(TextNodeContent content, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Create(VirtualNodeKind.Text, key, NodeContent.FromText(content), properties, ReadOnlySpan<VirtualNode>.Empty);

    public static VirtualNode Rectangle(params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Create(VirtualNodeKind.Rectangle, default, default, properties, ReadOnlySpan<VirtualNode>.Empty);

    public static VirtualNode Rectangle(NodeKey key, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Create(VirtualNodeKind.Rectangle, key, default, properties, ReadOnlySpan<VirtualNode>.Empty);

    public static VirtualNode Button(TextNodeContent label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        if (label.IsNone)
        {
            throw new ArgumentException("Button label must be explicit.", nameof(label));
        }

        var children = new VirtualNodeChildrenBuilder();
        children.Add(Text(label));
        return Create(VirtualNodeKind.Button, key, default, properties, ref children);
    }

    public static VirtualNode ScrollContainer(NodeKey key = default, params scoped ReadOnlySpan<VirtualNode> children) =>
        Create(VirtualNodeKind.ScrollContainer, key, default, ReadOnlySpan<VirtualNodeProperty>.Empty, children);

    public static VirtualNode ScrollContainer(
        NodeKey key,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ReadOnlySpan<VirtualNode> children) =>
        Create(VirtualNodeKind.ScrollContainer, key, default, properties, children);

    public static VirtualNode ScrollContainer(
        NodeKey key,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ref VirtualNodeChildrenBuilder children) =>
        Create(VirtualNodeKind.ScrollContainer, key, default, properties, ref children);
}

// ── PoC authoring helper (R13-6: edge accepts string, writes to arena) ──

public static class VirtualNodeBuilder
{
    public static VirtualNode Text(VirtualTextArena arena, string content, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        var textContent = arena.AddText(content.AsSpan());
        return VirtualNodeFactory.Text(textContent, key, properties);
    }

    public static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(label);
        var labelContent = arena.AddText(label.AsSpan());
        return VirtualNodeFactory.Button(labelContent, key, properties);
    }
}
