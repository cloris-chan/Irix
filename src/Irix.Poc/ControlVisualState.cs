namespace Irix.Poc;

internal readonly record struct ControlVisualState(
    bool IsHovered,
    bool IsPressed,
    bool IsFocused);

internal static class ControlVisualStateProjection
{
    internal static ControlVisualState Project(OwnershipSnapshot ownership, ActionId targetId) =>
        new(
            IsHovered: ownership.HoveredTarget == targetId,
            IsPressed: ownership.IsPointerPressed && ownership.PressedTarget == targetId,
            IsFocused: ownership.FocusedTarget == targetId);
}

internal static class ControlVisualStatePropertyAdapter
{
    internal static VirtualNodeProperty[] ToProperties(ControlVisualState state) =>
        StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.Hovered(state.IsHovered),
            StyleDeclaration.Pressed(state.IsPressed),
            StyleDeclaration.Focused(state.IsFocused)
        ]);
}

internal static class ControlActionPropertyAdapter
{
    internal static VirtualNodeProperty ToProperty(ActionId actionId) =>
        VirtualNodeProperty.Action(actionId);
}

internal static class ButtonPropertyBundle
{
    internal static VirtualNodeProperty[] Create(ActionId actionId, ControlVisualState visualState) =>
        [
            ControlActionPropertyAdapter.ToProperty(actionId),
            .. ControlVisualStatePropertyAdapter.ToProperties(visualState)
        ];
}

internal static class ControlNodeBuilder
{
    internal static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Button(arena.AddText(label.AsSpan()), key, properties);
    }

    internal static VirtualNode Button(TextContentResource label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        if (label.IsNone)
        {
            throw new ArgumentException("Button label must be explicit.", nameof(label));
        }

        SplitButtonProperties(
            properties,
            out var containerProperties,
            out var rectangleProperties,
            out var textProperties);
        var children = new VirtualNodeChildrenBuilder();
        children.Add(VirtualNodeFactory.Rectangle(rectangleProperties));
        children.Add(VirtualNodeFactory.Text(label, properties: textProperties));
        return VirtualNodeFactory.Container(key, containerProperties, ref children);
    }

    private static void SplitButtonProperties(
        ReadOnlySpan<VirtualNodeProperty> properties,
        out VirtualNodeProperty[] containerProperties,
        out VirtualNodeProperty[] rectangleProperties,
        out VirtualNodeProperty[] textProperties)
    {
        if (properties.IsEmpty)
        {
            containerProperties = [];
            rectangleProperties = [];
            textProperties = [];
            return;
        }

        var container = new VirtualNodeProperty[properties.Length];
        var rectangle = new VirtualNodeProperty[properties.Length];
        var text = new VirtualNodeProperty[properties.Length];
        var containerCount = 0;
        var rectangleCount = 0;
        var textCount = 0;
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, property.Key))
            {
                container[containerCount++] = property;
            }
            else if (property.Key == VirtualPropertyKey.Background || property.Key == VirtualPropertyKey.Border)
            {
                rectangle[rectangleCount++] = property;
            }
            else if (property.Key == VirtualPropertyKey.ForegroundColor)
            {
                text[textCount++] = property;
            }
            else
            {
                throw new ArgumentException(
                    $"Property {VirtualPropertyDiagnostics.Format(property.Key)} cannot be applied to a button control template.",
                    nameof(properties));
            }
        }

        containerProperties = Trim(container, containerCount);
        rectangleProperties = Trim(rectangle, rectangleCount);
        textProperties = Trim(text, textCount);
    }

    private static VirtualNodeProperty[] Trim(VirtualNodeProperty[] properties, int count)
    {
        if (count == 0)
        {
            return [];
        }

        if (count == properties.Length)
        {
            return properties;
        }

        var result = new VirtualNodeProperty[count];
        Array.Copy(properties, result, count);
        return result;
    }
}
