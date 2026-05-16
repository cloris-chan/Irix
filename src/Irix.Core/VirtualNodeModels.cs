using System.Runtime.InteropServices;

namespace Irix;

public enum VirtualNodeKind
{
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
    ActionId
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

[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct PropertyValue : IEquatable<PropertyValue>
{
    [FieldOffset(0)] private readonly PropertyValueKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _uintValue;
    [FieldOffset(8)] private readonly ulong _data0;

    private PropertyValue(PropertyValueKind kind, uint uintValue, ulong data0)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _uintValue = uintValue;
        _data0 = data0;
    }

    public PropertyValueKind Kind => _kind;

    public static PropertyValue None => default;

    public static PropertyValue FromNumber(double value) =>
        new(PropertyValueKind.Number, 0, BitConverter.DoubleToUInt64Bits(value));

    public static PropertyValue FromBoolean(bool value) =>
        new(PropertyValueKind.Boolean, value ? 1u : 0u, 0);

    public static PropertyValue FromActionId(ActionId value) =>
        new(PropertyValueKind.ActionId, value.Value, 0);

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

    public double Number => _kind == PropertyValueKind.Number ? BitConverter.UInt64BitsToDouble(_data0) : 0;
    public bool Boolean => _kind == PropertyValueKind.Boolean && _uintValue != 0;
    public ActionId ActionIdValue => _kind == PropertyValueKind.ActionId ? new ActionId(_uintValue) : ActionId.None;

    public bool Equals(PropertyValue other) => _kind == other._kind && _uintValue == other._uintValue && _data0 == other._data0;

    public override bool Equals(object? obj) => obj is PropertyValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, _uintValue, _data0);

    public static bool operator ==(PropertyValue left, PropertyValue right) => left.Equals(right);

    public static bool operator !=(PropertyValue left, PropertyValue right) => !left.Equals(right);
}

// ── VirtualNodeTree / VirtualNode (R13-6: factory key -> NodeKey) ─

public readonly struct VirtualNodeTree : IEquatable<VirtualNodeTree>
{
    public VirtualNodeTree(VirtualNode root, TextBufferSnapshot textSnapshot = default)
    {
        Root = root;
        TextSnapshot = textSnapshot;
    }

    public VirtualNode Root { get; }
    public TextBufferSnapshot TextSnapshot { get; }

    public bool Equals(VirtualNodeTree other) => Root == other.Root && TextSnapshot.Equals(other.TextSnapshot);

    public override bool Equals(object? obj) => obj is VirtualNodeTree other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Root, TextSnapshot);

    public static bool operator ==(VirtualNodeTree left, VirtualNodeTree right) => left.Equals(right);

    public static bool operator !=(VirtualNodeTree left, VirtualNodeTree right) => !left.Equals(right);
}

public readonly struct VirtualNode : IEquatable<VirtualNode>
{
    public VirtualNode(
        VirtualNodeKind kind,
        NodeKey key = default,
        NodeContent content = default,
        VirtualNodeProperty[]? properties = null,
        VirtualNode[]? children = null)
    {
        Kind = kind;
        Key = key;
        Content = content;
        Properties = VirtualNodePropertySet.Create(kind, properties);
        Children = children ?? [];
    }

    public VirtualNodeKind Kind { get; }
    public NodeKey Key { get; }
    public NodeContent Content { get; }
    public VirtualNodeProperty[] Properties { get; }
    public VirtualNode[] Children { get; }

    public bool Equals(VirtualNode other) =>
        Kind == other.Kind
        && Key == other.Key
        && Content == other.Content
        && ReferenceEquals(Properties, other.Properties)
        && ReferenceEquals(Children, other.Children);

    public override bool Equals(object? obj) => obj is VirtualNode other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Key, Content, Properties, Children);

    public static bool operator ==(VirtualNode left, VirtualNode right) => left.Equals(right);

    public static bool operator !=(VirtualNode left, VirtualNode right) => !left.Equals(right);
}

