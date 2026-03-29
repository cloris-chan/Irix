using Irix.Platform;

namespace Irix.Poc;

internal readonly record struct WindowHitTarget(PixelRectangle Bounds, string Action);

internal readonly record struct WindowBackendRenderResult(
    IReadOnlyList<WindowContentElement> Elements,
    IReadOnlyList<WindowHitTarget> HitTargets);
