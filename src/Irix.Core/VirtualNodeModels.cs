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

public enum AttributeValueKind : byte
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

// ── AttributeValue: pure value union (R13-7) ─────────────────────

[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct AttributeValue : IEquatable<AttributeValue>
{
    [FieldOffset(0)] private readonly AttributeValueKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _uintValue;
    [FieldOffset(8)] private readonly ulong _data0;

    private AttributeValue(AttributeValueKind kind, uint uintValue, ulong data0)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _uintValue = uintValue;
        _data0 = data0;
    }

    public AttributeValueKind Kind => _kind;

    public static AttributeValue None => default;

    public static AttributeValue FromNumber(double value) =>
        new(AttributeValueKind.Number, 0, BitConverter.DoubleToUInt64Bits(value));

    public static AttributeValue FromBoolean(bool value) =>
        new(AttributeValueKind.Boolean, value ? 1u : 0u, 0);

    public static AttributeValue FromActionId(ActionId value) =>
        new(AttributeValueKind.ActionId, value.Value, 0);

    public bool TryGetNumber(out double value)
    {
        if (_kind != AttributeValueKind.Number) { value = 0; return false; }
        value = BitConverter.UInt64BitsToDouble(_data0);
        return true;
    }

    public bool TryGetBoolean(out bool value)
    {
        if (_kind != AttributeValueKind.Boolean) { value = false; return false; }
        value = _uintValue != 0;
        return true;
    }

    public bool TryGetActionId(out ActionId value)
    {
        if (_kind != AttributeValueKind.ActionId) { value = ActionId.None; return false; }
        value = new ActionId(_uintValue);
        return true;
    }

    public double Number => _kind == AttributeValueKind.Number ? BitConverter.UInt64BitsToDouble(_data0) : 0;
    public bool Boolean => _kind == AttributeValueKind.Boolean && _uintValue != 0;
    public ActionId ActionIdValue => _kind == AttributeValueKind.ActionId ? new ActionId(_uintValue) : ActionId.None;

    public bool Equals(AttributeValue other) => _kind == other._kind && _uintValue == other._uintValue && _data0 == other._data0;

    public override bool Equals(object? obj) => obj is AttributeValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, _uintValue, _data0);

    public static bool operator ==(AttributeValue left, AttributeValue right) => left.Equals(right);

    public static bool operator !=(AttributeValue left, AttributeValue right) => !left.Equals(right);
}

// ── VirtualNodeTree / VirtualNode (R13-6: factory key → NodeKey) ─

public readonly record struct VirtualNodeTree(VirtualNode Root, TextBufferSnapshot TextSnapshot = default);

public readonly record struct VirtualNode
{
    public VirtualNode(
        VirtualNodeKind kind,
        NodeKey key = default,
        NodeContent content = default,
        VirtualNodeAttribute[]? attributes = null,
        VirtualNode[]? children = null)
    {
        Kind = kind;
        Key = key;
        Content = content;
        Attributes = attributes ?? [];
        Children = children ?? [];
    }

    public VirtualNodeKind Kind { get; }
    public NodeKey Key { get; }
    public NodeContent Content { get; }
    public VirtualNodeAttribute[] Attributes { get; }
    public VirtualNode[] Children { get; }
}

// ── VirtualNodeAttribute (R13-9: domain-scoped key, R13-18: helpers) ─

public readonly record struct VirtualNodeAttribute
{
    internal VirtualNodeAttribute(VirtualAttributeKey Key, AttributeValue Value)
    {
        this.Key = Key;
        this.Value = Value;
    }

    public VirtualAttributeKey Key { get; }
    public AttributeValue Value { get; }

    public static VirtualNodeAttribute Action(ActionId actionId) =>
        new(VirtualAttributeKey.ActionId, AttributeValue.FromActionId(actionId));

    public static VirtualNodeAttribute LayoutWidth(double value) =>
        new(VirtualAttributeKey.Width, AttributeValue.FromNumber(value));

    public static VirtualNodeAttribute LayoutHeight(double value) =>
        new(VirtualAttributeKey.Height, AttributeValue.FromNumber(value));

    public static VirtualNodeAttribute StateHovered(bool value) =>
        new(VirtualAttributeKey.IsHovered, AttributeValue.FromBoolean(value));

    public static VirtualNodeAttribute StatePressed(bool value) =>
        new(VirtualAttributeKey.IsPressed, AttributeValue.FromBoolean(value));

    public static VirtualNodeAttribute StateFocused(bool value) =>
        new(VirtualAttributeKey.IsFocused, AttributeValue.FromBoolean(value));

    public static VirtualNodeAttribute ScrollY(double value) =>
        new(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(value));
}

// ── VirtualNodePatch ─────────────────────────────────────────────

public readonly record struct VirtualNodePatch(
    VirtualNodePatchOperation Operation,
    int NodeIndex,
    VirtualNode Node,
    int ScreenId = 0);

// ── VirtualNodeFactory (R13-6: Text accepts TextNodeContent, R13-19: NodeKey) ──

public static class VirtualNodeFactory
{
    public static VirtualNode Text(TextNodeContent content, NodeKey key = default, params VirtualNodeAttribute[] attributes) =>
        new(VirtualNodeKind.Text, key, NodeContent.FromText(content), attributes);

    public static VirtualNode Rectangle(double width, double height, NodeKey key = default, params VirtualNodeAttribute[] attributes) =>
        new(
            VirtualNodeKind.Rectangle,
            key,
            attributes:
            [
                .. attributes,
                VirtualNodeAttribute.LayoutWidth(width),
                VirtualNodeAttribute.LayoutHeight(height)
            ]);

    public static VirtualNode Button(NodeKey key = default, params VirtualNodeAttribute[] attributes) =>
        new(VirtualNodeKind.Button, key, attributes: attributes);

    public static VirtualNode ScrollContainer(NodeKey key = default, params VirtualNode[] children) =>
        new(VirtualNodeKind.ScrollContainer, key, children: children);
}

// ── PoC authoring helper (R13-6: edge accepts string, writes to arena) ──

public static class VirtualNodeBuilder
{
    public static VirtualNode Text(VirtualTextArena arena, string content, NodeKey key = default, params VirtualNodeAttribute[] attributes)
    {
        var textContent = arena.AddText(content.AsSpan());
        return VirtualNodeFactory.Text(textContent, key, attributes);
    }

    public static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params VirtualNodeAttribute[] attributes)
    {
        var labelContent = arena.AddText(label.AsSpan());
        return new VirtualNode(
            VirtualNodeKind.Button,
            key,
            attributes: attributes,
            children: [VirtualNodeFactory.Text(labelContent)]);
    }
}
