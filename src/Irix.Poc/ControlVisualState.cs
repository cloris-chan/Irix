namespace Irix.Poc;

internal readonly record struct ControlVisualState(
    bool IsHovered,
    bool IsPressed,
    bool IsFocused);

internal static class ControlVisualStateProjection
{
    internal static ControlVisualState Project(OwnershipSnapshot ownership, string targetId) =>
        new(
            IsHovered: ownership.HoveredTarget == targetId,
            IsPressed: ownership.IsPointerPressed && ownership.PressedTarget == targetId,
            IsFocused: ownership.FocusedTarget == targetId);
}

internal static class ControlVisualStateAttributeAdapter
{
    internal static VirtualNodeAttribute[] ToAttributes(ControlVisualState state) =>
        [
            new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(state.IsHovered)),
            new VirtualNodeAttribute("IsPressed", AttributeValue.FromBoolean(state.IsPressed)),
            new VirtualNodeAttribute("IsFocused", AttributeValue.FromBoolean(state.IsFocused))
        ];
}