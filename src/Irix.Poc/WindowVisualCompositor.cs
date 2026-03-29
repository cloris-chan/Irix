using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed class WindowVisualCompositor(INativeWindow window) : ICompositor
{
    private readonly WindowBackend _windowBackend = new();
    private readonly Lock _hitTargetsLock = new();
    private WindowHitTarget[] _hitTargets = [];

    public ValueTask RenderAsync(DrawCommandBatch drawCommandBatch, CancellationToken cancellationToken = default)
    {
        if (drawCommandBatch.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var result = _windowBackend.Build(drawCommandBatch.Memory.Span[..drawCommandBatch.Count]);
        window.SetContentElements(result.Elements);

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. result.HitTargets];
        }

        return ValueTask.CompletedTask;
    }

    public bool TryGetActionAt(int x, int y, out string action)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (Contains(hitTarget.Bounds, x, y))
                {
                    action = hitTarget.Action;
                    return true;
                }
            }
        }

        action = string.Empty;
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

