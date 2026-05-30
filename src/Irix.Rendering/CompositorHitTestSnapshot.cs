using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly struct CompositorHitTestResult(
    ActionId ActionId,
    int ScreenX,
    int ScreenY,
    float LocalX,
    float LocalY,
    CompositionLayerId LayerId,
    int AppliedLayerCount,
    bool MappedThroughFixedClip) : IEquatable<CompositorHitTestResult>
{
    public ActionId ActionId { get; } = ActionId;
    public int ScreenX { get; } = ScreenX;
    public int ScreenY { get; } = ScreenY;
    public float LocalX { get; } = LocalX;
    public float LocalY { get; } = LocalY;
    public CompositionLayerId LayerId { get; } = LayerId;
    public int AppliedLayerCount { get; } = AppliedLayerCount;
    public bool MappedThroughFixedClip { get; } = MappedThroughFixedClip;

    public bool MappedThroughComposition => AppliedLayerCount > 0;

    public bool Equals(CompositorHitTestResult other)
    {
        return ActionId == other.ActionId
            && ScreenX == other.ScreenX
            && ScreenY == other.ScreenY
            && LocalX.Equals(other.LocalX)
            && LocalY.Equals(other.LocalY)
            && LayerId == other.LayerId
            && AppliedLayerCount == other.AppliedLayerCount
            && MappedThroughFixedClip == other.MappedThroughFixedClip;
    }

    public override bool Equals(object? obj) => obj is CompositorHitTestResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ActionId, ScreenX, ScreenY, LocalX, LocalY, LayerId, AppliedLayerCount, MappedThroughFixedClip);

    public static bool operator ==(CompositorHitTestResult left, CompositorHitTestResult right) => left.Equals(right);

    public static bool operator !=(CompositorHitTestResult left, CompositorHitTestResult right) => !left.Equals(right);
}

internal readonly struct CompositorHitTestSnapshot
{
    private readonly HitTestTarget[]? _hitTargets;
    private readonly CompositorHitTestLayerNode[]? _layers;

    private CompositorHitTestSnapshot(
        HitTestTarget[] hitTargets,
        CompositorHitTestLayerNode[] layers,
        int commandCount)
    {
        _hitTargets = hitTargets;
        _layers = layers;
        CommandCount = commandCount;
    }

    public int CommandCount { get; }
    public int HitTargetCount => _hitTargets?.Length ?? 0;
    public int LayerCount => _layers?.Length ?? 0;
    public bool IsEmpty => HitTargetCount == 0;

    public static CompositorHitTestSnapshot Create(
        HitTestTarget[] hitTargets,
        int commandCount,
        in CompositionFrame compositionFrame)
    {
        ArgumentNullException.ThrowIfNull(hitTargets);
        if (hitTargets.Length == 0)
        {
            return default;
        }

        commandCount = Math.Max(0, commandCount);
        var layers = CompositorHitTestLayerNode.CreateSnapshot(compositionFrame, commandCount);
        return new CompositorHitTestSnapshot(hitTargets, layers, commandCount);
    }

    public bool TryGetActionIdAtLogicalPixel(int x, int y, out ActionId actionId)
    {
        if (TryHitTestLogicalPixel(x, y, out var result))
        {
            actionId = result.ActionId;
            return true;
        }

        actionId = ActionId.None;
        return false;
    }

    public bool TryHitTestLogicalPixel(int x, int y, out CompositorHitTestResult result)
    {
        var hitTargets = _hitTargets;
        if (hitTargets is null || hitTargets.Length == 0)
        {
            result = default;
            return false;
        }

        for (var i = hitTargets.Length - 1; i >= 0; i--)
        {
            var hitTarget = hitTargets[i];
            if (TryHitTestTarget(hitTarget, x, y, out result))
            {
                return true;
            }
        }

        result = default;
        return false;
    }

    private bool TryHitTestTarget(
        in HitTestTarget hitTarget,
        int x,
        int y,
        out CompositorHitTestResult result)
    {
        var targetX = (float)x;
        var targetY = (float)y;
        var mappedThroughFixedClip = false;
        var appliedLayerCount = 0;
        var layerId = default(CompositionLayerId);

        var layers = _layers;
        if (layers is not null)
        {
            for (var i = layers.Length - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (!layer.ContainsHitTarget(hitTarget))
                {
                    continue;
                }

                if (layer.HasFixedClip)
                {
                    if (!layer.ContainsFixedClip(targetX, targetY))
                    {
                        result = default;
                        return false;
                    }

                    mappedThroughFixedClip = true;
                }

                targetX -= layer.Transform.TranslateX;
                targetY -= layer.Transform.TranslateY;
                layerId = layer.Id;
                appliedLayerCount++;
            }
        }

        if (!Contains(hitTarget.Bounds, targetX, targetY))
        {
            result = default;
            return false;
        }

        if (hitTarget.ClipBounds.Width > 0 && hitTarget.ClipBounds.Height > 0)
        {
            var clipX = mappedThroughFixedClip ? x : targetX;
            var clipY = mappedThroughFixedClip ? y : targetY;
            if (!Contains(hitTarget.ClipBounds, clipX, clipY))
            {
                result = default;
                return false;
            }
        }

        result = new CompositorHitTestResult(
            hitTarget.ActionId,
            x,
            y,
            targetX,
            targetY,
            layerId,
            appliedLayerCount,
            mappedThroughFixedClip);
        return true;
    }

    private static bool Contains(in PixelRectangle bounds, float x, float y)
    {
        return x >= bounds.X
            && y >= bounds.Y
            && x < bounds.X + bounds.Width
            && y < bounds.Y + bounds.Height;
    }
}

