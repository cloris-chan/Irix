#if IRIX_DIAGNOSTICS
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct CounterViewportDiagnostics(
    PixelRectangle RendererViewport,
    PixelRectangle LayoutViewport,
    ViewportScaleMode ScaleMode,
    DisplayScale Scale = default,
    PixelRectangle LogicalViewport = default);

internal readonly struct CounterLayoutDiagnostics : IEquatable<CounterLayoutDiagnostics>
{
    public CounterLayoutDiagnostics(
        long layoutRebuildCount,
        LayoutRebuildReason lastLayoutRebuildReason,
        LayoutDirtyClassificationList lastDirtyClassifications)
    {
        LayoutRebuildCount = layoutRebuildCount;
        LastLayoutRebuildReason = lastLayoutRebuildReason;
        LastDirtyClassifications = lastDirtyClassifications;
    }

    public long LayoutRebuildCount { get; }
    public LayoutRebuildReason LastLayoutRebuildReason { get; }
    public LayoutDirtyClassificationList LastDirtyClassifications { get; }

    public static CounterLayoutDiagnostics Empty => new(0, LayoutRebuildReason.None, LayoutDirtyClassificationList.Empty);

    public bool Equals(CounterLayoutDiagnostics other)
    {
        return LayoutRebuildCount == other.LayoutRebuildCount
            && LastLayoutRebuildReason == other.LastLayoutRebuildReason
            && LayoutDirtyClassificationsEqual(LastDirtyClassifications, other.LastDirtyClassifications);
    }

    public override bool Equals(object? obj) => obj is CounterLayoutDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LayoutRebuildCount);
        hash.Add(LastLayoutRebuildReason);
        AddLayoutDirtyClassificationsHash(ref hash, LastDirtyClassifications);
        return hash.ToHashCode();
    }

    public static bool operator ==(CounterLayoutDiagnostics left, CounterLayoutDiagnostics right) => left.Equals(right);

    public static bool operator !=(CounterLayoutDiagnostics left, CounterLayoutDiagnostics right) => !left.Equals(right);

    private static bool LayoutDirtyClassificationsEqual(
        LayoutDirtyClassificationList left,
        LayoutDirtyClassificationList right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void AddLayoutDirtyClassificationsHash(ref HashCode hash, LayoutDirtyClassificationList classifications)
    {
        for (var i = 0; i < classifications.Count; i++)
        {
            hash.Add(classifications[i]);
        }
    }
}

internal abstract partial record CounterMessage
{
    public sealed record ViewportDiagnosticsChanged(CounterViewportDiagnostics Diagnostics) : CounterMessage;

    public sealed record LayoutDiagnosticsChanged(CounterLayoutDiagnostics Diagnostics) : CounterMessage;

    public sealed record DebugDiagnosticsChanged(CounterViewportDiagnostics ViewportDiagnostics, CounterLayoutDiagnostics LayoutDiagnostics) : CounterMessage;
}

internal sealed partial class CounterApplication
{
    private bool _showDiagnostics;
    private CounterViewportDiagnostics _viewportDiagnostics;
    private CounterLayoutDiagnostics _layoutDiagnostics;

    internal CounterApplication(
        bool showDiagnostics,
        CounterViewportDiagnostics initialViewportDiagnostics = default,
        CounterLayoutDiagnostics initialLayoutDiagnostics = default)
    {
        ConfigureDiagnostics(showDiagnostics, initialViewportDiagnostics, initialLayoutDiagnostics);
    }

    internal void ConfigureDiagnostics(
        bool showDiagnostics,
        CounterViewportDiagnostics initialViewportDiagnostics = default,
        CounterLayoutDiagnostics initialLayoutDiagnostics = default)
    {
        _showDiagnostics = showDiagnostics;
        _viewportDiagnostics = initialViewportDiagnostics;
        _layoutDiagnostics = NormalizeLayoutDiagnostics(initialLayoutDiagnostics);
    }

    partial void TryUpdateOptional(CounterModel model, CounterMessage message, ref bool handled, ref UpdateResult<CounterModel, CounterMessage> result)
    {
        switch (message)
        {
            case CounterMessage.ViewportDiagnosticsChanged viewport:
                _viewportDiagnostics = viewport.Diagnostics;
                handled = true;
                result = new UpdateResult<CounterModel, CounterMessage>(model);
                return;
            case CounterMessage.LayoutDiagnosticsChanged layout:
                _layoutDiagnostics = NormalizeLayoutDiagnostics(layout.Diagnostics);
                handled = true;
                result = new UpdateResult<CounterModel, CounterMessage>(model);
                return;
            case CounterMessage.DebugDiagnosticsChanged diagnostics:
                _viewportDiagnostics = diagnostics.ViewportDiagnostics;
                _layoutDiagnostics = NormalizeLayoutDiagnostics(diagnostics.LayoutDiagnostics);
                handled = true;
                result = new UpdateResult<CounterModel, CounterMessage>(model);
                return;
        }
    }

    partial void TryBuildOptionalHeaderRows(VirtualTextArena arena, CounterModel model, ref VirtualNode[]? headerRows)
    {
        if (!_showDiagnostics)
        {
            return;
        }

        var debugDiagnostics = new DefaultDebugDiagnosticsSnapshotBridge(_viewportDiagnostics, _layoutDiagnostics, model.Scroll, model.InputOwnership).Capture();

        headerRows =
        [
            VirtualNodeBuilder.Text(arena, $"Count: {model.Count}", new NodeKey(2)),
            VirtualNodeBuilder.Text(arena, DebugDiagnosticsFormatter.FormatScrollDiagnosticRow(debugDiagnostics), new NodeKey(3)),
            VirtualNodeBuilder.Text(arena, "Click a button or use Up/Down, mouse wheel, and R.", new NodeKey(4)),
            VirtualNodeBuilder.Text(arena, DebugDiagnosticsFormatter.FormatInputDiagnosticRow(debugDiagnostics), new NodeKey(9)),
            VirtualNodeBuilder.Text(arena, DebugDiagnosticsFormatter.FormatClipModeDiagnosticRow(debugDiagnostics), new NodeKey(10)),
            VirtualNodeBuilder.Text(arena, DebugDiagnosticsFormatter.FormatViewportDiagnosticRow(debugDiagnostics), new NodeKey(11)),
            VirtualNodeBuilder.Text(arena, DebugDiagnosticsFormatter.FormatLayoutDirtyDiagnosticRow(debugDiagnostics), new NodeKey(12))
        ];
    }

    private static CounterLayoutDiagnostics NormalizeLayoutDiagnostics(CounterLayoutDiagnostics diagnostics)
    {
        return diagnostics;
    }
}
#endif
