using Irix.Rendering;

namespace Irix.Poc;

internal interface IActionHitTestResolver : IInputHitTestService
{
    ActionId Resolve(int x, int y);
}

internal readonly struct DelegateActionHitTestResolver(Func<int, int, ActionId> resolve) : IActionHitTestResolver
{
    public ActionId Resolve(int x, int y) => resolve(x, y);

    public bool TryHitTestPhysicalPixel(int x, int y, out ActionId actionId)
    {
        actionId = Resolve(x, y);
        return !actionId.IsNone;
    }
}

internal readonly struct DrawingBackendCompositorInputHitTestService(DrawingBackendCompositor compositor) : IInputHitTestService
{
    public bool TryHitTestPhysicalPixel(int x, int y, out ActionId actionId)
    {
        return compositor.TryGetActionIdAtPhysicalPixel(x, y, out actionId);
    }
}

internal readonly struct DrawingBackendCompositorActionHitTestResolver(DrawingBackendCompositor compositor) : IActionHitTestResolver
{
    public ActionId Resolve(int x, int y)
    {
        return TryHitTestPhysicalPixel(x, y, out var actionId)
            ? actionId
            : ActionId.None;
    }

    public bool TryHitTestPhysicalPixel(int x, int y, out ActionId actionId)
    {
        return compositor.TryGetActionIdAtPhysicalPixel(x, y, out actionId);
    }
}
