using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ProgramDiagnosticsTests
{
    [Fact]
    public void Text_composition_mode_defaults_to_glyph_atlas_and_ignores_removed_overlay()
    {
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode([]));
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode(["--text-composition", "overlay"]));
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode(["--text-composition", "glyph-atlas"]));
        Assert.Equal(TextCompositionMode.GlyphAtlas, Program.ParseTextCompositionMode(["--text-composition", "atlas"]));
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
    public void Glyph_atlas_fallback_classifier_accepts_single_line_ascii_and_rejects_known_unsupported_runs()
    {
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("ASCII 123".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("ASCII 測試".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Line\nBreak".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Line\r\nBreak".AsSpan(), TextStyle.Default));
        Assert.Equal(
            D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None,
            GlyphAtlasTextCompositionHelpers.GetUnsupportedReason("Tab\tBreak".AsSpan(), TextStyle.Default));

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
    public void Glyph_atlas_line_planner_degrades_wrap_when_word_cannot_fit()
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

        Assert.Equal(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Wrapping, reason);
        Assert.Equal(0, lineCount);
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
        Assert.True(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFontCollection: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: false, hasFontCollection: true));
        Assert.False(GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(hasFactory: true, hasFontCollection: false));
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
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.VertexHeader));
        Assert.Equal("DXBC", System.Text.Encoding.ASCII.GetString(lengths.PixelHeader));
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
        var glyphSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.cs")));

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
        var glyphSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.cs")));

        Assert.Contains("var frameResourceIndex = (int)_frameIndex;", rendererSource);
        Assert.Contains("RenderRectangles(_list, rects, width, height, frameResourceIndex)", rendererSource);
        Assert.Contains("TryRecordGlyphAtlasTextPass(textRuns, resources, frameResourceIndex)", rendererSource);
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
    public void D3D12_overlay_renderer_sources_are_removed()
    {
        var root = FindRepoRoot();
        var platformWindows = Path.Combine(root, "src", "Irix.Platform.Windows");

        Assert.False(File.Exists(Path.Combine(platformWindows, "D3D12TextRenderer.cs")));
        Assert.False(File.Exists(Path.Combine(platformWindows, "TextOverlaySyncStrategy.cs")));
        Assert.False(File.Exists(Path.Combine(platformWindows, "D3D11DeviceContextQueryExtensions.cs")));

        foreach (var sourcePath in Directory.EnumerateFiles(platformWindows, "*.cs"))
        {
            var source = NormalizeLineEndings(File.ReadAllText(sourcePath));
            Assert.DoesNotContain("D3D11On12CreateDevice", source);
            Assert.DoesNotContain("ID3D11On12Device", source);
            Assert.DoesNotContain("Windows.Win32.Graphics.Direct2D", source);
            Assert.DoesNotContain("D2D1CreateFactory", source);
        }
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
        var glyphSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.cs")));

        Assert.Contains("private readonly Dictionary<GlyphKey, GlyphAtlasEntryHandle> _glyphs", glyphSource);
        Assert.Contains("private readonly List<GlyphEntry> _glyphEntries", glyphSource);
        Assert.Contains("private const int MaxAtlasPages = 4;", glyphSource);
        Assert.Contains("private const int AtlasPagePixels = AtlasWidth * AtlasHeight;", glyphSource);
        Assert.Contains("private const int AtlasBudgetPixels = MaxAtlasPages * AtlasPagePixels;", glyphSource);
        Assert.Contains("private readonly List<int> _freeGlyphEntryIndices", glyphSource);
        Assert.Contains("private readonly List<GlyphAtlasPage> _atlasPages = new(MaxAtlasPages);", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle _activeAtlasPage;", glyphSource);
        Assert.Contains("private GlyphAtlasPageReuseRequest _pendingAtlasPageReuse;", glyphSource);
        Assert.Contains("private long _glyphRecordSerial;", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasEntryHandle(int Index, int Generation)", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageHandle(int Index, int Generation)", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageReuseRequest(GlyphAtlasPageHandle Page, long RequestedRecordSerial)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(!IsNone, RequestedRecordSerial, recordSerial, oldestRetainedRecordSerial)", glyphSource);
        Assert.Contains("private sealed unsafe class GlyphAtlasPage", glyphSource);
        Assert.Contains("private GlyphAtlasEntryHandle AddGlyphEntry(in GlyphEntry entry)", glyphSource);
        Assert.Contains("_freeGlyphEntryIndices.RemoveAt(freeIndex);", glyphSource);
        Assert.Contains("private bool TryResolveGlyph(GlyphAtlasEntryHandle handle, long recordSerial, out GlyphEntry entry)", glyphSource);
        Assert.Contains("!entry.IsLive || entry.Generation != handle.Generation", glyphSource);
        Assert.Contains("page.Touch(recordSerial);", glyphSource);
        Assert.Contains("entry = entry.WithLastUsedSerial(recordSerial);", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle AddAtlasPage(", glyphSource);
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
        Assert.Contains("ApplyPendingAtlasPageEviction(recordSerial, oldestRetainedRecordSerial: recordSerial);", glyphSource);
        Assert.Contains("private void ApplyPendingAtlasPageEviction(long recordSerial, long oldestRetainedRecordSerial)", glyphSource);
        Assert.Contains("if (!_pendingAtlasPageReuse.CanApply(recordSerial, oldestRetainedRecordSerial))", glyphSource);
        Assert.Contains("var reusedPage = page.Handle;", glyphSource);
        Assert.Contains("RemoveGlyphsForReusedPage(reusedPage);", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(", glyphSource);
        Assert.Contains("_freeGlyphEntryIndices.Add(i);", glyphSource);
        Assert.Contains("private GlyphAtlasPage? SelectWritableAtlasPage(int width, int height, long recordSerial)", glyphSource);
        Assert.Contains("private GlyphAtlasPageHandle SelectOldestAtlasPageHandle()", glyphSource);
        Assert.Equal(2, CountOccurrences(glyphSource, "GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage("));
        Assert.Contains("private static bool CanAllocateGlyph(GlyphAtlasPage page, int width, int height)", glyphSource);
        Assert.Contains("private void ScheduleAtlasPageReuse(long recordSerial)", glyphSource);
        Assert.Contains("var page = SelectOldestAtlasPageHandle();", glyphSource);
        Assert.Contains("_pendingAtlasPageReuse = new GlyphAtlasPageReuseRequest(page, recordSerial);", glyphSource);
        Assert.Contains("_diagnostics = _diagnostics.WithAtlasPageReuseRequest();", glyphSource);
        Assert.Contains("_diagnostics = _diagnostics.WithAtlasFullWithoutPageReuse();", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.RequireActiveAtlasPage found a stale active glyph atlas page handle.", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.ApplyPendingAtlasPageEviction found a stale pending glyph atlas page reuse handle.", glyphSource);
        Assert.Contains("D3D12GlyphAtlasTextRenderer.DrawGlyphs found a stale glyph atlas page handle.", glyphSource);
        Assert.DoesNotContain("Glyph atlas active page handle is invalid.", glyphSource);
        Assert.DoesNotContain("Glyph atlas pending page reuse handle is stale.", glyphSource);
        Assert.Contains("public GlyphAtlasPageHandle NextGeneration()", glyphSource);
        Assert.Contains("public GlyphAtlasPageHandle ResetForReuse()", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.CreatePageReuseResetState(AtlasWidth, AtlasHeight, AtlasPadding)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphRecordCommandList(list != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(_dwriteFactory != null, _fontCollection != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(fontFace.Face != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(family != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(font != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphRunAnalysisResource(analysis != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphVertexUploadResource(vbuf != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(page.Texture != null, upload != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(_pso != null, _rootSig != null)", glyphSource);
        Assert.Contains("GlyphAtlasTextCompositionHelpers.HasAtlasDrawResources(heap != null)", glyphSource);
        Assert.Contains("DirectWrite,", glyphSource);
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
        Assert.Contains("public bool IsLive => LastUsedSerial > 0;", glyphSource);
        Assert.Contains("public int ComputeAllocatedPixels()", glyphSource);
        Assert.Contains("private GlyphAtlasPageUsage GetAtlasPageUsage()", glyphSource);
        Assert.Contains("private readonly struct GlyphAtlasPageUsage(int UsedPixels, int FragmentedPixels, long OldestPageAge, long NewestPageAge)", glyphSource);
        Assert.Contains(".WithAtlasPageUsage(pageUsage.UsedPixels, pageUsage.FragmentedPixels)", glyphSource);
        Assert.Contains(".WithAtlasTouchMetrics(_glyphRecordSerial, pageUsage.OldestPageAge, pageUsage.NewestPageAge)", glyphSource);
        Assert.Contains(".WithAtlasPendingPageReuse(_pendingAtlasPageReuse.IsNone ? 0 : 1)", glyphSource);
        Assert.Contains("public int AtlasBudgetPages => MaxAtlasPages;", glyphSource);
        Assert.Contains("public int AtlasPageWidth => AtlasWidth;", glyphSource);
        Assert.Contains("public int AtlasPageHeight => AtlasHeight;", glyphSource);
        Assert.Contains("public int AtlasCapacityPixels => AtlasBudgetPixels;", glyphSource);
        Assert.Contains("public int AtlasPendingPageReuses { get; } = AtlasPendingPageReuses;", glyphSource);
        Assert.Contains("public int AtlasPageReuseRequests { get; } = AtlasPageReuseRequests;", glyphSource);
        Assert.Contains("public int AtlasFullWithoutPageReuse { get; } = AtlasFullWithoutPageReuse;", glyphSource);
        Assert.Contains("public GlyphEntry WithLastUsedSerial(long serial)", glyphSource);
        Assert.Contains("page.UsedPixels = checked(page.UsedPixels + width * height);", glyphSource);
        Assert.Contains("page.AllocatedPixels = Math.Max(page.AllocatedPixels, page.ComputeAllocatedPixels());", glyphSource);
        Assert.Contains(".WithAtlasEviction()", glyphSource);
        Assert.Contains("_glyphs[key] = handle;", glyphSource);
        Assert.DoesNotContain("Dictionary<GlyphKey, GlyphEntry> _glyphs", glyphSource);
        Assert.DoesNotContain("_glyphs.Add(key, glyph)", glyphSource);
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
            .WithAtlasPages(1)
            .WithAtlasEviction()
            .WithAtlasPageReuseRequest()
            .WithAtlasFullWithoutPageReuse()
            .WithAtlasPageUsage(2048, 512)
            .WithUploadedGlyph()
            .WithUploadedGlyph()
            .WithDegradation(0, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii)
            .WithInitializationFailure(D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.ShaderCompile);

        var summary = diagnostics.FormatSummary();

        Assert.Contains("cachedGlyphs=12", summary);
        Assert.Contains("atlasPages=1", summary);
        Assert.Contains("atlasBudgetPages=4", summary);
        Assert.Contains("atlasPage=1024x1024", summary);
        Assert.Contains("atlasCapacity=4194304 px", summary);
        Assert.Contains("atlasEvictions=1", summary);
        Assert.Contains("atlasPendingPageReuses=0", summary);
        Assert.Contains("atlasPageReuseRequests=1", summary);
        Assert.Contains("atlasFullWithoutPageReuse=1", summary);
        Assert.Contains("atlasUsed=2048 px", summary);
        Assert.Contains("atlasFragmented=512 px", summary);
        Assert.Contains("atlasRecordSerial=0", summary);
        Assert.Contains("atlasOldestPageAge=0", summary);
        Assert.Contains("atlasNewestPageAge=0", summary);
        Assert.Contains("uploads=4096 bytes", summary);
        Assert.Contains("uploadedGlyphs=2", summary);
        Assert.Contains("atlasRuns=7", summary);
        Assert.Contains("degradedRuns=0", summary);
        Assert.Contains("fallbacks=2", summary);
        Assert.Contains("NonAscii=1", summary);
        Assert.Contains("initFailurePhase=ShaderCompile", summary);
        Assert.Contains("recordFailurePhase=None", summary);
        Assert.Contains("RecordFailed=0", summary);
        Assert.Contains("rasterScratch=768 bytes/2 resizes", summary);
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
            .WithDegradation(3, D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii);

        Assert.Equal(1, diagnostics.FallbackFrames);
        Assert.Equal(3, diagnostics.UnsupportedRuns);
        Assert.Equal(3, diagnostics.DegradedRuns);
        Assert.Equal(3, diagnostics.Reasons.NonAscii);
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
        Assert.Equal(2, summary.AtlasCandidateRuns);
        Assert.Equal(2, summary.DegradedCandidateRuns);
        Assert.Equal(2, summary.NonAsciiFallbackRuns);
        Assert.Equal(1, summary.ClippedAtlasCandidateRuns);
        Assert.Equal(1, summary.ClippedDegradedCandidateRuns);
        Assert.True(summary.HasDegradedBeforeLaterAtlas);
        Assert.Contains("commands=atlas,degraded,atlas,degraded", ordering);
        Assert.Contains("zOrderLimit=FalseForDegradedText", ordering);
    }

    [Fact]
    public void Glyph_atlas_wrap_smoke_scene_classifies_space_wrap_and_explicit_degradation()
    {
        using var resources = FrameDrawingResources.Rent();
        var commands = GlyphAtlasWrapDiagnosticRunner.BuildWrapCommands(resources, frameIndex: 0);
        resources.Seal();

        var summary = GlyphAtlasWrapDiagnosticRunner.AnalyzeWrapScene(commands, resources);
        var expectedLine = GlyphAtlasWrapDiagnosticRunner.FormatExpectedLine(summary);

        Assert.Equal(6, summary.TextRuns);
        Assert.Equal(4, summary.AtlasCandidateRuns);
        Assert.Equal(2, summary.DegradedCandidateRuns);
        Assert.Equal(1, summary.WrappedAtlasCandidateRuns);
        Assert.Equal(1, summary.WrappingFallbackRuns);
        Assert.Equal(1, summary.NonAsciiFallbackRuns);
        Assert.Contains("atlasRuns=4", expectedLine);
        Assert.Contains("degradedRuns=2", expectedLine);
        Assert.Contains("wrappedAtlasRuns=1", expectedLine);
        Assert.Contains("Wrapping=1", expectedLine);
        Assert.Contains("NonAscii=1", expectedLine);
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
        Assert.Contains("atlasBudgetPages=4", report);
        Assert.Contains("atlasCapacity=4194304 px", report);
        Assert.Contains("degradedRuns=1", report);
        Assert.Contains("AtlasFull=1", report);
        Assert.Contains("=== Glyph atlas stress diagnostic complete ===", report);
    }

    [Fact]
    public void Glyph_atlas_mixed_stress_commands_keep_prefix_atlas_candidates_and_trailing_fallback()
    {
        using var resources = FrameDrawingResources.Rent();
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
            "Glyph atlas: cachedGlyphs=8, atlasPages=1, atlasBudgetPages=4, atlasPage=1024x1024, atlasCapacity=4194304 px, atlasEvictions=0, atlasPendingPageReuses=0, atlasPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasUsed=0 px, atlasFragmented=0 px, atlasRecordSerial=0, atlasOldestPageAge=0, atlasNewestPageAge=0, drawnGlyphs=24, atlasRuns=0, degradedRuns=0, uploads=2048 bytes, uploadedGlyphs=0, hits=30, misses=8, "
                + "fallbacks=0, unsupportedRuns=0, reasons=[NonAscii=0, Clip=0, Wrapping=0, Alignment=0, AtlasFull=0, VertexLimit=0, "
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
