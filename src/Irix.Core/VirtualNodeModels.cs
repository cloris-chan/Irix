using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Irix;

internal enum VirtualNodeKind
{
    None,
    Container,
    Content
}

internal enum VirtualNodePatchOperation
{
    ReplaceRoot,
    Add,
    Remove,
    Update,
    Move
}

internal enum PropertyValueKind : byte
{
    None,
    Number,
    Boolean,
    ActionId,
    Color,
    Paint,
    BorderStroke
}

internal enum ContentResourceKind : byte
{
    None,
    Text,
    Rectangle
}

// ── InvalidationKind (R13-17) ────────────────────────────────────

internal enum InvalidationKind : byte
{
    None,
    CompositeOnly,
    VisualOnly,
    TextMeasure,
    Layout,
    TreeStructure,
    ViewportChanged,
}

// ── ContentResource: 24-byte pure value union ───────────────────

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal readonly struct ContentResource : IEquatable<ContentResource>
{
    [FieldOffset(0)] private readonly ContentResourceKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _padding2;
    [FieldOffset(8)] private readonly ulong _data0;
    [FieldOffset(16)] private readonly ulong _data1;

    private ContentResource(ContentResourceKind kind, ulong data0, ulong data1)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _padding2 = 0;
        _data0 = data0;
        _data1 = data1;
    }

    public ContentResourceKind Kind => _kind;

    public static ContentResource None => default;

    public static ContentResource FromText(TextContentResource textContent) =>
        new(ContentResourceKind.Text, textContent.BufferId.Value, (ulong)(uint)textContent.Range.Start | ((ulong)(uint)textContent.Range.Length << 32));

    public static ContentResource Rectangle => new(ContentResourceKind.Rectangle, 0, 0);

    public bool TryGetText(out TextContentResource textContent)
    {
        if (_kind != ContentResourceKind.Text) { textContent = default; return false; }
        textContent = new TextContentResource(new TextBufferId((uint)_data0), new TextRange((int)(_data1 & 0xFFFFFFFF), (int)(_data1 >> 32)));
        return true;
    }

    public bool Equals(ContentResource other) => _kind == other._kind && _data0 == other._data0 && _data1 == other._data1;

    public override bool Equals(object? obj) => obj is ContentResource other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((byte)_kind, _data0, _data1);

    public static bool operator ==(ContentResource left, ContentResource right) => left.Equals(right);

    public static bool operator !=(ContentResource left, ContentResource right) => !left.Equals(right);
}

// ── PropertyValue: pure value union (R13-7) ─────────────────────

