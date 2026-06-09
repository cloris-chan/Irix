using Irix.Drawing;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

[Trait("Category", "Guard")]
public sealed class DiagnosticsScenarioRegressionTests
{
    [Fact]
    public async Task Scenario_transcript_pins_scroll_presentation_continuity()
    {
        var diagnostics = await ScrollPresentationRuntimeDiagnosticRunner.RunCoreAsync(TestContext.Current.CancellationToken);
        var transcript = DiagnosticsScenarioTranscript.BuildScrollPresentationContinuity(diagnostics);

        Assert.Contains(transcript, line => line.StartsWith("scenario scroll-continuity retarget ", StringComparison.Ordinal)
            && line.Contains("position=54", StringComparison.Ordinal)
            && line.Contains("target=54", StringComparison.Ordinal)
            && line.Contains("retargets=1", StringComparison.Ordinal)
            && line.Contains("cancels=0", StringComparison.Ordinal)
            && line.Contains("cancelReason=None", StringComparison.Ordinal)
            && line.Contains("lastPresented=54", StringComparison.Ordinal));
        Assert.Contains(transcript, line => line.StartsWith("scenario scroll-continuity chain ", StringComparison.Ordinal)
            && line.Contains("position=108", StringComparison.Ordinal)
            && line.Contains("target=108", StringComparison.Ordinal)
            && line.Contains("retargets=2", StringComparison.Ordinal)
            && line.Contains("cancels=0", StringComparison.Ordinal)
            && line.Contains("cancelReason=None", StringComparison.Ordinal)
            && line.Contains("cancelInvalidation=None", StringComparison.Ordinal)
            && line.Contains("lastPresented=108", StringComparison.Ordinal));
        Assert.Contains(transcript, line => line.StartsWith("scenario scroll-continuity cancel-explicit ", StringComparison.Ordinal)
            && line.Contains("cancels=1", StringComparison.Ordinal)
            && line.Contains("cancelReason=Explicit", StringComparison.Ordinal)
            && line.Contains("explicitCancels=1", StringComparison.Ordinal)
            && line.Contains("activeAfter=False", StringComparison.Ordinal));
        Assert.Contains(transcript, line => line.StartsWith("scenario scroll-continuity cancel-layout ", StringComparison.Ordinal)
            && line.Contains("cancelReason=RenderInvalidation", StringComparison.Ordinal)
            && line.Contains("cancelInvalidation=LayoutAffecting", StringComparison.Ordinal)
            && line.Contains("invalidationCancels=1", StringComparison.Ordinal)
            && line.Contains("activeDuringRender=False", StringComparison.Ordinal)
            && line.Contains("activeAfter=False", StringComparison.Ordinal));
    }