internal static class VirtualNodePropertySet
{
    public static VirtualNodeProperty[] Create(VirtualNodeKind kind, VirtualNodeProperty[]? properties)
    {
        if (properties is null || properties.Length == 0)
        {
            return [];
        }

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

        return properties;
    }
}

// ── VirtualNodeProperty (R13-9: domain-scoped key, R13-18: helpers) ─

public readonly struct VirtualNodeProperty : IEquatable<VirtualNodeProperty>
{
    internal VirtualNodeProperty(VirtualPropertyKey key, PropertyValue value)
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

    public static VirtualNodeProperty Opacity(double value) =>
        new(VirtualPropertyKey.Opacity, PropertyValue.FromNumber(value));

    public static VirtualNodeProperty Hovered(bool value) =>
        new(VirtualPropertyKey.IsHovered, PropertyValue.FromBoolean(value));

    public static VirtualNodeProperty Pressed(bool value) =>
        new(VirtualPropertyKey.IsPressed, PropertyValue.FromBoolean(value));

    public static VirtualNodeProperty Focused(bool value) =>
        new(VirtualPropertyKey.IsFocused, PropertyValue.FromBoolean(value));

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

public readonly struct VirtualNodePatch : IEquatable<VirtualNodePatch>
{
    public VirtualNodePatch(VirtualNodePatchOperation operation, int nodeIndex, VirtualNode node, int screenId = 0)
    {
        Operation = operation;
        NodeIndex = nodeIndex;
        Node = node;
        ScreenId = screenId;
    }

    public VirtualNodePatchOperation Operation { get; }
    public int NodeIndex { get; }
    public VirtualNode Node { get; }
    public int ScreenId { get; }

    public VirtualNodePatch WithScreenId(int screenId) => new(Operation, NodeIndex, Node, screenId);

    public bool Equals(VirtualNodePatch other) =>
        Operation == other.Operation
        && NodeIndex == other.NodeIndex
        && Node == other.Node
        && ScreenId == other.ScreenId;

    public override bool Equals(object? obj) => obj is VirtualNodePatch other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Operation, NodeIndex, Node, ScreenId);

    public static bool operator ==(VirtualNodePatch left, VirtualNodePatch right) => left.Equals(right);

    public static bool operator !=(VirtualNodePatch left, VirtualNodePatch right) => !left.Equals(right);
}

// ── VirtualNodeFactory (R13-6: Text accepts TextNodeContent, R13-19: NodeKey) ──

public static class VirtualNodeFactory
{
    public static VirtualNode Text(TextNodeContent content, NodeKey key = default, params VirtualNodeProperty[] properties) =>
        new(VirtualNodeKind.Text, key, NodeContent.FromText(content), properties);

    public static VirtualNode Rectangle(double width, double height, NodeKey key = default, params VirtualNodeProperty[] properties) =>
        new(
            VirtualNodeKind.Rectangle,
            key,
            properties:
            [
                .. properties,
                VirtualNodeProperty.Width(width),
                VirtualNodeProperty.Height(height)
            ]);

    public static VirtualNode Button(TextNodeContent label, NodeKey key = default, params VirtualNodeProperty[] properties) =>
        new(VirtualNodeKind.Button, key, properties: properties, children: [Text(label)]);

    public static VirtualNode ScrollContainer(NodeKey key = default, params VirtualNode[] children) =>
        new(VirtualNodeKind.ScrollContainer, key, children: children);
}

// ── PoC authoring helper (R13-6: edge accepts string, writes to arena) ──

public static class VirtualNodeBuilder
{
    public static VirtualNode Text(VirtualTextArena arena, string content, NodeKey key = default, params VirtualNodeProperty[] properties)
    {
        var textContent = arena.AddText(content.AsSpan());
        return VirtualNodeFactory.Text(textContent, key, properties);
    }

    public static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params VirtualNodeProperty[] properties)
    {
        var labelContent = arena.AddText(label.AsSpan());
        return VirtualNodeFactory.Button(labelContent, key, properties);
    }
}
