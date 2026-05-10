using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct CounterViewportDiagnostics(PixelRectangle RendererViewport, PixelRectangle LayoutViewport, string ScaleMode);

internal readonly record struct CounterLayoutDiagnostics(long LayoutRebuildCount, LayoutRebuildReason LastLayoutRebuildReason, string LastDirtyClassifications)
{
    public static CounterLayoutDiagnostics Empty => new(0, LayoutRebuildReason.None, "(none)");
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

    /// <summary>Raw wheel delta from input. Never reaches Update — coalesced by HandleInput.</summary>
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
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        var inputOwnership = model.InputOwnership;
        var headerRows = _showDiagnostics
            ? BuildDiagnosticHeaderRows(model.Count, scrollY, model.Scroll, Program.DiagPendingPx, inputOwnership, model.ViewportDiagnostics, model.LayoutDiagnostics)
            : [
                VirtualNodeFactory.Text($"Count: {model.Count}", 2),
                VirtualNodeFactory.Text("Click a button or use Up/Down, mouse wheel, and R.", 4)
            ];

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
            children: [
                .. headerRows,
                VirtualNodeFactory.Rectangle(220, 48, 5),
                BuildButton("Increment", 6, nameof(CounterMessage.Increment), inputOwnership),
                BuildButton("Decrement", 7, nameof(CounterMessage.Decrement), inputOwnership),
                BuildButton("Reset", 8, nameof(CounterMessage.Reset), inputOwnership),
                .. BuildScrollProbeRows()
            ]);

        return new VirtualNodeTree(root);
    }

    private static VirtualNode[] BuildDiagnosticHeaderRows(int count, int scrollY, ScrollState scroll, double pendingPx, OwnershipSnapshot inputOwnership, CounterViewportDiagnostics viewportDiagnostics, CounterLayoutDiagnostics layoutDiagnostics)
    {
        var maxScrollText = !scroll.HasMaxScrollY
            ? "unknown"
            : scroll.MaxScrollY == 0
                ? "0(known-zero)"
                : $"{scroll.MaxScrollY:F0}";

        return [
            VirtualNodeFactory.Text($"Count: {count}", 2),
            VirtualNodeFactory.Text($"ScrollY: applied={scrollY} target={scroll.TargetPosition:F1} pos={scroll.Position:F2} max={maxScrollText} acc={scroll.Accumulator:F3} anim={scroll.IsAnimating} pendingPx={pendingPx:F0} drained={Program.DiagScrollDrainedPixels:F0} frames={Program.DiagScrollDispatchedFrameCount} waitMs={Program.DiagScrollRenderWaitMs:F1} dt={Program.DiagScrollLastDt:F3} frameQueued={Program.DiagScrollFrameQueued} tickLoop={Program.DiagTickLoopRunning}", 3),
            VirtualNodeFactory.Text("Click a button or use Up/Down, mouse wheel, and R.", 4),
            VirtualNodeFactory.Text($"Input: hover={FormatTarget(inputOwnership.HoveredTarget)} focus={FormatTarget(inputOwnership.FocusedTarget)} pressed={FormatTarget(inputOwnership.PressedTarget)} capture={FormatTarget(inputOwnership.CapturedTarget)} hoverChanges={inputOwnership.HoverChangeCount}", 9),
            VirtualNodeFactory.Text($"ClipMode: {Program.DiagBackendClipMode}", 10),
            VirtualNodeFactory.Text($"Viewport: renderer={FormatSize(viewportDiagnostics.RendererViewport)} layout={FormatSize(viewportDiagnostics.LayoutViewport)} scaleMode={viewportDiagnostics.ScaleMode}", 11),
            VirtualNodeFactory.Text($"LayoutDirty: layoutRebuildCount={layoutDiagnostics.LayoutRebuildCount} LastLayoutRebuildReason={layoutDiagnostics.LastLayoutRebuildReason} LastDirtyClassifications={layoutDiagnostics.LastDirtyClassifications}", 12)
        ];
    }

    private static CounterLayoutDiagnostics NormalizeLayoutDiagnostics(CounterLayoutDiagnostics diagnostics)
    {
        return diagnostics.LastDirtyClassifications is null ? CounterLayoutDiagnostics.Empty : diagnostics;
    }

    internal static ButtonVisualState DeriveButtonState(OwnershipSnapshot ownership, string actionId)
    {
        return new ButtonVisualState(
            IsHovered: ownership.HoveredTarget == actionId,
            IsPressed: ownership.IsPointerPressed && ownership.PressedTarget == actionId,
            IsFocused: ownership.FocusedTarget == actionId);
    }

    private static VirtualNode BuildButton(string label, ulong key, string actionId, OwnershipSnapshot ownership)
    {
        var state = DeriveButtonState(ownership, actionId);
        return VirtualNodeFactory.Button(
            label,
            key,
            new VirtualNodeAttribute("ActionId", AttributeValue.FromText(actionId)),
            new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(state.IsHovered)),
            new VirtualNodeAttribute("IsPressed", AttributeValue.FromBoolean(state.IsPressed)),
            new VirtualNodeAttribute("IsFocused", AttributeValue.FromBoolean(state.IsFocused)));
    }

    private static VirtualNode[] BuildScrollProbeRows()
    {
        var rows = new VirtualNode[50];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = VirtualNodeFactory.Text($"Scroll row {index + 1:00}", (ulong)(100 + index));
        }

        return rows;
    }

    private static string FormatTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target) ? "-" : target;
    }

    private static string FormatSize(PixelRectangle rectangle)
    {
        return $"{rectangle.Width}x{rectangle.Height}";
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
