namespace Irix.Rendering;

internal readonly ref struct PropertyReader
{
    private readonly ReadOnlySpan<VirtualNodeProperty> _properties;

    public PropertyReader(ReadOnlySpan<VirtualNodeProperty> properties)
    {
        _properties = properties;
    }

    public double GetNumber(VirtualPropertyKey key, double defaultValue = 0)
    {
        foreach (var property in _properties)
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
        foreach (var property in _properties)
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
        foreach (var property in _properties)
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
        foreach (var property in _properties)
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
        foreach (var property in _properties)
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
        foreach (var property in _properties)
        {
            if (property.Key == key && property.Value.TryGetBorderStroke(out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}
