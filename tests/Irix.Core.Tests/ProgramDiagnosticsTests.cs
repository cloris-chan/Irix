using Irix.Poc;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ProgramDiagnosticsTests
{
    [Fact]
    public async Task Diagnose_scroll_outputs_scroll_pump_counters()
    {
        var writer = new StringWriter();

        await Program.RunScrollDiagnosticModeAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Scroll Pump Diagnostics ===", output);
        Assert.Contains("frames=2", output);
        Assert.Contains("waitMs=", output);
        Assert.Contains("dt=", output);
        Assert.Contains("drained=54.0", output);
        Assert.Contains("pending=0.0", output);
    }

    [Fact]
    public async Task Diagnose_input_outputs_ownership_state_transitions()
    {
        var writer = new StringWriter();

        await Program.RunInputDiagnosticModeAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Input Ownership Diagnostics ===", output);
        Assert.Contains("afterMove hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False", output);
        Assert.Contains("afterPress hover=Increment focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("duringCaptureMove hover=Decrement focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("releaseOutside mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("pressEmpty mapped=False hover=Decrement focus=- pressed=- capture=-", output);
        Assert.Contains("releaseAfterEmptyPress mapped=False", output);
        Assert.Contains("focusLost hover=- focus=- pressed=- capture=-", output);
    }
}