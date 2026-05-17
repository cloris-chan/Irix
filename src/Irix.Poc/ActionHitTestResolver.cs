using Irix.Rendering;

namespace Irix.Poc;

internal interface IActionHitTestResolver
{
    ActionId Resolve(int x, int y);
}

internal readonly struct DelegateActionHitTestResolver(Func<int, int, ActionId> resolve) : IActionHitTestResolver
{
    public ActionId Resolve(int x, int y) => resolve(x, y);
}

internal readonly struct DrawingBackendCompositorActionHitTestResolver(DrawingBackendCompositor compositor) : IActionHitTestResolver
{
    public ActionId Resolve(int x, int y)
    {
        return compositor.TryGetActionIdAtPhysicalPixel(x, y, out var actionId)
            ? actionId
            : ActionId.None;
    }
}
