using System.Text;
using Irix.Platform.Windows;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Poc;

internal static class GlyphAtlasBidiOracleDiagnosticRunner
{
    internal static void Run(TextWriter output)
    {
        var snapshot = DWriteBidiOracleDiagnostic.Capture();

        output.WriteLine("=== Glyph Atlas BiDi Oracle Diagnostic ===");
        output.WriteLine(FormatSummary(snapshot));
        foreach (var result in snapshot.Results)
        {
            output.WriteLine(FormatProbe(result));
        }

        output.WriteLine("=== glyph atlas bidi oracle diagnostic complete ===");
    }

    internal static string FormatSummary(BidiOracleDiagnosticSnapshot snapshot)
    {
        return string.IsNullOrEmpty(snapshot.Failure)
            ? $"BiDi oracle: factory={snapshot.FactoryAvailable}, analyzer={snapshot.AnalyzerAvailable}, probes={snapshot.ProbeCount}, mixedLevelProbes={snapshot.MixedLevelProbes}, visualReorderedProbes={snapshot.VisualReorderedProbes}, failedProbes={snapshot.FailedProbes}"
            : $"BiDi oracle: failure={snapshot.Failure}, factory={snapshot.FactoryAvailable}, analyzer={snapshot.AnalyzerAvailable}, probes={snapshot.ProbeCount}, mixedLevelProbes=0, visualReorderedProbes=0, failedProbes=0";
    }

    internal static string FormatProbe(BidiOracleProbeResult result)
    {
        var builder = new StringBuilder(256);
        builder.Append("Probe: ");
        builder.Append(result.Label);
        builder.Append(" base=");
        builder.Append(FormatDirection(result.BaseDirection));
        builder.Append(" textLength=");
        builder.Append(result.TextLength);
        if (!string.IsNullOrEmpty(result.Failure))
        {
            builder.Append(" failure=");
            builder.Append(result.Failure);
            return builder.ToString();
        }

        builder.Append(" levels=");
        AppendLevels(builder, result.Levels);
        builder.Append(" logicalRuns=");
        AppendRuns(builder, result.LogicalRuns);
        builder.Append(" visualRuns=");
        AppendRuns(builder, result.VisualRuns);
        builder.Append(" charOrder=");
        AppendOrder(builder, result.CharacterVisualOrder);
        return builder.ToString();
    }

    private static string FormatDirection(DWRITE_READING_DIRECTION direction) =>
        direction == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? "RTL" : "LTR";

    private static void AppendLevels(StringBuilder builder, IReadOnlyList<byte> levels)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(levels[i]);
        }
    }

    private static void AppendRuns(StringBuilder builder, IReadOnlyList<BidiOracleLevelRun> runs)
    {
        builder.Append('[');
        for (var i = 0; i < runs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            var run = runs[i];
            builder.Append(run.TextStart);
            builder.Append("..");
            builder.Append(run.TextEnd);
            builder.Append('@');
            builder.Append(run.Level);
        }

        builder.Append(']');
    }

    private static void AppendOrder(StringBuilder builder, IReadOnlyList<int> order)
    {
        builder.Append('[');
        for (var i = 0; i < order.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(order[i]);
        }

        builder.Append(']');
    }
}
