using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private static bool TryMeasureCharacterAdvance(
        CachedFontFace fontFace,
        float emSize,
        char character,
        out float advance)
    {
        advance = 0;
        if (!TryMapCharacterToSimpleGlyph(fontFace, character, out var glyphAtom))
        {
            return false;
        }

        advance = ComputeGlyphAdvance(fontFace, emSize, glyphAtom.GlyphIndex);
        return true;
    }

    private static void AddDegradedRun(
        GlyphAtlasFallbackReason reason,
        ref int degradedRunCount,
        ref GlyphAtlasFallbackReasonCounts degradationReasons)
    {
        degradedRunCount++;
        degradationReasons = degradationReasons.With(reason);
    }

    private bool TryAppendDrawBatch(
        ref int batchCount,
        ref int vertexCount,
        int batchStart,
        IntegerScissorRect scissor,
        GlyphAtlasPageHandle page)
    {
        if (vertexCount == batchStart)
        {
            return true;
        }

        if (batchCount >= MaxGlyphDrawBatches || page.IsNone)
        {
            return false;
        }

        _batches[batchCount++] = new GlyphDrawBatch(batchStart, vertexCount - batchStart, scissor, page);
        return true;
    }

    private static IntegerScissorRect ResolveRunScissor(D3D12TextRun textRun, int viewportWidth, int viewportHeight)
    {
        if (textRun.ClipEnabled)
        {
            return DrawingScissor.ToIntegerScissorRect(textRun.EffectiveClip, viewportWidth, viewportHeight);
        }

        return new IntegerScissorRect(0, 0, viewportWidth, viewportHeight);
    }

    private bool TryGetFontFace(TextStyle style, out CachedFontFace fontFace)
    {
        var key = new FontFaceKey(style.FontFamily, style.FontWeight, style.FontStyle, style.FontStretch, style.FontSize);
        if (_fontFaces.TryGetValue(key, out fontFace!))
        {
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(fontFace.Face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing cached font face.");
            }

            return true;
        }

        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;

        try
        {
            _fontCollection->FindFamilyName(ToDirectWriteFontFamily(style.FontFamily), out var familyIndex, out var exists);
            if (!exists)
            {
                _fontCollection->FindFamilyName(ToDirectWriteFontFamily(TextStyle.Default.FontFamily), out familyIndex, out exists);
                if (!exists)
                {
                    return false;
                }
            }

            _fontCollection->GetFontFamily(familyIndex, &family);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(family != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font family.");
            }

            family->GetFirstMatchingFont(
                ToDirectWriteFontWeight(style.FontWeight),
                ToDirectWriteFontStretch(style.FontStretch),
                ToDirectWriteFontStyle(style.FontStyle),
                &font);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(font != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font.");
            }

            font->CreateFontFace(&face);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font face.");
            }

            face->GetMetrics(out var metrics);
            face4 = TryQueryFontFace4(face);
            fontFace = new CachedFontFace(new FontFaceIdentity(_nextFontFaceIdentity++), face, metrics, face4);
            _fontFaces.Add(key, fontFace);
            face = null;
            face4 = null;
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.TryGetFontFace",
                ex);
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (face != null) face->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
        }
    }

    private bool TryGetGlyph(
        CachedFontFace fontFace,
        TextStyle style,
        char character,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (!TryMapCharacterToSimpleGlyph(fontFace, character, out var glyphAtom))
        {
            glyph = default;
            unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
            return false;
        }

        var key = new GlyphKey(fontFace.Identity, style.FontSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, style.FontSize, glyphAtom, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetShapedGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        in ShapedGlyph shapedGlyph,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var glyphAtom = GlyphAtom.ShapedPlacement(
            shapedGlyph.GlyphIndex,
            shapedGlyph.IsDiacritic,
            shapedGlyph.IsZeroWidthSpace);
        var key = new GlyphKey(fontFace.Identity, fontEmSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, fontEmSize, shapedGlyph, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetColorLayerGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        ushort glyphIndex,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var glyphAtom = GlyphAtom.ColorLayer(glyphIndex);
        var key = new GlyphKey(fontFace.Identity, fontEmSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, fontEmSize, ShapedGlyph.Simple(glyphIndex, advance), recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetBgraColorGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out bool glyphHadBgra,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        glyph = default;
        glyphHadBgra = false;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        glyphHadBgra = true;
        var key = new GlyphKey(fontFace.Identity, fontEmSize, GlyphAtom.BgraGlyph(glyphIndex, pixelsPerEm));
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeBgraColorGlyph(key, fontFace, fontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetEncodedBitmapColorGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        DWRITE_GLYPH_IMAGE_FORMATS imageFormat,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        glyph = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        var key = new GlyphKey(fontFace.Identity, fontEmSize, GlyphAtom.EncodedBitmapGlyph(glyphIndex, pixelsPerEm, GetEncodedBitmapGlyphFormatId(imageFormat)));
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeEncodedBitmapColorGlyph(key, fontFace, fontEmSize, pixelsPerEm, glyphIndex, imageFormat, advance, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private GlyphAtlasEntryHandle AddGlyphEntry(in GlyphEntry entry)
    {
        if (_freeGlyphEntryIndices.Count > 0)
        {
            var freeIndex = _freeGlyphEntryIndices.Count - 1;
            var entryIndex = _freeGlyphEntryIndices[freeIndex];
            _freeGlyphEntryIndices.RemoveAt(freeIndex);
            var generation = checked(_glyphEntries[entryIndex].Generation + 1);
            var reusedHandle = new GlyphAtlasEntryHandle(entryIndex, generation);
            _glyphEntries[entryIndex] = entry.WithGeneration(generation);
            TrackRunGlyphEntry(entryIndex);
            return reusedHandle;
        }

        var handle = new GlyphAtlasEntryHandle(_glyphEntries.Count, 1);
        _glyphEntries.Add(entry.WithGeneration(handle.Generation));
        TrackRunGlyphEntry(handle.Index);
        return handle;
    }

    private void TrackRunGlyphEntry(int entryIndex)
    {
        if (_runAtlasMutationActive)
        {
            _runGlyphEntryIndices.Add(entryIndex);
        }
    }

    private void BeginAtlasRunMutation()
    {
        _runAtlasMutationActive = true;
        _runAtlasMutationUsedPageReuse = false;
        _runGlyphEntryIndices.Clear();
        _runGlyphEntryStates.Clear();
        _runActiveAtlasPage = _activeAtlasPage;
        _runPendingAlphaAtlasPageReuse = _pendingAlphaAtlasPageReuse;
        _runPendingBgraAtlasPageReuse = _pendingBgraAtlasPageReuse;
        _runCachedGlyphCount = _cachedGlyphCount;
        _runAtlasPageCount = _atlasPages.Count;
        _runDiagnostics = _diagnostics;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _runPageStates[i] = _atlasPages[i].CaptureMutationState();
        }
    }

    private void CommitAtlasRunMutation()
    {
        _runAtlasMutationActive = false;
        _runAtlasMutationUsedPageReuse = false;
        _runGlyphEntryIndices.Clear();
        _runGlyphEntryStates.Clear();
        _runActiveAtlasPage = default;
        _runPendingAlphaAtlasPageReuse = default;
        _runPendingBgraAtlasPageReuse = default;
        _runAtlasPageCount = 0;
    }

    private void RollbackAtlasRunMutation(long recordSerial, GlyphAtlasFallbackReason reason)
    {
        if (!_runAtlasMutationActive)
        {
            return;
        }

        if (_runAtlasMutationUsedPageReuse)
        {
            RemoveRunGlyphEntries(clearPixels: true);
            RestoreRunGlyphEntryTouches();
            ReleaseAtlasPagesCreatedDuringRun();
            RestoreRunPageStates();
            ResetPagesReusedDuringRun(out var resetAlphaPageCount, out var resetBgraPageCount);
            _pendingAlphaAtlasPageReuse = _runPendingAlphaAtlasPageReuse;
            _pendingBgraAtlasPageReuse = _runPendingBgraAtlasPageReuse;
            _cachedGlyphCount = CountLiveGlyphEntries();
            _diagnostics = _runDiagnostics
                .WithCachedGlyphs(_cachedGlyphCount)
                .WithAtlasPages(_atlasPages.Count);
            for (var i = 0; i < resetAlphaPageCount; i++)
            {
                _diagnostics = _diagnostics.WithAtlasAlphaEviction();
            }

            for (var i = 0; i < resetBgraPageCount; i++)
            {
                _diagnostics = _diagnostics.WithAtlasBgraEviction();
            }
        }
        else
        {
            RemoveRunGlyphEntries(clearPixels: true);
            RestoreRunGlyphEntryTouches();
            ReleaseAtlasPagesCreatedDuringRun();
            RestoreRunPageStates();
            _activeAtlasPage = _runActiveAtlasPage;
            _pendingAlphaAtlasPageReuse = _runPendingAlphaAtlasPageReuse;
            _pendingBgraAtlasPageReuse = _runPendingBgraAtlasPageReuse;
            _cachedGlyphCount = _runCachedGlyphCount;
            _diagnostics = _runDiagnostics;
        }

        CommitAtlasRunMutation();
    }

    private void RemoveRunGlyphEntries(bool clearPixels)
    {
        for (var i = 0; i < _runGlyphEntryIndices.Count; i++)
        {
            var entryIndex = _runGlyphEntryIndices[i];
            if ((uint)entryIndex >= (uint)_glyphEntries.Count)
            {
                continue;
            }

            var entry = _glyphEntries[entryIndex];
            if (!entry.IsLive)
            {
                continue;
            }

            if (clearPixels)
            {
                ClearGlyphEntryPixels(entry);
            }

            _glyphs.Remove(entry.Key);
            _glyphEntries[entryIndex] = entry.Clear();
            _freeGlyphEntryIndices.Add(entryIndex);
        }
    }

    private void RestoreRunGlyphEntryTouches()
    {
        for (var i = 0; i < _runGlyphEntryStates.Count; i++)
        {
            var state = _runGlyphEntryStates[i];
            if ((uint)state.Index >= (uint)_glyphEntries.Count)
            {
                continue;
            }

            var entry = _glyphEntries[state.Index];
            if (entry.IsLive && entry.Generation == state.Generation)
            {
                _glyphEntries[state.Index] = entry.WithLastUsedSerial(state.LastUsedSerial);
            }
        }
    }

    private void ClearGlyphEntryPixels(GlyphEntry entry)
    {
        if (!TryResolveAtlasPage(entry.Page, out var page))
        {
            return;
        }

        var x = Math.Clamp((int)MathF.Round(entry.U1 * AtlasWidth), 0, AtlasWidth);
        var y = Math.Clamp((int)MathF.Round(entry.V1 * AtlasHeight), 0, AtlasHeight);
        var width = Math.Clamp((int)MathF.Round(entry.U2 * AtlasWidth), x, AtlasWidth) - x;
        var height = Math.Clamp((int)MathF.Round(entry.V2 * AtlasHeight), y, AtlasHeight) - y;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rowPitch = page.RowPitch;
        var bytesPerPixel = page.BytesPerPixel;
        var byteX = x * bytesPerPixel;
        var byteWidth = width * bytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            page.Pixels.AsSpan((y + row) * rowPitch + byteX, byteWidth).Clear();
        }
    }

    private void ResetPagesReusedDuringRun(out int alphaPageCount, out int bgraPageCount)
    {
        alphaPageCount = 0;
        bgraPageCount = 0;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Handle == _runPageStates[i].Handle)
            {
                continue;
            }

            _activeAtlasPage = page.ResetForReuse();
            if (page.Format == GlyphAtlasPageFormat.Bgra)
            {
                bgraPageCount++;
            }
            else
            {
                alphaPageCount++;
            }
        }
    }

    private void RestoreRunPageStates()
    {
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _atlasPages[i].RestoreMutationState(_runPageStates[i]);
        }
    }

    private void ReleaseAtlasPagesCreatedDuringRun()
    {
        while (_atlasPages.Count > _runAtlasPageCount)
        {
            var pageIndex = _atlasPages.Count - 1;
            _atlasPages[pageIndex].Release();
            _atlasPages.RemoveAt(pageIndex);
        }
    }

    private int CountLiveGlyphEntries()
    {
        var count = 0;
        for (var i = 0; i < _glyphEntries.Count; i++)
        {
            if (_glyphEntries[i].IsLive)
            {
                count++;
            }
        }

        return count;
    }

    private bool TryResolveGlyph(GlyphAtlasEntryHandle handle, long recordSerial, out GlyphEntry entry)
    {
        if (handle.IsNone || (uint)handle.Index >= (uint)_glyphEntries.Count)
        {
            entry = default;
            return false;
        }

        entry = _glyphEntries[handle.Index];
        if (!entry.IsLive || entry.Generation != handle.Generation || !TryResolveAtlasPage(entry.Page, out var page))
        {
            return false;
        }

        if (_runAtlasMutationActive && entry.LastUsedSerial != recordSerial)
        {
            _runGlyphEntryStates.Add(new GlyphEntryMutationState(handle.Index, entry.Generation, entry.LastUsedSerial));
        }

        page.Touch(recordSerial);
        entry = entry.WithLastUsedSerial(recordSerial);
        _glyphEntries[handle.Index] = entry;
        return true;
    }

    private GlyphAtlasPageHandle AddAtlasPage(GlyphAtlasPageFormat format, ID3D12Resource* texture, ID3D12Resource*[] uploads, ID3D12DescriptorHeap* srvHeap, D3D12_RESOURCE_STATES textureState)
    {
        if (_atlasPages.Count >= AtlasPageBudget)
        {
            throw new InvalidOperationException("Glyph atlas page pool exceeded its fixed capacity.");
        }

        var handle = new GlyphAtlasPageHandle(_atlasPages.Count, 1);
        _atlasPages.Add(new GlyphAtlasPage(handle, format, texture, uploads, srvHeap, textureState, new byte[GetAtlasPixelBytes(format)]));
        return handle;
    }

    private bool TryResolveAtlasPage(GlyphAtlasPageHandle handle, [NotNullWhen(true)] out GlyphAtlasPage? page)
    {
        if (handle.IsNone || (uint)handle.Index >= (uint)_atlasPages.Count)
        {
            page = null;
            return false;
        }

        page = _atlasPages[handle.Index];
        return page.Generation == handle.Generation;
    }

    private GlyphAtlasPage RequireActiveAtlasPage()
    {
        if (!TryResolveAtlasPage(_activeAtlasPage, out var page))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.RequireActiveAtlasPage found a stale active glyph atlas page handle.");
        }

        return page;
    }

    private GlyphAtlasPage? SelectWritableAtlasPage(GlyphAtlasPageFormat format, int width, int height, long recordSerial)
    {
        if (width <= 0 || height <= 0)
        {
            var active = RequireActiveAtlasPage();
            if (active.Format == format)
            {
                return active;
            }

            return FindAtlasPageByFormat(format) ?? TryCreateAdditionalAtlasPage(format);
        }

        var activePage = RequireActiveAtlasPage();
        if (activePage.Format == format && CanAllocateGlyph(activePage, width, height))
        {
            return activePage;
        }

        var selectedIndex = -1;
        var selectedAvailablePixels = -1;
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Handle == activePage.Handle || page.Format != format || !CanAllocateGlyph(page, width, height))
            {
                continue;
            }

            var availablePixels = page.ComputeAvailablePixels();
            if (GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels, selectedLastUsedSerial, availablePixels, page.LastUsedSerial))
            {
                selectedIndex = i;
                selectedAvailablePixels = availablePixels;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        if (selectedIndex >= 0)
        {
            var page = _atlasPages[selectedIndex];
            _activeAtlasPage = page.Handle;
            return page;
        }

        var newPage = TryCreateAdditionalAtlasPage(format);
        if (newPage is not null)
        {
            _activeAtlasPage = newPage.Handle;
            return newPage;
        }

        var reusedPage = TryReuseColdAtlasPageForCurrentRecord(format, width, height, recordSerial);
        if (reusedPage is not null)
        {
            return reusedPage;
        }

        ScheduleAtlasPageReuse(recordSerial, format);
        return null;
    }

    private GlyphAtlasPage? FindAtlasPageByFormat(GlyphAtlasPageFormat format)
    {
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Format == format)
            {
                return page;
            }
        }

        return null;
    }

    private GlyphAtlasPage? TryReuseColdAtlasPageForCurrentRecord(GlyphAtlasPageFormat format, int width, int height, long recordSerial)
    {
        if (width + AtlasPadding * 2 > AtlasWidth || height + AtlasPadding * 2 > AtlasHeight)
        {
            return null;
        }

        var selectedIndex = -1;
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Format != format || !GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(page.LastUsedSerial, recordSerial))
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial, page.LastUsedSerial))
            {
                selectedIndex = i;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        if (selectedIndex < 0)
        {
            return null;
        }

        var selected = _atlasPages[selectedIndex];
        var reusedHandle = selected.Handle;
        if (_runAtlasMutationActive)
        {
            _runAtlasMutationUsedPageReuse = true;
        }

        _activeAtlasPage = selected.ResetForReuse();
        RemoveGlyphsForReusedPage(reusedHandle);
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPages(_atlasPages.Count)
            .WithAtlasEviction(selected.Format == GlyphAtlasPageFormat.Bgra);
        return selected;
    }

    private void ScheduleAtlasPageReuse(long recordSerial, GlyphAtlasPageFormat? format = null)
    {
        var page = SelectOldestAtlasPageHandle(format);
        if (page.IsNone)
        {
            _diagnostics = _diagnostics.WithAtlasFullWithoutPageReuse(format == GlyphAtlasPageFormat.Bgra);
            return;
        }

        if (!TryResolveAtlasPage(page, out var atlasPage))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.ScheduleAtlasPageReuse found a stale glyph atlas page handle.");
        }

        ref var pending = ref GetPendingAtlasPageReuse(atlasPage.Format);
        if (!pending.IsNone)
        {
            return;
        }

        pending = new GlyphAtlasPageReuseRequest(page, recordSerial);
        _diagnostics = _diagnostics.WithAtlasPageReuseRequest(atlasPage.Format == GlyphAtlasPageFormat.Bgra);
    }

    private ref GlyphAtlasPageReuseRequest GetPendingAtlasPageReuse(GlyphAtlasPageFormat format)
    {
        if (format == GlyphAtlasPageFormat.Bgra)
        {
            return ref _pendingBgraAtlasPageReuse;
        }

        return ref _pendingAlphaAtlasPageReuse;
    }

    private void CountPendingAtlasPageReuseRequests(out int alphaReuses, out int bgraReuses)
    {
        alphaReuses = _pendingAlphaAtlasPageReuse.IsNone ? 0 : 1;
        bgraReuses = _pendingBgraAtlasPageReuse.IsNone ? 0 : 1;
    }

    private GlyphAtlasPageHandle SelectOldestAtlasPageHandle(GlyphAtlasPageFormat? format)
    {
        var selected = default(GlyphAtlasPageHandle);
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (format.HasValue && page.Format != format.GetValueOrDefault())
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial, page.LastUsedSerial))
            {
                selected = page.Handle;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        return selected;
    }

    private GlyphAtlasPageUsage GetAtlasPageUsage()
    {
        var usedPixels = 0;
        var fragmentedPixels = 0;
        var alphaUsedPixels = 0;
        var bgraUsedPixels = 0;
        var alphaFragmentedPixels = 0;
        var bgraFragmentedPixels = 0;
        var alphaPageCount = 0;
        var bgraPageCount = 0;
        var oldestPageAge = 0L;
        var newestPageAge = 0L;
        var oldestAlphaPageAge = 0L;
        var oldestBgraPageAge = 0L;
        var hasUsedPage = false;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            var pageFragmentedPixels = Math.Max(0, page.AllocatedPixels - page.UsedPixels);
            if (page.Format == GlyphAtlasPageFormat.Bgra)
            {
                bgraPageCount++;
                bgraUsedPixels = checked(bgraUsedPixels + page.UsedPixels);
                bgraFragmentedPixels = checked(bgraFragmentedPixels + pageFragmentedPixels);
            }
            else
            {
                alphaPageCount++;
                alphaUsedPixels = checked(alphaUsedPixels + page.UsedPixels);
                alphaFragmentedPixels = checked(alphaFragmentedPixels + pageFragmentedPixels);
            }

            usedPixels = checked(usedPixels + page.UsedPixels);
            fragmentedPixels = checked(fragmentedPixels + pageFragmentedPixels);
            if (page.LastUsedSerial > 0)
            {
                var age = _glyphRecordSerial - page.LastUsedSerial;
                oldestPageAge = Math.Max(oldestPageAge, age);
                newestPageAge = hasUsedPage ? Math.Min(newestPageAge, age) : age;
                if (page.Format == GlyphAtlasPageFormat.Bgra)
                {
                    oldestBgraPageAge = Math.Max(oldestBgraPageAge, age);
                }
                else
                {
                    oldestAlphaPageAge = Math.Max(oldestAlphaPageAge, age);
                }

                hasUsedPage = true;
            }
        }

        return new GlyphAtlasPageUsage(usedPixels, fragmentedPixels, alphaPageCount, bgraPageCount, alphaUsedPixels, bgraUsedPixels, alphaFragmentedPixels, bgraFragmentedPixels, oldestPageAge, newestPageAge, oldestAlphaPageAge, oldestBgraPageAge);
    }

    private void ApplyPendingAtlasPageEviction(long recordSerial, long oldestRetainedRecordSerial)
    {
        ApplyPendingAtlasPageEviction(ref _pendingAlphaAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);
        ApplyPendingAtlasPageEviction(ref _pendingBgraAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);
    }

    private void ApplyPendingAtlasPageEviction(ref GlyphAtlasPageReuseRequest pendingPageReuse, long recordSerial, long oldestRetainedRecordSerial)
    {
        if (!pendingPageReuse.CanApply(recordSerial, oldestRetainedRecordSerial))
        {
            return;
        }

        var page = RequireReuseAtlasPage(pendingPageReuse);
        var reusedPage = page.Handle;
        _activeAtlasPage = page.ResetForReuse();
        RemoveGlyphsForReusedPage(reusedPage);
        pendingPageReuse = default;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPages(_atlasPages.Count)
            .WithAtlasEviction(page.Format == GlyphAtlasPageFormat.Bgra);
    }

    private GlyphAtlasPage RequireReuseAtlasPage(GlyphAtlasPageReuseRequest request)
    {
        if (!TryResolveAtlasPage(request.Page, out var page))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.ApplyPendingAtlasPageEviction found a stale pending glyph atlas page reuse handle.");
        }

        return page;
    }

    private void RemoveGlyphsForReusedPage(GlyphAtlasPageHandle pageHandle)
    {
        var cachedGlyphCount = 0;
        for (var i = 0; i < _glyphEntries.Count; i++)
        {
            var entry = _glyphEntries[i];
            if (!entry.IsLive)
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(
                entry.IsLive,
                entry.Page.Index,
                entry.Page.Generation,
                pageHandle.Index,
                pageHandle.Generation))
            {
                _glyphs.Remove(entry.Key);
                _glyphEntries[i] = entry.Clear();
                _freeGlyphEntryIndices.Add(i);
                continue;
            }

            cachedGlyphCount++;
        }

        _cachedGlyphCount = cachedGlyphCount;
    }

}

