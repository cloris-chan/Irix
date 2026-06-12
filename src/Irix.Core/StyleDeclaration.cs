namespace Irix;

internal enum StylePropertyId : byte
{
    None,
    Width,
    Height,
    Background,
    Foreground,
    Opacity,
    TranslationX,
    TranslationY,
    Hovered,
    Pressed,
    Focused,
}

internal readonly struct StyleValue : IEquatable<StyleValue>
{
    private readonly PropertyValue _value;

    private StyleValue(PropertyValue value)
    {
        _value = value;
    }

    public PropertyValueKind Kind => _value.Kind;

    public static StyleValue None => default;

    public static StyleValue FromNumber(double value) => new(PropertyValue.FromNumber(value));

    public static StyleValue FromBoolean(bool value) => new(PropertyValue.FromBoolean(value));

    public static StyleValue FromColor(StyleColor value) => new(PropertyValue.FromColor(value));

    public static StyleValue FromPaint(Paint value) => new(PropertyValue.FromPaint(value));

    public bool TryGetNumber(out double value) => _value.TryGetNumber(out value);

    public bool TryGetBoolean(out bool value) => _value.TryGetBoolean(out value);

    public bool TryGetColor(out StyleColor value) => _value.TryGetColor(out value);

    public bool TryGetPaint(out Paint value) => _value.TryGetPaint(out value);

    public double GetRequiredNumber() => _value.GetRequiredNumber();

    public bool GetRequiredBoolean() => _value.GetRequiredBoolean();

    public StyleColor GetRequiredColor() => _value.GetRequiredColor();

    public Paint GetRequiredPaint() => _value.GetRequiredPaint();

    internal PropertyValue ToPropertyValue() => _value;

    public bool Equals(StyleValue other) => _value == other._value;

    public override bool Equals(object? obj) => obj is StyleValue other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(StyleValue left, StyleValue right) => left.Equals(right);

    public static bool operator !=(StyleValue left, StyleValue right) => !left.Equals(right);
}

internal readonly struct StyleDeclaration : IEquatable<StyleDeclaration>
{
    private StyleDeclaration(StylePropertyId propertyId, StyleValue value)
    {
        PropertyId = propertyId;
        Value = value;
    }

    public StylePropertyId PropertyId { get; }
    public StyleValue Value { get; }

    public static StyleDeclaration Create(StylePropertyId propertyId, StyleValue value)
    {
        var expectedKind = GetExpectedValueKind(propertyId);
        if (expectedKind == PropertyValueKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyId), propertyId, "Unknown style property.");
        }

        if (value.Kind != expectedKind)
        {
            throw new ArgumentException(
                $"Style property {propertyId} expects {expectedKind} but got {value.Kind}.",
                nameof(value));
        }

        return new StyleDeclaration(propertyId, value);
    }

    public static StyleDeclaration Width(double value) => Create(StylePropertyId.Width, StyleValue.FromNumber(value));

    public static StyleDeclaration Height(double value) => Create(StylePropertyId.Height, StyleValue.FromNumber(value));

    public static StyleDeclaration Background(StyleColor value) => Background(Paint.Solid(value.Value));

    public static StyleDeclaration Background(Paint value) => Create(StylePropertyId.Background, StyleValue.FromPaint(value));

    public static StyleDeclaration Foreground(StyleColor value) => Create(StylePropertyId.Foreground, StyleValue.FromColor(value));

    public static StyleDeclaration Opacity(double value) => Create(StylePropertyId.Opacity, StyleValue.FromNumber(value));

    public static StyleDeclaration TranslationX(double value) => Create(StylePropertyId.TranslationX, StyleValue.FromNumber(value));

    public static StyleDeclaration TranslationY(double value) => Create(StylePropertyId.TranslationY, StyleValue.FromNumber(value));

    public static StyleDeclaration Hovered(bool value) => Create(StylePropertyId.Hovered, StyleValue.FromBoolean(value));

    public static StyleDeclaration Pressed(bool value) => Create(StylePropertyId.Pressed, StyleValue.FromBoolean(value));

    public static StyleDeclaration Focused(bool value) => Create(StylePropertyId.Focused, StyleValue.FromBoolean(value));

    public VirtualNodeProperty ToVirtualNodeProperty() =>
        PropertyId switch
        {
            StylePropertyId.Width => VirtualNodeProperty.Width(Value.GetRequiredNumber()),
            StylePropertyId.Height => VirtualNodeProperty.Height(Value.GetRequiredNumber()),
            StylePropertyId.Background => VirtualNodeProperty.Background(Value.GetRequiredPaint()),
            StylePropertyId.Foreground => VirtualNodeProperty.ForegroundColor(Value.GetRequiredColor()),
            StylePropertyId.Opacity => VirtualNodeProperty.LayerOpacity(Value.GetRequiredNumber()),
            StylePropertyId.TranslationX => VirtualNodeProperty.TranslateX(Value.GetRequiredNumber()),
            StylePropertyId.TranslationY => VirtualNodeProperty.TranslateY(Value.GetRequiredNumber()),
            StylePropertyId.Hovered => VirtualNodeProperty.Hovered(Value.GetRequiredBoolean()),
            StylePropertyId.Pressed => VirtualNodeProperty.Pressed(Value.GetRequiredBoolean()),
            StylePropertyId.Focused => VirtualNodeProperty.Focused(Value.GetRequiredBoolean()),
            _ => throw new InvalidOperationException("Unknown style property."),
        };

    public bool Equals(StyleDeclaration other) => PropertyId == other.PropertyId && Value == other.Value;

    public override bool Equals(object? obj) => obj is StyleDeclaration other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(PropertyId, Value);

    public static bool operator ==(StyleDeclaration left, StyleDeclaration right) => left.Equals(right);

    public static bool operator !=(StyleDeclaration left, StyleDeclaration right) => !left.Equals(right);

    private static PropertyValueKind GetExpectedValueKind(StylePropertyId propertyId) =>
        propertyId switch
        {
            StylePropertyId.Width or StylePropertyId.Height or StylePropertyId.Opacity or StylePropertyId.TranslationX or StylePropertyId.TranslationY => PropertyValueKind.Number,
            StylePropertyId.Background => PropertyValueKind.Paint,
            StylePropertyId.Foreground => PropertyValueKind.Color,
            StylePropertyId.Hovered or StylePropertyId.Pressed or StylePropertyId.Focused => PropertyValueKind.Boolean,
            _ => PropertyValueKind.None,
        };
}

internal static class StyleDeclarationMapper
{
    public static VirtualNodeProperty[] ToVirtualNodeProperties(scoped ReadOnlySpan<StyleDeclaration> declarations)
    {
        if (declarations.IsEmpty)
        {
            return [];
        }

        var properties = new VirtualNodeProperty[declarations.Length];
        for (var i = 0; i < declarations.Length; i++)
        {
            for (var j = i + 1; j < declarations.Length; j++)
            {
                if (declarations[i].PropertyId == declarations[j].PropertyId)
                {
                    throw new ArgumentException(
                        $"Duplicate style property {declarations[i].PropertyId}.",
                        nameof(declarations));
                }
            }

            properties[i] = declarations[i].ToVirtualNodeProperty();
        }

        return properties;
    }
}
