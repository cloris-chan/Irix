#if IRIX_DIAGNOSTICS
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class InputDiagnosticRunner
{
    internal static async Task RunAsync(
        TextWriter output,
        string? reportPath = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = BuildInputDiagnosticsSnapshot();
        var lines = DiagnosticsFormatter.BuildInputDiagnosticLines(snapshot);

        foreach (var line in lines)
        {
            await output.WriteLineAsync(line);
        }

        if (reportPath is not null)
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllLinesAsync(reportPath, lines, cancellationToken);
        }
    }

    internal static InputDiagnosticsSnapshot BuildInputDiagnosticsSnapshot()
    {
        var ownershipState = new InputOwnershipState();
        var ownershipSteps = new List<InputDiagnosticOwnershipStep>();
        var buttonStates = new List<InputDiagnosticButtonState>();

        AddButtonState(InputDiagnosticButtonStateKind.Normal, default);
        AddButtonState(InputDiagnosticButtonStateKind.Hovered, new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true));
        AddButtonState(InputDiagnosticButtonStateKind.Pressed, new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true));
        AddButtonState(InputDiagnosticButtonStateKind.Focused, new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true));

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipStep(InputDiagnosticOwnershipStepKind.AfterMove);
        AddDerivedButtonState(InputDiagnosticButtonStateKind.AfterMove, ownershipState.Snapshot);

        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipStep(InputDiagnosticOwnershipStepKind.AfterPress);
        AddDerivedButtonState(InputDiagnosticButtonStateKind.AfterPress, ownershipState.Snapshot);

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 3, X: 32, Y: 200),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipStep(InputDiagnosticOwnershipStepKind.DuringCaptureMove);
        AddDerivedButtonState(InputDiagnosticButtonStateKind.DuringCaptureMove, ownershipState.Snapshot);

        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 4,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out var releaseMessage);
        AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind.ReleaseOutside, releaseMapped, releaseMessage);
        AddDerivedButtonState(InputDiagnosticButtonStateKind.ReleaseOutside, ownershipState.Snapshot);

        var enterMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 5, X: 0, Y: 0, KeyCode: 0x0D),
            ownershipState,
            HitInputDiagnosticTarget,
            out var enterMessage);
        AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind.KeyboardEnter, enterMapped, enterMessage);

        var spaceMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 6, X: 0, Y: 0, KeyCode: 0x20),
            ownershipState,
            HitInputDiagnosticTarget,
            out var spaceMessage);
        AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind.KeyboardSpace, spaceMapped, spaceMessage);

        var pressEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 7,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind.PressEmpty, pressEmptyMapped);

        var releaseEmptyMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 8,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind.ReleaseAfterEmptyPress, releaseEmptyMapped);

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 9, X: 0, Y: 0),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipStep(InputDiagnosticOwnershipStepKind.FocusLost);
        AddDerivedButtonState(InputDiagnosticButtonStateKind.FocusLost, ownershipState.Snapshot);

        return new InputDiagnosticsSnapshot(
            ownershipState.Snapshot,
            buttonStates.ToArray(),
            ownershipSteps.ToArray(),
            ownershipState.DiagnosticEvents.ToArray(),
            BuildInputDirtyReasonDiagnostics());

        void AddButtonState(InputDiagnosticButtonStateKind kind, ButtonVisualState state)
        {
            buttonStates.Add(new InputDiagnosticButtonState(kind, ActionIdRegistry.Increment, state));
        }

        void AddDerivedButtonState(InputDiagnosticButtonStateKind kind, OwnershipSnapshot ownership)
        {
            AddButtonState(kind, CounterApplication.DeriveButtonState(ownership, ActionIdRegistry.Increment));
        }

        void AddOwnershipStep(InputDiagnosticOwnershipStepKind kind)
        {
            ownershipSteps.Add(new InputDiagnosticOwnershipStep(kind, ownershipState.Snapshot));
        }

        void AddMappedOwnershipStep(InputDiagnosticOwnershipStepKind kind, bool mapped, CounterMessage? message = null)
        {
            ownershipSteps.Add(new InputDiagnosticOwnershipStep(kind, ownershipState.Snapshot, HasMappedResult: true, Mapped: mapped, Message: message));
        }
    }

    internal static InputDirtyReasonDiagnostic[] BuildInputDirtyReasonDiagnostics()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();
        var model = app.Initialize();
        var currentTree = app.BuildView(model);
        var retainedTree = new RetainedTree(default);
        var pipeline = new RenderPipeline(CounterStylePreset.Default);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        using (var initialPatch = VirtualNodeDiffer.CreatePatchBatch(default, currentTree))
        {
            var initialResult = retainedTree.Apply(initialPatch);
            var initialSnapshot = retainedTree.Tree.TextSnapshot;
            using var initialFrame = pipeline.Build(retainedTree.Tree.Root, viewport, initialSnapshot, initialResult.Dirty);
            pipeline.RetainedFrame.Invalidate();
        }

        var diagnostics = new InputDirtyReasonDiagnostic[3];
        diagnostics[0] = ApplyInput(InputDirtyReasonCase.HoverOnly, new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140));
        diagnostics[1] = ApplyInput(InputDirtyReasonCase.Press, new RawInputEvent(
            RawInputEventKind.PointerPressed,
            Timestamp: 2,
            X: 32,
            Y: 140,
            Button: PointerButton.Left));
        diagnostics[2] = ApplyInput(InputDirtyReasonCase.Release, new RawInputEvent(
            RawInputEventKind.PointerReleased,
            Timestamp: 3,
            X: 500,
            Y: 500,
            Button: PointerButton.Left));

        return diagnostics;

        InputDirtyReasonDiagnostic ApplyInput(InputDirtyReasonCase @case, RawInputEvent inputEvent)
        {
            var hitTestResolver = new DelegateActionHitTestResolver(HitDiagnosticTarget);
            if (!Program.TryMapInputForRuntime(inputEvent, ownershipState, hitTestResolver, out var message) || message is null or CounterMessage.WheelRaw)
            {
                return new InputDirtyReasonDiagnostic(@case, LayoutRebuildReason.None, []);
            }

            model = app.Update(model, message).NextModel;
            var nextTree = app.BuildView(model);
            using var patch = VirtualNodeDiffer.CreatePatchBatch(currentTree, nextTree);
            var result = retainedTree.Apply(patch);
            var textSnapshot = retainedTree.Tree.TextSnapshot;
            var prevTextSnapshot = result.Dirty.Count > 0 ? (TextBufferSnapshot?)result.PreviousTextSnapshot : null;
            var previousRoot = result.Dirty.Count > 0 ? result.PreviousRoot : default;
            using var frame = pipeline.Build(retainedTree.Tree.Root, viewport, textSnapshot, result.Dirty, prevTextSnapshot, previousRoot);
            pipeline.RetainedFrame.Invalidate();
            currentTree = nextTree;
            return new InputDirtyReasonDiagnostic(@case, pipeline.LastLayoutRebuildReason, pipeline.LastDirtyClassifications.ToArray());
        }

        static ActionId HitDiagnosticTarget(int x, int y)
        {
            return x == 32 && y == 140 ? ActionIdRegistry.Increment : default;
        }
    }

    private static ActionId HitInputDiagnosticTarget(int x, int y)
    {
        return (x, y) switch
        {
            (32, 140) => ActionIdRegistry.Increment,
            (32, 200) => ActionIdRegistry.Decrement,
            _ => default
        };
    }
}
#endif
