using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct CounterViewportDiagnostics(
    PixelRectangle RendererViewport,
    PixelRectangle LayoutViewport,
    string ScaleMode,
    DisplayScale Scale = default,
    PixelRectangle LogicalViewport = default);

internal readonly struct CounterLayoutDiagnostics : IEquatable<CounterLayoutDiagnostics>
{
    public CounterLayoutDiagnostics(
        long layoutRebuildCount,
        LayoutRebuildReason lastLayoutRebuildReason,
        IReadOnlyList<LayoutDirtyClassification> lastDirtyClassifications)
    {
        LayoutRebuildCount = layoutRebuildCount;
        LastLayoutRebuildReason = lastLayoutRebuildReason;
        LastDirtyClassifications = lastDirtyClassifications.Count == 0 ? [] : lastDirtyClassifications.ToArray();
    }

    public long LayoutRebuildCount { get; }
    public LayoutRebuildReason LastLayoutRebuildReason { get; }
    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications { get; }

    public static CounterLayoutDiagnostics Empty => new(0, LayoutRebuildReason.None, []);

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
        IReadOnlyList<LayoutDirtyClassification> left,
        IReadOnlyList<LayoutDirtyClassification> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

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

    private static void AddLayoutDirtyClassificationsHash(ref HashCode hash, IReadOnlyList<LayoutDirtyClassification> classifications)
    {
        for (var i = 0; i < classifications.Count; i++)
        {
            hash.Add(classifications[i]);
        }
    }
}

internal sealed record CounterModel(int Count, ScrollState Scroll, OwnershipSnapshot InputOwnership, CounterViewportDiagnostics ViewportDiagnostics, CounterLayoutDiagnostics LayoutDiagnostics);

internal abstract record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;

    /// <summary>Apply a coalesced scroll delta and advance animation for one rendered frame.</summary>
    public sealed record ScrollFrame(ScrollDelta Delta, double DeltaTime) : CounterMessage;

    /// <summary>Update MaxScrollY from the layout pass.</summary>
    public sealed record UpdateMaxScrollY(double MaxScrollY) : CounterMessage;

    /// <summary>Raw wheel delta from input. Never reaches Update �?coalesced by HandleInput.</summary>
    public sealed record WheelRaw(int RawDelta) : CounterMessage;

    public sealed record InputVisualStateChanged(OwnershipSnapshot Snapshot) : CounterMessage;

    public sealed record ViewportDiagnosticsChanged(CounterViewportDiagnostics Diagnostics) : CounterMessage;

    public sealed record LayoutDiagnosticsChanged(CounterLayoutDiagnostics Diagnostics) : CounterMessage;

    public sealed record DebugDiagnosticsChanged(CounterViewportDiagnostics ViewportDiagnostics, CounterLayoutDiagnostics LayoutDiagnostics) : CounterMessage;

    public sealed record RoutedInput(CounterMessage? Action, OwnershipSnapshot Snapshot) : CounterMessage;
}

internal sealed class CounterApplication(bool showDiagnostics = false, CounterViewportDiagnostics initialViewportDiagnostics = default, CounterLayoutDiagnostics initialLayoutDiagnostics = default) : IApplication<CounterModel, CounterMessage>
{
    private readonly bool _showDiagnostics = showDiagnostics;
    private readonly CounterViewportDiagnostics _initialViewportDiagnostics = initialViewportDiagnostics;
    private readonly CounterLayoutDiagnostics _initialLayoutDiagnostics = NormalizeLayoutDiagnostics(initialLayoutDiagnostics);
    internal readonly VirtualTextArena _arena = new();

