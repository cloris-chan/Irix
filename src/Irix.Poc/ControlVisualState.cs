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

internal static class ControlVisualStateAttributeAdapter
{
    internal static VirtualNodeAttribute[] ToAttributes(ControlVisualState state) =>
        [
            new VirtualNodeAttribute(VirtualAttributeKey.IsHovered, AttributeValue.FromBoolean(state.IsHovered)),
            new VirtualNodeAttribute(VirtualAttributeKey.IsPressed, AttributeValue.FromBoolean(state.IsPressed)),
            new VirtualNodeAttribute(VirtualAttributeKey.IsFocused, AttributeValue.FromBoolean(state.IsFocused))
        ];
}

internal static class ControlActionAttributeAdapter
{
    internal static VirtualNodeAttribute ToAttribute(ActionId actionId) =>
        VirtualNodeAttribute.Action(actionId);
}

internal static class ButtonAttributeBundle
{
    internal static VirtualNodeAttribute[] Create(ActionId actionId, ControlVisualState visualState) =>
        [
            ControlActionAttributeAdapter.ToAttribute(actionId),
            .. ControlVisualStateAttributeAdapter.ToAttributes(visualState)
        ];
}