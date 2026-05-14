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
        var lines = new List<string>();
        var ownershipLines = new List<string>();
        var buttonVisualStateLines = new List<string>();

        lines.Add("buttonPriorityOrder Pressed > Hovered > Focused > Normal");
        AddButtonVisualStateLine($"buttonState normal Increment {DiagnosticsFormatter.FormatButtonState(default)}");
        AddButtonVisualStateLine($"buttonState hovered Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true))}");
        AddButtonVisualStateLine($"buttonState pressed Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true))}");
        AddButtonVisualStateLine($"buttonState focused Increment {DiagnosticsFormatter.FormatButtonState(new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true))}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"afterMove {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState afterMove Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, ActionIdRegistry.Increment))}");

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
        AddOwnershipLine($"afterPress {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState afterPress Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, ActionIdRegistry.Increment))}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 3, X: 32, Y: 200),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"duringCaptureMove {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState duringCaptureMove Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, ActionIdRegistry.Increment))}");

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
        AddOwnershipLine($"releaseOutside mapped={releaseMapped} message={DiagnosticsFormatter.FormatMessage(releaseMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState releaseOutside Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, ActionIdRegistry.Increment))}");

        var enterMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 5, X: 0, Y: 0, KeyCode: 0x0D),
            ownershipState,
            HitInputDiagnosticTarget,
            out var enterMessage);
        AddOwnershipLine($"keyboardEnter mapped={enterMapped} message={DiagnosticsFormatter.FormatMessage(enterMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        var spaceMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 6, X: 0, Y: 0, KeyCode: 0x20),
            ownershipState,
            HitInputDiagnosticTarget,
            out var spaceMessage);
        AddOwnershipLine($"keyboardSpace mapped={spaceMapped} message={DiagnosticsFormatter.FormatMessage(spaceMessage)} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

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
        AddOwnershipLine($"pressEmpty mapped={pressEmptyMapped} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

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
        AddOwnershipLine($"releaseAfterEmptyPress mapped={releaseEmptyMapped} {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 9, X: 0, Y: 0),
            ownershipState,
            HitInputDiagnosticTarget,
            out _);
        AddOwnershipLine($"focusLost {DiagnosticsFormatter.FormatOwnership(ownershipState.Snapshot)}");
        AddButtonVisualStateLine($"buttonState focusLost Increment {DiagnosticsFormatter.FormatButtonState(CounterApplication.DeriveButtonState(ownershipState.Snapshot, ActionIdRegistry.Increment))}");
        lines.Add("events:");
        var eventLines = new List<string>();
        foreach (var diagnosticEvent in ownershipState.DiagnosticEvents)
        {
            var eventLine = $"  {DiagnosticsFormatter.FormatOwnershipEvent(diagnosticEvent)}";
            eventLines.Add(eventLine);
            lines.Add(eventLine);
        }

        var dirtyReasonLines = BuildInputDirtyReasonDiagnosticLines();
        lines.AddRange(dirtyReasonLines);

        return new InputDiagnosticsSnapshot(ownershipState.Snapshot, lines, ownershipLines, buttonVisualStateLines, eventLines, dirtyReasonLines);

        void AddOwnershipLine(string line)
        {
            ownershipLines.Add(line);
            lines.Add(line);
        }

        void AddButtonVisualStateLine(string line)
        {
            buttonVisualStateLines.Add(line);
            lines.Add(line);
        }
    }

    internal static string[] BuildInputDirtyReasonDiagnosticLines()
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

        var lines = new List<string>
        {
            "dirtyReasons:"
        };

        ApplyInput("hoverOnly", new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140));
        ApplyInput("press", new RawInputEvent(
            RawInputEventKind.PointerPressed,
            Timestamp: 2,
            X: 32,
            Y: 140,
            Button: PointerButton.Left));
        ApplyInput("release", new RawInputEvent(
            RawInputEventKind.PointerReleased,
            Timestamp: 3,
            X: 500,
            Y: 500,
            Button: PointerButton.Left));

        return lines.ToArray();

        void ApplyInput(string name, RawInputEvent inputEvent)
        {
            if (!Program.TryMapInputForRuntime(inputEvent, ownershipState, HitDiagnosticTarget, out var message) || message is null or CounterMessage.WheelRaw)
            {
                lines.Add($"dirtyReason {name} reason={LayoutRebuildReason.None} classifications=(none)");
                return;
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
            lines.Add($"dirtyReason {name} reason={pipeline.LastLayoutRebuildReason} classifications={DiagnosticsFormatter.FormatLayoutDirtyClassifications(pipeline.LastDirtyClassifications)}");
            currentTree = nextTree;
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