internal readonly struct CompositorHitTestLayerNode(
    CompositionLayerId Id,
    int CommandStart,
    int CommandCount,
    CompositionTransform Transform,
    DrawRect FixedClip) : IEquatable<CompositorHitTestLayerNode>
{
    public CompositionLayerId Id { get; } = Id;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionTransform Transform { get; } = Transform;
    public DrawRect FixedClip { get; } = FixedClip;
    public bool HasFixedClip => FixedClip.Width > 0f && FixedClip.Height > 0f;

    public static CompositorHitTestLayerNode[] CreateSnapshot(in CompositionFrame frame, int commandCount)
    {
        var layerCount = frame.LayerCount;
        if (layerCount == 0 || commandCount <= 0)
        {
            return [];
        }

        var validLayerCount = 0;
        for (var i = 0; i < layerCount; i++)
        {
            if (frame.GetLayer(i).IsValidForCommandCount(commandCount))
            {
                validLayerCount++;
            }
        }

        if (validLayerCount == 0)
        {
            return [];
        }

        var layers = new CompositorHitTestLayerNode[validLayerCount];
        var index = 0;
        for (var i = 0; i < layerCount; i++)
        {
            var layer = frame.GetLayer(i);
            if (!layer.IsValidForCommandCount(commandCount))
            {
                continue;
            }

            layers[index++] = FromLayer(layer);
        }

        return layers;
    }

    public bool ContainsHitTarget(in HitTestTarget hitTarget)
    {
        if (!hitTarget.HasCommandRange)
        {
            return false;
        }

        var layerEnd = CommandStart + CommandCount;
        var hitTargetEnd = hitTarget.CommandStart + hitTarget.CommandCount;
        return hitTarget.CommandStart >= CommandStart && hitTargetEnd <= layerEnd;
    }

    public bool ContainsFixedClip(float x, float y)
    {
        return !HasFixedClip
            || (x >= FixedClip.X
                && y >= FixedClip.Y
                && x < FixedClip.X + FixedClip.Width
                && y < FixedClip.Y + FixedClip.Height);
    }

    public bool Equals(CompositorHitTestLayerNode other)
    {
        return Id == other.Id
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && Transform == other.Transform
            && FixedClip == other.FixedClip;
    }

    public override bool Equals(object? obj) => obj is CompositorHitTestLayerNode other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, CommandStart, CommandCount, Transform, FixedClip);

    public static bool operator ==(CompositorHitTestLayerNode left, CompositorHitTestLayerNode right) => left.Equals(right);

    public static bool operator !=(CompositorHitTestLayerNode left, CompositorHitTestLayerNode right) => !left.Equals(right);

    private static CompositorHitTestLayerNode FromLayer(in CompositionLayer layer)
    {
        return new CompositorHitTestLayerNode(
            layer.Id,
            layer.CommandStart,
            layer.CommandCount,
            layer.Transform,
            layer.HasFixedClip ? layer.ClipBounds : default);
    }
}
