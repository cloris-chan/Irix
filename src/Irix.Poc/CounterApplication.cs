using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal sealed record CounterModel(int Count, ScrollState Scroll, OwnershipSnapshot InputOwnership);

internal abstract partial record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;

    /// <summary>Apply a coalesced scroll delta and advance animation for one rendered frame.</summary>
    public sealed record ScrollFrame(ScrollDelta Delta, double DeltaTime) : CounterMessage;

    public sealed record ScrollPresentationInterrupted(ScrollPresentationInterruptDecision Decision) : CounterMessage;

    /// <summary>Update MaxScrollY from the layout pass.</summary>
    public sealed record UpdateMaxScrollY(double MaxScrollY) : CounterMessage;

    /// <summary>Raw wheel delta from input. Never reaches Update �?coalesced by HandleInput.</summary>
    public sealed record WheelRaw(int RawDelta) : CounterMessage;

    public sealed record InputVisualStateChanged(OwnershipSnapshot Snapshot) : CounterMessage;

    public sealed record RoutedInput(CounterMessage? Action, OwnershipSnapshot Snapshot) : CounterMessage;
}

internal sealed partial class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    private const int ScrollProbeRowCount = 50;
    private const int RootFixedChildCount = 4;

    internal readonly VirtualTextArena _arena = new();

    internal CounterApplication()
    {
    }

    public CounterModel Initialize() => new(0, ScrollState.Default, default);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message)
    {
        var optionalHandled = false;
        var optionalResult = default(UpdateResult<CounterModel, CounterMessage>);
        TryUpdateOptional(model, message, ref optionalHandled, ref optionalResult);
        if (optionalHandled)
        {
            return optionalResult;
        }

        return message switch
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
            CounterMessage.ScrollPresentationInterrupted interrupted => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = interrupted.Decision.NextState,
            }),
            CounterMessage.UpdateMaxScrollY update => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.WithMaxScrollY(model.Scroll, update.MaxScrollY),
            }),
            CounterMessage.InputVisualStateChanged input => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                InputOwnership = input.Snapshot,
            }),
            CounterMessage.RoutedInput input => ApplyRoutedInput(model, input),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };
    }

    public VirtualNodeTree BuildView(CounterModel model)
    {
        _arena.BeginFrame();
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        var inputOwnership = model.InputOwnership;
        VirtualNode[]? headerRows = null;
        TryBuildOptionalHeaderRows(_arena, model, ref headerRows);
        headerRows ??=
            [
                VirtualNodeBuilder.Text(_arena, $"Count: {model.Count}", new NodeKey(2)),
                VirtualNodeBuilder.Text(_arena, "Click a button or use Up/Down, mouse wheel, and R.", new NodeKey(4))
            ];

        var rootProperties = new[] { VirtualNodeProperty.ScrollY(scrollY) };
        VirtualNodePropertySet.Validate(VirtualNodeKind.Container, rootProperties);
        var rootChildren = CreateRootChildren(_arena, headerRows, inputOwnership);
        var root = VirtualNode.CreateFromOwnedArraysUnsafe(VirtualNodeKind.Container, new NodeKey(1), ContentResource.None, rootProperties, rootChildren);

        return new VirtualNodeTree(root, _arena.GetOrCreateSnapshot());
    }

    partial void TryUpdateOptional(CounterModel model, CounterMessage message, ref bool handled, ref UpdateResult<CounterModel, CounterMessage> result);

    partial void TryBuildOptionalHeaderRows(VirtualTextArena arena, CounterModel model, ref VirtualNode[]? headerRows);

    internal static ButtonVisualState DeriveButtonState(OwnershipSnapshot ownership, ActionId actionId)
    {
        var state = ControlVisualStateProjection.Project(ownership, actionId);
        return new ButtonVisualState(state.IsHovered, state.IsPressed, state.IsFocused);
    }

    private static VirtualNode BuildButton(VirtualTextArena arena, string label, uint key, ActionId actionId, OwnershipSnapshot ownership)
    {
        var visualState = ControlVisualStateProjection.Project(ownership, actionId);
        return ControlNodeBuilder.Button(
            arena,
            label,
            new NodeKey(key),
            actionId,
            visualState);
    }

    private static VirtualNode[] CreateRootChildren(VirtualTextArena arena, ReadOnlySpan<VirtualNode> headerRows, OwnershipSnapshot inputOwnership)
    {
        var children = new VirtualNode[headerRows.Length + RootFixedChildCount + ScrollProbeRowCount];
        headerRows.CopyTo(children);
        var next = headerRows.Length;

        children[next++] = VirtualNodeFactory.Rectangle(new NodeKey(5), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48));
        children[next++] = BuildButton(arena, "Increment", 6, ActionIdRegistry.Increment, inputOwnership);
        children[next++] = BuildButton(arena, "Decrement", 7, ActionIdRegistry.Decrement, inputOwnership);
        children[next++] = BuildButton(arena, "Reset", 8, ActionIdRegistry.Reset, inputOwnership);
        next = WriteScrollProbeRows(arena, children, next);

        if (next != children.Length)
        {
            throw new InvalidOperationException("Counter root child publication count changed unexpectedly.");
        }

        return children;
    }

    private static int WriteScrollProbeRows(VirtualTextArena arena, VirtualNode[] children, int startIndex)
    {
        var next = startIndex;
        for (var index = 0; index < ScrollProbeRowCount; index++)
        {
            children[next++] = VirtualNodeBuilder.Text(arena, $"Scroll row {index + 1:00}", new NodeKey((uint)(100 + index)));
        }

        return next;
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
