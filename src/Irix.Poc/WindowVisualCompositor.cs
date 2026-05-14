using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowVisualCompositor(INativeWindow window) : ICompositor
{
    private readonly WindowBackend _windowBackend = new();
    private readonly Lock _hitTargetsLock = new();
    private HitTestTarget[] _hitTargets = [];

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            window.SetContentElements([]);

            lock (_hitTargetsLock)
            {
                _hitTargets = [];
            }

            return ValueTask.CompletedTask;
        }

        var result = _windowBackend.Build(
            renderFrameBatch.Commands.Memory.Span[..renderFrameBatch.Commands.Count],
            renderFrameBatch.HitTargets,
            renderFrameBatch.Resources);
        window.SetContentElements(result.Elements);

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. result.HitTargets];
        }

        return ValueTask.CompletedTask;
    }

    public bool TryGetActionIdAt(int x, int y, out ActionId actionId)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (Contains(hitTarget.Bounds, x, y))
                {
                    actionId = hitTarget.ActionId;
                    return true;
                }
            }
        }

        actionId = ActionId.None;
        return false;
    }

    private static bool Contains(PixelRectangle bounds, int x, int y)
    {
        return x >= bounds.X
            && y >= bounds.Y
            && x < bounds.X + bounds.Width
            && y < bounds.Y + bounds.Height;
    }
}