[StructLayout(LayoutKind.Explicit, Size = 44)]
internal readonly struct PropertyValue : IEquatable<PropertyValue>
{
    [FieldOffset(0)] private readonly PropertyValueKind _kind;
    [FieldOffset(1)] private readonly byte _padding0;
    [FieldOffset(2)] private readonly ushort _padding1;
    [FieldOffset(4)] private readonly uint _uintValue;
    [FieldOffset(8)] private readonly ulong _data0;
    [FieldOffset(8)] private readonly Color _colorValue;
    [FieldOffset(4)] private readonly Paint _paintValue;
    [FieldOffset(4)] private readonly BorderStroke _borderStrokeValue;

    private PropertyValue(PropertyValueKind kind, uint uintValue, ulong data0)
    {
        _kind = kind;
        _padding0 = 0;
        _padding1 = 0;
        _borderStrokeValue = default;
        _paintValue = default;
        _colorValue = default;
        _uintValue = uintValue;
        _data0 = data0;
    }

    private PropertyValue(Color color)
    {
        _kind = PropertyValueKind.Color;
        _padding0 = 0;
        _padding1 = 0;
        _borderStrokeValue = default;
        _paintValue = default;
        _uintValue = 0;
        _data0 = 0;
        _colorValue = color;
    }

    private PropertyValue(Paint paint)
    {
        _kind = PropertyValueKind.Paint;
        _padding0 = 0;
        _padding1 = 0;
        _borderStrokeValue = default;
        _uintValue = 0;
        _data0 = 0;
        _colorValue = default;
        _paintValue = paint;
    }

    private PropertyValue(BorderStroke borderStroke)
    {
        _kind = PropertyValueKind.BorderStroke;
        _padding0 = 0;
        _padding1 = 0;
        _uintValue = 0;
        _data0 = 0;
        _colorValue = default;
        _paintValue = default;
        _borderStrokeValue = borderStroke;
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

    internal static PropertyValue FromPaint(Paint value)
    {
        if (value.Kind == PaintKind.None)
        {
            throw new ArgumentException("Paint value must be explicit.", nameof(value));
        }

        return new PropertyValue(value);
    }

    internal static PropertyValue FromBorderStroke(BorderStroke value)
    {
        if (value.IsNone)
        {
            throw new ArgumentException("Border stroke must be explicit.", nameof(value));
        }

        return new PropertyValue(value);
    }

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

    public bool TryGetPaint(out Paint value)
    {
        if (_kind != PropertyValueKind.Paint) { value = default; return false; }
        value = _paintValue;
        return true;
    }

    public bool TryGetBorderStroke(out BorderStroke value)
    {
        if (_kind != PropertyValueKind.BorderStroke) { value = default; return false; }
        value = _borderStrokeValue;
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

    public Paint GetRequiredPaint()
    {
        if (TryGetPaint(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.Paint}.");
    }

    public BorderStroke GetRequiredBorderStroke()
    {
        if (TryGetBorderStroke(out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Property value is {_kind}, not {PropertyValueKind.BorderStroke}.");
    }

    public bool Equals(PropertyValue other) =>
        _kind == other._kind
        && _kind switch
        {
            PropertyValueKind.Number => _data0 == other._data0,
            PropertyValueKind.Boolean or PropertyValueKind.ActionId => _uintValue == other._uintValue,
            PropertyValueKind.Color => _colorValue == other._colorValue,
            PropertyValueKind.Paint => _paintValue == other._paintValue,
            PropertyValueKind.BorderStroke => _borderStrokeValue == other._borderStrokeValue,
            _ => true
        };

    public override bool Equals(object? obj) => obj is PropertyValue other && Equals(other);

    public override int GetHashCode() =>
        _kind switch
        {
            PropertyValueKind.Number => HashCode.Combine((byte)_kind, _data0),
            PropertyValueKind.Boolean or PropertyValueKind.ActionId => HashCode.Combine((byte)_kind, _uintValue),
            PropertyValueKind.Color => HashCode.Combine((byte)_kind, _colorValue),
            PropertyValueKind.Paint => HashCode.Combine((byte)_kind, _paintValue),
            PropertyValueKind.BorderStroke => HashCode.Combine((byte)_kind, _borderStrokeValue),
            _ => 0
        };

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

internal readonly struct PaintSlot(Paint Value, bool HasValue) : IEquatable<PaintSlot>
{
    public Paint Value { get; } = Value;
    public bool HasValue { get; } = HasValue;

    public static PaintSlot None => default;

    public static PaintSlot Some(Paint value) => new(value, true);

    public bool Equals(PaintSlot other) => Value == other.Value && HasValue == other.HasValue;

    public override bool Equals(object? obj) => obj is PaintSlot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasValue);

    public static bool operator ==(PaintSlot left, PaintSlot right) => left.Equals(right);

    public static bool operator !=(PaintSlot left, PaintSlot right) => !left.Equals(right);
}

internal readonly struct BorderStrokeSlot(BorderStroke Value, bool HasValue) : IEquatable<BorderStrokeSlot>
{
    public BorderStroke Value { get; } = Value;
    public bool HasValue { get; } = HasValue;

    public static BorderStrokeSlot None => default;

    public static BorderStrokeSlot Some(BorderStroke value) => new(value, true);

    public bool Equals(BorderStrokeSlot other) => Value == other.Value && HasValue == other.HasValue;

    public override bool Equals(object? obj) => obj is BorderStrokeSlot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Value, HasValue);

    public static bool operator ==(BorderStrokeSlot left, BorderStrokeSlot right) => left.Equals(right);

    public static bool operator !=(BorderStrokeSlot left, BorderStrokeSlot right) => !left.Equals(right);
}

// ── VirtualNodeTree / VirtualNode (R13-6: factory key -> NodeKey) ─

internal readonly struct VirtualNodeTree(VirtualNode root, TextBufferSnapshot textSnapshot = default)
{
    public VirtualNode Root { get; } = root;
    public TextBufferSnapshot TextSnapshot { get; } = textSnapshot;

    internal VirtualNodeTreeReader CreateReader() => new(this);
}

internal ref struct VirtualNodeTreePublicationBuilder
{
    private VirtualNode[]? _children;
    private int _written;

    public VirtualNodeTreePublicationBuilder(int childCapacity)
    {
        if (childCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(childCapacity));
        }

        _children = childCapacity == 0 ? null : new VirtualNode[childCapacity];
        _written = 0;
    }

    public readonly int WrittenChildCount => _written;

    public readonly int ChildCapacity => _children?.Length ?? 0;

    public Span<VirtualNode> ReserveChildRange(int count, out int start)
    {
        start = Reserve(count);
        return count == 0 ? Span<VirtualNode>.Empty : _children!.AsSpan(start, count);
    }

    public VirtualNodeChildList PublishReservedChildren(int start, int count)
    {
        if (start < 0 || count < 0 || start > _written || count > _written - start)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (count == 0)
        {
            return VirtualNodeChildList.Empty;
        }

        var children = _children ?? throw new InvalidOperationException("No child publication storage is available.");
        return VirtualNodeChildList.FromOwnedArrayRange(children, start, count);
    }

    public VirtualNodeChildList PublishChildren(VirtualNode first, VirtualNode second)
    {
        var start = Reserve(2);
        var children = _children!;
        children[start] = first;
        children[start + 1] = second;
        return VirtualNodeChildList.FromOwnedArrayRange(children, start, 2);
    }

    public VirtualNodeChildList PublishChildren(scoped ReadOnlySpan<VirtualNode> children)
    {
        if (children.IsEmpty)
        {
            return VirtualNodeChildList.Empty;
        }

        var start = Reserve(children.Length);
        var target = _children!;
        children.CopyTo(target.AsSpan(start, children.Length));
        return VirtualNodeChildList.FromOwnedArrayRange(target, start, children.Length);
    }

    private int Reserve(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var start = _written;
        var required = checked(start + count);
        EnsureCapacity(required);
        _written = required;
        return start;
    }

    private void EnsureCapacity(int required)
    {
        var children = _children;
        if (children is not null && required <= children.Length)
        {
            return;
        }

        var nextCapacity = children is null ? 4 : children.Length * 2;
        while (nextCapacity < required)
        {
            nextCapacity *= 2;
        }

        Array.Resize(ref _children, nextCapacity);
    }
}

internal readonly ref struct VirtualNodeTreeReader
{
    private readonly VirtualNode _root;

    public VirtualNodeTreeReader(VirtualNodeTree tree)
    {
        _root = tree.Root;
        TextSnapshot = tree.TextSnapshot;
    }

    public TextBufferSnapshot TextSnapshot { get; }

    public VirtualNodeReader Root => new(_root, 0);

    public bool IsDefault => Root.IsDefault;
}

internal readonly ref struct VirtualNodeReader
{
    private readonly VirtualNode _node;

    public VirtualNodeReader(VirtualNode node, int dfsIndex)
    {
        _node = node;
        DfsIndex = dfsIndex;
    }

    public int DfsIndex { get; }
    public VirtualNode Node => _node;
    public VirtualNodeKind Kind => _node.Kind;
    public NodeKey Key => _node.Key;
    public ContentResource Content => _node.Content;
    public VirtualNodePropertyList Properties => _node.Properties;
    public VirtualNodeChildList Children => _node.Children;
    public int ChildCount => _node.Children.Length;

    public bool IsDefault =>
        Kind == VirtualNodeKind.None
        && Key == NodeKey.None
        && Content == ContentResource.None
        && Properties.Length == 0
        && ChildCount == 0;

    public VirtualNodeReader GetChild(int childIndex, int dfsIndex)
    {
        var children = _node.Children;
        if ((uint)childIndex >= (uint)children.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(childIndex));
        }

        return new VirtualNodeReader(children[childIndex], dfsIndex);
    }

    public NodeKey GetChildKey(int childIndex)
    {
        var children = _node.Children;
        if ((uint)childIndex >= (uint)children.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(childIndex));
        }

        return children[childIndex].Key;
    }

    public int CountSubtreeNodes()
    {
        var count = 1;
        var children = _node.Children;
        for (var i = 0; i < children.Length; i++)
        {
            count += new VirtualNodeReader(children[i], 0).CountSubtreeNodes();
        }

        return count;
    }
}

internal readonly struct VirtualNode
{
    private readonly VirtualNodePropertyList _properties;
    private readonly VirtualNodeChildList _children;

    public VirtualNode(
        VirtualNodeKind kind,
        NodeKey key = default,
        ContentResource content = default,
        scoped ReadOnlySpan<VirtualNodeProperty> properties = default,
        scoped ReadOnlySpan<VirtualNode> children = default)
        : this(kind, key, content, VirtualNodePropertySet.Create(kind, properties), VirtualNodeChildList.CopyFrom(children))
    {
    }

    internal VirtualNode(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodePropertyList properties,
        scoped ReadOnlySpan<VirtualNode> children)
        : this(kind, key, content, properties, VirtualNodeChildList.CopyFrom(children))
    {
    }

    private VirtualNode(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodePropertyList properties,
        VirtualNodeChildList children)
    {
        Kind = kind;
        Key = key;
        Content = content;
        _properties = properties;
        _children = children;
        ValidateNodeShape(kind, key, content, properties, children);
    }

    public VirtualNodeKind Kind { get; }
    public NodeKey Key { get; }
    public ContentResource Content { get; }
    public VirtualNodePropertyList Properties => _properties;
    public VirtualNodeChildList Children => _children;

    /// <summary>
    /// Takes ownership of already validated/frozen arrays without copying large retained publications again.
    /// Callers must not mutate the arrays after this call.
    /// </summary>
    internal static VirtualNode CreateFromOwnedArraysUnsafe(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodeProperty[] properties,
        VirtualNode[] children) =>
        new(kind, key, content, VirtualNodePropertyList.FromOwnedArray(kind, properties), VirtualNodeChildList.FromOwnedArray(children));

    /// <summary>
    /// Takes ownership of already frozen child storage while publishing properties through the typed value list.
    /// Callers must not mutate the child array after this call.
    /// </summary>
    internal static VirtualNode CreateFromOwnedChildrenUnsafe(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        VirtualNode[] children) =>
        new(kind, key, content, VirtualNodePropertySet.Create(kind, properties), VirtualNodeChildList.FromOwnedArray(children));

    /// <summary>
    /// Takes ownership of already frozen child storage while retaining an existing property publication.
    /// Callers must not mutate the child array after this call.
    /// </summary>
    internal static VirtualNode CreateFromOwnedChildrenUnsafe(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodePropertyList properties,
        VirtualNode[] children) =>
        new(kind, key, content, properties, VirtualNodeChildList.FromOwnedArray(children));

    /// <summary>
    /// Takes ownership of already frozen child storage while retaining existing property and child publications.
    /// </summary>
    internal static VirtualNode CreateFromOwnedChildrenUnsafe(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodePropertyList properties,
        VirtualNodeChildList children) =>
        new(kind, key, content, properties, children);

    internal static VirtualNodeChildList CreateOwnedChildren(scoped ReadOnlySpan<VirtualNode> children) =>
        VirtualNodeChildList.CopyFrom(children);

    private static void ValidateNodeShape(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        VirtualNodePropertyList properties,
        VirtualNodeChildList children)
    {
        switch (kind)
        {
            case VirtualNodeKind.None:
                if (key != NodeKey.None
                    || content != ContentResource.None
                    || properties.Count != 0
                    || children.Length != 0)
                {
                    throw new ArgumentException("None nodes must be empty.");
                }

                return;

            case VirtualNodeKind.Container:
                if (content != ContentResource.None)
                {
                    throw new ArgumentException("Container nodes cannot have content.", nameof(content));
                }

                return;

            case VirtualNodeKind.Content:
                if (children.Length != 0)
                {
                    throw new ArgumentException("Content nodes cannot have children.", nameof(children));
                }

                if (content.Kind == ContentResourceKind.None)
                {
                    throw new ArgumentException("Content nodes require one content resource.", nameof(content));
                }

                return;

            default:
                return;
        }
    }

}

internal readonly struct VirtualNodeChildList : IEquatable<VirtualNodeChildList>
{
    private readonly VirtualNode[]? _items;
    private readonly int _start;
    private readonly int _count;

    private VirtualNodeChildList(VirtualNode[] items)
        : this(items, 0, items.Length)
    {
    }

    private VirtualNodeChildList(VirtualNode[] items, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (start < 0 || count < 0 || start + count > items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _items = count == 0 ? null : items;
        _start = start;
        _count = count;
    }

    public int Count => _items is null ? 0 : _count;

    public int Length => Count;

    public bool IsEmpty => Count == 0;

    public VirtualNode this[int index]
    {
        get
        {
            var items = _items;
            if (items is null || (uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return items[_start + index];
        }
    }

    public static VirtualNodeChildList Empty => default;

    internal static VirtualNodeChildList FromOwnedArray(VirtualNode[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return children.Length == 0 ? Empty : new VirtualNodeChildList(children);
    }

    internal static VirtualNodeChildList FromOwnedArrayRange(VirtualNode[] children, int start, int count) =>
        count == 0 ? Empty : new VirtualNodeChildList(children, start, count);

    public static VirtualNodeChildList CopyFrom(scoped ReadOnlySpan<VirtualNode> children) =>
        children.IsEmpty ? Empty : new VirtualNodeChildList(children.ToArray());

    public ReadOnlySpan<VirtualNode> AsSpan() =>
        _items is null ? ReadOnlySpan<VirtualNode>.Empty : _items.AsSpan(_start, _count);

    public VirtualNode[] ToArray()
    {
        if (Count == 0)
        {
            return [];
        }

        var result = new VirtualNode[Count];
        CopyTo(result);
        return result;
    }

    public void CopyTo(Span<VirtualNode> destination)
    {
        var count = Count;
        if (destination.Length < count)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        AsSpan().CopyTo(destination);
    }

    public Enumerator GetEnumerator() => new(this);

    public bool Equals(VirtualNodeChildList other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (!NodesEqual(this[i], other[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NodesEqual(VirtualNode left, VirtualNode right) =>
        left.Kind == right.Kind
        && left.Key == right.Key
        && left.Content == right.Content
        && left.Properties == right.Properties
        && left.Children == right.Children;

    public override bool Equals(object? obj) => obj is VirtualNodeChildList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < Count; i++)
        {
            AddNodeHash(ref hash, this[i]);
        }

        return hash.ToHashCode();
    }

    private static void AddNodeHash(ref HashCode hash, VirtualNode node)
    {
        hash.Add(node.Kind);
        hash.Add(node.Key);
        hash.Add(node.Content);
        hash.Add(node.Properties);
        hash.Add(node.Children);
    }

    public static bool operator ==(VirtualNodeChildList left, VirtualNodeChildList right) => left.Equals(right);

    public static bool operator !=(VirtualNodeChildList left, VirtualNodeChildList right) => !left.Equals(right);

    public struct Enumerator
    {
        private readonly VirtualNodeChildList _children;
        private int _index;

        internal Enumerator(VirtualNodeChildList children)
        {
            _children = children;
            _index = -1;
        }

        public readonly VirtualNode Current => _children[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_children.Count)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}

internal readonly struct VirtualNodePropertyList : IReadOnlyList<VirtualNodeProperty>, IEquatable<VirtualNodePropertyList>
{
    private readonly VirtualNodeProperty[]? _items;
    private readonly uint _actionIdValue;
    private readonly byte _compactKind;
    private readonly byte _controlStateBits;

    private const byte CompactKindNone = 0;
    private const byte CompactKindAction = 1;
    private const byte CompactKindControlBundle = 2;
    private const byte HoveredBit = 1;
    private const byte PressedBit = 2;
    private const byte FocusedBit = 4;

    private VirtualNodePropertyList(
        VirtualNodeProperty[]? items,
        uint actionIdValue,
        byte compactKind,
        byte controlStateBits)
    {
        _items = items;
        _actionIdValue = actionIdValue;
        _compactKind = compactKind;
        _controlStateBits = controlStateBits;
    }

    public int Count => _items is not null ? _items.Length : _compactKind == CompactKindAction ? 1 : _compactKind == CompactKindControlBundle ? 4 : 0;

    public int Length => Count;

    public bool IsEmpty => Count == 0;

    public VirtualNodeProperty this[int index]
    {
        get
        {
            if (_items is not null)
            {
                if ((uint)index >= (uint)_items.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }

            if (_compactKind == CompactKindAction)
            {
                if (index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return VirtualNodeProperty.Action(new ActionId(_actionIdValue));
            }

            if (_compactKind == CompactKindControlBundle)
            {
                return index switch
                {
                    0 => VirtualNodeProperty.Action(new ActionId(_actionIdValue)),
                    1 => VirtualNodeProperty.Hovered((_controlStateBits & HoveredBit) != 0),
                    2 => VirtualNodeProperty.Pressed((_controlStateBits & PressedBit) != 0),
                    3 => VirtualNodeProperty.Focused((_controlStateBits & FocusedBit) != 0),
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static VirtualNodePropertyList Empty => default;

    internal static VirtualNodePropertyList FromOwnedArray(VirtualNodeKind kind, VirtualNodeProperty[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        VirtualNodePropertySet.Validate(kind, properties);
        return TryCreateCompact(properties, out var compact)
            ? compact
            : new VirtualNodePropertyList(properties, 0, CompactKindNone, 0);
    }

    public static VirtualNodePropertyList CopyFrom(VirtualNodeKind kind, scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        VirtualNodePropertySet.Validate(kind, properties);
        return CopyValidated(properties);
    }

    private static VirtualNodePropertyList CopyValidated(scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        if (properties.IsEmpty)
        {
            return Empty;
        }

        return TryCreateCompact(properties, out var compact)
            ? compact
            : new VirtualNodePropertyList(properties.ToArray(), 0, CompactKindNone, 0);
    }

    private static bool TryCreateCompact(ReadOnlySpan<VirtualNodeProperty> properties, out VirtualNodePropertyList compact)
    {
        compact = default;
        if (properties.Length == 1
            && properties[0].Key == VirtualPropertyKey.ActionId
            && properties[0].Value.TryGetActionId(out var actionId)
            && !actionId.IsNone)
        {
            compact = new VirtualNodePropertyList(null, actionId.Value, CompactKindAction, 0);
            return true;
        }

        if (properties.Length != 4
            || properties[0].Key != VirtualPropertyKey.ActionId
            || properties[1].Key != VirtualPropertyKey.IsHovered
            || properties[2].Key != VirtualPropertyKey.IsPressed
            || properties[3].Key != VirtualPropertyKey.IsFocused
            || !properties[0].Value.TryGetActionId(out actionId)
            || actionId.IsNone
            || !properties[1].Value.TryGetBoolean(out var isHovered)
            || !properties[2].Value.TryGetBoolean(out var isPressed)
            || !properties[3].Value.TryGetBoolean(out var isFocused))
        {
            return false;
        }

        var stateBits = (byte)((isHovered ? HoveredBit : 0) | (isPressed ? PressedBit : 0) | (isFocused ? FocusedBit : 0));
        compact = new VirtualNodePropertyList(null, actionId.Value, CompactKindControlBundle, stateBits);
        return true;
    }

    public VirtualNodeProperty[] ToArray()
    {
        var count = Count;
        if (count == 0)
        {
            return [];
        }

        var copy = new VirtualNodeProperty[count];
        CopyTo(copy);
        return copy;
    }

    public void CopyTo(Span<VirtualNodeProperty> destination)
    {
        var count = Count;
        if (destination.Length < count)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        if (_items is not null)
        {
            _items.AsSpan(0, _items.Length).CopyTo(destination);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            destination[i] = this[i];
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<VirtualNodeProperty> IEnumerable<VirtualNodeProperty>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(VirtualNodePropertyList other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (this[i] != other[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is VirtualNodePropertyList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < Count; i++)
        {
            hash.Add(this[i]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(VirtualNodePropertyList left, VirtualNodePropertyList right) => left.Equals(right);

    public static bool operator !=(VirtualNodePropertyList left, VirtualNodePropertyList right) => !left.Equals(right);

    public struct Enumerator : IEnumerator<VirtualNodeProperty>
    {
        private readonly VirtualNodePropertyList _properties;
        private int _index;

        internal Enumerator(VirtualNodePropertyList properties)
        {
            _properties = properties;
            _index = -1;
        }

        public readonly VirtualNodeProperty Current => _properties[_index];

        readonly object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_properties.Count)
            {
                return false;
            }

            _index = next;
            return true;
        }

        public void Reset() => _index = -1;

        public readonly void Dispose()
        {
        }
    }
}

internal ref struct VirtualNodePropertyListBuilder
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

    public void AddBackground(Color value) => Add(VirtualNodeProperty.Background(value));

    public void AddBackground(Paint value) => Add(VirtualNodeProperty.Background(value));

    public void AddBorder(BorderStroke value) => Add(VirtualNodeProperty.Border(value));

    public void AddBorder(Color color, float thickness = 1f) => AddBorder(BorderStroke.Solid(color, thickness));

    public void AddBorder(Paint paint, float thickness = 1f) => AddBorder(new BorderStroke(paint, thickness));

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

internal ref struct VirtualNodeChildrenBuilder
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
    public static VirtualNodePropertyList Create(VirtualNodeKind kind, scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        VirtualNodePropertyList.CopyFrom(kind, properties);

    internal static void Validate(VirtualNodeKind kind, scoped ReadOnlySpan<VirtualNodeProperty> properties)
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

internal readonly struct VirtualNodeProperty : IEquatable<VirtualNodeProperty>
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

    public static VirtualNodeProperty Background(Color value) =>
        Background(Paint.Solid(value));

    public static VirtualNodeProperty Background(Paint value)
    {
        if (value.Kind == PaintKind.None)
        {
            throw new ArgumentException("Background paint must be explicit.", nameof(value));
        }

        return new VirtualNodeProperty(VirtualPropertyKey.Background, PropertyValue.FromPaint(value));
    }

    public static VirtualNodeProperty Border(BorderStroke value)
    {
        if (value.IsNone)
        {
            throw new ArgumentException("Border stroke must be explicit.", nameof(value));
        }

        return new VirtualNodeProperty(VirtualPropertyKey.Border, PropertyValue.FromBorderStroke(value));
    }

    public static VirtualNodeProperty Border(Color color, float thickness = 1f) =>
        Border(BorderStroke.Solid(color, thickness));

    public static VirtualNodeProperty Border(Paint paint, float thickness = 1f) =>
        Border(new BorderStroke(paint, thickness));

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

internal readonly struct VirtualNodePatch(VirtualNodePatchOperation operation, int nodeIndex, VirtualNode node, int screenId = 0)
{
    public VirtualNodePatchOperation Operation { get; } = operation;
    public int NodeIndex { get; } = nodeIndex;
    public VirtualNode Node { get; } = node;
    public int ScreenId { get; } = screenId;

    public VirtualNodePatch WithScreenId(int screenId) => new(Operation, NodeIndex, Node, screenId);

}

// ── VirtualNodeFactory (R13-6: Text accepts TextContentResource, R13-19: NodeKey) ──

internal static class VirtualNodeFactory
{
    public static VirtualNode Create(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ReadOnlySpan<VirtualNode> children) =>
        new(kind, key, content, properties, children);

    public static VirtualNode Create(
        VirtualNodeKind kind,
        NodeKey key,
        ContentResource content,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ref VirtualNodeChildrenBuilder children)
    {
        var childArray = children.ToArray();
        return VirtualNode.CreateFromOwnedChildrenUnsafe(kind, key, content, properties, childArray);
    }

    public static VirtualNode Container(NodeKey key = default, params scoped ReadOnlySpan<VirtualNode> children) =>
        Create(VirtualNodeKind.Container, key, default, ReadOnlySpan<VirtualNodeProperty>.Empty, children);

    public static VirtualNode Container(
        NodeKey key,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ReadOnlySpan<VirtualNode> children) =>
        Create(VirtualNodeKind.Container, key, default, properties, children);

    public static VirtualNode Container(
        NodeKey key,
        scoped ReadOnlySpan<VirtualNodeProperty> properties,
        scoped ref VirtualNodeChildrenBuilder children) =>
        Create(VirtualNodeKind.Container, key, default, properties, ref children);

    public static VirtualNode Content(
        ContentResource content,
        NodeKey key = default,
        params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Create(VirtualNodeKind.Content, key, content, properties, ReadOnlySpan<VirtualNode>.Empty);

    public static VirtualNode Text(TextContentResource content, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Content(ContentResource.FromText(content), key, properties);

    public static VirtualNode Rectangle(params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Content(ContentResource.Rectangle, default, properties);

    public static VirtualNode Rectangle(NodeKey key, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        Content(ContentResource.Rectangle, key, properties);
}

internal static class VirtualNodeBuilder
{
    public static VirtualNode Text(VirtualTextArena arena, string content, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        var textContent = arena.AddText(content.AsSpan());
        return VirtualNodeFactory.Text(textContent, key, properties);
    }
}