    public CounterModel Initialize() => new(0, ScrollState.Default, default, _initialViewportDiagnostics, _initialLayoutDiagnostics);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message) =>
        message switch
        {
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(model with { Count = reset.Value }),
            CounterMessage.ScrollFrame frame => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.Tick(
                    ScrollController.ApplyScrollDelta(
                        model.Scroll,
                        frame.Delta,
                        ScrollMetrics.DefaultText,
                        SystemScrollSettings.Default),
                    frame.DeltaTime),
            }),
            CounterMessage.UpdateMaxScrollY update => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.WithMaxScrollY(model.Scroll, update.MaxScrollY),
            }),
            CounterMessage.InputVisualStateChanged input => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                InputOwnership = input.Snapshot,
            }),
            CounterMessage.ViewportDiagnosticsChanged viewport => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                ViewportDiagnostics = viewport.Diagnostics,
            }),
            CounterMessage.LayoutDiagnosticsChanged layout => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                LayoutDiagnostics = NormalizeLayoutDiagnostics(layout.Diagnostics),
            }),
            CounterMessage.DebugDiagnosticsChanged diagnostics => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                ViewportDiagnostics = diagnostics.ViewportDiagnostics,
                LayoutDiagnostics = NormalizeLayoutDiagnostics(diagnostics.LayoutDiagnostics),
            }),
            CounterMessage.RoutedInput input => ApplyRoutedInput(model, input),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };

    public VirtualNodeTree BuildView(CounterModel model)
    {
        _arena.BeginFrame();
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        var inputOwnership = model.InputOwnership;
        var headerRows = _showDiagnostics
            ? BuildDiagnosticHeaderRows(_arena, model.Count, model.Scroll, inputOwnership, model.ViewportDiagnostics, model.LayoutDiagnostics)
            :
            [
                VirtualNodeBuilder.Text(_arena, $"Count: {model.Count}", new NodeKey(2)),
                VirtualNodeBuilder.Text(_arena, "Click a button or use Up/Down, mouse wheel, and R.", new NodeKey(4))
            ];

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: new NodeKey(1),
            properties: [VirtualNodeProperty.ScrollY(scrollY)],
            children:
            [
                .. headerRows,
                VirtualNodeFactory.Rectangle(new NodeKey(5), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48)),
                BuildButton(_arena, "Increment", 6, ActionIdRegistry.Increment, inputOwnership),
                BuildButton(_arena, "Decrement", 7, ActionIdRegistry.Decrement, inputOwnership),
                BuildButton(_arena, "Reset", 8, ActionIdRegistry.Reset, inputOwnership),
                .. BuildScrollProbeRows(_arena)
            ]);

        return new VirtualNodeTree(root, _arena.GetOrCreateSnapshot());
    }

    private static VirtualNode[] BuildDiagnosticHeaderRows(VirtualTextArena arena, int count, ScrollState scroll, OwnershipSnapshot inputOwnership, CounterViewportDiagnostics viewportDiagnostics, CounterLayoutDiagnostics layoutDiagnostics)
    {
        var debugDiagnostics = new DefaultDebugDiagnosticsSnapshotBridge(viewportDiagnostics, layoutDiagnostics, scroll, inputOwnership).Capture();

        return [
            VirtualNodeBuilder.Text(arena, $"Count: {count}", new NodeKey(2)),
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
        return diagnostics.LastDirtyClassifications is null ? CounterLayoutDiagnostics.Empty : diagnostics;
    }

    internal static ButtonVisualState DeriveButtonState(OwnershipSnapshot ownership, ActionId actionId)
    {
        var state = ControlVisualStateProjection.Project(ownership, actionId);
        return new ButtonVisualState(state.IsHovered, state.IsPressed, state.IsFocused);
    }

    private static VirtualNode BuildButton(VirtualTextArena arena, string label, uint key, ActionId actionId, OwnershipSnapshot ownership)
    {
        var visualState = ControlVisualStateProjection.Project(ownership, actionId);
        return VirtualNodeBuilder.Button(
            arena,
            label,
            new NodeKey(key),
            ButtonPropertyBundle.Create(actionId, visualState));
    }

    private static VirtualNode[] BuildScrollProbeRows(VirtualTextArena arena)
    {
        var rows = new VirtualNode[50];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = VirtualNodeBuilder.Text(arena, $"Scroll row {index + 1:00}", new NodeKey((uint)(100 + index)));
        }

        return rows;
    }

    private static UpdateResult<CounterModel, CounterMessage> ApplyRoutedInput(CounterModel model, CounterMessage.RoutedInput input)
    {
        var modelWithInput = model with { InputOwnership = input.Snapshot };
        return input.Action switch
        {
            null => new UpdateResult<CounterModel, CounterMessage>(modelWithInput),
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = modelWithInput.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = modelWithInput.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(modelWithInput with { Count = reset.Value }),
            _ => throw new NotSupportedException($"Unsupported routed input action type: {input.Action.GetType().Name}")
        };
    }
}
