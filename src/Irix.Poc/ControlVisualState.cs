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
    private const int PropertyCount = 3;

    internal static VirtualNodeProperty[] ToProperties(ControlVisualState state) =>
        StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.Hovered(state.IsHovered),
            StyleDeclaration.Pressed(state.IsPressed),
            StyleDeclaration.Focused(state.IsFocused)
        ]);

    internal static void WriteProperties(ControlVisualState state, Span<VirtualNodeProperty> destination)
    {
        if (destination.Length < PropertyCount)
        {
            throw new ArgumentException("Destination is too small for control visual state properties.", nameof(destination));
        }

        StyleDeclarationMapper.WriteVirtualNodeProperties(
        [
            StyleDeclaration.Hovered(state.IsHovered),
            StyleDeclaration.Pressed(state.IsPressed),
            StyleDeclaration.Focused(state.IsFocused)
        ], destination[..PropertyCount]);
    }
}

internal static class ControlActionPropertyAdapter
{
    internal static VirtualNodeProperty ToProperty(ActionId actionId) =>
        VirtualNodeProperty.Action(actionId);
}

internal static class ButtonPropertyBundle
{
    private const int PropertyCount = 4;

    internal static void Write(ActionId actionId, ControlVisualState visualState, Span<VirtualNodeProperty> destination)
    {
        if (destination.Length < PropertyCount)
        {
            throw new ArgumentException("Destination is too small for a button property bundle.", nameof(destination));
        }

        destination[0] = ControlActionPropertyAdapter.ToProperty(actionId);
        ControlVisualStatePropertyAdapter.WriteProperties(visualState, destination[1..PropertyCount]);
    }
}

internal static class ControlNodeBuilder
{
    internal static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Button(arena.AddText(label.AsSpan()), key, properties);
    }

    internal static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key, ActionId actionId, ControlVisualState visualState)
    {
        ArgumentNullException.ThrowIfNull(label);
        Span<VirtualNodeProperty> properties = stackalloc VirtualNodeProperty[4];
        ButtonPropertyBundle.Write(actionId, visualState, properties);
        return Button(arena.AddText(label.AsSpan()), key, properties);
    }

    internal static VirtualNode Button(TextContentResource label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties)
    {
        if (label.IsNone)
        {
            throw new ArgumentException("Button label must be explicit.", nameof(label));
        }

        CountButtonProperties(properties, out var containerCount, out var rectangleCount, out var textCount);
        var containerProperties = CreateButtonPropertyList(properties, containerCount, ButtonPropertyTarget.Container);
        var rectangleProperties = CreateButtonPropertyList(properties, rectangleCount, ButtonPropertyTarget.Rectangle);
        var textProperties = CreateButtonPropertyList(properties, textCount, ButtonPropertyTarget.Text);
        var children = CreateButtonChildren(label, rectangleProperties, textProperties);
        return VirtualNode.CreateFromOwnedChildrenUnsafe(VirtualNodeKind.Container, key, default, containerProperties, children);
    }

    internal static VirtualNode[] CreateButtonChildren(
        TextContentResource label,
        VirtualNodePropertyList rectangleProperties,
        VirtualNodePropertyList textProperties)
    {
        if (label.IsNone)
        {
            throw new ArgumentException("Button label must be explicit.", nameof(label));
        }

        return VirtualNode.CreateOwnedChildren(
        [
            new VirtualNode(
                VirtualNodeKind.Content,
                NodeKey.None,
                ContentResource.Rectangle,
                rectangleProperties,
                ReadOnlySpan<VirtualNode>.Empty),
            new VirtualNode(
                VirtualNodeKind.Content,
                NodeKey.None,
                ContentResource.FromText(label),
                textProperties,
                ReadOnlySpan<VirtualNode>.Empty)
        ]);
    }

    internal static void CountButtonProperties(
        ReadOnlySpan<VirtualNodeProperty> properties,
        out int containerCount,
        out int rectangleCount,
        out int textCount)
    {
        containerCount = 0;
        rectangleCount = 0;
        textCount = 0;
        for (var i = 0; i < properties.Length; i++)
        {
            var target = GetButtonPropertyTarget(properties[i].Key);
            switch (target)
            {
                case ButtonPropertyTarget.Container:
                    containerCount++;
                    break;
                case ButtonPropertyTarget.Rectangle:
                    rectangleCount++;
                    break;
                case ButtonPropertyTarget.Text:
                    textCount++;
                    break;
            }
        }
    }

    internal static VirtualNodePropertyList CreateButtonPropertyList(
        ReadOnlySpan<VirtualNodeProperty> properties,
        int count,
        ButtonPropertyTarget target)
    {
        if (count == 0)
        {
            return VirtualNodePropertyList.Empty;
        }

        const int StackPropertyLimit = 8;
        if (count > StackPropertyLimit)
        {
            return CreateLargeButtonPropertyList(properties, count, target);
        }

        Span<VirtualNodeProperty> result = stackalloc VirtualNodeProperty[count];
        var writeIndex = 0;
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (GetButtonPropertyTarget(property.Key) == target)
            {
                result[writeIndex++] = property;
            }
        }

        return VirtualNodePropertySet.Create(target == ButtonPropertyTarget.Container ? VirtualNodeKind.Container : VirtualNodeKind.Content, result);
    }

    private static VirtualNodePropertyList CreateLargeButtonPropertyList(
        ReadOnlySpan<VirtualNodeProperty> properties,
        int count,
        ButtonPropertyTarget target)
    {
        var result = new VirtualNodeProperty[count];
        var writeIndex = 0;
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (GetButtonPropertyTarget(property.Key) == target)
            {
                result[writeIndex++] = property;
            }
        }

        return VirtualNodePropertyList.FromOwnedArray(target == ButtonPropertyTarget.Container ? VirtualNodeKind.Container : VirtualNodeKind.Content, result);
    }

    private static ButtonPropertyTarget GetButtonPropertyTarget(VirtualPropertyKey key)
    {
        if (VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, key))
        {
            return ButtonPropertyTarget.Container;
        }

        if (key == VirtualPropertyKey.Background || key == VirtualPropertyKey.Border)
        {
            return ButtonPropertyTarget.Rectangle;
        }

        if (key == VirtualPropertyKey.ForegroundColor)
        {
            return ButtonPropertyTarget.Text;
        }

        throw new ArgumentException($"Property {VirtualPropertyDiagnostics.Format(key)} cannot be applied to a button control template.");
    }

    internal enum ButtonPropertyTarget
    {
        Container,
        Rectangle,
        Text
    }
}
