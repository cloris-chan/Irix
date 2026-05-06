using Irix.Drawing;

namespace Irix.Rendering;

/// <summary>
/// Compositor that delegates rendering to an <see cref="IDrawingBackend"/>.
/// Caches hit targets from the frame for input routing.
/// This is the bridge between the RenderFrameBatch world and the IDrawingBackend world.
/// </summary>
public sealed class DrawingBackendCompositor : ICompositor, IDisposable
{
    private readonly IDrawingBackend _backend;
    private readonly Lock _hitTargetsLock = new();
    private HitTestTarget[] _hitTargets = [];

    public DrawingBackendCompositor(IDrawingBackend backend)
    {
        _backend = backend;
    }

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            lock (_hitTargetsLock)
            {
                _hitTargets = [];
            }

            return ValueTask.CompletedTask;
        }

        var frameContext = new FrameContext(0, 0); // Viewport size not needed for PoC backend
        _backend.BeginFrame(frameContext);

        _backend.Execute(
            renderFrameBatch.Commands.Memory.Span[..renderFrameBatch.Commands.Count],
            renderFrameBatch.Resources);

        _backend.EndFrame();

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. renderFrameBatch.HitTargets];
        }

        return ValueTask.CompletedTask;
    }

    public bool TryGetActionIdAt(int x, int y, out string actionId)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (x >= hitTarget.Bounds.X
                    && y >= hitTarget.Bounds.Y
                    && x < hitTarget.Bounds.X + hitTarget.Bounds.Width
                    && y < hitTarget.Bounds.Y + hitTarget.Bounds.Height)
                {
                    actionId = hitTarget.ActionId;
                    return true;
                }
            }
        }

        actionId = string.Empty;
        return false;
    }

    public void Dispose()
    {
        _backend.Dispose();
    }
}
