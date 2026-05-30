#if IRIX_DIAGNOSTICS
namespace Irix.Poc;

internal static class ScrollPresentationPolicyDiagnosticRunner
{
    internal static void Run(TextWriter output)
    {
        var diagnostics = RunCore();
        output.WriteLine("=== Scroll Presentation Policy Diagnostic ===");
        output.WriteLine(Format(diagnostics));
        output.WriteLine("=== scroll presentation policy diagnostic complete ===");
    }

    internal static ScrollPresentationPolicyDiagnostics RunCore()
    {
        var state = new ScrollState
        {
            Position = 120,
            TargetPosition = 180,
            IsAnimating = true,
            MaxScrollY = 240,
            HasMaxScrollY = true,
        };
        const double presentedScrollY = 132;
        var delta = new ScrollDelta(ScrollDeltaUnit.Pixel, 54);
        var commit = ScrollController.ResolvePresentationInterrupt(
            state,
            presentedScrollY,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 0),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.CommitPresented);
        var cancel = ScrollController.ResolvePresentationInterrupt(
            state,
            presentedScrollY,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 0),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.CancelToLogicalTarget);
        var retarget = ScrollController.ResolvePresentationInterrupt(
            state,
            presentedScrollY,
            delta,
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);
        return new ScrollPresentationPolicyDiagnostics(state, presentedScrollY, delta, commit, cancel, retarget);
    }

    internal static string Format(in ScrollPresentationPolicyDiagnostics diagnostics)
    {
        return $"scroll-presentation-policy actual initialPos={diagnostics.InitialState.Position:0.##} initialTarget={diagnostics.InitialState.TargetPosition:0.##} presented={diagnostics.PresentedScrollY:0.##} deltaPx={diagnostics.InputDelta.Value:0.##} commitPos={diagnostics.Commit.NextState.Position:0.##} commitTarget={diagnostics.Commit.NextState.TargetPosition:0.##} commitAnimating={diagnostics.Commit.NextState.IsAnimating} cancelPos={diagnostics.Cancel.NextState.Position:0.##} cancelTarget={diagnostics.Cancel.NextState.TargetPosition:0.##} cancelAnimating={diagnostics.Cancel.NextState.IsAnimating} retargetPos={diagnostics.Retarget.NextState.Position:0.##} retargetTarget={diagnostics.Retarget.NextState.TargetPosition:0.##} retargetAnimating={diagnostics.Retarget.NextState.IsAnimating}";
    }
}

internal readonly struct ScrollPresentationPolicyDiagnostics(
    ScrollState InitialState,
    double PresentedScrollY,
    ScrollDelta InputDelta,
    ScrollPresentationInterruptDecision Commit,
    ScrollPresentationInterruptDecision Cancel,
    ScrollPresentationInterruptDecision Retarget) : IEquatable<ScrollPresentationPolicyDiagnostics>
{
    public ScrollState InitialState { get; } = InitialState;
    public double PresentedScrollY { get; } = PresentedScrollY;
    public ScrollDelta InputDelta { get; } = InputDelta;
    public ScrollPresentationInterruptDecision Commit { get; } = Commit;
    public ScrollPresentationInterruptDecision Cancel { get; } = Cancel;
    public ScrollPresentationInterruptDecision Retarget { get; } = Retarget;

    public bool Equals(ScrollPresentationPolicyDiagnostics other)
    {
        return InitialState == other.InitialState
            && PresentedScrollY.Equals(other.PresentedScrollY)
            && InputDelta == other.InputDelta
            && Commit == other.Commit
            && Cancel == other.Cancel
            && Retarget == other.Retarget;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationPolicyDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(InitialState, PresentedScrollY, InputDelta, HashCode.Combine(Commit, Cancel, Retarget));

    public static bool operator ==(ScrollPresentationPolicyDiagnostics left, ScrollPresentationPolicyDiagnostics right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationPolicyDiagnostics left, ScrollPresentationPolicyDiagnostics right) => !left.Equals(right);
}
#endif
