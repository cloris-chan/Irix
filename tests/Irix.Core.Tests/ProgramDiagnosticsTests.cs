using System.Diagnostics;
using System.Numerics;
using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Poc;
using Irix.Rendering;
using Windows.Win32.Graphics.DirectWrite;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ProgramDiagnosticsTests
{
    [Fact]
    public void Text_composition_mode_defaults_to_glyph_atlas_and_rejects_unsupported_modes()
    {
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode([]));
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode(["--text-composition", "glyph-atlas"]));
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode(["--text-composition", "atlas"]));
        var ex = Assert.Throws<ArgumentException>(() => Program.ParseTextCompositionMode(["--text-composition", "cpu"]));
        Assert.Contains("GlyphAtlas is the only active text composition mode", ex.Message);
    }

    [Fact]
    public void Clip_mode_defaults_to_scissor_and_accepts_diagnostic_rollback()
    {
        Assert.Equal(DrawingBackendClipMode.Scissor, Program.ParseClipMode([]));
        Assert.Equal(DrawingBackendClipMode.Scissor, Program.ParseClipMode(["--enable-scissor"]));
        Assert.Equal(DrawingBackendClipMode.Scissor, Program.ParseClipMode(["--clip-mode", "scissor"]));
        Assert.Equal(DrawingBackendClipMode.Diagnostic, Program.ParseClipMode(["--disable-scissor"]));
        Assert.Equal(DrawingBackendClipMode.Diagnostic, Program.ParseClipMode(["--clip-mode", "diagnostic"]));
    }

    [Fact]
    public void Diagnose_scale_accepts_percent_and_multiplier_values()
    {
        Assert.Equal(DisplayScale.Identity, Program.ParseDiagnosticScale([]).Normalize());
        Assert.Equal(new DisplayScale(1.5f, 1.5f), Program.ParseDiagnosticScale(["--diagnose-scale", "150"]));
        Assert.Equal(new DisplayScale(2f, 2f), Program.ParseDiagnosticScale(["--diagnose-scale", "200%"]));
        Assert.Equal(new DisplayScale(1.25f, 1.25f), Program.ParseDiagnosticScale(["--diagnose-scale", "1.25"]));
    }

    [Fact]
    public void Glyph_atlas_fallback_classifier_accepts_simple_bmp_text_and_rejects_known_unsupported_runs()
    {
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("ASCII 123".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("cafe \u00E9lan".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Latin \u0100\u024F".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Greek \u0391\u03A9".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Cyrillic \u0416".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Line\nBreak".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Line\r\nBreak".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Tab\tBreak".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("ASCII 測試".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("e\u0301 combining".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("emoji \ud83d\ude00".AsSpan(), TextStyle.Default));
        Assert.True(GlyphAtlasTextCompositionHelpers.IsSupportedSimpleGlyphAtlasCharacter('\u00E9'));
        Assert.True(GlyphAtlasTextCompositionHelpers.IsSupportedSimpleGlyphAtlasCharacter('\u0394'));
        Assert.True(GlyphAtlasTextCompositionHelpers.IsSupportedSimpleGlyphAtlasCharacter('\u0416'));
        Assert.False(GlyphAtlasTextCompositionHelpers.IsSupportedSimpleGlyphAtlasCharacter('\u0301'));
        Assert.Equal(16, GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(0));
        Assert.Equal(17, GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(1));
        Assert.Equal(40, GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(16));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsLineBreakOrTab("shape".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsLineBreakOrTab("line\nbreak".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsLineBreakOrTab("tab\tbreak".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector("shape".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector("emoji \ud83d\ude00".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector("heart \u2764\uFE0F".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate("emoji \ud83d\ude00".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate("heart \u2764\uFE0F".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate("broken surrogate \uD83D".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate("cjk ext b \uD840\uDC00".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate("heart text \u2764\uFE0E".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate("shape cafe\u0301".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate("arabic \u0645\u0631\u062D\u0628\u0627".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate("arabic presentation \uFE8E".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate("hebrew presentation \uFB1D".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate("thai \u0E44\u0E17\u0E22".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate("arabic \u0645\u0631\u062D\u0628\u0627".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate("arabic presentation \uFE8E".AsSpan()));
        Assert.True(GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate("hebrew presentation \uFB1D".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate("heart \uFE0F".AsSpan()));
        Assert.False(GlyphAtlasTextCompositionHelpers.ContainsRightToLeftScriptCandidate("thai \u0E44\u0E17\u0E22".AsSpan()));

        var wrappingStyle = new TextStyle(
            TextStyle.Default.FontFamily,
            TextStyle.Default.FontSize,
            TextStyle.Default.FontWeight,
            TextStyle.Default.FontStyle,
            TextStyle.Default.FontStretch,
            TextStyle.Default.HorizontalAlignment,
            TextStyle.Default.VerticalAlignment,
            TextWrapping.Wrap);
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("ASCII 123".AsSpan(), wrappingStyle));
    }

    [Fact]
    public void Glyph_atlas_bidi_visual_order_handles_nested_levels()
    {
        int[] ltrOrder = [0, 1, 2, 3, 4];
        byte[] ltrLevels = [0, 1, 2, 1, 0];
        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(ltrOrder, ltrLevels);
        Assert.Equal([0, 3, 2, 1, 4], ltrOrder);

        int[] rtlNestedOrder = [0, 1, 2, 3, 4];
        byte[] rtlNestedLevels = [1, 2, 3, 2, 1];
        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(rtlNestedOrder, rtlNestedLevels);
        Assert.Equal([4, 1, 2, 3, 0], rtlNestedOrder);

        int[] evenOnlyOrder = [0, 1, 2];
        byte[] evenOnlyLevels = [0, 2, 2];
        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(evenOnlyOrder, evenOnlyLevels);
        Assert.Equal([0, 1, 2], evenOnlyOrder);
    }

    [Fact]
    public void Glyph_atlas_bidi_ordering_oracle_pins_current_resolved_level_projection()
    {
        AssertBidiOrder([0, 1, 2, 3, 4, 5], [0, 1, 1, 2, 1, 0], [0, 4, 3, 2, 1, 5]);
        AssertBidiOrder([0, 1, 2, 3], [1, 2, 2, 1], [3, 1, 2, 0]);
        AssertBidiOrder([0, 1, 2, 3, 4, 5], [0, 0, 1, 2, 2, 1], [0, 1, 5, 3, 4, 2]);
        AssertBidiOrder([0, 1, 2, 3], [2, 2, 2, 2], [0, 1, 2, 3]);

        static void AssertBidiOrder(int[] logicalOrder, byte[] levels, int[] expectedVisualOrder)
        {
            GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(logicalOrder, levels);
            Assert.Equal(expectedVisualOrder, logicalOrder);
        }
    }

    [Fact]
    public void Glyph_atlas_bidi_oracle_diagnostic_formats_directwrite_level_projection()
    {
        var results = new[]
        {
            BidiOracleProbeResult.Create(
                "nested",
                6,
                DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT,
                [0, 1, 1, 2, 1, 0],
                [new BidiOracleLevelRun(0, 1, 0), new BidiOracleLevelRun(1, 2, 1), new BidiOracleLevelRun(3, 1, 2), new BidiOracleLevelRun(4, 1, 1), new BidiOracleLevelRun(5, 1, 0)],
                [new BidiOracleLevelRun(0, 1, 0), new BidiOracleLevelRun(4, 1, 1), new BidiOracleLevelRun(3, 1, 2), new BidiOracleLevelRun(1, 2, 1), new BidiOracleLevelRun(5, 1, 0)],
                [0, 4, 3, 2, 1, 5])
        };
        var snapshot = BidiOracleDiagnosticSnapshot.Create(factoryAvailable: true, analyzerAvailable: true, results);

        Assert.Equal(1, snapshot.MixedLevelProbes);
        Assert.Equal(1, snapshot.VisualReorderedProbes);
        Assert.Equal(0, snapshot.FailedProbes);
        Assert.Equal("BiDi oracle: factory=True, analyzer=True, probes=1, mixedLevelProbes=1, visualReorderedProbes=1, failedProbes=0", GlyphAtlasBidiOracleDiagnosticRunner.FormatSummary(snapshot));
        Assert.Equal("Probe: nested base=LTR textLength=6 levels=0,1,1,2,1,0 logicalRuns=[0..1@0|1..3@1|3..4@2|4..5@1|5..6@0] visualRuns=[0..1@0|4..5@1|3..4@2|1..3@1|5..6@0] charOrder=[0,4,3,2,1,5]", GlyphAtlasBidiOracleDiagnosticRunner.FormatProbe(results[0]));
    }

    [Theory]
    [InlineData(TextHorizontalAlignment.Leading, 10, 100, 40, 10)]
    [InlineData(TextHorizontalAlignment.Center, 10, 100, 40, 40)]
    [InlineData(TextHorizontalAlignment.Trailing, 10, 100, 40, 70)]
    [InlineData(TextHorizontalAlignment.Center, 10, 30, 40, 10)]
    [InlineData(TextHorizontalAlignment.Trailing, 10, 30, 40, 10)]
    public void Glyph_atlas_alignment_pen_uses_resolved_line_width(
        TextHorizontalAlignment alignment,
        float runX,
        float runWidth,
        float lineWidth,
        float expectedPenX)
    {
        Assert.Equal(
            expectedPenX,
            GlyphAtlasTextCompositionHelpers.ComputeAlignedPenX(runX, runWidth, alignment, lineWidth));
    }

    [Theory]
    [InlineData(TextVerticalAlignment.Top, 22)]
    [InlineData(TextVerticalAlignment.Center, 7)]
    [InlineData(TextVerticalAlignment.Bottom, -8)]
    public void Glyph_atlas_first_baseline_allows_overheight_text_stack_to_clip(
        TextVerticalAlignment alignment,
        float expectedBaselineY)
    {
        Assert.Equal(
            expectedBaselineY,
            GlyphAtlasTextCompositionHelpers.ComputeFirstBaselineY(
                runY: 10,
                runHeight: 30,
                alignment,
                ascent: 12,
                lineHeight: 20,
                lineCount: 3));
    }

    [Fact]
    public void Glyph_atlas_line_planner_keeps_no_wrap_as_single_line_or_clip()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[2];
        Span<float> advances = [10, 12, 8];

        var acceptedReason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "ABC".AsSpan(),
            advances,
            maxLineWidth: 30,
            TextWrapping.NoWrap,
            lines,
            out var acceptedLineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, acceptedReason);
        Assert.Equal(1, acceptedLineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 3, 30), lines[0]);

        var clippedReason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "ABC".AsSpan(),
            advances,
            maxLineWidth: 29,
            TextWrapping.NoWrap,
            lines,
            out var clippedLineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Clip, clippedReason);
        Assert.Equal(0, clippedLineCount);
    }

    [Fact]
    public void Glyph_atlas_line_planner_wraps_ascii_at_spaces()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[4];
        Span<float> advances = [10, 10, 5, 5, 10, 10, 5, 10];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "AB  CD E".AsSpan(),
            advances,
            maxLineWidth: 25,
            TextWrapping.Wrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(3, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 2, 20), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(4, 6, 20), lines[1]);
        Assert.Equal(new GlyphAtlasLayoutLine(7, 8, 10), lines[2]);
    }

    [Fact]
    public void Glyph_atlas_line_planner_wraps_ascii_at_tabs()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[3];
        Span<float> advances = [10, 10, 20, 10, 10];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "AB\tCD".AsSpan(),
            advances,
            maxLineWidth: 25,
            TextWrapping.Wrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(2, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 2, 20), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(3, 5, 20), lines[1]);
    }

    [Fact]
    public void Glyph_atlas_line_planner_breaks_explicit_ascii_lines()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[4];
        Span<float> advances = [10, 10, 0, 10, 10, 0, 0, 10];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "AB\nCD\r\nE".AsSpan(),
            advances,
            maxLineWidth: 100,
            TextWrapping.NoWrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(3, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 2, 20), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(3, 5, 20), lines[1]);
        Assert.Equal(new GlyphAtlasLayoutLine(7, 8, 10), lines[2]);
    }

    [Fact]
    public void Glyph_atlas_line_planner_counts_trailing_explicit_line_break()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[3];
        Span<float> advances = [10, 0];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "A\n".AsSpan(),
            advances,
            maxLineWidth: 100,
            TextWrapping.NoWrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(2, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 1, 10), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(2, 2, 0), lines[1]);
    }

    [Fact]
    public void Glyph_atlas_line_planner_allows_one_more_line_than_text_length()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[3];
        Span<float> advances = [0, 0];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "\n\n".AsSpan(),
            advances,
            maxLineWidth: 100,
            TextWrapping.NoWrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(3, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 0, 0), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(1, 1, 0), lines[1]);
        Assert.Equal(new GlyphAtlasLayoutLine(2, 2, 0), lines[2]);
    }

    [Fact]
    public void Glyph_atlas_line_planner_keeps_unbreakable_wrap_words_as_clipped_lines()
    {
        Span<GlyphAtlasLayoutLine> lines = stackalloc GlyphAtlasLayoutLine[2];
        Span<float> advances = [10, 10, 5, 10];

        var reason = GlyphAtlasTextCompositionHelpers.PlanLines(
            "AB C".AsSpan(),
            advances,
            maxLineWidth: 15,
            TextWrapping.Wrap,
            lines,
            out var lineCount);

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        Assert.Equal(2, lineCount);
        Assert.Equal(new GlyphAtlasLayoutLine(0, 2, 20), lines[0]);
        Assert.Equal(new GlyphAtlasLayoutLine(3, 4, 10), lines[1]);
    }

    [Fact]
    public void Glyph_atlas_page_reuse_request_requires_later_record_and_retained_frame_floor()
    {
        Assert.False(GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(
            false,
            requestedRecordSerial: 7,
            currentRecordSerial: 8,
            oldestRetainedRecordSerial: 8));
        Assert.False(GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(
            true,
            requestedRecordSerial: 7,
            currentRecordSerial: 7,
            oldestRetainedRecordSerial: 8));
        Assert.False(GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(
            true,
            requestedRecordSerial: 7,
            currentRecordSerial: 8,
            oldestRetainedRecordSerial: 7));
        Assert.True(GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(
            true,
            requestedRecordSerial: 7,
            currentRecordSerial: 8,
            oldestRetainedRecordSerial: 8));
    }

    [Fact]
    public void Glyph_atlas_page_reuse_clears_only_live_entries_from_matching_generation()
    {
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(false, glyphPageIndex: 2, glyphPageGeneration: 4, reusedPageIndex: 2, reusedPageGeneration: 4));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(true, glyphPageIndex: 2, glyphPageGeneration: 3, reusedPageIndex: 2, reusedPageGeneration: 4));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(true, glyphPageIndex: 1, glyphPageGeneration: 4, reusedPageIndex: 2, reusedPageGeneration: 4));
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(true, glyphPageIndex: 2, glyphPageGeneration: 4, reusedPageIndex: 2, reusedPageGeneration: 4));
    }

    [Fact]
    public void Glyph_atlas_page_reuse_reset_marks_full_page_dirty_and_clears_usage()
    {
        var reset = GlyphAtlasTextCompositionHelpers.CreatePageReuseResetState(atlasWidth: 1024, atlasHeight: 1024, atlasPadding: 1);

        Assert.Equal(1, reset.NextX);
        Assert.Equal(1, reset.NextY);
        Assert.Equal(0, reset.RowHeight);
        Assert.True(reset.IsDirty);
        Assert.Equal(0, reset.DirtyLeft);
        Assert.Equal(0, reset.DirtyTop);
        Assert.Equal(1024, reset.DirtyRight);
        Assert.Equal(1024, reset.DirtyBottom);
        Assert.Equal(0, reset.UsedPixels);
        Assert.Equal(0, reset.AllocatedPixels);
        Assert.Equal(0, reset.LastUsedSerial);
    }

    [Fact]
    public void Glyph_atlas_page_selection_prefers_strictly_older_pages()
    {
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(long.MaxValue, candidateLastUsedSerial: 12));
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial: 12, candidateLastUsedSerial: 3));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial: 12, candidateLastUsedSerial: 12));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial: 12, candidateLastUsedSerial: 30));
    }

    [Fact]
    public void Glyph_atlas_writable_page_selection_prefers_remaining_space_then_older_pages()
    {
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels: -1, selectedLastUsedSerial: long.MaxValue, candidateAvailablePixels: 100, candidateLastUsedSerial: 8));
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels: 100, selectedLastUsedSerial: 3, candidateAvailablePixels: 200, candidateLastUsedSerial: 9));
        Assert.True(GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels: 200, selectedLastUsedSerial: 9, candidateAvailablePixels: 200, candidateLastUsedSerial: 3));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels: 200, selectedLastUsedSerial: 3, candidateAvailablePixels: 100, candidateLastUsedSerial: 1));
        Assert.False(GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels: 200, selectedLastUsedSerial: 3, candidateAvailablePixels: 200, candidateLastUsedSerial: 9));
    }

    [Fact]
    public void Glyph_atlas_current_record_reuse_only_accepts_pages_not_touched_this_record()
    {
        Assert.True(GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(pageLastUsedSerial: 3, currentRecordSerial: 4));
        Assert.False(GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(pageLastUsedSerial: 4, currentRecordSerial: 4));
        Assert.False(GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(pageLastUsedSerial: 5, currentRecordSerial: 4));
    }

    [Fact]
    public void Glyph_atlas_record_resource_guards_require_gpu_resources()
    {
        Assert.True(GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(hasTexture: true, hasUpload: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(hasTexture: false, hasUpload: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(hasTexture: true, hasUpload: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasAtlasDrawResources(hasSrvHeap: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasAtlasDrawResources(hasSrvHeap: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(hasPipelineState: true, hasRootSignature: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(hasPipelineState: false, hasRootSignature: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(hasPipelineState: true, hasRootSignature: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphRecordCommandList(hasCommandList: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphRecordCommandList(hasCommandList: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFactory4: true, hasFontCollection: true, hasTextAnalyzer: true, hasFontFallback: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: false, hasFactory4: true, hasFontCollection: true, hasTextAnalyzer: true, hasFontFallback: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFactory4: false, hasFontCollection: true, hasTextAnalyzer: true, hasFontFallback: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFactory4: true, hasFontCollection: false, hasTextAnalyzer: true, hasFontFallback: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFactory4: true, hasFontCollection: true, hasTextAnalyzer: false, hasFontFallback: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFactory4: true, hasFontCollection: true, hasTextAnalyzer: true, hasFontFallback: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(hasFontFace: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(hasFontFace: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(hasFontFamily: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(hasFontFamily: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(hasFont: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(hasFont: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphRunAnalysisResource(hasGlyphRunAnalysis: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphRunAnalysisResource(hasGlyphRunAnalysis: false));
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphVertexUploadResource(hasVertexUploadBuffer: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphVertexUploadResource(hasVertexUploadBuffer: false));
    }

    [Fact]
    public void Glyph_atlas_dirty_rect_merges_new_glyph_bounds()
    {
        var first = GlyphAtlasTextCompositionHelpers.MergeDirtyRect(
            hasDirtyRect: false,
            currentLeft: 1024,
            currentTop: 1024,
            currentRight: 0,
            currentBottom: 0,
            x: 10,
            y: 20,
            width: 30,
            height: 40);
        Assert.Equal(new GlyphAtlasDirtyRect(true, 10, 20, 40, 60), first);

        var merged = GlyphAtlasTextCompositionHelpers.MergeDirtyRect(
            first.HasDirtyRect,
            first.Left,
            first.Top,
            first.Right,
            first.Bottom,
            x: 5,
            y: 35,
            width: 12,
            height: 8);
        Assert.Equal(new GlyphAtlasDirtyRect(true, 5, 20, 40, 60), merged);

        var ignored = GlyphAtlasTextCompositionHelpers.MergeDirtyRect(
            merged.HasDirtyRect,
            merged.Left,
            merged.Top,
            merged.Right,
            merged.Bottom,
            x: 1,
            y: 1,
            width: 0,
            height: 10);
        Assert.Equal(merged, ignored);
    }

    [Fact]
    public void Glyph_atlas_initialization_wrapper_preserves_phase_and_existing_initialization_exception()
    {
        var inner = new InvalidOperationException("compile failed");
        var wrapped = GlyphAtlasTextCompositionHelpers.WrapInitializationException(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.ShaderCompile,
            inner);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.ShaderCompile, wrapped.Phase);
        Assert.Same(inner, wrapped.InnerException);

        var existing = new D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationException(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.PSO,
            new InvalidOperationException("pso"));
        Assert.Same(
            existing,
            GlyphAtlasTextCompositionHelpers.WrapInitializationException(
                D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.RootSignature,
                existing));
    }

    [Fact]
    public void Glyph_atlas_embedded_shader_bytecode_decodes_for_packaging_guard()
    {
        var lengths = D3D12GlyphAtlasTextRenderer.GetEmbeddedShaderBytecodeLengths();

        Assert.True(lengths.VertexBytes >= 4);
        Assert.True(lengths.PixelBytes >= 4);
        Assert.True(lengths.BgraPixelBytes >= 4);
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.VertexHeader));
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.PixelHeader));
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.BgraPixelHeader));
    }

    [Fact]
    public void D3D12_rect_pass_embedded_shader_bytecode_decodes_for_packaging_guard()
    {
        var lengths = D3D12Renderer2D.GetEmbeddedShaderBytecodeLengths();

        Assert.True(lengths.VertexBytes >= 4);
        Assert.True(lengths.PixelBytes >= 4);
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.VertexHeader));
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.PixelHeader));
    }

    [Fact]
    public void D3D12_upload_map_paths_unmap_in_finally()
    {
        var root = FindRepoRoot();
        var rectSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer2D.cs")));
        var glyphSource = ReadGlyphAtlasRendererSources(root);

        Assert.Equal(1, CountOccurrences(rectSource, "finally\n        {\n            vbuf->Unmap(0, null);\n        }"));
        Assert.Equal(1, CountOccurrences(glyphSource, "finally\n        {\n            vbuf->Unmap(0, null);\n        }"));
        Assert.Equal(1, CountOccurrences(glyphSource, "finally\n        {\n            upload->Unmap(0, null);\n        }"));
    }

    [Fact]
    public void D3D12_upload_resources_are_frame_slot_owned()
    {
        var root = FindRepoRoot();
        var rendererSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer.cs")));
        var rectSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer2D.cs")));
        var glyphSource = ReadGlyphAtlasRendererSources(root);

        Assert.Contains("var frameResourceIndex = (int)_frameIndex;", rendererSource);
        Assert.Contains("RenderRectangles(_list, rects, width, height, frameResourceIndex)", rendererSource);
        Assert.Contains("TryRecordGlyphAtlasTextPass(textRuns, resources, frameResourceIndex, width, height)", rendererSource);
        Assert.Contains("_glyphAtlasTextRenderer?.BeginFrame(frameResourceIndex);", rendererSource);
        Assert.DoesNotContain("WaitForReusableUploadResources", rendererSource);
        Assert.DoesNotContain("ReusableUploadResourceWait", rendererSource);

        Assert.Contains("private const int UploadFrameCount = 2;", rectSource);
        Assert.Contains("private readonly ID3D12Resource*[] _vbufs = new ID3D12Resource*[UploadFrameCount];", rectSource);
        Assert.Contains("private readonly D3D12_VERTEX_BUFFER_VIEW[] _vbvs = new D3D12_VERTEX_BUFFER_VIEW[UploadFrameCount];", rectSource);
        Assert.Contains("private void CreateVertexBuffers()", rectSource);
        Assert.Contains("var uploadSlot = frameResourceIndex % UploadFrameCount;", rectSource);
        Assert.Contains("var vbuf = _vbufs[uploadSlot];", rectSource);
        Assert.Contains("var vbv = _vbvs[uploadSlot];", rectSource);

        Assert.Contains("private const int UploadFrameCount = 2;", glyphSource);
        Assert.Contains("private readonly ID3D12Resource*[] _vbufs = new ID3D12Resource*[UploadFrameCount];", glyphSource);
        Assert.Contains("private readonly D3D12_VERTEX_BUFFER_VIEW[] _vbvs = new D3D12_VERTEX_BUFFER_VIEW[UploadFrameCount];", glyphSource);
        Assert.Contains("private void CreateVertexBuffers()", glyphSource);
        Assert.Contains("int frameResourceIndex)", glyphSource);
        Assert.Contains("var upload = page.Uploads[uploadSlot];", glyphSource);
        Assert.Contains("public ID3D12Resource*[] Uploads { get; } = uploads;", glyphSource);
        Assert.DoesNotContain("public ID3D12Resource* Upload", glyphSource);
    }

    [Fact]
    public void D3D12_swapchain_intermediate_com_objects_release_in_finally()
    {
        var root = FindRepoRoot();
        var rendererSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer.cs")));

        Assert.Equal(1, CountOccurrences(rendererSource, "factory->CreateSwapChainForHwnd("));
        Assert.Equal(1, CountOccurrences(rendererSource, "sc1->QueryInterface(typeof(IDXGISwapChain3).GUID"));
        Assert.Contains("IDXGIFactory4* factory = null;", rendererSource);
        Assert.Contains("IDXGISwapChain1* sc1 = null;", rendererSource);
        Assert.Contains("finally\n        {\n            if (sc1 != null) sc1->Release();\n            if (factory != null) factory->Release();\n        }", rendererSource);
    }

    [Fact]
    public void D3D12_core_resource_creation_uses_shared_guarded_path()
    {
        var root = FindRepoRoot();
        var rendererSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer.cs")));

        Assert.Equal(1, CountOccurrences(rendererSource, "PInvoke.D3D12CreateDevice("));
        Assert.Equal(1, CountOccurrences(rendererSource, "_device->CreateCommandQueue("));
        Assert.Equal(1, CountOccurrences(rendererSource, "_device->CreateDescriptorHeap("));
        Assert.Equal(1, CountOccurrences(rendererSource, "_device->CreateCommandList("));
        Assert.Equal(1, CountOccurrences(rendererSource, "_device->CreateFence("));
        Assert.Contains("catch\n        {\n            ReleaseDeviceResources(waitForGpu: false);\n            throw;\n        }", rendererSource);
        Assert.Contains("catch (Exception ex)\n        {\n            ReleaseDeviceResources(waitForGpu: false);\n            _deviceRemoved = true;", rendererSource);
        Assert.Contains("ReleaseDeviceResources(waitForGpu: true);", rendererSource);
        Assert.Contains("return (ID3D12Device*)RequirePointer(deviceObj, \"D3D12Renderer.D3D12CreateDevice returned a null device.\");", rendererSource);
        Assert.Contains("_list = (ID3D12GraphicsCommandList*)RequirePointer(listObj, \"D3D12Renderer.CreateCommandList returned a null command list.\");", rendererSource);
    }

    [Fact]
    public void D3D12_renderer_sources_use_glyph_atlas_owned_paths()
    {
        var root = FindRepoRoot();
        var platformWindows = Path.Combine(root, "src", "Irix.Platform.Windows");

        var direct2DCommonReferences = 0;
        var d2dPointReferences = 0;
        foreach (var sourcePath in Directory.EnumerateFiles(platformWindows, "*.cs"))
        {
            var source = NormalizeLineEndings(File.ReadAllText(sourcePath));
            Assert.DoesNotContain("CreateTextLayout", source);
            direct2DCommonReferences += CountOccurrences(source, "Windows.Win32.Graphics.Direct2D.Common");
            d2dPointReferences += CountOccurrences(source, "D2D_POINT_2F");
            var allowsD2dPointInterop = sourcePath.EndsWith("D3D12GlyphAtlasTextRenderer.ColorGlyph.cs", StringComparison.Ordinal)
                || sourcePath.EndsWith("DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs", StringComparison.Ordinal);
            if (!allowsD2dPointInterop)
            {
                Assert.DoesNotContain("Windows.Win32.Graphics.Direct2D.Common", source);
                Assert.DoesNotContain("D2D_POINT_2F", source);
            }
        }

        Assert.True(direct2DCommonReferences <= 2);
        Assert.True(d2dPointReferences <= 2);

        var nativeMethods = NormalizeLineEndings(File.ReadAllText(Path.Combine(platformWindows, "NativeMethods.txt")));
        var nativeMethodLines = nativeMethods.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.DoesNotContain("IDWriteFactory2", nativeMethods);
        Assert.Contains("IDWriteFontFace4", nativeMethods);
        Assert.Contains("IDWriteFactory4", nativeMethods);
        Assert.DoesNotContain("IDWriteColorGlyphRunEnumerator", nativeMethodLines);
        Assert.Contains("IDWriteColorGlyphRunEnumerator1", nativeMethodLines);
        Assert.Contains("IDWriteTextAnalysisSink", nativeMethods);
        Assert.Contains("CoCreateInstance", nativeMethods);
        Assert.Contains("CoInitializeEx", nativeMethods);
        Assert.Contains("CoUninitialize", nativeMethods);
        Assert.Contains("IWICImagingFactory", nativeMethods);
        Assert.Contains("IWICStream", nativeMethods);
        Assert.Contains("IWICBitmapDecoder", nativeMethods);
        Assert.Contains("IWICBitmapFrameDecode", nativeMethods);
        Assert.Contains("IWICFormatConverter", nativeMethods);

        var diagnosticBaseline = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "diagnostic-baseline.ps1")));
        Assert.Contains("--diagnose-sync", diagnosticBaseline);
        Assert.Contains("--diagnose-text-cache", diagnosticBaseline);
        Assert.Contains("[ValidateSet(\"Sync\", \"TextCache\", \"Smoke\", \"All\")]", diagnosticBaseline);
        Assert.DoesNotContain("SyncStrategy", diagnosticBaseline);
    }

    [Fact]
    public void Active_sources_keep_final_text_composition_on_glyph_atlas_path()
    {
        var root = FindRepoRoot();
        var d2dPointReferences = 0;
        foreach (var sourcePath in EnumerateActiveSourceGuardFiles(root))
        {
            var source = NormalizeLineEndings(File.ReadAllText(sourcePath));
            Assert.DoesNotContain("CreateTextLayout", source);

            var allowsD2dPointInterop = sourcePath.EndsWith(Path.Combine("Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.ColorGlyph.cs"), StringComparison.Ordinal)
                || sourcePath.EndsWith(Path.Combine("Irix.Platform.Windows", "DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs"), StringComparison.Ordinal);
            if (allowsD2dPointInterop)
            {
                d2dPointReferences += CountOccurrences(source, "D2D_POINT_2F");
            }
            else
            {
                Assert.DoesNotContain("Windows.Win32.Graphics.Direct2D.Common", source);
                Assert.DoesNotContain("D2D_POINT_2F", source);
            }
        }

        Assert.Equal(2, d2dPointReferences);
    }

    [Fact]
    public void Glyph_atlas_renderer_source_boundaries_keep_wic_and_dwrite_oracles_on_owned_paths()
    {
        var root = FindRepoRoot();
        var platformWindows = Path.Combine(root, "src", "Irix.Platform.Windows");
        var glyphSource = ReadGlyphAtlasRendererSources(root);
        var renderer2DSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(platformWindows, "D3D12Renderer2D.cs")));

        foreach (var token in new[]
        {
            "public D3D12GlyphAtlasTextRenderer(",
            "public bool IsDisabled",
            "public DeviceErrorDiagnostic DeviceError",
            "public void BeginFrame",
            "public GlyphAtlasTextRendererDiagnostics GetDiagnostics",
            "public void ResetDiagnostics",
            "public GlyphAtlasRecordResult TryRecord",
            "public readonly struct GlyphAtlasRecordResult",
            "public enum GlyphAtlasInitializationPhase",
            "public enum GlyphAtlasRecordFailurePhase",
            "public sealed class GlyphAtlasInitializationException",
            "public enum GlyphAtlasFallbackReason",
            "public readonly struct GlyphAtlasFallbackReasonCounts",
            "public readonly struct GlyphAtlasTextRendererDiagnostics"
        })
        {
            Assert.DoesNotContain(token, glyphSource);
        }

        Assert.Contains("internal D3D12GlyphAtlasTextRenderer(", glyphSource);
        Assert.Contains("internal bool IsDisabled", glyphSource);
        Assert.Contains("internal DeviceErrorDiagnostic DeviceError", glyphSource);
        Assert.Contains("internal void BeginFrame", glyphSource);
        Assert.Contains("internal GlyphAtlasTextRendererDiagnostics GetDiagnostics", glyphSource);
        Assert.Contains("internal void ResetDiagnostics", glyphSource);
        Assert.Contains("internal GlyphAtlasRecordResult TryRecord", glyphSource);
        Assert.Contains("internal readonly struct GlyphAtlasRecordResult", glyphSource);
        Assert.Contains("internal enum GlyphAtlasInitializationPhase", glyphSource);
        Assert.Contains("internal enum GlyphAtlasRecordFailurePhase", glyphSource);
        Assert.Contains("internal sealed class GlyphAtlasInitializationException", glyphSource);
        Assert.Contains("internal enum GlyphAtlasFallbackReason", glyphSource);
        Assert.Contains("internal readonly struct GlyphAtlasFallbackReasonCounts", glyphSource);
        Assert.Contains("internal readonly struct GlyphAtlasTextRendererDiagnostics", glyphSource);
        Assert.Contains("private readonly int[] _usedVertices", glyphSource);
        Assert.Contains("private int AllocateVertexUploadRange", glyphSource);
        Assert.Contains("(uint)(baseVertex + batch.StartVertex)", glyphSource);
        Assert.DoesNotContain("public struct Vertex", renderer2DSource);
        Assert.DoesNotContain("public readonly struct RectData", renderer2DSource);
        Assert.Contains("private struct Vertex", renderer2DSource);
        Assert.Contains("internal readonly struct RectData", renderer2DSource);

        AssertSourceTokensOnlyIn(
            platformWindows,
            [
                "Windows.Win32.Graphics.Imaging",
                "Windows.Win32.System.Com;",
                "IWIC",
                "WICBitmap",
                "WICDecodeOptions",
                "WicImagingFactoryClsid",
                "WicPixelFormat32bppPbgra",
                "PInvoke.CoCreateInstance<IWICImagingFactory>",
                "PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED)",
                "PInvoke.CoUninitialize();",
                "CLSCTX.CLSCTX_INPROC_SERVER",
                "COINIT.COINIT_MULTITHREADED",
                "CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad)"
            ],
            [
                "D3D12GlyphAtlasTextRenderer.Rasterization.cs",
                "DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs"
            ]);

        AssertSourceTokensOnlyIn(
            platformWindows,
            [
                "_wicDecodeScratch",
                "_wicFactoryUnavailable",
                "_wicComInitializedForFactory",
                "_wicComInitializationThreadId",
                "_wicFactory",
                "private void ReleaseWicFactory()"
            ],
            ["D3D12GlyphAtlasTextRenderer.Rasterization.cs"]);

        AssertSourceTokensOnlyIn(
            platformWindows,
            ["TranslateColorGlyphRun"],
            [
                "D3D12GlyphAtlasTextRenderer.ColorGlyph.cs",
                "DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs"
            ]);

        AssertSourceTokensOnlyIn(
            platformWindows,
            ["GetGlyphImageData"],
            [
                "D3D12GlyphAtlasTextRenderer.Rasterization.cs",
                "DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs"
            ]);

        AssertSourceTokensOnlyIn(
            platformWindows,
            [
                "_textAnalyzer->AnalyzeScript(",
                "_textAnalyzer->AnalyzeBidi(",
                "_textAnalyzer->GetGlyphs(",
                "_textAnalyzer->GetGlyphPlacements("
            ],
            ["D3D12GlyphAtlasTextRenderer.ShapingAnalysis.cs"]);

        AssertSourceTokensOnlyIn(
            platformWindows,
            [
                "analyzer->AnalyzeScript(",
                "analyzer->AnalyzeBidi(",
                "analyzer->AnalyzeLineBreakpoints(",
                "analyzer->GetGlyphs(",
                "analyzer->GetGlyphPlacements("
            ],
            [
                "DWriteBidiOracleDiagnostic.optional-diagnostics.cs",
                "DWriteGlyphOracleDiagnostic.optional-diagnostics.cs"
            ]);
    }

    [Fact]
    public void D3D12_text_run_ir_does_not_retain_text_strings()
    {
        var root = FindRepoRoot();
        var textRunSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12TextRun.cs")));

        Assert.Contains("TextSlice Text", textRunSource);
        Assert.Contains("public TextSlice Text { get; }", textRunSource);
        Assert.DoesNotContain("string Text", textRunSource);
        Assert.DoesNotContain("Text.ToString()", textRunSource);
    }

    [Fact]
    public void Glyph_atlas_cache_uses_stable_entry_handles()
    {
        var root = FindRepoRoot();
        var glyphSource = ReadGlyphAtlasRendererSources(root);

        Assert.Contains("private readonly Dictionary<GlyphKey, GlyphAtlasEntryHandle> _glyphs", glyphSource);
        Assert.Contains("private readonly List<GlyphEntry> _glyphEntries", glyphSource);
        Assert.Contains("internal const int AtlasPageBudget = 48;", glyphSource);
        Assert.Contains("private const int AtlasPagePixels = AtlasWidth * AtlasHeight;", glyphSource);
        Assert.Contains("private const int AtlasBudgetPixels = AtlasPageBudget * AtlasPagePixels;", glyphSource);
        Assert.Contains("public int AtlasBudgetPages => AtlasPageBudget;", glyphSource);
        Assert.Contains("public int AtlasAlphaPages { get; } = AtlasAlphaPages;", glyphSource);
        Assert.Contains("public int AtlasBgraPages { get; } = AtlasBgraPages;", glyphSource);
        Assert.Contains("WithAtlasPageCounts(_atlasPages.Count, pageUsage.AlphaPageCount, pageUsage.BgraPageCount)", glyphSource);
        Assert.Contains("private readonly List<int> _freeGlyphEntryIndices", glyphSource);
        Assert.Contains("private readonly List<int> _runGlyphEntryIndices = new(128);", glyphSource);
        Assert.Contains("private readonly List<GlyphEntryMutationState> _runGlyphEntryStates = new(128);", glyphSource);
        Assert.Contains("private readonly List<GlyphAtlasPage> _atlasPages = new(AtlasPageBudget);", glyphSource);
        Assert.Contains("private readonly GlyphAtlasPageMutationState[] _runPageStates = new GlyphAtlasPageMutationState[AtlasPageBudget];", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle _activeAtlasPage;", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle _runActiveAtlasPage;", glyphSource);
        Assert.Contains("private GlyphAtlasPageReuseRequest _pendingAlphaAtlasPageReuse;", glyphSource);
        Assert.Contains("private GlyphAtlasPageReuseRequest _pendingBgraAtlasPageReuse;", glyphSource);
        Assert.Contains("private GlyphAtlasPageReuseRequest _runPendingAlphaAtlasPageReuse;", glyphSource);
        Assert.Contains("private GlyphAtlasPageReuseRequest _runPendingBgraAtlasPageReuse;", glyphSource);
        Assert.Contains("private long _glyphRecordSerial;", glyphSource);
        Assert.Contains("private bool _runAtlasMutationActive;", glyphSource);
        Assert.Contains("private bool _runAtlasMutationUsedPageReuse;", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasEntryHandle(int Index, int Generation)", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageHandle(int Index, int Generation)", glyphSource);
        Assert.Contains("private readonly struct GlyphAtom(byte Kind, uint CodePoint, ushort GlyphIndex, byte Flags)", glyphSource);
        Assert.Contains("public static GlyphAtom SimpleCodePoint(uint codePoint, ushort glyphIndex)", glyphSource);
        Assert.Contains("public static GlyphAtom ShapedPlacement(ushort glyphIndex, bool isDiacritic, bool isZeroWidthSpace)", glyphSource);
        Assert.Contains("private readonly struct FontFaceIdentity(int Value)", glyphSource);
        Assert.Contains("private readonly struct GlyphKey(FontFaceIdentity FontFace, float EmSize, GlyphAtom Glyph)", glyphSource);
        Assert.Contains("private static bool TryMapCharacterToSimpleGlyph(CachedFontFace fontFace, char character, out GlyphAtom glyph)", glyphSource);
        Assert.Contains("GlyphAtom.SimpleCodePoint(codePoint, glyphIndex[0])", glyphSource);
        Assert.Contains("private IDWriteTextAnalyzer* _textAnalyzer;", glyphSource);
        Assert.Contains("private IDWriteFontFallback* _fontFallback;", glyphSource);
        Assert.Contains("private IDWriteFactory4* _dwriteFactory4;", glyphSource);
        Assert.Contains("GlyphAtlasInitializationPhase.TextAnalyzer", glyphSource);
        Assert.Contains("GlyphAtlasInitializationPhase.FontFallback", glyphSource);
        Assert.Contains("_dwriteFactory->CreateTextAnalyzer(&textAnalyzer);", glyphSource);
        Assert.Contains("_dwriteFactory->QueryInterface<IDWriteFactory4>(out var factory4).ThrowOnFailure();", glyphSource);
        Assert.Contains("_dwriteFactory4->GetSystemFontFallback(&fontFallback);", glyphSource);
        Assert.Contains("_textAnalyzer = textAnalyzer;", glyphSource);
        Assert.Contains("private ShapedGlyph[] _shapedGlyphScratch = [];", glyphSource);
        Assert.Contains("private ShapedGlyphSegment[] _shapedSegmentScratch = [];", glyphSource);
        Assert.Contains("private ShapedGlyphLine[] _shapedLineScratch = [];", glyphSource);
        Assert.Contains("private GlyphAtlasLayoutLine[] _shapedLayoutLineScratch = [];", glyphSource);
        Assert.Contains("private float[] _shapedTextAdvanceScratch = [];", glyphSource);
        Assert.Contains("private const int MaxShapedRunSegments = 64;", glyphSource);
        Assert.Contains("private bool TryProbeShapedRun(ReadOnlySpan<char> text, TextStyle style, float maxLineWidth, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)", glyphSource);
        Assert.Contains("private bool TryShapeRun(ReadOnlySpan<char> text, TextStyle style, CachedFontFace baseFontFace, float maxLineWidth, bool requiresColorGlyph, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)", glyphSource);
        Assert.Contains("private bool TryShapeTextRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)", glyphSource);
        Assert.Contains("private bool TryShapeTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)", glyphSource);
        Assert.Contains("private bool TryShapeBidiLevelRuns(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)", glyphSource);
        Assert.Contains("private bool TryShapeUniformBidiTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, byte bidiLevel, ref int glyphStart, ref int segmentCount)", glyphSource);
        Assert.Contains("private bool TryAppendShapedControlSegment(TextStyle style, CachedFontFace baseFontFace, int textStart, int glyphStart, ref int segmentCount, int spaceCount)", glyphSource);
        Assert.Contains("private bool TryShapeSegmentedFallbackRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)", glyphSource);
        Assert.Contains("private bool TryAssignShapedTextAdvances(int textStart, int textLength, int glyphStart, int glyphCount)", glyphSource);
        Assert.Contains("private bool TryBuildShapedLinesFromLayout(ReadOnlySpan<char> text, int segmentCount, int plannedLineCount, out int lineCount)", glyphSource);
        Assert.Contains("private void ApplyShapedLineVisualOrder(int segmentStart, int segmentCount, bool lineIsRightToLeft)", glyphSource);
        Assert.Contains("private void ReverseShapedSegments(int start, int end)", glyphSource);
        Assert.Contains("TryShapeTextSegment(", glyphSource);
        Assert.Contains("private bool TryGetCachedFallbackFontFace(IDWriteFont* font, out CachedFontFace fontFace)", glyphSource);
        Assert.Contains("_fontFallback->MapCharacters(", glyphSource);
        Assert.Contains("var fontFace = baseFontFace;", glyphSource);
        Assert.Contains("TryBuildShapedAtlasRun(text, textRun, style, shapedRun", glyphSource);
        Assert.Contains("private bool TryBuildShapedAtlasRun(", glyphSource);
        Assert.Contains("private bool TryAppendShapedSegment(", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text.Slice(shapedSegment.TextStart, shapedSegment.TextLength))", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate(text)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character)", glyphSource);
        Assert.Contains("_textAnalyzer->AnalyzeScript(", glyphSource);
        Assert.Contains("_textAnalyzer->AnalyzeBidi(", glyphSource);
        Assert.Contains("IDWriteTextAnalysisSink*", glyphSource);
        Assert.Contains("private DWRITE_SCRIPT_ANALYSIS[] _shapeScriptScratch = [];", glyphSource);
        Assert.Contains("private byte[] _shapeBidiLevelScratch = [];", glyphSource);
        Assert.Contains("private bool HasRightToLeftBidiLevel(int textStart, int textLength)", glyphSource);
        Assert.Contains("private bool TryGetUniformBidiLevel(int textStart, int textLength, out byte bidiLevel)", glyphSource);
        Assert.Contains("private void PromoteRtlSpanBaseLevel(ReadOnlySpan<char> text, int textStart, int textLength)", glyphSource);
        Assert.Contains("private static bool IsRtlOnlyStrongSpan(ReadOnlySpan<char> text)", glyphSource);
        Assert.Contains("private static DWRITE_READING_DIRECTION DetermineParagraphReadingDirection(ReadOnlySpan<char> text)", glyphSource);
        Assert.Contains("private static bool TryDetermineRangeReadingDirection(ReadOnlySpan<char> text, int start, int end, out DWRITE_READING_DIRECTION direction)", glyphSource);
        Assert.Contains("private static bool TryGetStrongReadingDirection(char character, out DWRITE_READING_DIRECTION direction)", glyphSource);
        Assert.Contains("public DWRITE_READING_DIRECTION ReadingDirection;", glyphSource);
        Assert.Contains("public byte BidiLevel { get; } = BidiLevel;", glyphSource);
        Assert.Contains("public bool IsRightToLeft => (BidiLevel & 1) != 0;", glyphSource);
        Assert.Contains("private readonly struct ShapedGlyphLine(int SegmentStart, int SegmentCount, int GlyphStart, int GlyphCount, float Width, byte BidiLevel)", glyphSource);
        Assert.Contains("if (line.IsRightToLeft)", glyphSource);
        Assert.Contains("bidiLevel = shapedSegment.BidiLevel", glyphSource);
        Assert.Contains("return TryShapeBidiLevelRuns(text, textStart, textLength, style, baseFontFace, ref glyphStart, ref segmentCount);", glyphSource);
        Assert.Contains("ApplyShapedLineVisualOrder(lineSegmentStart, lineSegmentCount, lineIsRightToLeft);", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(segments, lineBidiLevels);", glyphSource);
        Assert.Contains("ReverseShapedSegments(segmentStart, segmentStart + segmentCount - 1);", glyphSource);
        Assert.Contains("var firstGlyphSegmentBidiLevel = (byte)0;", glyphSource);
        Assert.Contains("firstGlyphSegmentBidiLevel = segment.BidiLevel;", glyphSource);
        Assert.Contains("TryDetermineRangeReadingDirection(text, plannedLine.Start, plannedLine.End, out var lineDirection)", glyphSource);
        Assert.Contains("lineDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT", glyphSource);
        Assert.Contains(": hasGlyphSegment && (firstGlyphSegmentBidiLevel & 1) != 0;", glyphSource);
        Assert.Contains("lineIsRightToLeft ? (byte)1 : (byte)0", glyphSource);
        Assert.Contains("TryAppendColorGlyphSegmentLayers(", glyphSource);
        Assert.Contains("TryAppendColorGlyphLayer(", glyphSource);
        Assert.Contains("private bool TryGetColorLayerGlyph(", glyphSource);
        Assert.Contains("private bool TryAppendBgraColorGlyphSegment(", glyphSource);
        Assert.Contains("private bool TryGetBgraColorGlyph(", glyphSource);
        Assert.Contains("private bool RasterizeBgraColorGlyph(", glyphSource);
        Assert.Contains("private bool TryAppendEncodedBitmapColorGlyphSegment(", glyphSource);
        Assert.Contains("private bool TryGetEncodedBitmapColorGlyph(", glyphSource);
        Assert.Contains("private bool RasterizeEncodedBitmapColorGlyph(", glyphSource);
        Assert.Contains("private bool TryDecodeWicGlyphImage(", glyphSource);
        Assert.Contains("private bool TryEnsureWicFactory(", glyphSource);
        Assert.Contains("fontFace.Face4->GetGlyphImageData(", glyphSource);
        Assert.Contains("fontFace.Face4->ReleaseGlyphImageData(glyphDataContext);", glyphSource);
        Assert.Contains("SelectWritableAtlasPage(GlyphAtlasPageFormat.Bgra, width, height, recordSerial)", glyphSource);
        Assert.Contains("GlyphAtom.BgraGlyph(glyphIndex, pixelsPerEm)", glyphSource);
        Assert.Contains("GlyphAtom.EncodedBitmapGlyph(glyphIndex, pixelsPerEm, GetEncodedBitmapGlyphFormatId(imageFormat))", glyphSource);
        Assert.Contains("public static GlyphAtom EncodedBitmapGlyph(ushort glyphIndex, uint pixelsPerEm, byte formatId)", glyphSource);
        Assert.Contains("PInvoke.CoCreateInstance<IWICImagingFactory>", glyphSource);
        Assert.Contains("PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED)", glyphSource);
        Assert.Contains("PInvoke.CoUninitialize();", glyphSource);
        Assert.Contains("private void ReleaseWicFactory()", glyphSource);
        Assert.Contains("_wicFactory->Release();\n            _wicFactory = null;", glyphSource);
        Assert.Contains("_wicComInitializedForFactory = false;\n        _wicComInitializationThreadId = 0;", glyphSource);
        Assert.Contains("IWICStream* stream = null;", glyphSource);
        Assert.Contains("IWICBitmapDecoder* decoder = null;", glyphSource);
        Assert.Contains("IWICBitmapFrameDecode* frame = null;", glyphSource);
        Assert.Contains("IWICFormatConverter* converter = null;", glyphSource);
        Assert.Contains("CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad)", glyphSource);
        Assert.Contains("WICBitmapDitherType.WICBitmapDitherTypeNone", glyphSource);
        Assert.Contains("WICBitmapPaletteType.WICBitmapPaletteTypeCustom", glyphSource);
        Assert.Contains("WicPixelFormat32bppPbgra", glyphSource);
        Assert.Contains("ComputeGlyphImageScale(fontEmSize, glyphData.pixelsPerEm)", glyphSource);
        Assert.Contains("var width = Math.Clamp((int)MathF.Round(entry.U2 * AtlasWidth), x, AtlasWidth) - x;", glyphSource);
        Assert.Contains("private const DWRITE_GLYPH_IMAGE_FORMATS SupportedLayerColorGlyphFormats", glyphSource);
        Assert.Contains("private const DWRITE_GLYPH_IMAGE_FORMATS EncodedBitmapColorGlyphFormats", glyphSource);
        Assert.Contains("private const DWRITE_GLYPH_IMAGE_FORMATS UnsupportedNonLayerColorGlyphFormats", glyphSource);
        Assert.Contains("private bool TryGetUnsupportedOnlyColorGlyphImageFormatReason(ShapedGlyphSegment shapedSegment, out GlyphAtlasFallbackReason unsupportedReason)", glyphSource);
        Assert.Contains("internal static GlyphAtlasFallbackReason GetUnsupportedColorGlyphImageFormatReason(DWRITE_GLYPH_IMAGE_FORMATS formats)", glyphSource);
        Assert.Contains("GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra", glyphSource);
        Assert.Contains("GlyphAtlasFallbackReason.ColorGlyphPaintTree", glyphSource);
        Assert.Contains("private static IDWriteFontFace4* TryQueryFontFace4(IDWriteFontFace* face)", glyphSource);
        Assert.Contains("shapedSegment.FontFace.Face4->GetGlyphImageFormats(", glyphSource);
        Assert.Contains("DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG", glyphSource);
        Assert.Contains("DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG", glyphSource);
        Assert.Contains("DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE", glyphSource);
        Assert.Contains("GlyphAtom.ColorLayer(glyphIndex)", glyphSource);
        Assert.Contains("public static GlyphAtom ColorLayer(ushort glyphIndex)", glyphSource);
        Assert.Contains("public static GlyphAtom BgraGlyph(ushort glyphIndex, uint pixelsPerEm)", glyphSource);
        Assert.Contains("private IDWriteFactory4* _dwriteFactory4;", glyphSource);
        Assert.Contains("private const DWRITE_GLYPH_IMAGE_FORMATS ColorGlyphRunImageFormats", glyphSource);
        Assert.Contains("private const DWRITE_GLYPH_IMAGE_FORMATS ColorGlyphRunImageFormats =\n        SupportedLayerColorGlyphFormats\n        | SupportedBitmapColorGlyphFormats;", glyphSource);
        Assert.Contains("IDWriteColorGlyphRunEnumerator1* colorRuns", glyphSource);
        Assert.Contains("_dwriteFactory4->TranslateColorGlyphRun(", glyphSource);
        Assert.Contains("DWRITE_COLOR_GLYPH_RUN1* colorGlyphRun", glyphSource);
        Assert.Contains("colorGlyphRun->glyphImageFormat", glyphSource);
        Assert.Contains("private bool TryAppendBitmapColorGlyphRun(", glyphSource);
        Assert.DoesNotContain("_dwriteFactory2", glyphSource);
        Assert.DoesNotContain("IDWriteColorGlyphRunEnumerator* colorLayers", glyphSource);
        Assert.Contains("DWRITE_COLOR_GLYPH_RUN* colorGlyphRun", glyphSource);
        Assert.Contains("ResolveColorGlyphLayerColor(baseRun->runColor, baseRun->paletteIndex, currentBrush)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.IsTab(text[position])", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(text[position])", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.PlanLines(", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.TabAdvanceSpaceCount", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ComputeFirstBaselineY(textRun.Y, textRun.Height, style.VerticalAlignment, ascent, lineHeight, lineCount)", glyphSource);
        Assert.DoesNotContain("private static bool TextMetricsFit", glyphSource);
        Assert.DoesNotContain("TextMetricsFit(textRun, lineHeight", glyphSource);
        Assert.Contains("spaceAdvance * spaceCount", glyphSource);
        Assert.Contains("_shapedTextAdvanceScratch[textIndex] = ComputeShapedGlyphAdvance(", glyphSource);
        Assert.Contains("if (shapedSegment.GlyphCount == 0)", glyphSource);
        Assert.Contains("shapedRun.HasMissingGlyph()", glyphSource);
        Assert.Contains("private bool TryGetShapedGlyph(", glyphSource);
        Assert.Contains("GlyphAtom.ShapedPlacement(", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(text.Length)", glyphSource);
        Assert.Contains("fixed (char* textPtr = text)", glyphSource);
        Assert.Contains("_textAnalyzer->GetGlyphs(", glyphSource);
        Assert.Contains("new PCWSTR(textPtr)", glyphSource);
        Assert.Contains("_textAnalyzer->GetGlyphPlacements(", glyphSource);
        Assert.Contains("ProjectShapedGlyphs(glyphStart, glyphCount);", glyphSource);
        Assert.Contains("_diagnostics = _diagnostics.WithShapedGlyphProbe(shapedRun.GlyphCount);", glyphSource);
        Assert.Contains("private void ProjectShapedGlyphs(int glyphStart, int glyphCount)", glyphSource);
        Assert.Contains("ShapedGlyph.FromDirectWrite(glyphIndices[i], advances[i], offsets[i], glyphProps[i])", glyphSource);
        Assert.Contains("private readonly struct ShapedGlyph(", glyphSource);
        Assert.Contains("public static ShapedGlyph FromDirectWrite(", glyphSource);
        Assert.Contains("offset.advanceOffset", glyphSource);
        Assert.Contains("offset.ascenderOffset", glyphSource);
        Assert.Contains("properties.isClusterStart", glyphSource);
        Assert.Contains("properties.isDiacritic", glyphSource);
        Assert.Contains("properties.isZeroWidthSpace", glyphSource);
        Assert.Contains("private readonly struct ShapedGlyphSegment(", glyphSource);
        Assert.Contains("private readonly struct ShapedGlyphLine(", glyphSource);
        Assert.Contains("float ControlAdvance", glyphSource);
        Assert.Contains("public int TextEnd => TextStart + TextLength;", glyphSource);
        Assert.Contains("public float ControlAdvance { get; } = ControlAdvance;", glyphSource);
        Assert.Contains("public IDWriteFontFace4* Face4 { get; } = face4;", glyphSource);
        Assert.Contains("private readonly ref struct ShapedGlyphRun(", glyphSource);
        Assert.Contains("float FontEmSize", glyphSource);
        Assert.Contains("public bool RequiresColorGlyph { get; } = RequiresColorGlyph;", glyphSource);
        Assert.Contains("ReadOnlySpan<ShapedGlyph> Glyphs", glyphSource);
        Assert.Contains("ReadOnlySpan<ShapedGlyphSegment> Segments", glyphSource);
        Assert.Contains("ReadOnlySpan<ShapedGlyphLine> Lines", glyphSource);
        Assert.Contains("ReadOnlySpan<ushort> ClusterMap", glyphSource);
        Assert.Contains("public int GlyphCount => Glyphs.Length;", glyphSource);
        Assert.Contains("public int LineCount => Lines.Length;", glyphSource);
        Assert.Contains("public float ComputeAdvance()", glyphSource);
        Assert.Contains("public float ComputeLineHeight()", glyphSource);
        Assert.Contains("public float ComputeAscent()", glyphSource);
        Assert.Contains("public bool HasMissingGlyph()", glyphSource);
        Assert.DoesNotContain("string Text", glyphSource);
        Assert.DoesNotContain("string SourceText", glyphSource);
        Assert.Contains("Shape probe font lookup skipped", glyphSource);
        Assert.Contains("Shape probe font fallback skipped", glyphSource);
        Assert.Contains("Shape probe GetGlyphs failed", glyphSource);
        Assert.Contains("Shape probe GetGlyphPlacements failed", glyphSource);
        Assert.Contains("public int ShapedProbeRuns { get; } = ShapedProbeRuns;", glyphSource);
        Assert.Contains("public int ShapedProbeGlyphs { get; } = ShapedProbeGlyphs;", glyphSource);
        Assert.Contains("public int ColorLayerRuns { get; } = ColorLayerRuns;", glyphSource);
        Assert.Contains("public int ColorBitmapRuns { get; } = ColorBitmapRuns;", glyphSource);
        Assert.Contains("public GlyphAtlasTextRendererDiagnostics WithShapedGlyphProbe(int glyphCount)", glyphSource);
        Assert.Contains("public GlyphAtlasTextRendererDiagnostics WithColorGlyphRuns(int layerRuns, int bitmapRuns)", glyphSource);
        Assert.Contains("shapedProbeRuns={ShapedProbeRuns}, shapedProbeGlyphs={ShapedProbeGlyphs}, colorLayerRuns={ColorLayerRuns}, colorBitmapRuns={ColorBitmapRuns}", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageReuseRequest(GlyphAtlasPageHandle Page, long RequestedRecordSerial)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(!IsNone, RequestedRecordSerial, recordSerial, oldestRetainedRecordSerial)", glyphSource);
        Assert.Contains("private sealed unsafe class GlyphAtlasPage", glyphSource);
        Assert.Contains("private GlyphAtlasEntryHandle AddGlyphEntry(in GlyphEntry entry)", glyphSource);
        Assert.Contains("_freeGlyphEntryIndices.RemoveAt(freeIndex);", glyphSource);
        Assert.Contains("private bool TryResolveGlyph(GlyphAtlasEntryHandle handle, long recordSerial, out GlyphEntry entry)", glyphSource);
        Assert.Contains("!entry.IsLive || entry.Generation != handle.Generation", glyphSource);
        Assert.Contains("page.Touch(recordSerial);", glyphSource);
        Assert.Contains("entry = entry.WithLastUsedSerial(recordSerial);", glyphSource);
        Assert.Contains("private void BeginAtlasRunMutation()", glyphSource);
        Assert.Contains("private void CommitAtlasRunMutation()", glyphSource);
        Assert.Contains("private void RollbackAtlasRunMutation(long recordSerial, GlyphAtlasFallbackReason reason)", glyphSource);
        var rollbackStart = glyphSource.IndexOf("private void RollbackAtlasRunMutation", StringComparison.Ordinal);
        var rollbackEnd = glyphSource.IndexOf("private void RemoveRunGlyphEntries", StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleAtlasPageReuse(recordSerial);", glyphSource[rollbackStart..rollbackEnd]);
        Assert.Contains("private void RemoveRunGlyphEntries(bool clearPixels)", glyphSource);
        Assert.Contains("private void RestoreRunGlyphEntryTouches()", glyphSource);
        Assert.Contains("private void ClearGlyphEntryPixels(GlyphEntry entry)", glyphSource);
        Assert.Contains("var rowPitch = page.RowPitch;", glyphSource);
        Assert.Contains("var bytesPerPixel = page.BytesPerPixel;", glyphSource);
        Assert.Contains("private void ReleaseAtlasPagesCreatedDuringRun()", glyphSource);
        Assert.Contains("private void ResetPagesReusedDuringRun(out int alphaPageCount, out int bgraPageCount)", glyphSource);
        Assert.Contains("private void RestoreRunPageStates()", glyphSource);
        Assert.Contains("private int CountLiveGlyphEntries()", glyphSource);
        Assert.Contains("BeginAtlasRunMutation();", glyphSource);
        Assert.Contains("RollbackAtlasRunMutation(recordSerial, unsupportedReason);", glyphSource);
        Assert.Contains("CommitAtlasRunMutation();", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle AddAtlasPage(", glyphSource);
        Assert.Contains("private GlyphAtlasPage? TryCreateAdditionalAtlasPage(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("ID3D12Resource*[] uploads, ID3D12DescriptorHeap* srvHeap, D3D12_RESOURCE_STATES textureState)", glyphSource);
        Assert.Contains("private bool TryResolveAtlasPage(GlyphAtlasPageHandle handle,", glyphSource);
        Assert.Contains("entry.Generation != handle.Generation || !TryResolveAtlasPage(entry.Page, out var page)", glyphSource);
        Assert.Contains("GlyphAtlasPageHandle Page", glyphSource);
        Assert.Contains("private bool TryAppendDrawBatch(", glyphSource);
        Assert.Contains("batchSegmentStart = vertexCount;", glyphSource);
        Assert.Contains("private readonly struct GlyphDrawBatch(int StartVertex, int VertexCount, IntegerScissorRect Scissor, GlyphAtlasPageHandle Page)", glyphSource);
        Assert.Contains("_batches[batchCount++] = new GlyphDrawBatch(batchStart, vertexCount - batchStart, scissor, page);", glyphSource);
        Assert.Contains("private GlyphAtlasPage ResolveDrawBatchPage(GlyphAtlasPageHandle pageHandle)", glyphSource);
        Assert.Contains("var page = ResolveDrawBatchPage(batch.Page);", glyphSource);
        Assert.Contains("private enum GlyphAtlasPageFormat : byte", glyphSource);
        Assert.Contains("AlphaAtlasBytesPerPixel = 1", glyphSource);
        Assert.Contains("BgraAtlasBytesPerPixel = 4", glyphSource);
        Assert.Contains("GlyphAtlasPageFormat.Bgra => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM", glyphSource);
        Assert.Contains("GlyphAtlasPageFormat.Bgra => BgraAtlasBytesPerPixel", glyphSource);
        Assert.Contains("private const string BgraPixelShaderBytecodeBase64", glyphSource);
        Assert.Contains("private ID3D12PipelineState* _bgraPso;", glyphSource);
        Assert.Contains("private byte[] _bgraPixelShaderBytecode = [];", glyphSource);
        Assert.Contains("_bgraPso = CreateGlyphPipelineState(_bgraPixelShaderBytecode", glyphSource);
        Assert.Contains("GlyphAtlasPageFormat.Bgra => _bgraPso", glyphSource);
        Assert.Contains("private static int GetAtlasRowPitch(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("private static int GetAtlasPixelBytes(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("private static DXGI_FORMAT GetDxgiFormat(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("SelectWritableAtlasPage(GlyphAtlasPageFormat.Alpha, width, height, recordSerial)", glyphSource);
        Assert.Contains("private GlyphAtlasPage? SelectWritableAtlasPage(GlyphAtlasPageFormat format, int width, int height, long recordSerial)", glyphSource);
        Assert.Contains("private GlyphAtlasPage? FindAtlasPageByFormat(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("page.Format != format", glyphSource);
        Assert.Contains("private GlyphAtlasPage? TryReuseColdAtlasPageForCurrentRecord(GlyphAtlasPageFormat format, int width, int height, long recordSerial)", glyphSource);
        Assert.Contains("var reusedPage = TryReuseColdAtlasPageForCurrentRecord(format, width, height, recordSerial);", glyphSource);
        Assert.Contains("private void ScheduleAtlasPageReuse(long recordSerial, GlyphAtlasPageFormat? format = null)", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle SelectOldestAtlasPageHandle(GlyphAtlasPageFormat? format)", glyphSource);
        Assert.Contains("page.Format != format.GetValueOrDefault()", glyphSource);
        Assert.Contains("ScheduleAtlasPageReuse(recordSerial, format);", glyphSource);
        Assert.Contains("public GlyphAtlasPageFormat Format { get; } = format;", glyphSource);
        Assert.Contains("public int BytesPerPixel => GetAtlasBytesPerPixel(Format);", glyphSource);
        Assert.Contains("public int RowPitch => GetAtlasRowPitch(Format);", glyphSource);
        Assert.Contains("public DXGI_FORMAT DxgiFormat => GetDxgiFormat(Format);", glyphSource);
        Assert.Contains("public ID3D12DescriptorHeap* SrvHeap { get; private set; }", glyphSource);
        Assert.Contains("public ID3D12Resource*[] Uploads { get; } = uploads;", glyphSource);
        Assert.Contains("list->SetGraphicsRootDescriptorTable(0, page.SrvHeap->GetGPUDescriptorHandleForHeapStart());", glyphSource);
        Assert.Contains("D3D12_RESOURCE_STATES textureState", glyphSource);
        Assert.Contains("private D3D12_RESOURCE_STATES _textureState = textureState;", glyphSource);
        Assert.Contains("public D3D12_RESOURCE_BARRIER TransitionTexture(D3D12_RESOURCE_STATES after)", glyphSource);
        Assert.Contains("var barrier = Transition(Texture, _textureState, after);", glyphSource);
        Assert.Contains("_textureState = after;", glyphSource);
        Assert.Contains("var toCopyDest = page.TransitionTexture(D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);", glyphSource);
        Assert.Contains("var toShaderResource = page.TransitionTexture(D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);", glyphSource);
        Assert.Contains("Format = page.DxgiFormat", glyphSource);
        Assert.Contains("Format = dxgiFormat", glyphSource);
        Assert.Contains("Width = (ulong)atlasPixelBytes", glyphSource);
        Assert.Contains("var dirtyRowBytes = dirtyWidth * bytesPerPixel;", glyphSource);
        Assert.Contains("ApplyPendingAtlasPageEviction(recordSerial, oldestRetainedRecordSerial: recordSerial);", glyphSource);
        Assert.Contains("private void ApplyPendingAtlasPageEviction(long recordSerial, long oldestRetainedRecordSerial)", glyphSource);
        Assert.Contains("ApplyPendingAtlasPageEviction(ref _pendingAlphaAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);", glyphSource);
        Assert.Contains("ApplyPendingAtlasPageEviction(ref _pendingBgraAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);", glyphSource);
        Assert.Contains("if (!pendingPageReuse.CanApply(recordSerial, oldestRetainedRecordSerial))", glyphSource);
        Assert.Contains("var reusedPage = page.Handle;", glyphSource);
        Assert.Contains("RemoveGlyphsForReusedPage(reusedPage);", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(", glyphSource);
        Assert.Contains("_freeGlyphEntryIndices.Add(i);", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(page.LastUsedSerial, recordSerial)", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle SelectOldestAtlasPageHandle(GlyphAtlasPageFormat? format)", glyphSource);
        Assert.Equal(2, CountOccurrences(glyphSource, "GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage("));
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(", glyphSource);
        Assert.Contains("private static bool CanAllocateGlyph(GlyphAtlasPage page, int width, int height)", glyphSource);
        Assert.Contains("public int ComputeAvailablePixels()", glyphSource);
        Assert.Contains("private void ScheduleAtlasPageReuse(long recordSerial, GlyphAtlasPageFormat? format = null)", glyphSource);
        Assert.Contains("var page = SelectOldestAtlasPageHandle(format);", glyphSource);
        Assert.Contains("private ref GlyphAtlasPageReuseRequest GetPendingAtlasPageReuse(GlyphAtlasPageFormat format)", glyphSource);
        Assert.Contains("return ref _pendingBgraAtlasPageReuse;", glyphSource);
        Assert.Contains("return ref _pendingAlphaAtlasPageReuse;", glyphSource);
        Assert.Contains("private void CountPendingAtlasPageReuseRequests(out int alphaReuses, out int bgraReuses)", glyphSource);
        Assert.Contains("pending = new GlyphAtlasPageReuseRequest(page, recordSerial);", glyphSource);
        Assert.Contains("_diagnostics = _diagnostics.WithAtlasPageReuseRequest(atlasPage.Format == GlyphAtlasPageFormat.Bgra);", glyphSource);
        Assert.Contains("_diagnostics = _diagnostics.WithAtlasFullWithoutPageReuse(format == GlyphAtlasPageFormat.Bgra);", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.RequireActiveAtlasPage found a stale active glyph atlas page handle.", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.ApplyPendingAtlasPageEviction found a stale pending glyph atlas page reuse handle.", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.DrawGlyphs found a stale glyph atlas page handle.", glyphSource);
        Assert.DoesNotContain("Glyph atlas active page handle is invalid.", glyphSource);
        Assert.DoesNotContain("Glyph atlas pending page reuse handle is stale.", glyphSource);
        Assert.Contains("public GlyphAtlasPageHandle NextGeneration()", glyphSource);
        Assert.Contains("public GlyphAtlasPageHandle ResetForReuse()", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.CreatePageReuseResetState(AtlasWidth, AtlasHeight, AtlasPadding)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphRecordCommandList(list != null)", glyphSource);
        Assert.Contains("RequirePointer(textAnalyzer, \"D3D12GlyphAtlasTextRenderer.CreateTextAnalyzer returned a null text analyzer.\");", glyphSource);
        Assert.Contains("RequirePointer(factory4, \"D3D12GlyphAtlasTextRenderer.QueryInterface(IDWriteFactory4) returned a null factory.\");", glyphSource);
        Assert.Contains("RequirePointer(fontFallback, \"D3D12GlyphAtlasTextRenderer.GetSystemFontFallback returned a null font fallback.\");", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(_dwriteFactory != null, _dwriteFactory4 != null, _fontCollection != null, _textAnalyzer != null, _fontFallback != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(fontFace.Face != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(family != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(font != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphRunAnalysisResource(analysis != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphVertexUploadResource(vbuf != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(page.Texture != null, upload != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(_pso != null && _bgraPso != null, _rootSig != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasAtlasDrawResources(heap != null)", glyphSource);
        Assert.Contains("DirectWrite,", glyphSource);
        Assert.Contains("TextAnalyzer,", glyphSource);
        Assert.Contains("CommandList,", glyphSource);
        Assert.Contains("AtlasPage,", glyphSource);
        Assert.Contains("Pipeline,", glyphSource);
        Assert.Contains("AtlasDraw", glyphSource);
        Assert.Contains("GlyphAtlasRecordFailurePhase.CommandList", glyphSource);
        Assert.Contains("GlyphAtlasRecordFailurePhase.DirectWrite", glyphSource);
        Assert.Contains("GlyphAtlasRecordFailurePhase.AtlasPage", glyphSource);
        Assert.Contains("GlyphAtlasRecordFailurePhase.Pipeline", glyphSource);
        Assert.Contains("GlyphAtlasRecordFailurePhase.AtlasDraw", glyphSource);
        Assert.Contains("public int UsedPixels { get; set; }", glyphSource);
        Assert.Contains("public int AllocatedPixels { get; set; }", glyphSource);
        Assert.Contains("public long LastUsedSerial { get; private set; }", glyphSource);
        Assert.Contains("public void Touch(long serial)", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageMutationState(", glyphSource);
        Assert.Contains("private readonly struct GlyphEntryMutationState(", glyphSource);
        Assert.Contains("public GlyphAtlasPageMutationState CaptureMutationState()", glyphSource);
        Assert.Contains("public void RestoreMutationState(GlyphAtlasPageMutationState state)", glyphSource);
        Assert.Contains("public bool IsLive => LastUsedSerial > 0;", glyphSource);
        Assert.Contains("public int ComputeAllocatedPixels()", glyphSource);
        Assert.Contains("private GlyphAtlasPageUsage GetAtlasPageUsage()", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageUsage(", glyphSource);
        Assert.Contains("int AlphaUsedPixels,", glyphSource);
        Assert.Contains("int BgraUsedPixels,", glyphSource);
        Assert.Contains("int AlphaFragmentedPixels,", glyphSource);
        Assert.Contains("int BgraFragmentedPixels,", glyphSource);
        Assert.Contains("long OldestAlphaPageAge,", glyphSource);
        Assert.Contains("long OldestBgraPageAge)", glyphSource);
        Assert.Contains(".WithAtlasPageUsage(pageUsage.UsedPixels, pageUsage.FragmentedPixels, pageUsage.AlphaUsedPixels, pageUsage.BgraUsedPixels, pageUsage.AlphaFragmentedPixels, pageUsage.BgraFragmentedPixels)", glyphSource);
        Assert.Contains(".WithAtlasTouchMetrics(_glyphRecordSerial, pageUsage.OldestPageAge, pageUsage.NewestPageAge, pageUsage.OldestAlphaPageAge, pageUsage.OldestBgraPageAge)", glyphSource);
        Assert.Contains(".WithAtlasPageCounts(_atlasPages.Count, pageUsage.AlphaPageCount, pageUsage.BgraPageCount)", glyphSource);
        Assert.Contains("CountPendingAtlasPageReuseRequests(out var pendingAlphaReuses, out var pendingBgraReuses);", glyphSource);
        Assert.Contains(".WithAtlasPendingPageReuse(pendingAlphaReuses, pendingBgraReuses)", glyphSource);
        Assert.Contains("RasterScratchBytes: _clearTypeScratch.Length + _grayscaleScratch.Length + GetShapeScratchByteCount()", glyphSource);
        Assert.Contains("_shapeScratchResizeCount = 0;", glyphSource);
        Assert.Contains("public int AtlasBudgetPages => AtlasPageBudget;", glyphSource);
        Assert.Contains("public int AtlasPageWidth => AtlasWidth;", glyphSource);
        Assert.Contains("public int AtlasPageHeight => AtlasHeight;", glyphSource);
        Assert.Contains("public int AtlasCapacityPixels => AtlasBudgetPixels;", glyphSource);
        Assert.Contains("public long AtlasCpuBytes => ComputeAtlasResidentBytes(AtlasAlphaPages, AtlasBgraPages);", glyphSource);
        Assert.Contains("public long AtlasUploadBytes => checked(AtlasGpuBytes * UploadFrameCount);", glyphSource);
        Assert.Contains("public long AtlasGpuBytes => ComputeAtlasResidentBytes(AtlasAlphaPages, AtlasBgraPages);", glyphSource);
        Assert.Contains("private static long ComputeAtlasResidentBytes(int alphaPages, int bgraPages)", glyphSource);
        Assert.Contains("public int AtlasPendingPageReuses { get; } = AtlasPendingPageReuses;", glyphSource);
        Assert.Contains("public int AtlasPendingAlphaPageReuses { get; } = AtlasPendingAlphaPageReuses;", glyphSource);
        Assert.Contains("public int AtlasPendingBgraPageReuses { get; } = AtlasPendingBgraPageReuses;", glyphSource);
        Assert.Contains("public int AtlasAlphaEvictions { get; } = AtlasAlphaEvictions;", glyphSource);
        Assert.Contains("public int AtlasBgraEvictions { get; } = AtlasBgraEvictions;", glyphSource);
        Assert.Contains("public int AtlasPageReuseRequests { get; } = AtlasPageReuseRequests;", glyphSource);
        Assert.Contains("public int AtlasAlphaPageReuseRequests { get; } = AtlasAlphaPageReuseRequests;", glyphSource);
        Assert.Contains("public int AtlasBgraPageReuseRequests { get; } = AtlasBgraPageReuseRequests;", glyphSource);
        Assert.Contains("public int AtlasFullWithoutPageReuse { get; } = AtlasFullWithoutPageReuse;", glyphSource);
        Assert.Contains("public int AtlasAlphaFullWithoutPageReuse { get; } = AtlasAlphaFullWithoutPageReuse;", glyphSource);
        Assert.Contains("public int AtlasBgraFullWithoutPageReuse { get; } = AtlasBgraFullWithoutPageReuse;", glyphSource);
        Assert.Contains("public GlyphEntry WithLastUsedSerial(long serial)", glyphSource);
        Assert.Contains("page.UsedPixels = checked(page.UsedPixels + width * height);", glyphSource);
        Assert.Contains("page.AllocatedPixels = Math.Max(page.AllocatedPixels, page.ComputeAllocatedPixels());", glyphSource);
        Assert.Contains(".WithAtlasEviction(page.Format == GlyphAtlasPageFormat.Bgra)", glyphSource);
        Assert.Contains(".WithAtlasEviction(selected.Format == GlyphAtlasPageFormat.Bgra)", glyphSource);
        Assert.Contains("_glyphs[key] = handle;", glyphSource);
        Assert.DoesNotContain("Dictionary<GlyphKey, GlyphEntry> _glyphs", glyphSource);
        Assert.DoesNotContain("_glyphs.Add(key, glyph)", glyphSource);
        Assert.DoesNotContain("private readonly struct GlyphKey(FontFaceKey FontFace, char Character)", glyphSource);
        Assert.DoesNotContain("private ID3D12Resource* _atlasTexture", glyphSource);
        Assert.DoesNotContain("private ID3D12Resource* _atlasUpload", glyphSource);
        Assert.DoesNotContain("private ID3D12DescriptorHeap* _srvHeap", glyphSource);
        Assert.DoesNotContain("Transition(page.Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST)", glyphSource);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_summary_includes_reasons_init_phase_and_scratch()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 12,
            UploadedBytes: 4096,
            DrawnGlyphs: 48,
            CacheHits: 9,
            CacheMisses: 3,
            FallbackFrames: 1,
            UnsupportedRuns: 1,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 768,
            RasterScratchResizes: 2)
            .WithAtlasRuns(7)
            .WithAtlasPageCounts(1, 1, 0)
            .WithAtlasEviction()
            .WithAtlasBgraEviction()
            .WithAtlasPendingPageReuse(1, 2)
            .WithAtlasPageReuseRequest()
            .WithAtlasPageReuseRequest(isBgra: true)
            .WithAtlasFullWithoutPageReuse()
            .WithAtlasFullWithoutPageReuse(isBgra: true)
            .WithAtlasPageUsage(2048, 512, 1536, 512, 384, 128)
            .WithAtlasTouchMetrics(recordSerial: 4, oldestPageAge: 3, newestPageAge: 1, oldestAlphaPageAge: 3, oldestBgraPageAge: 2)
            .WithUploadedGlyph()
            .WithUploadedGlyph()
            .WithColorGlyphRuns(3, 1)
            .WithDegradation(0, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii)
            .WithInitializationFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.ShaderCompile);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("cachedGlyphs=12", summary);
        Assert.Contains("atlasPages=1", summary);
        Assert.Contains("atlasAlphaPages=1", summary);
        Assert.Contains("atlasBgraPages=0", summary);
        Assert.Contains("atlasBudgetPages=48", summary);
        Assert.Contains("atlasPage=1024x1024", summary);
        Assert.Contains("atlasCapacity=50331648 px", summary);
        Assert.Contains("atlasCpuBytes=1048576 bytes", summary);
        Assert.Contains("atlasUploadBytes=2097152 bytes", summary);
        Assert.Contains("atlasGpuBytes=1048576 bytes", summary);
        Assert.Contains("atlasEvictions=2", summary);
        Assert.Contains("atlasAlphaEvictions=0", summary);
        Assert.Contains("atlasBgraEvictions=1", summary);
        Assert.Contains("atlasPendingPageReuses=3", summary);
        Assert.Contains("atlasPendingAlphaPageReuses=1", summary);
        Assert.Contains("atlasPendingBgraPageReuses=2", summary);
        Assert.Contains("atlasPageReuseRequests=2", summary);
        Assert.Contains("atlasAlphaPageReuseRequests=0", summary);
        Assert.Contains("atlasBgraPageReuseRequests=1", summary);
        Assert.Contains("atlasFullWithoutPageReuse=2", summary);
        Assert.Contains("atlasAlphaFullWithoutPageReuse=0", summary);
        Assert.Contains("atlasBgraFullWithoutPageReuse=1", summary);
        Assert.Contains("atlasUsed=2048 px", summary);
        Assert.Contains("atlasFragmented=512 px", summary);
        Assert.Contains("atlasAlphaUsed=1536 px", summary);
        Assert.Contains("atlasBgraUsed=512 px", summary);
        Assert.Contains("atlasAlphaFragmented=384 px", summary);
        Assert.Contains("atlasBgraFragmented=128 px", summary);
        Assert.Contains("atlasRecordSerial=4", summary);
        Assert.Contains("atlasOldestPageAge=3", summary);
        Assert.Contains("atlasNewestPageAge=1", summary);
        Assert.Contains("atlasOldestAlphaPageAge=3", summary);
        Assert.Contains("atlasOldestBgraPageAge=2", summary);
        Assert.Contains("uploads=4096 bytes", summary);
        Assert.Contains("uploadedGlyphs=2", summary);
        Assert.Contains("shapedProbeRuns=0", summary);
        Assert.Contains("shapedProbeGlyphs=0", summary);
        Assert.Contains("colorLayerRuns=3", summary);
        Assert.Contains("colorBitmapRuns=1", summary);
        Assert.Contains("atlasRuns=7", summary);
        Assert.Contains("degradedRuns=0", summary);
        Assert.Contains("fallbacks=2", summary);
        Assert.Contains("NonAscii=1", summary);
        Assert.Contains("ColorGlyph=0", summary);
        Assert.Contains("ComplexScript=0", summary);
        Assert.Contains("ColorGlyphPremultipliedBgra=0", summary);
        Assert.Contains("initFailurePhase=ShaderCompile", summary);
        Assert.Contains("recordFailurePhase=None", summary);
        Assert.Contains("RecordFailed=0", summary);
        Assert.Contains("rasterScratch=768 bytes/2 resizes", summary);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_resident_bytes_follow_page_formats()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithAtlasPageCounts(3, 2, 1);
        var summary = diagnostics.FormatSummary();

        Assert.Equal(6291456, diagnostics.AtlasCpuBytes);
        Assert.Equal(12582912, diagnostics.AtlasUploadBytes);
        Assert.Equal(6291456, diagnostics.AtlasGpuBytes);
        Assert.Contains("atlasCpuBytes=6291456 bytes", summary);
        Assert.Contains("atlasUploadBytes=12582912 bytes", summary);
        Assert.Contains("atlasGpuBytes=6291456 bytes", summary);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_counts_color_glyph_formats_separately_from_aggregate()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(
                1,
                D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii
                | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph
                | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra
                | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphPaintTree);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("NonAscii=1", summary);
        Assert.Contains("ColorGlyph=1", summary);
        Assert.Contains("ColorGlyphPremultipliedBgra=1", summary);
        Assert.Contains("ColorGlyphPaintTree=1", summary);
        Assert.Contains("ColorGlyphSvg=0", summary);
        Assert.Contains("ComplexScript=0", summary);
    }

    [Fact]
    public void Glyph_atlas_classifies_unsupported_color_glyph_image_formats()
    {
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            D3D12GlyphAtlasTextRenderer.GetUnsupportedColorGlyphImageFormatReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG));

        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            D3D12GlyphAtlasTextRenderer.GetUnsupportedColorGlyphImageFormatReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8));

        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            D3D12GlyphAtlasTextRenderer.GetUnsupportedColorGlyphImageFormatReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF));

        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphSvg
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphPaintTree,
            D3D12GlyphAtlasTextRenderer.GetUnsupportedColorGlyphImageFormatReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE));
    }

    [Fact]
    public void Glyph_atlas_selects_bitmap_color_glyph_format_by_d3d12_route_priority()
    {
        Assert.True(D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8
            | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG,
            out var bgraFormat));
        Assert.Equal(DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8, bgraFormat);

        Assert.True(D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
            | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
            | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG,
            out var encodedFormat));
        Assert.Equal(DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG, encodedFormat);

        Assert.True(D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
            | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG,
            out var tiffFormat));
        Assert.Equal(DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF, tiffFormat);

        Assert.False(D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(
            DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG
            | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE,
            out _));
    }

    [Fact]
    public void Glyph_atlas_color_glyph_run_format_fallback_reason_uses_selected_bitmap_route()
    {
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra,
            D3D12GlyphAtlasTextRenderer.GetColorGlyphRunImageFormatFallbackReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG));

        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphPng,
            D3D12GlyphAtlasTextRenderer.GetColorGlyphRunImageFormatFallbackReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG));

        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph
            | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyphTiff,
            D3D12GlyphAtlasTextRenderer.GetColorGlyphRunImageFormatFallbackReason(
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF
                | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG));
    }

    [Fact]
    public void Glyph_atlas_bitmap_color_glyphs_preserve_image_color_and_apply_text_alpha()
    {
        Assert.Equal(
            new Vector4(1f, 1f, 1f, 0.375f),
            D3D12GlyphAtlasTextRenderer.ResolveColorGlyphBitmapColor(new Vector4(0.2f, 0.4f, 0.6f, 0.375f)));
    }

    [Fact]
    public void Glyph_atlas_color_format_diagnostic_reports_bitmap_route_candidates()
    {
        var results = new[]
        {
            new ColorGlyphFormatProbeResult("grinning", 0x1F600, 42, GlyphFound: true, Factory4Available: true, Face4Available: true, Formats: ColorGlyphImageFormatFlags.Colr | ColorGlyphImageFormatFlags.PremultipliedBgra, BitmapRoute: ColorGlyphBitmapRoute.Bgra, Status: ColorGlyphFormatProbeStatus.Ok, Error: "", ColorRunCount: 1, ColorRunFormats: ColorGlyphImageFormatFlags.Colr, ColorRunBitmapRoute: ColorGlyphBitmapRoute.None, ImageDataRoute: ColorGlyphBitmapRoute.Bgra, ImageDataBytes: 4096, ImageDataPixelsPerEm: 64, ImageDataWidth: 32, ImageDataHeight: 32, ImageDecodeBytes: 4096, ImageDecodeWidth: 32, ImageDecodeHeight: 32),
            new ColorGlyphFormatProbeResult("rocket", 0x1F680, 43, GlyphFound: true, Factory4Available: true, Face4Available: true, Formats: ColorGlyphImageFormatFlags.Png, BitmapRoute: ColorGlyphBitmapRoute.Png, Status: ColorGlyphFormatProbeStatus.Ok, Error: "", ColorRunCount: 1, ColorRunFormats: ColorGlyphImageFormatFlags.Png, ColorRunBitmapRoute: ColorGlyphBitmapRoute.Png, ImageDataRoute: ColorGlyphBitmapRoute.Png, ImageDataBytes: 2048, ImageDataPixelsPerEm: 128, ImageDataWidth: 0, ImageDataHeight: 0, ImageDecodeBytes: 16384, ImageDecodeWidth: 64, ImageDecodeHeight: 64),
            new ColorGlyphFormatProbeResult("paint-tree", 0x1F3AF, 44, GlyphFound: true, Factory4Available: true, Face4Available: true, Formats: ColorGlyphImageFormatFlags.ColrPaintTree, BitmapRoute: ColorGlyphBitmapRoute.None, Status: ColorGlyphFormatProbeStatus.Ok, Error: "")
        };
        var snapshot = ColorGlyphFormatDiagnosticSnapshot.Create("Segoe UI Emoji", 64, factory4Available: true, face4Available: true, results);

        Assert.Equal(3, snapshot.Glyphs);
        Assert.Equal(2, snapshot.ColorRunCandidates);
        Assert.Equal(1, snapshot.LayerCandidates);
        Assert.Equal(1, snapshot.BgraCandidates);
        Assert.Equal(1, snapshot.EncodedBitmapCandidates);
        Assert.Equal(1, snapshot.UnsupportedColorCandidates);
        Assert.Equal(2, snapshot.BitmapRenderableCandidates);
        Assert.Equal(2, snapshot.ImageDataCandidates);
        Assert.Equal(2, snapshot.DecodedBitmapCandidates);
        Assert.Equal("COLR|BGRA", GlyphAtlasColorFormatDiagnosticRunner.FormatFlags(results[0].Formats));
        Assert.Equal("Probe: U+1F600 grinning glyph=42 status=Ok formats=COLR|BGRA route=Bgra colorRuns=1 runFormats=COLR runRoute=None imageDataRoute=Bgra imageDataBytes=4096 imageDataPpem=64 imageDataSize=32x32 decodeBytes=4096 decodeSize=32x32", GlyphAtlasColorFormatDiagnosticRunner.FormatProbe(results[0]));
        Assert.Equal("Probe: U+1F680 rocket glyph=43 status=Ok formats=PNG route=Png colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=2048 imageDataPpem=128 imageDataSize=0x0 decodeBytes=16384 decodeSize=64x64", GlyphAtlasColorFormatDiagnosticRunner.FormatProbe(results[1]));
        Assert.Equal("Color glyph formats: factory4=True, face4=True, probes=3, glyphs=3, colorRunCandidates=2, layerCandidates=1, bgraCandidates=1, encodedBitmapCandidates=1, unsupportedColorCandidates=1, bitmapRenderableCandidates=2, imageDataCandidates=2, decodedBitmapCandidates=2", GlyphAtlasColorFormatDiagnosticRunner.FormatSummary(snapshot));
        Assert.Equal("Color glyph natural coverage: status=BitmapRenderableAvailable, layerRoute=True, bgraRoute=True, encodedBitmapRoute=True, bitmapRenderableRoute=True, imageDataRoute=True, decodedBitmapRoute=True, naturalBgraSmoke=True", GlyphAtlasColorFormatDiagnosticRunner.FormatNaturalCoverage(snapshot));
    }

    [Fact]
    public void Glyph_atlas_color_format_diagnostic_reports_layer_only_natural_coverage_without_bitmap_claims()
    {
        var results = new[]
        {
            new ColorGlyphFormatProbeResult("heart", 0x2764, 42, GlyphFound: true, Factory4Available: true, Face4Available: true, Formats: ColorGlyphImageFormatFlags.None, BitmapRoute: ColorGlyphBitmapRoute.None, Status: ColorGlyphFormatProbeStatus.Ok, Error: "", ColorRunCount: 4, ColorRunFormats: ColorGlyphImageFormatFlags.TrueType | ColorGlyphImageFormatFlags.Colr, ColorRunBitmapRoute: ColorGlyphBitmapRoute.None)
        };
        var snapshot = ColorGlyphFormatDiagnosticSnapshot.Create("Segoe UI Emoji", 64, factory4Available: true, face4Available: true, results);

        Assert.Equal(1, snapshot.LayerCandidates);
        Assert.Equal(0, snapshot.BgraCandidates);
        Assert.Equal(0, snapshot.EncodedBitmapCandidates);
        Assert.Equal(0, snapshot.BitmapRenderableCandidates);
        Assert.Equal(0, snapshot.ImageDataCandidates);
        Assert.Equal(0, snapshot.DecodedBitmapCandidates);
        Assert.Equal("Color glyph natural coverage: status=LayerOnly, layerRoute=True, bgraRoute=False, encodedBitmapRoute=False, bitmapRenderableRoute=False, imageDataRoute=False, decodedBitmapRoute=False, naturalBgraSmoke=False", GlyphAtlasColorFormatDiagnosticRunner.FormatNaturalCoverage(snapshot));
    }

    [Fact]
    public void Glyph_atlas_color_format_diagnostic_cli_is_wired()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var runnerSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "GlyphAtlasColorFormatDiagnosticRunner.optional-diagnostics.cs")));
        var platformSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "DWriteColorGlyphFormatDiagnostic.optional-diagnostics.cs")));

        Assert.Contains("--diagnose-glyph-atlas-color-formats", programSource);
        Assert.Contains("--diagnose-color-glyph-family", programSource);
        Assert.Contains("--diagnose-color-glyph-font-file", programSource);
        Assert.Contains("DWriteColorGlyphFormatDiagnostic.Capture(familyName, pixelsPerEm)", runnerSource);
        Assert.Contains("DWriteColorGlyphFormatDiagnostic.CaptureFromFontFile(fontFilePath, pixelsPerEm)", runnerSource);
        Assert.Contains("factory4={snapshot.Factory4Available}", runnerSource);
        Assert.Contains("colorRunCandidates={snapshot.ColorRunCandidates}", runnerSource);
        Assert.Contains("imageDataCandidates={snapshot.ImageDataCandidates}", runnerSource);
        Assert.Contains("decodedBitmapCandidates={snapshot.DecodedBitmapCandidates}", runnerSource);
        Assert.Contains("FormatNaturalCoverage(snapshot)", runnerSource);
        Assert.Contains("naturalBgraSmoke={bitmapRenderableRoute}", runnerSource);
        Assert.Contains("IDWriteFontFace4*", platformSource);
        Assert.Contains("IDWriteFactory4*", platformSource);
        Assert.Contains("factory->QueryInterface<IDWriteFactory4>(out factory4).ThrowOnFailure();", platformSource);
        Assert.Contains("CreateFontFileReference(fullPath, null, &fontFile)", platformSource);
        Assert.Contains("CreateFontFace(", platformSource);
        Assert.Contains("fontFile->Analyze(out var isSupportedFontType", platformSource);
        Assert.Contains("factory4->TranslateColorGlyphRun(", platformSource);
        Assert.Contains("ProbeGlyphImageData(face4", platformSource);
        Assert.Contains("face4->GetGlyphImageData(glyphIndex, pixelsPerEm, imageFormat, out var glyphData, out glyphDataContext)", platformSource);
        Assert.Contains("face4->ReleaseGlyphImageData(glyphDataContext)", platformSource);
        Assert.Contains("TryDecodeWicImage(new ReadOnlySpan<byte>(glyphData.imageData", platformSource);
        Assert.Contains("PInvoke.CoCreateInstance<IWICImagingFactory>", platformSource);
        Assert.Contains("CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad)", platformSource);
        Assert.Contains("D2D_POINT_2F", platformSource);
        Assert.Contains("IDWriteColorGlyphRunEnumerator1* colorRuns", platformSource);
        Assert.DoesNotContain("Factory4Missing", platformSource);
        Assert.DoesNotContain("Factory4AndFace4Missing", platformSource);
        Assert.Contains("GetGlyphImageFormats(glyphIndex[0], pixelsPerEm, pixelsPerEm, out var formats)", platformSource);
        Assert.Contains("TrySelectColorGlyphBitmapImageFormat(formats, out var selectedFormat)", platformSource);
    }

    [Fact]
    public void Glyph_atlas_scales_color_glyph_image_data_to_requested_em_size()
    {
        Assert.Equal(1f, D3D12GlyphAtlasTextRenderer.ComputeGlyphImageScale(18f, 18));
        Assert.Equal(0.75f, D3D12GlyphAtlasTextRenderer.ComputeGlyphImageScale(18f, 24));
        Assert.Equal(1.25f, D3D12GlyphAtlasTextRenderer.ComputeGlyphImageScale(20f, 16));
        Assert.Equal(1f, D3D12GlyphAtlasTextRenderer.ComputeGlyphImageScale(18f, 0));
        Assert.Equal(1f, D3D12GlyphAtlasTextRenderer.ComputeGlyphImageScale(float.NaN, 18));
    }

    [Fact]
    public void Glyph_atlas_diagnostics_summary_reports_initialization_failure_phase()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(1, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.InitializationFailed)
            .WithInitializationFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.UploadBuffer);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("InitializationFailed=1", summary);
        Assert.Contains("degradedRuns=1", summary);
        Assert.Contains("initFailurePhase=UploadBuffer", summary);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_summary_reports_runtime_record_failure_phase_separately()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(1, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.RecordFailed)
            .WithRecordFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.Record);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("InitializationFailed=0", summary);
        Assert.Contains("RecordFailed=1", summary);
        Assert.Contains("degradedRuns=1", summary);
        Assert.Contains("initFailurePhase=None", summary);
        Assert.Contains("recordFailurePhase=Record", summary);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_summary_reports_directwrite_record_failure_phase()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(2, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.RecordFailed)
            .WithRecordFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.DirectWrite);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("RecordFailed=2", summary);
        Assert.Contains("degradedRuns=2", summary);
        Assert.Contains("recordFailurePhase=DirectWrite", summary);
    }

    [Fact]
    public void Glyph_atlas_diagnostics_summary_reports_specific_record_resource_failure_phase()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(3, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.RecordFailed)
            .WithRecordFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.AtlasDraw);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("RecordFailed=3", summary);
        Assert.Contains("degradedRuns=3", summary);
        Assert.Contains("recordFailurePhase=AtlasDraw", summary);
    }

    [Fact]
    public void Glyph_atlas_renderable_run_counter_skips_empty_and_zero_size_runs()
    {
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var visibleText = resources.AddText("visible");
        var emptyText = resources.AddText("");
        resources.Seal();
        var runs = new[]
        {
            TextRun(visibleText, style, width: 100, height: 20),
            TextRun(emptyText, style, width: 100, height: 20),
            TextRun(visibleText, style, width: 0, height: 20)
        };

        var count = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(runs, resources);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Glyph_atlas_degradation_diagnostics_count_reasons_per_run()
    {
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(2, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ColorGlyph)
            .WithDegradation(1, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii | D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.ComplexScript);

        Assert.Equal(2, diagnostics.FallbackFrames);
        Assert.Equal(3, diagnostics.UnsupportedRuns);
        Assert.Equal(3, diagnostics.DegradedRuns);
        Assert.Equal(3, diagnostics.Reasons.NonAscii);
        Assert.Equal(2, diagnostics.Reasons.ColorGlyph);
        Assert.Equal(1, diagnostics.Reasons.ComplexScript);
    }

    [Fact]
    public void Glyph_atlas_mixed_fallback_smoke_scene_classifies_expected_runs_and_order_limit()
    {
        using var resources = FrameDrawingResources.Rent();
        var commands = GlyphAtlasMixedFallbackDiagnosticRunner.BuildMixedFallbackCommands(resources, frameIndex: 0);
        resources.Seal();

        var summary = GlyphAtlasMixedFallbackDiagnosticRunner.AnalyzeMixedFallbackScene(commands, resources);
        var ordering = GlyphAtlasMixedFallbackDiagnosticRunner.BuildOrderingLine(summary);

        Assert.Equal(4, summary.TextRuns);
        Assert.Equal(4, summary.AtlasCandidateRuns);
        Assert.Equal(0, summary.DegradedCandidateRuns);
        Assert.Equal(0, summary.NonAsciiFallbackRuns);
        Assert.Equal(2, summary.ClippedAtlasCandidateRuns);
        Assert.Equal(0, summary.ClippedDegradedCandidateRuns);
        Assert.False(summary.HasDegradedBeforeLaterAtlas);
        Assert.Contains("commands=atlasOnly", ordering);
        Assert.Contains("zOrderLimit=False", ordering);
    }

    [Fact]
    public void Glyph_atlas_wrap_smoke_scene_classifies_space_wrap_and_explicit_degradation()
    {
        using var resources = FrameDrawingResources.Rent();
        var commands = GlyphAtlasWrapDiagnosticRunner.BuildWrapCommands(resources, frameIndex: 0);
        resources.Seal();

        var summary = GlyphAtlasWrapDiagnosticRunner.AnalyzeWrapScene(commands, resources);
        var expectedLine = GlyphAtlasWrapDiagnosticRunner.FormatExpectedLine(summary);

        Assert.Equal(15, summary.TextRuns);
        Assert.Equal(15, summary.AtlasCandidateRuns);
        Assert.Equal(0, summary.DegradedCandidateRuns);
        Assert.Equal(5, summary.WrappedAtlasCandidateRuns);
        Assert.Equal(0, summary.WrappingFallbackRuns);
        Assert.Equal(0, summary.NonAsciiFallbackRuns);
        Assert.Equal(0, summary.ColorGlyphFallbackRuns);
        Assert.Equal(0, summary.ComplexScriptFallbackRuns);
        Assert.Contains("atlasRuns=15", expectedLine);
        Assert.Contains("degradedRuns=0", expectedLine);
        Assert.Contains("wrappedAtlasRuns=5", expectedLine);
        Assert.Contains("Wrapping=0", expectedLine);
        Assert.Contains("NonAscii=0", expectedLine);
        Assert.Contains("ColorGlyph=0", expectedLine);
        Assert.Contains("ComplexScript=0", expectedLine);
    }

    [Fact]
    public void Glyph_atlas_regression_matrix_pins_script_wrap_tab_crlf_and_emoji_coverage()
    {
        using var resources = FrameDrawingResources.Rent();
        var commands = GlyphAtlasRegressionMatrixDiagnosticRunner.BuildMatrixCommands(resources, frameIndex: 0);
        resources.Seal();

        var summary = GlyphAtlasRegressionMatrixDiagnosticRunner.AnalyzeMatrixScene(commands, resources);
        var expectedLine = GlyphAtlasRegressionMatrixDiagnosticRunner.FormatSummary(summary);
        var contractLine = GlyphAtlasRegressionMatrixDiagnosticRunner.FormatContract(summary.Contract);

        Assert.Equal(13, summary.TextRuns);
        Assert.Equal(13, summary.AtlasRuns);
        Assert.Equal(0, summary.DegradedRuns);
        Assert.Equal(2, summary.WrappedRuns);
        Assert.Equal(1, summary.TabRuns);
        Assert.Equal(1, summary.ExplicitLineRuns);
        Assert.Equal(3, summary.SimpleBmpRuns);
        Assert.Equal(5, summary.ShapedRuns);
        Assert.Equal(1, summary.CjkRuns);
        Assert.Equal(2, summary.ArabicRuns);
        Assert.Equal(1, summary.HebrewRuns);
        Assert.Equal(1, summary.MixedBidiRuns);
        Assert.Equal(1, summary.EmojiRuns);
        Assert.Contains("ASCII=True", "Matrix cases: ASCII=True LatinExtended=True Greek=True Cyrillic=True CJK=True Arabic=True Hebrew=True MixedBidi=True Emoji=True Wrap=True Tab=True CRLF=True");
        Assert.Contains("atlasRuns=13", expectedLine);
        Assert.Contains("degradedRuns=0", expectedLine);
        Assert.Contains("simpleBmpRuns=3", expectedLine);
        Assert.Contains("shapedRuns=5", expectedLine);
        Assert.Contains("emojiRuns=1", expectedLine);
        Assert.Equal("matrix.expected textRuns=13 atlasRuns=13 degradedRuns=0 wrappedRuns=2 tabRuns=1 explicitLineRuns=1 simpleBmpRuns=3 shapedRuns=5 cjkRuns=1 arabicRuns=2 hebrewRuns=1 mixedBidiRuns=1 emojiRuns=1 finalComposition=D3D12", GlyphAtlasRegressionMatrixDiagnosticRunner.FormatExpectedMachineLine(summary));
        Assert.Equal("matrix.actual frameSerial=3 presentSerial=3 syncWaits=0 glyphAtlasInitialized=False atlasRuns=0 degradedRuns=0 colorLayerRuns=0 colorBitmapRuns=0 finalComposition=D3D12", GlyphAtlasRegressionMatrixDiagnosticRunner.FormatActualMachineLine(3, 3, 0, null));
        Assert.Contains("svgColorGlyph=True", contractLine);
        Assert.Contains("colrPaintTreeColorGlyph=True", contractLine);
        Assert.Contains("bidiBeyondResolvedLevels=True", contractLine);
        Assert.Contains("finalComposition=D3D12", contractLine);
    }

    [Fact]
    public void Glyph_atlas_degradation_contract_marks_known_remaining_cases_as_explicit_d3d12_degradation()
    {
        var contract = GlyphAtlasDegradationContract.CreateDefault();

        Assert.True(contract.SvgColorGlyph);
        Assert.True(contract.ColrPaintTreeColorGlyph);
        Assert.True(contract.BidiBeyondResolvedLevels);
        Assert.True(contract.AtlasFullAfterBudget);
        Assert.True(contract.RecordFailure);
        Assert.True(contract.InitializationFailure);
    }

    [Fact]
    public void Glyph_atlas_regression_matrix_cli_is_wired()
    {
        var root = FindRepoRoot();
        var source = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));

        Assert.Contains("--diagnose-glyph-atlas-matrix", source);
        Assert.Contains("GlyphAtlasRegressionMatrixDiagnosticRunner.Run", source);
        Assert.Contains("ParseTextCompositionMode(args)", source);
        Assert.Contains("ParseDiagnosticScale(args)", source);
    }

    [Fact]
    public void D3D12_composition_transform_implementation_has_doc_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "D3D12-Composition.md")));
        var status = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Project_Status_and_Todo.md")));

        using var resources = FrameDrawingResources.Rent();
        var commands = CompositionTransformDiagnosticRunner.BuildCommands(resources);
        resources.Seal();
        var frame = CompositionTransformDiagnosticRunner.BuildCompositionFrame(commands.Length);
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Contains("--diagnose-composition-transform", programSource);
        Assert.Contains("CompositionTransformDiagnosticRunner.Run", programSource);
        Assert.Contains("D3D12-backed layer updates for translation, opacity, fixed-clip scroll presentation, and multi-layer composition frames", design);
        Assert.Contains("D3D12-Composition.md", status);
        Assert.Equal("composition.expected finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 layerStart=1 layerCommands=2 translatedCommands=2 opacityAppliedCommands=2 translate=(24,18) opacity=0.75", CompositionTransformDiagnosticRunner.FormatExpected(diagnostics));
        Assert.Equal("composition.actual finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 translatedCommands=2 opacityAppliedCommands=2 frameSerial=1 presentSerial=1 syncWaits=0 deviceRemoved=False", CompositionTransformDiagnosticRunner.FormatActual(diagnostics, 1, 1, 0, false));
    }

    [Fact]
    public void D3D12_composition_scroll_implementation_has_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "D3D12-Composition.md")));

        using var resources = FrameDrawingResources.Rent();
        var commands = CompositionScrollDiagnosticRunner.BuildCommands(resources);
        resources.Seal();
        var frame = CompositionScrollDiagnosticRunner.BuildCompositionFrame(commands.Length);
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Contains("--diagnose-composition-scroll", programSource);
        Assert.Contains("CompositionScrollDiagnosticRunner.Run", programSource);
        Assert.Contains("fixed clip", design);
        Assert.Equal("composition-scroll.expected finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 layerStart=1 layerCommands=2 translatedCommands=2 opacityAppliedCommands=0 fixedClip=True clip=(24,24,280,96) translate=(0,42)", CompositionScrollDiagnosticRunner.FormatExpected(diagnostics));
        Assert.Equal("composition-scroll.actual finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 translatedCommands=2 opacityAppliedCommands=0 fixedClip=True frameSerial=1 presentSerial=1 syncWaits=0 deviceRemoved=False", CompositionScrollDiagnosticRunner.FormatActual(diagnostics, 1, 1, 0, false));
    }

    [Fact]
    public void D3D12_composition_multilayer_implementation_has_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "D3D12-Composition.md")));

        using var resources = FrameDrawingResources.Rent();
        var commands = CompositionMultiLayerDiagnosticRunner.BuildCommands(resources);
        resources.Seal();
        var frame = CompositionMultiLayerDiagnosticRunner.BuildCompositionFrame(commands.Length);
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 640, 360),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Contains("--diagnose-composition-multilayer", programSource);
        Assert.Contains("CompositionMultiLayerDiagnosticRunner.Run", programSource);
        Assert.Contains("multi-layer", design);
        Assert.Equal("composition-multilayer.expected finalComposition=D3D12 d3d12Backed=True layers=2 commands=3 firstLayerStart=1 firstLayerCommands=2 translatedCommands=3 opacityAppliedCommands=2 fixedClipLayers=1", CompositionMultiLayerDiagnosticRunner.FormatExpected(diagnostics, frame));
        Assert.Equal("composition-multilayer.actual finalComposition=D3D12 d3d12Backed=True layers=2 commands=3 translatedCommands=3 opacityAppliedCommands=2 fixedClipLayers=1 frameSerial=1 presentSerial=1 syncWaits=0 deviceRemoved=False", CompositionMultiLayerDiagnosticRunner.FormatActual(diagnostics, frame, 1, 1, 0, false));
    }

    [Fact]
    public void D3D12_composition_layer_cache_implementation_has_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "D3D12-Composition.md")));

        var diagnostics = CompositionLayerCacheDiagnosticRunner.RunCore();

        Assert.Contains("--diagnose-composition-layer-cache", programSource);
        Assert.Contains("CompositionLayerCacheDiagnosticRunner.Run", programSource);
        Assert.Contains("layer content cache", design);
        Assert.Equal("composition-layer-cache.first finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 cacheHits=0 cacheMisses=1 cachedCommands=2 translatedCommands=2 opacityAppliedCommands=2", CompositionLayerCacheDiagnosticRunner.FormatFirst(diagnostics.First));
        Assert.Equal("composition-layer-cache.second finalComposition=D3D12 d3d12Backed=True layers=1 commands=3 cacheHits=1 cacheMisses=0 cachedCommands=2 translatedCommands=2 opacityAppliedCommands=2", CompositionLayerCacheDiagnosticRunner.FormatSecond(diagnostics.Second));
    }

    [Fact]
    public async Task Composition_marker_runtime_diagnostic_maps_marker_event_to_counter_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));

        var diagnostics = await CompositionMarkerRuntimeDiagnosticRunner.RunCoreAsync(cancellationToken);
        var summary = CompositionMarkerRuntimeDiagnosticRunner.Format(diagnostics);

        Assert.Contains("--diagnose-composition-marker-runtime", programSource);
        Assert.Contains("CompositionMarkerRuntimeDiagnosticRunner.RunAsync", programSource);
        Assert.Equal("composition-marker-runtime actual drainedEvents=1 dispatchedMessages=1 unmappedEvents=0 finalCount=1 executeCompositionCount=2 layerId=6", summary);
    }

    [Fact]
    public void Composition_skip_diagnostic_has_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "D3D12-Composition.md")));

        var diagnostics = CompositionSkipDiagnosticRunner.RunCore();
        var summary = CompositionSkipDiagnosticRunner.Format(diagnostics);

        Assert.Contains("--diagnose-composition-skip", programSource);
        Assert.Contains("CompositionSkipDiagnosticRunner.Run", programSource);
        Assert.Contains("Composition skip diagnostics", design);
        Assert.Equal(
            "composition-skip actual transform=TransformOpacityTick:BackendDoesNotImplementComposition:required=TransformOpacity:backend=None:pacing=SoftwareTimer:layers=1:commands=1 scroll=ScrollPresentationTick:MissingBackendCapability:required=ScrollPresentation:backend=TransformOpacity:pacing=SoftwareTimer:layers=1:commands=2 retainedUpdate=RetainedUpdateScrollPresentation:MissingBackendCapability:required=ScrollPresentation:backend=TransformOpacity:pacing=SoftwareTimer:layers=1:commands=2 executed=TransformOpacityTick:None:required=TransformOpacity:backend=TransformOpacity|ScrollPresentation|MultiLayer:pacing=SoftwareTimer:layers=1:commands=1 executedCompositionCount=1 skippedCompositionCount=0",
            summary);
    }

    [Fact]
    public void Scroll_presentation_policy_diagnostic_has_cli_and_machine_readable_fields()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var mainProgramSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.cs")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Animation-Composition.md")));

        var diagnostics = ScrollPresentationPolicyDiagnosticRunner.RunCore();

        Assert.Contains("--diagnose-scroll-presentation-policy", programSource);
        Assert.Contains("ScrollPresentationPolicyDiagnosticRunner.Run", programSource);
        Assert.Contains("ScrollPresentationCoordinator", mainProgramSource);
        Assert.Contains("commit", design);
        Assert.Equal("scroll-presentation-policy actual initialPos=120 initialTarget=180 presented=132 deltaPx=54 commitPos=132 commitTarget=132 commitAnimating=False cancelPos=180 cancelTarget=180 cancelAnimating=False retargetPos=132 retargetTarget=234 retargetAnimating=True", ScrollPresentationPolicyDiagnosticRunner.Format(diagnostics));
    }

    [Fact]
    public async Task Scroll_presentation_runtime_diagnostic_runs_compositor_ticks_after_logical_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));

        var diagnostics = await ScrollPresentationRuntimeDiagnosticRunner.RunCoreAsync(cancellationToken);
        var summary = ScrollPresentationRuntimeDiagnosticRunner.Format(diagnostics);

        Assert.Contains("--diagnose-scroll-presentation-runtime", programSource);
        Assert.Contains("ScrollPresentationRuntimeDiagnosticRunner.RunAsync", programSource);
        Assert.Equal(54, diagnostics.Position);
        Assert.Equal(54, diagnostics.TargetPosition);
        Assert.False(diagnostics.IsAnimating);
        Assert.Equal(1, diagnostics.RetargetCount);
        Assert.Equal(1, diagnostics.RetainedStageCount);
        Assert.Equal(0, diagnostics.CancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, diagnostics.Cancellation.LastReason);
        Assert.Equal(2, diagnostics.ExecuteCount);
        Assert.True(diagnostics.ExecuteCompositionCount > 0);
        Assert.True(diagnostics.LoopTickCount > 0);

        var chain = diagnostics.RetargetChain;
        Assert.Equal(108, chain.Position);
        Assert.Equal(108, chain.TargetPosition);
        Assert.False(chain.IsAnimating);
        Assert.Equal(2, chain.RetargetCount);
        Assert.Equal(2, chain.RetainedStageCount);
        Assert.Equal(1, chain.CancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, chain.Cancellation.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.LayoutAffecting, chain.Cancellation.LastInvalidationKind);
        Assert.Equal(1, chain.Cancellation.RenderInvalidationCount);

        var explicitCancellation = diagnostics.ExplicitCancellation;
        Assert.Equal("explicit", explicitCancellation.Name);
        Assert.Equal(1, explicitCancellation.CancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.Explicit, explicitCancellation.Cancellation.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, explicitCancellation.Cancellation.LastInvalidationKind);
        Assert.Equal(1, explicitCancellation.Cancellation.ExplicitCount);
        Assert.Equal(0, explicitCancellation.RenderCount);
        Assert.False(explicitCancellation.PresentationActiveAfter);

        var viewportCancellation = diagnostics.ViewportInvalidationCancellation;
        Assert.Equal("viewport", viewportCancellation.Name);
        Assert.Equal(1, viewportCancellation.CancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, viewportCancellation.Cancellation.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.ViewportChanged, viewportCancellation.Cancellation.LastInvalidationKind);
        Assert.Equal(1, viewportCancellation.RenderCount);
        Assert.False(viewportCancellation.PresentationActiveDuringRender);
        Assert.False(viewportCancellation.PresentationActiveAfter);

        var maxScrollCancellation = diagnostics.MaxScrollInvalidationCancellation;
        Assert.Equal("maxScroll", maxScrollCancellation.Name);
        Assert.Equal(1, maxScrollCancellation.CancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, maxScrollCancellation.Cancellation.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.MaxScrollChanged, maxScrollCancellation.Cancellation.LastInvalidationKind);
        Assert.Equal(1, maxScrollCancellation.RenderCount);
        Assert.False(maxScrollCancellation.PresentationActiveDuringRender);
        Assert.False(maxScrollCancellation.PresentationActiveAfter);

        Assert.Contains("loopTicks=", summary);
        Assert.Contains("scroll-presentation-runtime actual position=54 target=54 animating=False scenario=initial", summary);
        Assert.Contains("cancels=0 cancelReason=None cancelInvalidation=None", summary);
        Assert.Contains("scroll-presentation-runtime actual position=108 target=108 animating=False scenario=chain", summary);
        Assert.Contains("cancels=1 cancelReason=RenderInvalidation cancelInvalidation=LayoutAffecting", summary);
        Assert.Contains("scroll-presentation-runtime.cancel scenario=explicit cancels=1 cancelReason=Explicit cancelInvalidation=None", summary);
        Assert.Contains("scroll-presentation-runtime.cancel scenario=viewport cancels=1 cancelReason=RenderInvalidation cancelInvalidation=ViewportChanged", summary);
        Assert.Contains("scroll-presentation-runtime.cancel scenario=maxScroll cancels=1 cancelReason=RenderInvalidation cancelInvalidation=MaxScrollChanged", summary);
        Assert.Contains("activeDuringRender=False activeAfter=False", summary);
        Assert.Contains("retainedStages=1", summary);
        Assert.Contains("execute=2", summary);
        Assert.Contains("lastPresented=54", summary);
    }

    [Fact]
    public void Scroll_presentation_frame_pacing_does_not_add_delay_after_backend_overruns_interval()
    {
        var frameInterval = CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency / 100);
        var tick = CompositionTimestamp.FromStopwatchTicks(1_000);

        Assert.Equal(5, CompositorLoop.ComputeNextTickDelayMilliseconds(
            tick,
            tick + CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency / 200),
            frameInterval));
        Assert.Equal(0, CompositorLoop.ComputeNextTickDelayMilliseconds(
            tick,
            tick + frameInterval,
            frameInterval));
        Assert.Equal(0, CompositorLoop.ComputeNextTickDelayMilliseconds(
            tick,
            tick + CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency / 50),
            frameInterval));
    }

    [Fact]
    public void Scroll_presentation_frame_pacing_does_not_add_software_delay_when_backend_presents()
    {
        var frameInterval = CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency / 240);
        var tick = CompositionTimestamp.FromStopwatchTicks(1_000);

        Assert.Equal(0, CompositorLoop.ComputeNextTickDelayMilliseconds(
            tick,
            tick + CompositionDuration.FromStopwatchTicks(1),
            frameInterval,
            CompositionFramePacing.BackendPresentation));
    }

    [Fact]
    public void Composition_transform_demo_updates_composition_frame_without_rebuilding_commands()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.cs")));
        var demoSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "CompositionTransformDemoRunner.cs")));
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            CompositionTransformDemoRunner.BuildDemoRoot(arena),
            new PixelRectangle(0, 0, 640, 360),
            arena.GetOrCreateSnapshot());
        var first = CompositionTransformDemoRunner.BuildAnimatedCompositionFrameAt(pipeline.LastRetainedInputSnapshot!, CompositionDuration.Zero);
        var middle = CompositionTransformDemoRunner.BuildAnimatedCompositionFrameAt(pipeline.LastRetainedInputSnapshot!, CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency));
        var summary = CompositionTransformDemoRunner.FormatDemoSummary(
            new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: 1,
                CommandCount: frame.Commands.Count,
                TranslatedCommands: first.Layer.CommandCount,
                OpacityAppliedCommands: first.Layer.CommandCount),
            demoDurationMs: 4000,
            renderCount: 1,
            compositionTickCount: 120,
            frameSerial: 120,
            presentSerial: 120,
            syncWaits: 0,
            deviceRemoved: false);

        Assert.Contains("--composition-demo", programSource);
        Assert.Contains("CompositionTransformDemoRunner.RunAsync", programSource);
        Assert.DoesNotContain("Task.Delay", demoSource);
        Assert.DoesNotContain("Task.Yield", demoSource);
        Assert.DoesNotContain("ResolveFrameDelayMilliseconds", demoSource);
        Assert.Equal(2003, first.Layer.Id.Value);
        Assert.True(first.Layer.CommandStart >= 0);
        Assert.True(first.Layer.CommandCount > 0);
        Assert.NotEqual(first.Layer.Transform, middle.Layer.Transform);
        Assert.NotEqual(first.Layer.Opacity, middle.Layer.Opacity);
        Assert.Equal($"composition.demo finalComposition=D3D12 d3d12Backed=True layers=1 commands={frame.Commands.Count} translatedCommands={first.Layer.CommandCount} opacityAppliedCommands={first.Layer.CommandCount} clock=Stopwatch demoDurationMs=4000 animationDurationMs=1600 renderCount=1 compositionTicks=120 frameSerial=120 presentSerial=120 syncWaits=0 deviceRemoved=False", summary);
    }

    [Fact]
    public void Composition_transform_demo_progress_is_elapsed_time_based_not_frame_rate_based()
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            CompositionTransformDemoRunner.BuildDemoRoot(arena),
            new PixelRectangle(0, 0, 640, 360),
            arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var duration = CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency);
        var declaration60Hz = CompositionTransformDemoRunner.BuildAnimationDeclaration(CompositionTimestamp.Zero, duration);
        var declaration240Hz = CompositionTransformDemoRunner.BuildAnimationDeclaration(CompositionTimestamp.Zero, duration);
        var halfSecond = CompositionDuration.FromStopwatchTicks(Stopwatch.Frequency / 2);

        Assert.True(declaration60Hz.TryResolve(snapshot, frame.Commands.Count, out var plan60Hz));
        Assert.True(declaration240Hz.TryResolve(snapshot, frame.Commands.Count, out var plan240Hz));
        var frameAt60Hz = plan60Hz.Evaluate(frame.Commands.Count, CompositionTimestamp.Zero + halfSecond).Layer;
        var frameAt240Hz = plan240Hz.Evaluate(frame.Commands.Count, CompositionTimestamp.Zero + halfSecond).Layer;

        Assert.Equal(frameAt60Hz.Transform, frameAt240Hz.Transform);
        Assert.Equal(frameAt60Hz.Opacity, frameAt240Hz.Opacity);
    }

    [Fact]
    public void Glyph_atlas_regression_script_runs_fixed_matrix_soak_and_oracle_lane()
    {
        var root = FindRepoRoot();
        var script = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "glyph-atlas-regression.ps1")));

        Assert.Contains("[ValidateSet(\"Smoke\", \"Local\", \"Nightly\")]", script);
        Assert.Contains("SoakFrames = 60", script);
        Assert.Contains("SoakFrames = 300", script);
        Assert.Contains("SoakFrames = 900", script);
        Assert.Contains("--diagnose-glyph-atlas-matrix", script);
        Assert.Contains("--diagnose-glyph-atlas-soak", script);
        Assert.Contains("--diagnose-glyph-atlas-color-formats", script);
        Assert.Contains("--diagnose-glyph-atlas-bidi-oracle", script);
        Assert.Contains("--diagnose-glyph-atlas-glyph-oracle", script);
        Assert.Contains("matrix.expected", script);
        Assert.Contains("matrix.actual", script);
        Assert.Contains("Soak thresholds:", script);
        Assert.Contains("soak.actual", script);
        Assert.Contains("Color glyph natural coverage:", script);
        Assert.Contains("bidi-oracle.expected", script);
        Assert.Contains("bidi-oracle.actual", script);
        Assert.Contains("glyph-oracle.expected", script);
        Assert.Contains("glyph-oracle.actual", script);
        Assert.Contains("Assert-RegressionSummaries", script);
        Assert.Contains("glyph-atlas-regression.guard status=Passed", script);
        Assert.Contains("matrix.actual", script);
        Assert.Contains("degradedRuns\" \"0\"", script);
        Assert.Contains("glyphAtlasInitialized\" \"True\"", script);
        Assert.Contains("hardFullWithoutReuse\" \"0\"", script);
        Assert.Contains("RecordFailed=0", script);
        Assert.Contains("Assert-FieldsMatch $bidiExpectedFields $bidiActualFields", script);
        Assert.Contains("Assert-FieldsMatch $glyphExpectedFields $glyphActualFields", script);
        Assert.Contains("Glyph oracle:", script);
        Assert.Contains("finalComposition\" \"D3D12\"", script);
    }

    [Fact]
    public void Local_diagnostic_scripts_enable_optional_diagnostics_build()
    {
        var root = FindRepoRoot();
        var diagnosticBaseline = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "diagnostic-baseline.ps1")));
        var glyphRegression = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "glyph-atlas-regression.ps1")));
        var diagnose = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "diagnose.ps1")));

        Assert.Contains("\"-p:IrixDiagnostics=true\"", diagnosticBaseline);
        Assert.Contains("dotnet publish $pocProject -c Release -r win-x64 --self-contained -p:IrixDiagnostics=true", diagnosticBaseline);
        Assert.Contains("\"-p:IrixDiagnostics=true\"", glyphRegression);
        Assert.Contains("-p:IrixDiagnostics=true -- --diagnose", diagnose);
    }

    [Fact]
    public void Glyph_atlas_regression_lane_is_pre_merge_ci_step()
    {
        var root = FindRepoRoot();
        var workflow = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")));

        Assert.Contains("suite: glyph-atlas", workflow);
        Assert.Contains("Glyph atlas regression lane", workflow);
        Assert.Contains(".\\scripts\\glyph-atlas-regression.ps1 -Mode Smoke", workflow);
        Assert.Contains("windows-2025", workflow);
    }

    [Fact]
    public void Glyph_atlas_status_documents_actions_quota_and_local_guard_source()
    {
        var root = FindRepoRoot();
        var status = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Project_Status_and_Todo.md")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Glyph-Atlas-Design.md")));
        var worklist = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Active-Worklist.md")));

        Assert.Contains("GitHub Actions quota is currently exhausted", status);
        Assert.Contains("local guard summary is the current status source", design);
        Assert.Contains("TestResults\\glyph-atlas-regression-*-*.guard.summary.txt", status);
        Assert.Contains("Run `Smoke` before/after broad changes", worklist);
        Assert.Contains("Do not add artifact-upload work until Actions quota returns", worklist);
        Assert.Contains("`Nightly` after page-policy, eviction, or shaping overhauls", worklist);
    }

    [Fact]
    public void Project_docs_do_not_use_obsolete_release_or_version_stage_labels()
    {
        var root = FindRepoRoot();
        var docsRoot = Path.Combine(root, "docs");
        var obsoleteTerms = new[]
        {
            "v0",
            "v1",
            "V1",
            "Roadmap",
            "roadmap",
            "Backlog",
            "backlog",
            "milestone",
            "Milestone",
            "D3D11On12",
            "D2D overlay",
        };
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            if (ContainsWholeWord(relative, "GA") || ContainsWholeWord(text, "GA"))
            {
                offenders.Add($"{relative}: GA");
            }
            if (ContainsWholeWord(relative, "MVP") || ContainsWholeWord(text, "MVP"))
            {
                offenders.Add($"{relative}: MVP");
            }
            foreach (var term in obsoleteTerms)
            {
                if (relative.Contains(term, StringComparison.Ordinal) || text.Contains(term, StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {term}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        var index = -1;
        while ((index = text.IndexOf(word, index + 1, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + word.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];
            if (!IsAsciiLetterOrDigit(before) && !IsAsciiLetterOrDigit(after))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiLetterOrDigit(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    [Fact]
    public void Glyph_atlas_soak_thresholds_are_machine_readable()
    {
        Assert.Equal("Soak thresholds: noDeviceLost=True, finalComposition=D3D12, hardFullWithoutReuse=0, countersPresent=fragmentation|eviction|reuse|residentBytes", GlyphAtlasSoakDiagnosticRunner.FormatThresholds());
        Assert.Equal("soak.actual deviceLost=False finalComposition=D3D12 syncWaits=0 hardFullWithoutReuse=0 countersPresent=False", GlyphAtlasSoakDiagnosticRunner.FormatThresholdActual(deviceLost: false, syncWaits: 0, GlyphAtlasSoakSummary.Empty));
    }

    [Fact]
    public void Glyph_atlas_design_guard_gates_coverage_expansion()
    {
        var root = FindRepoRoot();
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Glyph-Atlas-Design.md")));

        Assert.Contains("Guarded Coverage Expansion", design);
        Assert.Contains("Glyph atlas coverage is guard-gated", design);
        Assert.Contains("New script or glyph-image-format support should move forward when it includes matching shaping oracle, regression matrix, and degradation-policy coverage", design);
        Assert.Contains("D3D12-only Degradation Policy", design);
        Assert.Contains("SVG and COLR paint-tree-only color glyphs", design);
        Assert.Contains("BiDi beyond the current resolved-level segment projection", design);
        Assert.Contains("AtlasFull after the 48-page budget", design);
        Assert.Contains("Degradation must preserve renderer stability, diagnostics, and clip semantics", design);
        Assert.Contains("Entry eviction design update", design);
        Assert.Contains("entry-level LRU and a sub-rect free-list remain design-only", design);
    }

    [Fact]
    public void Glyph_atlas_entry_eviction_remains_design_only_until_retained_ownership_is_explicit()
    {
        var root = FindRepoRoot();
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Glyph-Atlas-Entry-Eviction-Design.md")));
        var soakRunnerSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "GlyphAtlasSoakDiagnosticRunner.optional-diagnostics.cs")));
        var rendererSource = string.Join(
            "\n",
            Directory.EnumerateFiles(Path.Combine(root, "src", "Irix.Platform.Windows"), "D3D12GlyphAtlasTextRenderer*.cs")
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(static path => NormalizeLineEndings(File.ReadAllText(path))));

        Assert.Contains("design-only follow-up", design);
        Assert.Contains("Do not implement entry-level LRU or a sub-rect free-list until retained atlas command ownership is explicit", design);
        Assert.Contains("current page-level reuse policy remains stable under the fixed regression/soak lane", design);
        Assert.Contains("Retained atlas command ownership must expose the oldest retained atlas record serial", design);
        Assert.Contains("Do not add unguarded renderer coverage; new coverage needs matching oracle/regression coverage", design);
        Assert.Contains("entryLru=False", soakRunnerSource);
        Assert.Contains("subRectFreeList=False", soakRunnerSource);
        Assert.DoesNotContain("EntryLru", rendererSource);
        Assert.DoesNotContain("SubRectFreeList", rendererSource);
    }

    [Fact]
    public void Glyph_atlas_bidi_oracle_cli_is_wired()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var runnerSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "GlyphAtlasBidiOracleDiagnosticRunner.optional-diagnostics.cs")));
        var platformSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "DWriteBidiOracleDiagnostic.optional-diagnostics.cs")));

        Assert.Equal(
            "bidi-oracle.expected probes=4 labels=ltr-arabic-ltr|rtl-leading-digits|hebrew-weak-digits|nested-mixed fields=levels|logicalRuns|visualRuns|charOrder layoutOracle=False pixelOracle=False finalComposition=D3D12",
            GlyphAtlasBidiOracleDiagnosticRunner.FormatExpectedSnapshot());
        Assert.Contains("--diagnose-glyph-atlas-bidi-oracle", programSource);
        Assert.Contains("GlyphAtlasBidiOracleDiagnosticRunner.Run", programSource);
        Assert.Contains("DWriteBidiOracleDiagnostic.Capture()", runnerSource);
        Assert.Contains("FormatExpectedSnapshot()", runnerSource);
        Assert.Contains("bidi-oracle.actual probes={snapshot.ProbeCount}", runnerSource);
        Assert.Contains("BiDi oracle: factory={snapshot.FactoryAvailable}", runnerSource);
        Assert.Contains("visualRuns=", runnerSource);
        Assert.Contains("charOrder=", runnerSource);
        Assert.Contains("CreateTextAnalyzer(&analyzer)", platformSource);
        Assert.Contains("analyzer->AnalyzeBidi(", platformSource);
        Assert.Contains("TextAnalysisSourceShim", platformSource);
        Assert.Contains("TextAnalysisSinkSetBidiLevel", platformSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder", platformSource);
        Assert.DoesNotContain("CreateTextLayout", platformSource);
    }

    [Fact]
    public void Glyph_atlas_glyph_oracle_cli_is_wired_without_layout_dependency()
    {
        var root = FindRepoRoot();
        var programSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var runnerSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "GlyphAtlasGlyphOracleDiagnosticRunner.optional-diagnostics.cs")));
        var platformSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "DWriteGlyphOracleDiagnostic.optional-diagnostics.cs")));

        Assert.Equal(
            "glyph-oracle.expected probes=5 labels=ascii|cjk-fallback|arabic-rtl|mixed-bidi|tab-crlf fields=glyphCount|glyphIndices|advances|offsets|bidiLevels|lineBreaks|segments layoutOracle=False pixelOracle=False finalComposition=D3D12",
            GlyphAtlasGlyphOracleDiagnosticRunner.FormatExpectedSnapshot());
        Assert.Contains("--diagnose-glyph-atlas-glyph-oracle", programSource);
        Assert.Contains("GlyphAtlasGlyphOracleDiagnosticRunner.Run", programSource);
        Assert.Contains("DWriteGlyphOracleDiagnostic.Capture()", runnerSource);
        Assert.Contains("FormatExpectedSnapshot()", runnerSource);
        Assert.Contains("glyph-oracle.actual probes={snapshot.ProbeCount}", runnerSource);
        Assert.Contains("Glyph oracle: factory={snapshot.FactoryAvailable}", runnerSource);
        Assert.Contains("glyphCount=", runnerSource);
        Assert.Contains("bidiLevels=", runnerSource);
        Assert.Contains("lineBreaks=", runnerSource);
        Assert.Contains("segments=", runnerSource);
        Assert.Contains("glyphs=", runnerSource);
        Assert.Contains("glyph.Advance.ToString", runnerSource);
        Assert.Contains("glyph.AdvanceOffset", runnerSource);
        Assert.Contains("glyph.AscenderOffset", runnerSource);
        Assert.Contains("IDWriteFontFallback*", platformSource);
        Assert.Contains("fontFallback->MapCharacters(", platformSource);
        Assert.Contains("analyzer->AnalyzeScript(", platformSource);
        Assert.Contains("analyzer->AnalyzeBidi(", platformSource);
        Assert.Contains("analyzer->AnalyzeLineBreakpoints(", platformSource);
        Assert.Contains("analyzer->GetGlyphs(", platformSource);
        Assert.Contains("analyzer->GetGlyphPlacements(", platformSource);
        Assert.Contains("GlyphOracleLineBreak", platformSource);
        Assert.Contains("GlyphOracleSegment", platformSource);
        Assert.Contains("GlyphOracleGlyph", platformSource);
        Assert.DoesNotContain("CreateTextLayout", platformSource);
    }

    [Fact]
    public void Text_cache_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new TextCacheAllocationDiagnosticRunner.AllocationAttribution(
            TreeBytes: 300,
            DiffBytes: 120,
            TranslateBytes: 600,
            RenderBytes: 180);

        var summary = TextCacheAllocationDiagnosticRunner.FormatAllocationAttribution(attribution, frameCount: 3);

        Assert.Equal(
            "Allocation attribution: tree=300 bytes (100/frame), diff=120 bytes (40/frame), translate=600 bytes (200/frame), render=180 bytes (60/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_translate_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new WindowTranslateAllocationAttribution(
            RetainedApplyBytes: 120,
            ViewportBytes: 0,
            PipelineBuildBytes: 600,
            FeedbackBytes: 180,
            PipelineAttribution: new RenderPipelineBuildAllocationAttribution(
                ClassificationBytes: 12,
                LayoutBytes: 24,
                RecordBytes: 36,
                HitTargetsBytes: 48,
                SnapshotBytes: 60,
                RetainedFrameBytes: 72));

        var summary = TextCacheAllocationDiagnosticRunner.FormatTranslateAllocationAttribution(attribution, frameCount: 3);

        Assert.Equal(
            "Translate allocation: retainedApply=120 bytes (40/frame), viewport=0 bytes (0/frame), pipeline=600 bytes (200/frame), feedback=180 bytes (60/frame), measuredTotal=900 bytes (300/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_tree_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new TextCacheAllocationDiagnosticRunner.TreeAllocationAttribution(
            BeginFrameBytes: 30,
            BuildRootBytes: 210,
            SnapshotBytes: 60,
            BuildRootAttribution: new TextCacheAllocationDiagnosticRunner.BuildRootAllocationAttribution(
                ButtonBytes: 90,
                TextBytes: 30,
                ScrollPropertyBytes: 12,
                ChildrenBytes: 18,
                ContainerBytes: 60,
                ButtonAttribution: new TextCacheAllocationDiagnosticRunner.ButtonAllocationAttribution(
                    ActionPropertyBytes: 0,
                    LabelTextBytes: 12,
                    LabelNodeBytes: 0,
                    ChildrenArrayBytes: 30,
                    PropertyArrayBytes: 48,
                    ButtonNodeBytes: 0,
                    MeasuredBytes: 90)),
            SnapshotAttribution: new TextBufferSnapshotAllocationAttribution(
                CharBufferBytes: 48,
                SnapshotShellBytes: 0,
                MeasuredBytes: 48));

        var summary = TextCacheAllocationDiagnosticRunner.FormatTreeAllocationAttribution(attribution, frameCount: 3);
        var snapshotSummary = TextCacheAllocationDiagnosticRunner.FormatTreeSnapshotAllocationAttribution(attribution.SnapshotAttribution, frameCount: 3);
        var buildRootSummary = TextCacheAllocationDiagnosticRunner.FormatBuildRootAllocationAttribution(attribution.BuildRootAttribution, frameCount: 3);
        var buttonSummary = TextCacheAllocationDiagnosticRunner.FormatButtonAllocationAttribution(attribution.BuildRootAttribution.ButtonAttribution, frameCount: 3);

        Assert.Equal(
            "Tree allocation: beginFrame=30 bytes (10/frame), buildRoot=210 bytes (70/frame), snapshot=60 bytes (20/frame), measuredTotal=300 bytes (100/frame)",
            summary);
        Assert.Equal(
            "Tree snapshot allocation: textBuffer=48 bytes (16/frame), snapshotShell=0 bytes (0/frame), detailGap=0 bytes (0/frame), measuredTotal=48 bytes (16/frame)",
            snapshotSummary);
        Assert.Equal(
            "BuildRoot allocation: "
            + "buttons=90 bytes (30/frame), text=30 bytes (10/frame), "
            + "scrollProperty=12 bytes (4/frame), children=18 bytes (6/frame), "
            + "container=60 bytes (20/frame), measuredTotal=210 bytes (70/frame)",
            buildRootSummary);
        Assert.Equal(
            "Button allocation: "
            + "actionProperty=0 bytes (0/frame), labelText=12 bytes (4/frame), "
            + "labelNode=0 bytes (0/frame), childrenArray=30 bytes (10/frame), "
            + "propertyArray=48 bytes (16/frame), buttonNode=0 bytes (0/frame), "
            + "detailGap=0 bytes (0/frame), measuredTotal=90 bytes (30/frame)",
            buttonSummary);
    }

    [Fact]
    public void Text_cache_pipeline_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new RenderPipelineBuildAllocationAttribution(
            ClassificationBytes: 12,
            LayoutBytes: 24,
            RecordBytes: 36,
            HitTargetsBytes: 48,
            SnapshotBytes: 60,
            RetainedFrameBytes: 72,
            SnapshotAttribution: new RenderPipelineSnapshotAllocationAttribution(
                FrameBatchBytes: 12,
                RetainedInputBytes: 48,
                MeasuredBytes: 60));

        var summary = TextCacheAllocationDiagnosticRunner.FormatPipelineAllocationAttribution(attribution, frameCount: 3);
        var snapshotSummary = TextCacheAllocationDiagnosticRunner.FormatPipelineSnapshotAllocationAttribution(attribution.SnapshotAttribution, frameCount: 3);

        Assert.Equal(
            "Pipeline allocation: classify=12 bytes (4/frame), layout=24 bytes (8/frame), record=36 bytes (12/frame), hitTargets=48 bytes (16/frame), snapshot=60 bytes (20/frame), retainedFrame=72 bytes (24/frame), measuredTotal=252 bytes (84/frame)",
            summary);
        Assert.Equal(
            "Pipeline snapshot allocation: frameBatch=12 bytes (4/frame), retainedInput=48 bytes (16/frame), detailGap=0 bytes (0/frame), measuredTotal=60 bytes (20/frame)",
            snapshotSummary);
    }

    [Fact]
    public void Text_cache_layout_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new LayoutBuildAllocationAttribution(
            NodeWalkBytes: 12,
            DirtyRangeBytes: 24,
            ElementArrayBytes: 36,
            TreeNodeArrayBytes: 48,
            ScrollDiagnosticsArrayBytes: 60,
            ResultBytes: 72);

        var summary = TextCacheAllocationDiagnosticRunner.FormatLayoutAllocationAttribution(attribution, frameCount: 3);

        Assert.Equal(
            "Layout allocation: nodeWalk=12 bytes (4/frame), dirtyRanges=24 bytes (8/frame), elementsArray=36 bytes (12/frame), treeNodesArray=48 bytes (16/frame), scrollDiagnosticsArray=60 bytes (20/frame), result=72 bytes (24/frame), measuredTotal=252 bytes (84/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_record_allocation_attribution_formatter_outputs_stable_stage_fields()
    {
        var attribution = new DrawCommandRecordAllocationAttribution(
            ResourcesBytes: 12,
            StylesBytes: 24,
            CommandBuildBytes: 144,
            DirtyRangesBytes: 0);

        var summary = TextCacheAllocationDiagnosticRunner.FormatRecordAllocationAttribution(attribution, frameCount: 3);

        Assert.Equal(
            "Record allocation: resources=12 bytes (4/frame), styles=24 bytes (8/frame), commandBuild=144 bytes (48/frame), dirtyRanges=0 bytes (0/frame), measuredTotal=180 bytes (60/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_allocation_focus_formatter_selects_largest_candidate_bucket()
    {
        var attribution = new TextCacheAllocationDiagnosticRunner.AllocationAttribution(
            TreeBytes: 390,
            DiffBytes: 90,
            TranslateBytes: 660,
            RenderBytes: 120);
        var treeAttribution = new TextCacheAllocationDiagnosticRunner.TreeAllocationAttribution(
            BeginFrameBytes: 0,
            BuildRootBytes: 210,
            SnapshotBytes: 60,
            BuildRootAttribution: new TextCacheAllocationDiagnosticRunner.BuildRootAllocationAttribution(
                ButtonBytes: 90,
                TextBytes: 30,
                ScrollPropertyBytes: 0,
                ChildrenBytes: 0,
                ContainerBytes: 90,
                ButtonAttribution: new TextCacheAllocationDiagnosticRunner.ButtonAllocationAttribution(
                    ActionPropertyBytes: 0,
                    LabelTextBytes: 0,
                    LabelNodeBytes: 0,
                    ChildrenArrayBytes: 120,
                    PropertyArrayBytes: 90,
                    ButtonNodeBytes: 0,
                    MeasuredBytes: 210)));
        var translateAttribution = new WindowTranslateAllocationAttribution(
            RetainedApplyBytes: 0,
            ViewportBytes: 0,
            PipelineBuildBytes: 600,
            FeedbackBytes: 60,
            PipelineAttribution: new RenderPipelineBuildAllocationAttribution(
                ClassificationBytes: 0,
                LayoutBytes: 300,
                RecordBytes: 30,
                HitTargetsBytes: 45,
                SnapshotBytes: 150,
                RetainedFrameBytes: 75,
                LayoutAttribution: new LayoutBuildAllocationAttribution(
                    NodeWalkBytes: 30,
                    DirtyRangeBytes: 15,
                    ElementArrayBytes: 330,
                    TreeNodeArrayBytes: 60,
                    ScrollDiagnosticsArrayBytes: 45,
                    ResultBytes: 0)));

        var summary = TextCacheAllocationDiagnosticRunner.FormatAllocationFocus(attribution, treeAttribution, translateAttribution, frameCount: 3);

        Assert.Equal(
            "Allocation focus: largestCandidate=layout.elementsArray=330 bytes (110/frame), nextCandidate=pipeline.snapshot=150 bytes (50/frame), treeDetailGap=120 bytes (40/frame), pipelineDetailGap=0 bytes (0/frame), drawRecord=30 bytes (10/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_allocation_focus_formatter_uses_button_leaf_buckets()
    {
        var attribution = new TextCacheAllocationDiagnosticRunner.AllocationAttribution(
            TreeBytes: 390,
            DiffBytes: 90,
            TranslateBytes: 300,
            RenderBytes: 120);
        var treeAttribution = new TextCacheAllocationDiagnosticRunner.TreeAllocationAttribution(
            BeginFrameBytes: 0,
            BuildRootBytes: 300,
            SnapshotBytes: 60,
            BuildRootAttribution: new TextCacheAllocationDiagnosticRunner.BuildRootAllocationAttribution(
                ButtonBytes: 210,
                TextBytes: 0,
                ScrollPropertyBytes: 0,
                ChildrenBytes: 0,
                ContainerBytes: 90,
                ButtonAttribution: new TextCacheAllocationDiagnosticRunner.ButtonAllocationAttribution(
                    ActionPropertyBytes: 0,
                    LabelTextBytes: 0,
                    LabelNodeBytes: 0,
                    ChildrenArrayBytes: 120,
                    PropertyArrayBytes: 90,
                    ButtonNodeBytes: 0,
                    MeasuredBytes: 210)));
        var translateAttribution = new WindowTranslateAllocationAttribution(
            RetainedApplyBytes: 0,
            ViewportBytes: 0,
            PipelineBuildBytes: 240,
            FeedbackBytes: 60,
            PipelineAttribution: new RenderPipelineBuildAllocationAttribution(
                ClassificationBytes: 0,
                LayoutBytes: 30,
                RecordBytes: 30,
                HitTargetsBytes: 45,
                SnapshotBytes: 75,
                RetainedFrameBytes: 60));

        var summary = TextCacheAllocationDiagnosticRunner.FormatAllocationFocus(attribution, treeAttribution, translateAttribution, frameCount: 3);

        Assert.Equal(
            "Allocation focus: largestCandidate=tree.buildRoot.button.childrenArray=120 bytes (40/frame), nextCandidate=tree.buildRoot.button.propertyArray=90 bytes (30/frame), treeDetailGap=30 bytes (10/frame), pipelineDetailGap=0 bytes (0/frame), drawRecord=30 bytes (10/frame)",
            summary);
    }

    [Fact]
    public void Text_cache_allocation_diagnostic_uses_frame_scoped_text_arena()
    {
        var root = FindRepoRoot();
        var source = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "TextCacheAllocationDiagnosticRunner.optional-diagnostics.cs")));

        Assert.Contains("private static VirtualNodeTree BuildScenarioTree(VirtualTextArena arena, string text, int scrollY)", source);
        Assert.Contains("arena.BeginFrame();", source);
        Assert.Contains("var snapshot = arena.GetOrCreateSnapshotWithAllocationAttribution(out var snapshotAttribution);", source);
        Assert.Contains("return new VirtualNodeTree(root, snapshot);", source);
        Assert.Contains("out var treeFrameAttribution", source);
        Assert.Contains("treeAttribution = treeAttribution.Add(treeFrameAttribution);", source);
        Assert.Contains("output.WriteLine(FormatTreeSnapshotAllocationAttribution(treeAttribution.SnapshotAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatBuildRootAllocationAttribution(treeAttribution.BuildRootAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatButtonAllocationAttribution(treeAttribution.BuildRootAttribution.ButtonAttribution, frameCount));", source);
        Assert.Contains("BuildRootAllocationAttribution", source);
        Assert.Contains("ButtonAllocationAttribution", source);
        Assert.Contains("TextBufferSnapshotAllocationAttribution", source);
        Assert.Contains("BuildMeasuredButton", source);
        Assert.Contains("attribution = attribution.WithButton", source);
        Assert.Contains("attribution = attribution.WithText", source);
        Assert.Contains("attribution = attribution.WithScrollProperty", source);
        Assert.Contains("attribution = attribution.WithChildren", source);
        Assert.Contains("attribution = attribution.WithContainer", source);
        Assert.Contains("using var batch = translator.TranslateWithAllocationAttribution(patch, out var translateFrameAttribution);", source);
        Assert.Contains("output.WriteLine(FormatTreeAllocationAttribution(treeAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatTranslateAllocationAttribution(translateAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatPipelineAllocationAttribution(translateAttribution.PipelineAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatPipelineSnapshotAllocationAttribution(translateAttribution.PipelineAttribution.SnapshotAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatLayoutAllocationAttribution(translateAttribution.PipelineAttribution.LayoutAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatRecordAllocationAttribution(translateAttribution.PipelineAttribution.RecordAttribution, frameCount));", source);
        Assert.Contains("output.WriteLine(FormatAllocationFocus(attribution, treeAttribution, translateAttribution, frameCount));", source);
        Assert.DoesNotContain("return new VirtualNodeTree(BuildRoot", source);
    }

    [Fact]
    public void Diagnostic_only_sources_are_optional_compile_items()
    {
        var root = FindRepoRoot();
        var targets = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "Directory.Build.targets")));
        var mainProgramSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.cs")));
        var diagnosticProgramSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var counterApplicationSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "CounterApplication.cs")));
        var diagnosticCounterApplicationSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "CounterApplication.optional-diagnostics.cs")));
        var inputOwnershipSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "InputOwnershipState.cs")));
        var diagnosticInputOwnershipSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "InputOwnershipState.optional-diagnostics.cs")));

        Assert.Contains("<Compile Remove=\"**\\*.optional-diagnostics.cs\" />", targets);
        Assert.DoesNotContain("<Compile Remove=\"**\\*.diagnostics.cs\" />", targets);
        Assert.True(File.Exists(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.Diagnostics.cs")));
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", diagnosticProgramSource, StringComparison.Ordinal);
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", diagnosticCounterApplicationSource, StringComparison.Ordinal);
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", diagnosticInputOwnershipSource, StringComparison.Ordinal);
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "CompositionExecutionDiagnostics.optional-diagnostics.cs"))), StringComparison.Ordinal);
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "DrawingBackendCompositor.optional-diagnostics.cs"))), StringComparison.Ordinal);
        Assert.Contains("static partial void CreateDiagnosticCliTask", mainProgramSource);
        Assert.Contains("static partial void CreateDiagnosticCliTask", diagnosticProgramSource);
        Assert.Contains("partial void TryBuildOptionalHeaderRows", counterApplicationSource);
        Assert.Contains("CounterViewportDiagnostics", diagnosticCounterApplicationSource);
        Assert.DoesNotContain("FullDiagnosticRunner", mainProgramSource);
        Assert.DoesNotContain("--debug-ui", mainProgramSource);
        Assert.DoesNotContain("--diagnose-glyph-atlas", mainProgramSource);
        Assert.DoesNotContain("--diagnose-text-cache", mainProgramSource);
        Assert.DoesNotContain("--diagnose-composition", mainProgramSource);
        Assert.DoesNotContain("CounterViewportDiagnostics", counterApplicationSource);
        Assert.DoesNotContain("DebugDiagnosticsChanged", counterApplicationSource);
        Assert.DoesNotContain("InputOwnershipEvent", inputOwnershipSource);
        Assert.DoesNotContain("DiagnosticEvents", inputOwnershipSource);
        Assert.Contains("InputOwnershipEvent", diagnosticInputOwnershipSource);

        var offenders = new List<string>();
        foreach (var sourcePath in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(sourcePath);
            var isOptionalDiagnostics = fileName.EndsWith(".optional-diagnostics.cs", StringComparison.OrdinalIgnoreCase);
            var isDiagnosticOnly =
                fileName.Contains("DiagnosticRunner", StringComparison.Ordinal)
                || fileName.EndsWith("SmokeDiagnostics.cs", StringComparison.Ordinal)
                || fileName.EndsWith("SmokeDiagnostics.optional-diagnostics.cs", StringComparison.Ordinal)
                || fileName.StartsWith("DWrite", StringComparison.Ordinal) && fileName.Contains("Diagnostic", StringComparison.Ordinal);
            if (isOptionalDiagnostics)
            {
                var optionalSource = NormalizeLineEndings(File.ReadAllText(sourcePath));
                Assert.StartsWith("#if IRIX_DIAGNOSTICS", optionalSource, StringComparison.Ordinal);
                continue;
            }

            if (isDiagnosticOnly)
            {
                offenders.Add(Path.GetRelativePath(root, sourcePath));
                continue;
            }

            var source = File.ReadAllText(sourcePath);
            if (source.Contains("AllocationAttribution", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(root, sourcePath));
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Glyph_atlas_stress_report_includes_atlas_full_fallback_contract()
    {
        var glyphAtlas = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 470,
            UploadedBytes: 1048576,
            DrawnGlyphs: 1200,
            CacheHits: 40,
            CacheMisses: 471,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 8192,
            RasterScratchResizes: 4)
            .WithDegradation(1, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.AtlasFull);
        var frameSerial = new D3D12Renderer.FrameSerialDiagnostics(
            FrameSerial: 1,
            PresentSerial: 1,
            SyncWaitCount: 0,
            SyncWaitTicks: 0,
            BackBufferIndex: 0);
        var writer = new StringWriter();

        GlyphAtlasStressDiagnosticRunner.WriteReport(
            writer,
            TextCompositionMode.GlyphAtlas,
            refreshRateHz: 240,
            new DisplayScale(1.5f, 1.5f),
            runCount: 32,
            asciiCharsPerRun: 95,
            scenarioName: "AtlasFull",
            deviceRemoved: false,
            deviceError: DeviceErrorDiagnostic.None,
            frameSerial,
            glyphAtlas);

        var report = writer.ToString();
        Assert.Contains("=== Glyph Atlas Stress Diagnostic ===", report);
        Assert.Contains("Scenario: AtlasFull", report);
        Assert.Contains("Text composition mode: GlyphAtlas", report);
        Assert.Contains("Device removed: False", report);
        Assert.Contains("Frame serial: frameSerial=1, presentSerial=1, syncWaits=0", report);
        Assert.Contains("atlasBudgetPages=48", report);
        Assert.Contains("atlasCapacity=50331648 px", report);
        Assert.Contains("degradedRuns=1", report);
        Assert.Contains("AtlasFull=1", report);
        Assert.Contains("=== Glyph atlas stress diagnostic complete ===", report);
    }

    [Fact]
    public void Glyph_atlas_mixed_stress_commands_keep_prefix_atlas_candidates_and_trailing_fallback()
    {
        using var resources = new FrameDrawingResources();
        var ascii = new string(Enumerable.Range(32, 95).Select(static code => (char)code).ToArray());
        var commands = GlyphAtlasStressDiagnosticRunner.BuildMixedFallbackStressCommands(resources, ascii, 960, 540);
        resources.Seal();

        var textCommandCount = commands.Count(static command => command.Kind == DrawCommandKind.DrawTextRun);
        var firstPrefixReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
            resources.Resolve(commands[1].Text),
            resources.ResolveTextStyle(commands[1].Resource));
        var secondPrefixReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
            resources.Resolve(commands[2].Text),
            resources.ResolveTextStyle(commands[2].Resource));
        var trailingReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
            resources.Resolve(commands[^1].Text),
            resources.ResolveTextStyle(commands[^1].Resource));

        Assert.Equal(35, textCommandCount);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, firstPrefixReason);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, secondPrefixReason);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii, trailingReason);
    }

    [Fact]
    public void Glyph_atlas_reuse_stress_commands_are_atlas_candidates()
    {
        using var resources = FrameDrawingResources.Rent();
        var commands = GlyphAtlasStressDiagnosticRunner.BuildReuseCommands(resources, 960, 540);
        resources.Seal();

        var textCommandCount = commands.Count(static command => command.Kind == DrawCommandKind.DrawTextRun);
        var firstReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
            resources.Resolve(commands[1].Text),
            resources.ResolveTextStyle(commands[1].Resource));
        var secondReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
            resources.Resolve(commands[2].Text),
            resources.ResolveTextStyle(commands[2].Resource));

        Assert.Equal(2, textCommandCount);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, firstReason);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, secondReason);
    }

    [Fact]
    public void Glyph_atlas_soak_scenario_cadence_interleaves_pressure_matrix_wrap_and_reuse()
    {
        Assert.Equal(GlyphAtlasSoakScenario.Pressure, GlyphAtlasSoakDiagnosticRunner.SelectScenario(0, pressureEvery: 4));
        Assert.Equal(GlyphAtlasSoakScenario.Matrix, GlyphAtlasSoakDiagnosticRunner.SelectScenario(1, pressureEvery: 4));
        Assert.Equal(GlyphAtlasSoakScenario.Wrap, GlyphAtlasSoakDiagnosticRunner.SelectScenario(2, pressureEvery: 4));
        Assert.Equal(GlyphAtlasSoakScenario.Reuse, GlyphAtlasSoakDiagnosticRunner.SelectScenario(3, pressureEvery: 4));
        Assert.Equal(GlyphAtlasSoakScenario.Pressure, GlyphAtlasSoakDiagnosticRunner.SelectScenario(4, pressureEvery: 4));
    }

    [Fact]
    public void Glyph_atlas_soak_pressure_commands_are_alpha_atlas_candidates()
    {
        using var resources = FrameDrawingResources.Rent();
        var ascii = new string(Enumerable.Range(32, 95).Select(static code => (char)code).ToArray());
        var commands = GlyphAtlasSoakDiagnosticRunner.BuildPressureCommands(resources, ascii, 960, 540, pressureIndex: 2);
        resources.Seal();

        var textCommandCount = commands.Count(static command => command.Kind == DrawCommandKind.DrawTextRun);
        Assert.Equal(32, textCommandCount);
        for (var i = 1; i < commands.Length; i++)
        {
            var reason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(
                resources.Resolve(commands[i].Text),
                resources.ResolveTextStyle(commands[i].Resource));
            Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None, reason);
        }
    }

    [Fact]
    public void Glyph_atlas_soak_summary_tracks_peak_residency_and_current_reuse_counters()
    {
        var first = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 12,
            UploadedBytes: 1024,
            DrawnGlyphs: 30,
            CacheHits: 1,
            CacheMisses: 12,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            AtlasPages: 2,
            AtlasAlphaPages: 2,
            AtlasBgraPages: 0,
            AtlasUsedPixels: 120,
            AtlasFragmentedPixels: 30,
            AtlasAlphaUsedPixels: 120,
            AtlasBgraUsedPixels: 0,
            AtlasAlphaFragmentedPixels: 30,
            AtlasBgraFragmentedPixels: 0,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0);
        var second = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 24,
            UploadedBytes: 2048,
            DrawnGlyphs: 60,
            CacheHits: 2,
            CacheMisses: 24,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            AtlasPages: 3,
            AtlasAlphaPages: 2,
            AtlasBgraPages: 1,
            AtlasEvictions: 1,
            AtlasAlphaEvictions: 1,
            AtlasBgraEvictions: 0,
            AtlasPendingPageReuses: 1,
            AtlasPendingAlphaPageReuses: 1,
            AtlasPendingBgraPageReuses: 0,
            AtlasPageReuseRequests: 1,
            AtlasAlphaPageReuseRequests: 1,
            AtlasBgraPageReuseRequests: 0,
            AtlasFullWithoutPageReuse: 2,
            AtlasAlphaFullWithoutPageReuse: 1,
            AtlasBgraFullWithoutPageReuse: 1,
            AtlasUsedPixels: 180,
            AtlasFragmentedPixels: 50,
            AtlasAlphaUsedPixels: 140,
            AtlasBgraUsedPixels: 40,
            AtlasAlphaFragmentedPixels: 40,
            AtlasBgraFragmentedPixels: 10,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(2, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.AtlasFull);

        var summary = GlyphAtlasSoakSummary.Empty
            .WithFrame(GlyphAtlasSoakScenario.Pressure, first)
            .WithFrame(GlyphAtlasSoakScenario.Matrix, second);
        var formatted = GlyphAtlasSoakDiagnosticRunner.FormatSummary(summary);
        var policy = GlyphAtlasSoakDiagnosticRunner.FormatPagePolicy(summary);

        Assert.Equal(2, summary.Frames);
        Assert.Equal(1, summary.PressureFrames);
        Assert.Equal(1, summary.MatrixFrames);
        Assert.Equal(3, summary.MaxAtlasPages);
        Assert.Equal(2, summary.MaxAlphaPages);
        Assert.Equal(1, summary.MaxBgraPages);
        Assert.Equal(6291456, summary.MaxAtlasCpuBytes);
        Assert.Equal(6291456, summary.MaxAtlasGpuBytes);
        Assert.Equal(180, summary.MaxAtlasUsedPixels);
        Assert.Equal(50, summary.MaxAtlasFragmentedPixels);
        Assert.Equal(1, summary.AtlasEvictions);
        Assert.Equal(1, summary.AtlasPendingPageReuses);
        Assert.Equal(1, summary.AtlasPendingAlphaPageReuses);
        Assert.Equal(0, summary.AtlasPendingBgraPageReuses);
        Assert.Equal(1, summary.AtlasAlphaPageReuseRequests);
        Assert.Equal(2, summary.AtlasFullWithoutPageReuse);
        Assert.Equal(1, summary.AtlasAlphaFullWithoutPageReuse);
        Assert.Equal(1, summary.AtlasBgraFullWithoutPageReuse);
        Assert.Equal(2, summary.MaxDegradedRuns);
        Assert.Contains("pageReuse=FormatScopedColdPage", policy);
        Assert.Contains("currentRecordColdReuse=True", policy);
        Assert.Contains("sameRecordTouchedReuse=False", policy);
        Assert.Contains("entryLru=False", policy);
        Assert.Contains("subRectFreeList=False", policy);
        Assert.Contains("maxAtlasPages=3", formatted);
        Assert.Contains("atlasPendingPageReuses=1", formatted);
        Assert.Contains("atlasPendingAlphaPageReuses=1", formatted);
        Assert.Contains("atlasPendingBgraPageReuses=0", formatted);
        Assert.Contains("atlasPageReuseRequests=1", formatted);
        Assert.Contains("atlasFullWithoutPageReuse=2", formatted);
        Assert.Contains("atlasAlphaFullWithoutPageReuse=1", formatted);
        Assert.Contains("atlasBgraFullWithoutPageReuse=1", formatted);
        Assert.Contains("maxDegradedRuns=2", formatted);
    }

    [Fact]
    public void Glyph_atlas_soak_cli_is_wired()
    {
        var root = FindRepoRoot();
        var source = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.optional-diagnostics.cs")));
        var runnerSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "GlyphAtlasSoakDiagnosticRunner.optional-diagnostics.cs")));

        Assert.Contains("--diagnose-glyph-atlas-soak", source);
        Assert.Contains("--pressure-every", source);
        Assert.Contains("GlyphAtlasSoakDiagnosticRunner.Run", source);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.AtlasPageBudget", runnerSource);
        Assert.Contains("atlasPendingAlphaPageReuses={summary.AtlasPendingAlphaPageReuses}", runnerSource);
        Assert.Contains("atlasBgraFullWithoutPageReuse={summary.AtlasBgraFullWithoutPageReuse}", runnerSource);
    }

    [Fact]
    public void Glyph_atlas_record_failure_contract_degrades_all_renderable_runs()
    {
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var visibleA = resources.AddText("visible A");
        var visibleB = resources.AddText("visible B");
        var empty = resources.AddText("");
        resources.Seal();
        var runs = new[]
        {
            TextRun(visibleA, style, width: 100, height: 20),
            TextRun(empty, style, width: 100, height: 20),
            TextRun(visibleB, style, width: 120, height: 20)
        };

        var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(runs, resources);
        var diagnostics = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 0,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 0,
            RasterScratchResizes: 0)
            .WithDegradation(degradedRunCount, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.RecordFailed)
            .WithRecordFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.AtlasUploadMap);

        Assert.Equal(2, degradedRunCount);
        Assert.Equal(1, diagnostics.FallbackFrames);
        Assert.Equal(2, diagnostics.UnsupportedRuns);
        Assert.Equal(2, diagnostics.DegradedRuns);
        Assert.Equal(2, diagnostics.Reasons.RecordFailed);
        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.AtlasUploadMap, diagnostics.RecordFailurePhase);
    }

    #region Scroll Snapshot

    [Fact]
    public async Task Diagnose_scroll_outputs_scroll_pump_counters()
    {
        var writer = new StringWriter();

        await ScrollDiagnosticRunner.RunAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Scroll Pump Diagnostics ===", output);
        Assert.Contains("frames=2", output);
        Assert.Contains("waitMs=", output);
        Assert.Contains("dt=", output);
        Assert.Contains("drained=54.0", output);
        Assert.Contains("pending=0.0", output);
    }

    [Fact]
    public void Diagnose_scroll_snapshot_captures_formatter_fields()
    {
        var snapshot = new ScrollDiagnosticsSnapshot(
            DispatchedFrameCount: 2,
            RenderWaitMs: 30.125,
            LastDt: 0.0376,
            DrainedPixels: 54,
            LastFrameDrainedPixels: 0,
            PendingPixels: 0,
            FrameQueued: false,
            TickLoopRunning: false,
            AppliedScrollY: 54,
            TargetPosition: 54,
            MaxScrollY: 240,
            HasMaxScrollY: true);

        Assert.Equal(2, snapshot.DispatchedFrameCount);
        Assert.Equal(30.125, snapshot.RenderWaitMs);
        Assert.Equal(0.0376, snapshot.LastDt);
        Assert.Equal(54, snapshot.DrainedPixels);
        Assert.Equal(0, snapshot.LastFrameDrainedPixels);
        Assert.Equal(0, snapshot.PendingPixels);
        Assert.False(snapshot.FrameQueued);
        Assert.False(snapshot.TickLoopRunning);
        Assert.Equal(54, snapshot.AppliedScrollY);
        Assert.Equal(54, snapshot.TargetPosition);
        Assert.Equal(240, snapshot.MaxScrollY);
        Assert.True(snapshot.HasMaxScrollY);
    }

    [Fact]
    public void Diagnose_scroll_formatter_outputs_stable_fields()
    {
        var snapshot = new ScrollDiagnosticsSnapshot(
            DispatchedFrameCount: 2,
            RenderWaitMs: 30.125,
            LastDt: 0.0376,
            DrainedPixels: 54,
            LastFrameDrainedPixels: 0,
            PendingPixels: 0,
            FrameQueued: false,
            TickLoopRunning: false,
            AppliedScrollY: 54,
            TargetPosition: 54,
            MaxScrollY: 240,
            HasMaxScrollY: true);

        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildScrollDiagnosticLines(snapshot));

        Assert.Equal(string.Join(Environment.NewLine, [
            "=== Scroll Pump Diagnostics ===",
            "frames=2",
            "waitMs=30.125",
            "dt=0.0376",
            "drained=54.0",
            "lastFrameDrained=0.0",
            "pending=0.0",
            "=== Scroll diagnostic mode complete ==="
        ]), output);
    }

    #endregion

    #region Input Snapshot

    [Fact]
    public async Task Diagnose_input_outputs_ownership_state_transitions()
    {
        var writer = new StringWriter();

        await InputDiagnosticRunner.RunAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Input Ownership Diagnostics ===", output);
        Assert.Contains("buttonPriorityOrder Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("buttonState normal Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", output);
        Assert.Contains("buttonState hovered Increment hovered=True pressed=False focused=True priority=Hovered color=#FF4888FF", output);
        Assert.Contains("buttonState pressed Increment hovered=True pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("buttonState focused Increment hovered=False pressed=False focused=True priority=Focused color=#FF54A0FF", output);
        Assert.Contains("afterMove hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False", output);
        Assert.Contains("buttonState afterMove Increment hovered=True pressed=False focused=False priority=Hovered color=#FF4888FF", output);
        Assert.Contains("afterPress hover=Increment focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("buttonState afterPress Increment hovered=True pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("duringCaptureMove hover=Decrement focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("buttonState duringCaptureMove Increment hovered=False pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("releaseOutside mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("buttonState releaseOutside Increment hovered=False pressed=False focused=True priority=Focused color=#FF54A0FF", output);
        Assert.Contains("keyboardEnter mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("keyboardSpace mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("pressEmpty mapped=False hover=Decrement focus=- pressed=- capture=-", output);
        Assert.Contains("releaseAfterEmptyPress mapped=False", output);
        Assert.Contains("focusLost hover=- focus=- pressed=- capture=-", output);
        Assert.Contains("buttonState focusLost Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", output);
        Assert.Contains("HoverChanged previous=- current=Increment", output);
        Assert.Contains("FocusChanged previous=- current=Increment", output);
        Assert.Contains("PressedChanged previousPressed=- currentPressed=Increment", output);
        Assert.Contains("PressedChanged previousPressed=Increment currentPressed=-", output);
        Assert.Contains("FocusChanged previous=Increment current=-", output);
        Assert.Contains("dirtyReasons:", output);
        Assert.Contains("dirtyReason hoverOnly reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("dirtyReason press reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("dirtyReason release reason=TextSizeAffecting classifications=1:TextSizeAffecting/TextMeasure,4:StyleOnly/VisualOnly", output);
    }

    [Fact]
    public void Diagnose_input_snapshot_captures_formatter_fields()
    {
        var snapshot = InputDiagnosticRunner.BuildInputDiagnosticsSnapshot();

        Assert.True(snapshot.Ownership.HoveredTarget.IsNone);
        Assert.True(snapshot.Ownership.FocusedTarget.IsNone);
        Assert.True(snapshot.Ownership.PressedTarget.IsNone);
        Assert.True(snapshot.Ownership.CapturedTarget.IsNone);
        Assert.Equal(3, snapshot.Ownership.HoverChangeCount);
        Assert.False(snapshot.Ownership.IsPointerPressed);
        Assert.Contains(snapshot.OwnershipSteps, step => step.Kind == InputDiagnosticOwnershipStepKind.AfterMove && step.Ownership.HoveredTarget == ActionIdRegistry.Increment);
        Assert.Contains(snapshot.OwnershipSteps, step => step.Kind == InputDiagnosticOwnershipStepKind.KeyboardEnter && step is { HasMappedResult: true, Mapped: true } && step.Message is CounterMessage.Increment);
        Assert.Contains(snapshot.ButtonStates, state => state.Kind == InputDiagnosticButtonStateKind.Normal && state.ActionId == ActionIdRegistry.Increment && state.State == default);
        Assert.Contains(snapshot.ButtonStates, state => state.Kind == InputDiagnosticButtonStateKind.FocusLost && state.ActionId == ActionIdRegistry.Increment && state.State == default);
        Assert.Contains(snapshot.Events, diagnosticEvent => diagnosticEvent.Kind == InputOwnershipEventKind.HoverChanged && diagnosticEvent.PreviousTarget.IsNone && diagnosticEvent.CurrentTarget == ActionIdRegistry.Increment);
        Assert.Contains(snapshot.Events, diagnosticEvent => diagnosticEvent.Kind == InputOwnershipEventKind.FocusChanged && diagnosticEvent.PreviousTarget == ActionIdRegistry.Increment && diagnosticEvent.CurrentTarget.IsNone);
        Assert.Contains(snapshot.DirtyReasons, dirtyReason => dirtyReason.Case == InputDirtyReasonCase.HoverOnly && dirtyReason.Reason == LayoutRebuildReason.StyleOnly);
        Assert.Contains(snapshot.DirtyReasons, dirtyReason => dirtyReason.Case == InputDirtyReasonCase.Release && dirtyReason.Reason == LayoutRebuildReason.TextSizeAffecting && dirtyReason.Classifications.Count == 2);
        var ownershipLines = DiagnosticsFormatter.BuildInputOwnershipDiagnosticLines(snapshot);
        var buttonStateLines = DiagnosticsFormatter.BuildInputButtonStateDiagnosticLines(snapshot);
        var eventLines = DiagnosticsFormatter.BuildInputEventDiagnosticLines(snapshot);
        var dirtyReasonLines = DiagnosticsFormatter.BuildInputDirtyReasonDiagnosticLines(snapshot);
        Assert.Contains("afterMove hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False", ownershipLines);
        Assert.Contains(ownershipLines, line => line.StartsWith("keyboardEnter mapped=True message=Increment hover=Decrement focus=Increment", StringComparison.Ordinal));
        Assert.Contains("buttonState normal Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", buttonStateLines);
        Assert.Contains("buttonState focusLost Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", buttonStateLines);
        Assert.Contains("  HoverChanged previous=- current=Increment", eventLines);
        Assert.Contains("  FocusChanged previous=Increment current=-", eventLines);
        Assert.Contains("dirtyReason hoverOnly reason=StyleOnly classifications=4:StyleOnly/VisualOnly", dirtyReasonLines);
        Assert.Contains("dirtyReason release reason=TextSizeAffecting classifications=1:TextSizeAffecting/TextMeasure,4:StyleOnly/VisualOnly", dirtyReasonLines);
    }

    [Fact]
    public void Diagnose_input_formatter_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildInputDiagnosticLines(InputDiagnosticRunner.BuildInputDiagnosticsSnapshot()));

        Assert.Contains("=== Input Ownership Diagnostics ===", output);
        Assert.Contains("buttonPriorityOrder Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("afterPress hover=Increment focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("events:", output);
        Assert.Contains("dirtyReasons:", output);
        Assert.Contains("dirtyReason press reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("=== Input diagnostic mode complete ===", output);
    }

    #endregion

    #region Style Preset Diagnostics

    [Fact]
    public void Diagnose_style_preset_outputs_metrics_and_button_colors()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Contains("=== Style Preset Diagnostics ===", output);
        Assert.Contains("stylePreset name=RenderStylePreset.Default", output);
        Assert.Contains("layoutMetrics horizontalPadding=16 verticalPadding=16 itemSpacing=12 textHeight=32 buttonHeight=40 rectangleHeight=48 minimumButtonWidth=140 buttonTextWidthFactor=12 buttonHorizontalPadding=32", output);
        Assert.Contains("buttonStateColorPriority Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("buttonStateColor normal=#FF3478F6", output);
        Assert.Contains("buttonStateColor focused=#FF54A0FF", output);
        Assert.Contains("buttonStateColor hovered=#FF4888FF", output);
        Assert.Contains("buttonStateColor pressed=#FF245CD2", output);
    }

    #endregion

    #region StyleOnly Snapshot

    [Fact]
    public void Diagnose_style_only_patch_plan_snapshot_captures_formatter_fields()
    {
        var plan = StyleOnlyPatchPlan.CreateEligible(
            [(0, 1)],
            [(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(1), new PixelRectangle(0, 0, 960, 540))]);

        var snapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan(StyleOnlyPatchPlanCase.HoverOnly, plan);

        Assert.Equal(StyleOnlyPatchPlanCase.HoverOnly, snapshot.Case);
        Assert.True(snapshot.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.None, snapshot.FallbackReason);
        Assert.Equal([(0, 1)], snapshot.DirtyElementRanges);
        Assert.Equal([(0, 2)], snapshot.DirtyCommandRanges);
        Assert.Equal(1, snapshot.HitTargetCount);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_formatter_outputs_stable_fields()
    {
        var plan = StyleOnlyPatchPlan.CreateEligible(
            [(0, 1)],
            [(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(1), new PixelRectangle(0, 0, 960, 540))]);
        var snapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan(StyleOnlyPatchPlanCase.HoverOnly, plan);

        var line = DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(snapshot);

        Assert.Equal("styleOnlyPlan HoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", line);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_smoke_outputs_eligible_and_fallback()
    {
        var output = string.Join(Environment.NewLine, StyleOnlyPatchPlanSmokeDiagnostics.BuildDiagnosticLines());

        Assert.Contains("=== StyleOnly Patch Plan Diagnostics ===", output);
        Assert.Contains("styleOnlyPlan HoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", output);
        Assert.Contains("styleOnlyPlan LayoutAffecting eligible=False fallback=NotStyleOnly dirtyElementRanges=0:1 dirtyCommandRanges=(none) hitTargetCount=0", output);
    }

    #endregion

    #region Backend Clip/Text Snapshot

    [Fact]
    public void Diagnose_backend_clip_text_snapshot_captures_formatter_fields()
    {
        var lastEffectiveScissor = new EffectiveScissor(new DrawRect(32, 32, 80, 40), false);
        var lastEffectiveTextClip = new EffectiveScissor(new DrawRect(0, 0, 960, 20), false);
        var deviceError = DeviceErrorDiagnostic.FromFailure(DeviceErrorSite.Present);
        var snapshot = CreateBackendClipTextSnapshot(3, 1, 2, lastEffectiveScissor, lastEffectiveTextClip, textClipSkippedCount: 4, deviceRemoved: true, deviceError: deviceError);

        Assert.Equal(DrawingBackendClipMode.Scissor, snapshot.ClipMode);
        Assert.Equal(3, snapshot.ClippedCommandCount);
        Assert.Equal(1, snapshot.EmptyIntersectionSkippedCount);
        Assert.Equal(2, snapshot.ScissorStateChangeCount);
        Assert.Equal(lastEffectiveScissor, snapshot.LastEffectiveScissor);
        Assert.Equal(lastEffectiveTextClip, snapshot.LastEffectiveTextClip);
        Assert.Equal(4, snapshot.TextClipSkippedCount);
        Assert.True(snapshot.DeviceRemoved);
        Assert.Equal(deviceError, snapshot.DeviceError);
        Assert.True(snapshot.GpuScissor);
    }

    [Fact]
    public void Diagnose_clip_scissor_smoke_outputs_stable_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 0, 1, new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildClipScissorSmokeDiagnosticLine(new DrawRect(32, 32, 80, 40), snapshot);

        Assert.Equal("Scissor smoke: kind=FillRect clip=(32,32,80,40) effectiveClip=(32,32,80,40) nestedClip=False textClip=False gpuScissor=True clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_scissor_smoke_outputs_real_counter_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 0, 1, EffectiveScissor.Empty, EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildPipelineScissorSmokeDiagnosticLine(snapshot);

        Assert.Equal("Pipeline scissor smoke: source=ScrollContainerRectangle textClip=False clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False passed=True", line);
    }

    [Fact]
    public void Diagnose_empty_scissor_smoke_outputs_skip_counter()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 1, 0, EffectiveScissor.Empty, EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildEmptyScissorSmokeDiagnosticLine(snapshot);

        Assert.Equal("Empty scissor smoke: kind=FillRect clippedCommands=1 emptyIntersectionSkipped=1 scissorStateChanges=0 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_text_clip_smoke_outputs_effective_clip_and_skip_counter()
    {
        var snapshot = CreateBackendClipTextSnapshot(0, 0, 0, EffectiveScissor.Empty, new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), textClipSkippedCount: 1);

        var line = DiagnosticsFormatter.BuildTextClipSmokeDiagnosticLine(snapshot);

        Assert.Equal("Text clip smoke: kind=DrawTextRun textClip=True layoutClip=True effectiveClip=(32,32,80,40) textClipSkipped=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_text_clip_smoke_outputs_pipeline_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(2, 0, 1, EffectiveScissor.Empty, new EffectiveScissor(new DrawRect(0, 0, 960, 20), false));

        var line = DiagnosticsFormatter.BuildPipelineTextClipSmokeDiagnosticLine(snapshot);

        Assert.Equal("Pipeline text clip smoke: source=ScrollContainerButton textClip=True layoutClip=True effectiveClip=(0,0,960,20) clippedCommands=2 textClipSkipped=0 deviceRemoved=False passed=True", line);
    }

    #endregion

    #region Rendering Pipeline Snapshot

    [Fact]
    public void Diagnose_rendering_pipeline_snapshot_captures_minimal_fields()
    {
        var snapshot = CreateRenderingPipelineSnapshot();

        Assert.Equal([(0, 4)], snapshot.CompositorDirtyCommandRanges);
        Assert.Equal([(0, 4)], snapshot.BackendDirtyCommandRanges);
        Assert.True(snapshot.DirtyRangesAligned);
        Assert.Equal(0, snapshot.BackendClippedCommandCount);
        Assert.Equal(3, snapshot.LayoutCommandCount);
        Assert.Equal(3, snapshot.LayoutClippedCommandCount);
        Assert.Equal(1, snapshot.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.TreeStructure, snapshot.LayoutRebuildReason);
        Assert.Equal(InvalidationKind.TreeStructure, snapshot.LayoutInvalidationKind);
        Assert.Equal([new LayoutDirtyClassification(4, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)], snapshot.LayoutDirtyClassifications);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_snapshot_captures_additional_fields()
    {
        var snapshot = CreateRenderingPipelineSnapshot();

        Assert.Equal(3, snapshot.RenderCount);
        Assert.Equal(2, snapshot.PartialApplyCount);
        Assert.Equal(1, snapshot.FullApplyCount);
        Assert.Equal(0, snapshot.EmptyFrameCount);
        Assert.Equal(66.7, Math.Round(snapshot.PartialHitRate, 1));
        Assert.Single(snapshot.HitTargets);
        Assert.Equal(new ActionId(100), snapshot.HitTargets[0].ActionId);
        Assert.Single(snapshot.ScrollContainerDiagnostics);
        Assert.Equal(540, snapshot.ScrollContainerDiagnostics[0].VisibleHeight);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_compositor_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildRenderingPipelineCompositorDiagnosticLines(CreateRenderingPipelineSnapshot()));

        Assert.Equal(string.Join(Environment.NewLine, [
            "Render count: 3",
            "Partial apply: 2",
            "Full apply: 1",
            "Empty frames: 0",
            "Partial hit rate: 66.7%",
            "Compositor dirty ranges: 1 ranges",
            "  [0..3] (4 commands)",
            "Backend dirty ranges: 1 ranges",
            "  [0..3] (4 commands)",
            "Dirty ranges aligned: True",
            "Clipped commands: 0"
        ]), output);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_layout_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildRenderingPipelineLayoutDiagnosticLines(CreateRenderingPipelineSnapshot()));

        Assert.Equal(string.Join(Environment.NewLine, [
            "Layout commands: 3",
            "Layout clipped commands: 3",
            "Layout rebuild count: 1",
            "Layout rebuild reason: TreeStructure",
            "Layout invalidation kind: TreeStructure",
            "Layout dirty classifications: 4:StyleOnly/VisualOnly",
            "Layout hit targets: 1",
            "  Hit target: 100 bounds=(16,60,140,40) clip=(0,0,960,540)",
            "  ScrollContainer[0]: visible=540 content=96 scrollY=0 maxScrollY=0 elements=2/2 visible"
        ]), output);
    }

    #endregion

    #region Viewport Snapshot

    [Fact]
    public void Diagnose_resize_viewport_snapshot_captures_source_of_truth_fields()
    {
        var snapshot = new ViewportDiagnosticsSnapshot(
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            RenderCount: 80,
            LayoutRebuildCount: 80,
            LayoutRebuildReason: LayoutRebuildReason.ViewportChanged,
            ScreenScale: 1.25f,
            DpiAwareness: ViewportDpiAwareness.ProcessDefault,
            ScaleMode: ViewportScaleMode.PhysicalPixelsV0);

        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.WindowPhysicalBounds);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.RendererSwapchainBounds);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.TranslatorViewport);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.LayoutViewport);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.LastAppliedPendingResize);
        Assert.Equal(80, snapshot.RenderCount);
        Assert.Equal(80, snapshot.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.ViewportChanged, snapshot.LayoutRebuildReason);
        Assert.True(snapshot.ViewportMatchesRenderer);
        Assert.True(snapshot.LayoutUsesRendererSize);
        Assert.Equal(1.25f, snapshot.ScreenScale);
        Assert.Equal(ViewportDpiAwareness.ProcessDefault, snapshot.DpiAwareness);
        Assert.Equal(ViewportScaleMode.PhysicalPixelsV0, snapshot.ScaleMode);
    }

    [Fact]
    public void Diagnose_resize_viewport_outputs_source_of_truth_fields()
    {
        var snapshot = new ViewportDiagnosticsSnapshot(
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            RenderCount: 80,
            LayoutRebuildCount: 80,
            LayoutRebuildReason: LayoutRebuildReason.ViewportChanged,
            ScreenScale: 1.25f,
            DpiAwareness: ViewportDpiAwareness.ProcessDefault,
            ScaleMode: ViewportScaleMode.PhysicalPixelsV0);

        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildResizeViewportDiagnosticLines(snapshot));

        Assert.Contains("windowPhysicalSize=929x454", output);
        Assert.Contains("rendererSwapchainSize=929x454", output);
        Assert.Contains("translatorViewportSize=929x454", output);
        Assert.Contains("layoutViewportSize=929x454", output);
        Assert.Contains("lastAppliedPendingResize=929x454", output);
        Assert.Contains("renderCount=80", output);
        Assert.Contains("layoutRebuildCount=80", output);
        Assert.Contains("layoutRebuildReason=ViewportChanged", output);
        Assert.Contains("viewportMatchesRenderer=True", output);
        Assert.Contains("layoutUsesRendererSize=True", output);
        Assert.Contains("scaleMode=PhysicalPixelsV0", output);
        Assert.Contains("screenScale=1.25", output);
        Assert.Contains("dpiAwareness=ProcessDefault", output);
        Assert.Contains("coordinateSpace=PipelineLogicalPixels backendPhysicalPixels=True inputPhysicalMappedToLogical=True", output);
    }

    [Fact]
    public void Diagnose_resize_runner_report_outputs_stable_fields()
    {
        var snapshot = new ViewportDiagnosticsSnapshot(
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            RenderCount: 80,
            LayoutRebuildCount: 80,
            LayoutRebuildReason: LayoutRebuildReason.ViewportChanged,
            ScreenScale: 1.25f,
            DpiAwareness: ViewportDpiAwareness.ProcessDefault,
            ScaleMode: ViewportScaleMode.PhysicalPixelsV0);
        var writer = new StringWriter();
        var glyphAtlas = new D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics(
            CachedGlyphs: 8,
            UploadedBytes: 2048,
            DrawnGlyphs: 24,
            CacheHits: 30,
            CacheMisses: 8,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: 512,
            RasterScratchResizes: 2,
            AtlasPages: 1,
            AtlasAlphaPages: 1,
            AtlasBgraPages: 0,
            AtlasEvictions: 0,
            AtlasUsedPixels: 0,
            AtlasFragmentedPixels: 0);

        ResizeDiagnosticRunner.WriteReport(
            writer,
            deviceRemoved: false,
            deviceError: DeviceErrorDiagnostic.None,
            swapchainWidth: 929,
            swapchainHeight: 454,
            snapshot,
            TextCompositionMode.GlyphAtlas,
            glyphAtlas);

        Assert.Equal(string.Join(Environment.NewLine, [
            "=== D3D12 Resize Diagnostics ===",
            "Device removed: False",
            "Device error reason: (none)",
            "Swapchain size: 929x454",
            "Text composition mode: GlyphAtlas",
            "windowPhysicalSize=929x454",
            "rendererSwapchainSize=929x454",
            "translatorViewportSize=929x454",
            "layoutViewportSize=929x454",
            "lastAppliedPendingResize=929x454",
            "renderCount=80",
            "layoutRebuildCount=80",
            "layoutRebuildReason=ViewportChanged",
            "viewportMatchesRenderer=True",
            "layoutUsesRendererSize=True",
            "scaleMode=PhysicalPixelsV0",
            "screenScale=1.25",
            "dpiAwareness=ProcessDefault",
            "scale=0x0",
            "logicalViewport=0x0",
            "coordinateSpace=PipelineLogicalPixels backendPhysicalPixels=True inputPhysicalMappedToLogical=True",
            "Glyph atlas: cachedGlyphs=8, atlasPages=1, atlasAlphaPages=1, atlasBgraPages=0, atlasBudgetPages=48, atlasPage=1024x1024, atlasCapacity=50331648 px, atlasCpuBytes=1048576 bytes, atlasUploadBytes=2097152 bytes, atlasGpuBytes=1048576 bytes, atlasEvictions=0, atlasAlphaEvictions=0, atlasBgraEvictions=0, atlasPendingPageReuses=0, atlasPendingAlphaPageReuses=0, atlasPendingBgraPageReuses=0, atlasPageReuseRequests=0, atlasAlphaPageReuseRequests=0, atlasBgraPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasAlphaFullWithoutPageReuse=0, atlasBgraFullWithoutPageReuse=0, atlasUsed=0 px, atlasFragmented=0 px, atlasAlphaUsed=0 px, atlasBgraUsed=0 px, atlasAlphaFragmented=0 px, atlasBgraFragmented=0 px, atlasRecordSerial=0, atlasOldestPageAge=0, atlasNewestPageAge=0, atlasOldestAlphaPageAge=0, atlasOldestBgraPageAge=0, drawnGlyphs=24, atlasRuns=0, degradedRuns=0, uploads=2048 bytes, uploadedGlyphs=0, shapedProbeRuns=0, shapedProbeGlyphs=0, colorLayerRuns=0, colorBitmapRuns=0, hits=30, misses=8, "
                + "fallbacks=0, unsupportedRuns=0, reasons=[NonAscii=0, ColorGlyph=0, ComplexScript=0, ColorGlyphSvg=0, ColorGlyphPng=0, ColorGlyphJpeg=0, ColorGlyphTiff=0, ColorGlyphPremultipliedBgra=0, ColorGlyphPaintTree=0, Clip=0, Wrapping=0, Alignment=0, AtlasFull=0, VertexLimit=0, "
                + "FontMissing=0, CompileFailed=0, BatchLimit=0, InitializationFailed=0, RecordFailed=0], initFailurePhase=None, "
                + "recordFailurePhase=None, rasterScratch=512 bytes/2 resizes",
            "=== Resize diagnostic mode complete ===",
            string.Empty
        ]), writer.ToString());
    }

    #endregion

    #region Debug UI Bridge Baseline

    [Fact]
    public void Default_debug_bridge_captures_existing_debug_state()
    {
        var viewport = new CounterViewportDiagnostics(
            new PixelRectangle(0, 0, 929, 454),
            new PixelRectangle(0, 0, 929, 454),
            ViewportScaleMode.PhysicalPixelsV0);
        var layout = new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, [new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting), new LayoutDirtyClassification(3, LayoutRebuildReason.StyleOnly)]);
        var input = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 5,
            IsPointerPressed: false);
        var scroll = ScrollState.Default with
        {
            Accumulator = 0.375,
            Position = 42.4,
            TargetPosition = 48,
            IsAnimating = true,
            MaxScrollY = 240,
            HasMaxScrollY = true
        };

        var snapshot = new DefaultDebugDiagnosticsSnapshotBridge(viewport, layout, scroll, input).Capture();

        Assert.Equal(viewport, snapshot.Viewport);
        Assert.Equal(layout, snapshot.Layout);
        Assert.Equal(42, snapshot.Scroll.AppliedScrollY);
        Assert.Equal(42.4, snapshot.Scroll.Position);
        Assert.Equal(48, snapshot.Scroll.TargetPosition);
        Assert.Equal(0.375, snapshot.Scroll.Accumulator);
        Assert.True(snapshot.Scroll.IsAnimating);
        Assert.Equal(240, snapshot.Scroll.MaxScrollY);
        Assert.True(snapshot.Scroll.HasMaxScrollY);
        Assert.Equal(Program.DiagScrollDispatchedFrameCount, snapshot.Scroll.DispatchedFrameCount);
        Assert.Equal(Program.DiagScrollRenderWaitMs, snapshot.Scroll.RenderWaitMs);
        Assert.Equal(Program.DiagScrollLastDt, snapshot.Scroll.LastDt);
        Assert.Equal(Program.DiagScrollDrainedPixels, snapshot.Scroll.DrainedPixels);
        Assert.Equal(Program.DiagPendingPx, snapshot.Scroll.PendingPixels);
        Assert.Equal(Program.DiagScrollFrameQueued, snapshot.Scroll.FrameQueued);
        Assert.Equal(Program.DiagTickLoopRunning, snapshot.Scroll.TickLoopRunning);
        Assert.Equal(input, snapshot.InputOwnership);
        Assert.Equal(Program.DiagBackendClipMode, snapshot.BackendClipMode);
    }

    [Fact]
    public void Debug_diagnostics_formatter_outputs_stable_bridge_rows()
    {
        var snapshot = new DebugUiDiagnosticsSnapshot(
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                ViewportScaleMode.PhysicalPixelsV0),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, [new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting), new LayoutDirtyClassification(3, LayoutRebuildReason.StyleOnly)]),
            new ScrollDiagnosticsSnapshot(
                DispatchedFrameCount: 2,
                RenderWaitMs: 12.25,
                LastDt: 0.0167,
                DrainedPixels: 54,
                LastFrameDrainedPixels: 0,
                PendingPixels: 3,
                FrameQueued: true,
                TickLoopRunning: true,
                AppliedScrollY: 42,
                TargetPosition: 48,
                MaxScrollY: 240,
                HasMaxScrollY: true,
                Position: 42.4,
                Accumulator: 0.375,
                IsAnimating: true),
            new OwnershipSnapshot(
                HoveredTarget: new ActionId(1),
                FocusedTarget: new ActionId(1),
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: new ActionId(1),
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 5,
                IsPointerPressed: false),
            DrawingBackendClipMode.Diagnostic);

        Assert.Equal("Viewport: renderer=929x454 layout=929x454 scaleMode=PhysicalPixelsV0", DebugDiagnosticsFormatter.FormatViewportDiagnosticRow(snapshot));
        Assert.Equal("ScrollY: applied=42 target=48.0 pos=42.40 max=240 acc=0.375 anim=True pendingPx=3 drained=54 frames=2 waitMs=12.2 dt=0.017 frameQueued=True tickLoop=True", DebugDiagnosticsFormatter.FormatScrollDiagnosticRow(snapshot));
        Assert.Equal("ClipMode: Diagnostic", DebugDiagnosticsFormatter.FormatClipModeDiagnosticRow(snapshot));
        Assert.Equal("LayoutDirty: layoutRebuildCount=12 LastLayoutRebuildReason=LayoutAffecting LastDirtyClassifications=0:LayoutAffecting,3:StyleOnly", DebugDiagnosticsFormatter.FormatLayoutDirtyDiagnosticRow(snapshot));
        Assert.Equal("Input: hover=Increment focus=Increment pressed=- capture=- hoverChanges=5", DebugDiagnosticsFormatter.FormatInputDiagnosticRow(snapshot));
    }

    [Fact]
    public void Default_debug_bridge_exposes_provider_contract()
    {
        IDiagnosticsProvider<DebugUiDiagnosticsSnapshot> provider = new DefaultDebugDiagnosticsSnapshotBridge(
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                ViewportScaleMode.PhysicalPixelsV0),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, [new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting), new LayoutDirtyClassification(3, LayoutRebuildReason.StyleOnly)]),
            ScrollState.Default,
            default);

        var snapshot = provider.Capture();

        Assert.Equal(ViewportScaleMode.PhysicalPixelsV0, snapshot.Viewport.ScaleMode);
    }

    [Fact]
    public void Debug_ui_outputs_bridge_backed_diagnostic_rows()
    {
        var app = new CounterApplication(
            showDiagnostics: true,
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                ViewportScaleMode.PhysicalPixelsV0),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, [new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting), new LayoutDirtyClassification(3, LayoutRebuildReason.StyleOnly)]));
        var input = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 5,
            IsPointerPressed: false);
        var model = app.Initialize() with { InputOwnership = input };

        var tree = app.BuildView(model);

        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "Viewport: renderer=929x454 layout=929x454 scaleMode=PhysicalPixelsV0"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "ScrollY: applied=0 target=0.0 pos=0.00 max=unknown acc=0.000 anim=False pendingPx=0 drained=0 frames=0 waitMs=0.0 dt=0.000 frameQueued=False tickLoop=False"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "ClipMode: Scissor"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "LayoutDirty: layoutRebuildCount=12 LastLayoutRebuildReason=LayoutAffecting LastDirtyClassifications=0:LayoutAffecting,3:StyleOnly"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "Input: hover=Increment focus=Increment pressed=- capture=- hoverChanges=5"));
    }

    #endregion

    #region Test Helpers

    private static BackendClipTextDiagnosticSnapshot CreateBackendClipTextSnapshot(
        int clippedCommandCount,
        int emptyIntersectionSkippedCount,
        int scissorStateChangeCount,
        EffectiveScissor lastEffectiveScissor,
        EffectiveScissor lastEffectiveTextClip,
        int textClipSkippedCount = 0,
        bool deviceRemoved = false,
        DeviceErrorDiagnostic deviceError = default)
    {
        return new BackendClipTextDiagnosticSnapshot(
            DrawingBackendClipMode.Scissor,
            clippedCommandCount,
            emptyIntersectionSkippedCount,
            scissorStateChangeCount,
            lastEffectiveScissor,
            lastEffectiveTextClip,
            textClipSkippedCount,
            deviceRemoved,
            deviceError);
    }

    private static RenderingPipelineDiagnosticSnapshot CreateRenderingPipelineSnapshot()
    {
        return new RenderingPipelineDiagnosticSnapshot(
            RenderCount: 3,
            PartialApplyCount: 2,
            FullApplyCount: 1,
            EmptyFrameCount: 0,
            CompositorDirtyCommandRanges: [(0, 4)],
            BackendDirtyCommandRanges: [(0, 4)],
            BackendClippedCommandCount: 0,
            LayoutCommandCount: 3,
            LayoutClippedCommandCount: 3,
            LayoutRebuildCount: 1,
            LayoutRebuildReason: LayoutRebuildReason.TreeStructure,
            LayoutInvalidationKind: InvalidationKind.TreeStructure,
            LayoutDirtyClassifications: [new LayoutDirtyClassification(4, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)],
            HitTargets: [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(100), new PixelRectangle(0, 0, 960, 540))],
            ScrollContainerDiagnostics: [new ScrollContainerDiag(0, 540, 96, 0, 0, 2, 0)]);
    }

    #endregion

    private static D3D12TextRun TextRun(TextSlice text, ResourceHandle style, float width, float height)
    {
        return new D3D12TextRun(
            X: 0,
            Y: 0,
            Width: width,
            Height: height,
            R: 1,
            G: 1,
            B: 1,
            A: 1,
            Text: text,
            Style: style,
            EffectiveClip: default,
            ClipEnabled: false,
            ResolvedStyle: TextStyle.Default);
    }

    private static string ResolveNodeText(VirtualTextArena arena, NodeContent content) =>
        content.TryGetText(out var tc) ? arena.ResolveRequired(tc).ToString() : "";

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string ReadGlyphAtlasRendererSources(string root)
    {
        var platformWindows = Path.Combine(root, "src", "Irix.Platform.Windows");
        return NormalizeLineEndings(string.Join(
            "\n",
            Directory.EnumerateFiles(platformWindows, "D3D12GlyphAtlasTextRenderer*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            start = index + value.Length;
        }
    }

    private static void AssertSourceTokensOnlyIn(string directory, string[] tokens, string[] allowedFileNames)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = NormalizeLineEndings(File.ReadAllText(sourcePath));
            var fileName = Path.GetFileName(sourcePath);
            var allowed = allowedFileNames.Contains(fileName, StringComparer.Ordinal);
            foreach (var token in tokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    Assert.True(allowed, $"{token} should stay in {string.Join(", ", allowedFileNames)} but was found in {fileName}.");
                }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Irix.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find repo root (Irix.slnx)");
    }

    private static IEnumerable<string> EnumerateActiveSourceGuardFiles(string root)
    {
        var srcRoot = Path.Combine(root, "src");
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
        }

        yield return Path.Combine(root, "src", "Irix.Platform.Windows", "NativeMethods.txt");

        var scriptsRoot = Path.Combine(root, "scripts");
        foreach (var path in Directory.EnumerateFiles(scriptsRoot, "*.ps1", SearchOption.TopDirectoryOnly))
        {
            yield return path;
        }
    }

    private static bool ContainsNode(ReadOnlySpan<VirtualNode> nodes, Func<VirtualNode, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
            {
                return true;
            }
        }

        return false;
    }
}
