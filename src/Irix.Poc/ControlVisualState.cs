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
