using Irix.Rendering;

namespace Irix.Poc;

internal sealed record CounterModel(int Count, ScrollState Scroll);

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

    public sealed record InputVisualStateChanged : CounterMessage;
}

internal sealed class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    public CounterModel Initialize() => new(0, ScrollState.Default);

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
            CounterMessage.InputVisualStateChanged => new UpdateResult<CounterModel, CounterMessage>(model),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };

    public VirtualNodeTree BuildView(CounterModel model)
    {
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        var s = model.Scroll;
        var pendingPx = Program.DiagPendingPx;
        var inputOwnership = Program.DiagInputOwnership;
        var maxScrollText = !s.HasMaxScrollY
            ? "unknown"
            : s.MaxScrollY == 0
                ? "0(known-zero)"
                : $"{s.MaxScrollY:F0}";

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
            children: [
                VirtualNodeFactory.Text($"Count: {model.Count}", 2),
                VirtualNodeFactory.Text($"ScrollY: applied={scrollY} target={s.TargetPosition:F1} pos={s.Position:F2} max={maxScrollText} acc={s.Accumulator:F3} anim={s.IsAnimating} pendingPx={pendingPx:F0} drained={Program.DiagScrollDrainedPixels:F0} frames={Program.DiagScrollDispatchedFrameCount} waitMs={Program.DiagScrollRenderWaitMs:F1} dt={Program.DiagScrollLastDt:F3} frameQueued={Program.DiagScrollFrameQueued} tickLoop={Program.DiagTickLoopRunning}", 3),
                VirtualNodeFactory.Text("Click a button or use Up/Down, mouse wheel, and R.", 4),
                VirtualNodeFactory.Text($"Input: hover={FormatTarget(inputOwnership.HoveredTarget)} focus={FormatTarget(inputOwnership.FocusedTarget)} pressed={FormatTarget(inputOwnership.PressedTarget)} capture={FormatTarget(inputOwnership.CapturedTarget)} hoverChanges={inputOwnership.HoverChangeCount}", 9),
                VirtualNodeFactory.Rectangle(220, 48, 5),
                BuildButton("Increment", 6, nameof(CounterMessage.Increment), inputOwnership),
                BuildButton("Decrement", 7, nameof(CounterMessage.Decrement), inputOwnership),
                BuildButton("Reset", 8, nameof(CounterMessage.Reset), inputOwnership),
                .. BuildScrollProbeRows()
            ]);

        return new VirtualNodeTree(root);
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
}