    [Fact]
    public void Scenario_transcript_pins_style_transition_completion()
    {
        var snapshot = new StyleTransitionCompletionPumpDiagnosticSnapshot(
            IsRunning: false,
            HasActiveTransition: false,
            ActiveTargetKey: NodeKey.None,
            ActiveInstanceId: default,
            ActiveOwnerCount: 0,
            ActiveOwnerKind: StyleTransitionOwnerKind.None,
            LastResult: StyleTransitionCompletionPumpResult.CompletionCommitted(
                1,
                new StyleTransitionRuntimeResult(
                    StyleTransitionRuntimeResultKind.Committed,
                    new NodeKey(6))),
            TrackerResult: StyleTransitionCompletionResult.Completed(
                new NodeKey(6),
                new CompositionAnimationInstanceId(1001),
                StyleTransitionRuntimeDecision.Commit(new NodeKey(6))),
            TickCount: 2,
            CommitCount: 1,
            DrainedEventCount: 1,
            TickMode: StyleTransitionCompletionPumpTickMode.SingleAnimation,
            LastErrorKind: null);

        var transcript = DiagnosticsScenarioTranscript.BuildStyleTransitionCompletion(snapshot);

        Assert.Single(transcript);
        var line = transcript[0];
        Assert.StartsWith("scenario style-transition-completion ", line, StringComparison.Ordinal);
        Assert.Contains("activeOwnerCount=0", line, StringComparison.Ordinal);
        Assert.Contains("tickMode=SingleAnimation", line, StringComparison.Ordinal);
        Assert.Contains("lastResult=CompletionCommitted", line, StringComparison.Ordinal);
        Assert.Contains("lastDrainedEvents=1", line, StringComparison.Ordinal);
        Assert.Contains("lastCommitResult=Committed", line, StringComparison.Ordinal);
        Assert.Contains("trackerResult=Completed", line, StringComparison.Ordinal);
        Assert.Contains("tickCount=2", line, StringComparison.Ordinal);
        Assert.Contains("commitCount=1", line, StringComparison.Ordinal);
        Assert.Contains("drainedEvents=1", line, StringComparison.Ordinal);
        Assert.Contains("hasError=False", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenario_transcript_pins_material_output_diagnostics()
    {
        var snapshot = new DrawMaterialOutputDiagnostics(
            ColorOutputKind.SdrSrgb,
            DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient,
            DrawMaterialKind.LinearGradient,
            DrawMaterialFallbackReason.None,
            CommandCount: 5,
            SolidColorCommandCount: 2,
            LinearGradientCommandCount: 3,
            LinearGradientSingleRectCommandCount: 1,
            LinearGradientSegmentedCommandCount: 2,
            LinearGradientSegmentRectCount: 8,
            FallbackCommandCount: 0);

        var transcript = DiagnosticsScenarioTranscript.BuildMaterialOutput(snapshot);

        Assert.Single(transcript);
        var line = transcript[0];
        Assert.StartsWith("scenario material-output ", line, StringComparison.Ordinal);
        Assert.Contains("outputKind=SdrSrgb", line, StringComparison.Ordinal);
        Assert.Contains("backendCapabilities=SolidColor, LinearGradient", line, StringComparison.Ordinal);
        Assert.Contains("selectedMaterialKind=LinearGradient", line, StringComparison.Ordinal);
        Assert.Contains("fallbackReason=None", line, StringComparison.Ordinal);
        Assert.Contains("fallbackApplied=False", line, StringComparison.Ordinal);
        Assert.Contains("solidColorCommands=2", line, StringComparison.Ordinal);
        Assert.Contains("linearGradientCommands=3", line, StringComparison.Ordinal);
        Assert.Contains("linearGradientSingleRectCommands=1", line, StringComparison.Ordinal);
        Assert.Contains("linearGradientSegmentedCommands=2", line, StringComparison.Ordinal);
        Assert.Contains("linearGradientSegmentRects=8", line, StringComparison.Ordinal);
        Assert.Contains("fallbackCommands=0", line, StringComparison.Ordinal);
    }

    private static class DiagnosticsScenarioTranscript
    {
        public static string[] BuildScrollPresentationContinuity(in ScrollPresentationRuntimeDiagnostics diagnostics)
        {
            var lines = ScrollPresentationRuntimeDiagnosticRunner.Format(diagnostics)
                .Split(Environment.NewLine, StringSplitOptions.None);

            return
            [
                Prefix("scroll-continuity retarget", FindLine(lines, "scroll-presentation-runtime actual", "scenario=initial")),
                Prefix("scroll-continuity chain", FindLine(lines, "scroll-presentation-runtime actual", "scenario=chain")),
                Prefix("scroll-continuity cancel-explicit", FindLine(lines, "scroll-presentation-runtime.cancel", "scenario=explicit")),
                Prefix("scroll-continuity cancel-layout", FindLine(lines, "scroll-presentation-runtime.cancel", "scenario=layout"))
            ];
        }

        public static string[] BuildStyleTransitionCompletion(StyleTransitionCompletionPumpDiagnosticSnapshot snapshot)
        {
            return [Prefix("style-transition-completion", DiagnosticsFormatter.BuildStyleTransitionCompletionPumpDiagnosticLine(snapshot))];
        }

        public static string[] BuildMaterialOutput(DrawMaterialOutputDiagnostics snapshot)
        {
            return [Prefix("material-output", DiagnosticsFormatter.BuildMaterialOutputDiagnosticLine(snapshot))];
        }

        private static string Prefix(string scenario, string formattedLine)
        {
            var payloadStart = formattedLine.IndexOf(" status ", StringComparison.Ordinal);
            if (payloadStart >= 0)
            {
                return $"scenario {scenario} {formattedLine[(payloadStart + " status ".Length)..]}";
            }

            const string materialPrefix = "Material output status: ";
            if (formattedLine.StartsWith(materialPrefix, StringComparison.Ordinal))
            {
                return $"scenario {scenario} {formattedLine[materialPrefix.Length..]}";
            }

            return $"scenario {scenario} {formattedLine}";
        }

        private static string FindLine(string[] lines, string prefix, string contains)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal)
                    && line.Contains(contains, StringComparison.Ordinal))
                {
                    return line;
                }
            }

            throw new InvalidOperationException($"Missing diagnostics line with prefix '{prefix}' and token '{contains}'.");
        }
    }
}
