using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class PocRuntimeSettingsTests
{
    [Fact]
    public void Parse_collects_runtime_cli_flags_without_creating_public_provider()
    {
        var defaults = PocRuntimeSettings.Parse([]);

        Assert.Equal(PocRuntimeSettings.Default, defaults);
        Assert.Equal(DrawingBackendClipMode.Scissor, defaults.ClipMode);
        Assert.Equal(TextCompositionMode.GlyphAtlas, defaults.TextCompositionMode);
        Assert.True(defaults.EnablePartialApply);
        Assert.False(defaults.EnableConsoleMirror);
        Assert.Equal(RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled, defaults.RenderProductionOwnerOptions);
        Assert.Equal(DrawingBackendCompositorHandoffOptions.Enabled, defaults.CompositorHandoffOptions);
        Assert.Equal("ENABLED (default)", defaults.PartialApplyConsoleStatus);

        var rollback = PocRuntimeSettings.Parse(
            [
                "--clip-mode",
                "diagnostic",
                "--text-composition",
                "atlas",
                "--no-partial-apply",
                "--console"
            ]);

        Assert.Equal(DrawingBackendClipMode.Diagnostic, rollback.ClipMode);
        Assert.Equal(TextCompositionMode.GlyphAtlas, rollback.TextCompositionMode);
        Assert.False(rollback.EnablePartialApply);
        Assert.True(rollback.EnableConsoleMirror);
        Assert.Equal(RenderPipelineProductionOwnerOptions.Disabled, rollback.RenderProductionOwnerOptions);
        Assert.Equal(DrawingBackendCompositorHandoffOptions.Disabled, rollback.CompositorHandoffOptions);
        Assert.Equal("DISABLED (--no-partial-apply)", rollback.PartialApplyConsoleStatus);
    }
}
