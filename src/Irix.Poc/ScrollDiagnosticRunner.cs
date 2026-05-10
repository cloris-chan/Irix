namespace Irix.Poc;

internal static class ScrollDiagnosticRunner
{
    internal static async Task RunAsync(
        TextWriter output,
        string? reportPath = null,
        CancellationToken cancellationToken = default)
    {
        var pump = new ScrollFramePump();
        var scrollState = ScrollState.Default with { MaxScrollY = 240, HasMaxScrollY = true };
        var frameCount = 0;
        var totalDrainedPixels = 0.0;

        pump.AddPendingPixels(54);

        await pump.RunUntilIdleAsync(
            async (frame, token) =>
            {
                frameCount++;
                totalDrainedPixels += frame.Delta.Value;
                scrollState = ScrollController.ApplyScrollDelta(
                    scrollState,
                    frame.Delta,
                    ScrollMetrics.DefaultText,
                    SystemScrollSettings.Default);
                scrollState = ScrollController.Tick(scrollState, frame.DeltaTime);

                await Task.Delay(20, token);

                if (frameCount >= 2)
                {
                    scrollState = scrollState with
                    {
                        Position = scrollState.TargetPosition,
                        IsAnimating = false
                    };
                }
            },
            () => scrollState,
            cancellationToken);

        var snapshot = new ScrollDiagnosticsSnapshot(
            pump.DispatchedFrameCount,
            pump.RenderWaitMs,
            pump.LastDt,
            totalDrainedPixels,
            pump.DrainedPixels,
            pump.PendingPixels,
            pump.IsFrameQueued,
            pump.IsLoopRunning,
            ScrollController.GetScrollY(scrollState),
            scrollState.TargetPosition,
            scrollState.MaxScrollY,
            scrollState.HasMaxScrollY,
            scrollState.Position,
            scrollState.Accumulator,
            scrollState.IsAnimating);
        var lines = DiagnosticsFormatter.BuildScrollDiagnosticLines(snapshot);

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
}
