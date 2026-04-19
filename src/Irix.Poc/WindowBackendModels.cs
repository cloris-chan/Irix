using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct WindowBackendRenderResult(
    IReadOnlyList<WindowContentElement> Elements,
    IReadOnlyList<HitTestTarget> HitTargets);
