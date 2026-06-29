namespace Irix.Rendering;

internal readonly ref struct PropertyReader
{
    private readonly ReadOnlySpan<VirtualNodeProperty> _properties;
    private readonly VirtualNodePropertyList _propertyList;
    private readonly bool _usesPropertyList;

    public PropertyReader(ReadOnlySpan<VirtualNodeProperty> properties)
    {
        _properties = properties;
        _propertyList = default;
        _usesPropertyList = false;
    }

    public PropertyReader(VirtualNodePropertyList properties)
    {
        _properties = default;
        _propertyList = properties;
        _usesPropertyList = true;
    }

    public double GetNumber(VirtualPropertyKey key, double defaultValue = 0)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetNumber(out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    public bool GetBool(VirtualPropertyKey key, bool defaultValue = false)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetBoolean(out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    public ActionId GetActionId(VirtualPropertyKey key)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetActionId(out var value))
            {
                return value;
            }
        }

        return ActionId.None;
    }

    public bool TryGetColor(VirtualPropertyKey key, out StyleColor value)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetColor(out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TryGetPaint(VirtualPropertyKey key, out Paint value)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetPaint(out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public bool TryGetBorderStroke(VirtualPropertyKey key, out BorderStroke value)
    {
        foreach (var property in this)
        {
            if (property.Key == key && property.Value.TryGetBorderStroke(out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    public Enumerator GetEnumerator() => new(_properties, _propertyList, _usesPropertyList);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<VirtualNodeProperty> _properties;
        private readonly VirtualNodePropertyList _propertyList;
        private readonly bool _usesPropertyList;
        private int _index;

        internal Enumerator(ReadOnlySpan<VirtualNodeProperty> properties, VirtualNodePropertyList propertyList, bool usesPropertyList)
        {
            _properties = properties;
            _propertyList = propertyList;
            _usesPropertyList = usesPropertyList;
            _index = -1;
        }

        public readonly VirtualNodeProperty Current => _usesPropertyList ? _propertyList[_index] : _properties[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            var count = _usesPropertyList ? _propertyList.Count : _properties.Length;
            if ((uint)next >= (uint)count)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}
