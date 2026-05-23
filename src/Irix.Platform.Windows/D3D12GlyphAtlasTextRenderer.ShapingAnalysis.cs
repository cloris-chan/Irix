using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private static bool TryMapCharacterToSimpleGlyph(CachedFontFace fontFace, char character, out GlyphAtom glyph)
    {
        glyph = default;
        var codePoint = (uint)character;
        var glyphIndex = stackalloc ushort[1];
        try
        {
            fontFace.Face->GetGlyphIndices(new ReadOnlySpan<uint>(&codePoint, 1), new Span<ushort>(glyphIndex, 1));
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.GetGlyphIndices(simple)",
                ex);
        }

        if (glyphIndex[0] == 0 && character != ' ')
        {
            return false;
        }

        glyph = GlyphAtom.SimpleCodePoint(codePoint, glyphIndex[0]);
        return true;
    }

    private bool TryProbeShapedRun(ReadOnlySpan<char> text, TextStyle style, float maxLineWidth, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)
    {
        shapedRun = default;
        unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
        if (text.IsEmpty || text.Length > ushort.MaxValue)
        {
            return false;
        }

        var requiresColorGlyph = GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text);
        CachedFontFace fontFace;
        try
        {
            if (!TryGetFontFace(style, out fontFace))
            {
                unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
                return false;
            }
        }
        catch (GlyphAtlasRecordException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font lookup skipped: {ex.Phase}");
            return false;
        }

        if (!TryShapeRun(text, style, fontFace, maxLineWidth, requiresColorGlyph, out shapedRun, out unsupportedReason))
        {
            if (requiresColorGlyph)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            }

            return false;
        }

        return true;
    }

    private bool TryShapeRun(ReadOnlySpan<char> text, TextStyle style, CachedFontFace baseFontFace, float maxLineWidth, bool requiresColorGlyph, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)
    {
        shapedRun = default;
        unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
        var maxGlyphCount = GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(text.Length);
        EnsureShapeScratch(text.Length, maxGlyphCount, MaxShapedRunSegments, text.Length + 1);
        var advances = _shapedTextAdvanceScratch.AsSpan(0, text.Length);
        advances.Clear();
        _shapeScriptScratch.AsSpan(0, text.Length).Clear();
        var paragraphReadingDirection = DetermineParagraphReadingDirection(text);
        _shapeBidiLevelScratch.AsSpan(0, text.Length).Fill(paragraphReadingDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? (byte)1 : (byte)0);
        var hasComplexScriptCandidate = GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate(text);
        if (!TryAnalyzeShapedText(text, paragraphReadingDirection))
        {
            unsupportedReason = hasComplexScriptCandidate ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ComplexScript : GlyphAtlasFallbackReason.NonAscii;
            return false;
        }

        var hasRightToLeftBidiLevel = HasRightToLeftBidiLevel(0, text.Length);
        unsupportedReason = hasComplexScriptCandidate || hasRightToLeftBidiLevel ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ComplexScript : GlyphAtlasFallbackReason.NonAscii;

        var glyphStart = 0;
        var segmentCount = 0;
        var index = 0;
        while (true)
        {
            var lineStart = index;
            while (index < text.Length && !GlyphAtlasTextCompositionHelpers.IsLineBreak(text, index, out _))
            {
                index++;
            }

            if (!TryShapeTextRange(text, lineStart, index - lineStart, style, baseFontFace, ref glyphStart, ref segmentCount))
            {
                return false;
            }

            if (index >= text.Length)
            {
                break;
            }

            GlyphAtlasTextCompositionHelpers.IsLineBreak(text, index, out var lineBreakWidth);
            index += lineBreakWidth;
            if (index == text.Length)
            {
                break;
            }
        }

        unsupportedReason = GlyphAtlasTextCompositionHelpers.PlanLines(
            text,
            advances,
            maxLineWidth,
            style.Wrapping,
            _shapedLayoutLineScratch.AsSpan(0, text.Length + 1),
            out var plannedLineCount);
        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            return false;
        }

        if (!TryBuildShapedLinesFromLayout(text, segmentCount, plannedLineCount, out var lineCount))
        {
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
            return false;
        }

        shapedRun = new ShapedGlyphRun(
            _shapedGlyphScratch.AsSpan(0, glyphStart),
            _shapedSegmentScratch.AsSpan(0, segmentCount),
            _shapedLineScratch.AsSpan(0, lineCount),
            _shapeClusterScratch.AsSpan(0, text.Length),
            text.Length,
            requiresColorGlyph);
        return true;
    }

    private bool TryAnalyzeShapedText(ReadOnlySpan<char> text, DWRITE_READING_DIRECTION readingDirection)
    {
        if (_textAnalyzer == null || text.IsEmpty || text.Length > ushort.MaxValue)
        {
            return false;
        }

        fixed (char* textPtr = text)
        fixed (DWRITE_SCRIPT_ANALYSIS* scriptAnalysis = _shapeScriptScratch)
        fixed (byte* bidiLevels = _shapeBidiLevelScratch)
        {
            var locale = stackalloc char[6];
            locale[0] = 'e';
            locale[1] = 'n';
            locale[2] = '-';
            locale[3] = 'u';
            locale[4] = 's';
            locale[5] = '\0';

            var sourceVtbl = stackalloc void*[8];
            sourceVtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, Guid*, void**, HRESULT>)&TextAnalysisSourceQueryInterface;
            sourceVtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceAddRef;
            sourceVtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceRelease;
            sourceVtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextAtPosition;
            sourceVtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextBeforePosition;
            sourceVtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, DWRITE_READING_DIRECTION>)&TextAnalysisSourceGetParagraphReadingDirection;
            sourceVtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, ushort**, HRESULT>)&TextAnalysisSourceGetLocaleName;
            sourceVtbl[7] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, IDWriteNumberSubstitution**, HRESULT>)&TextAnalysisSourceGetNumberSubstitution;

            var sinkVtbl = stackalloc void*[7];
            sinkVtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, Guid*, void**, HRESULT>)&TextAnalysisSinkQueryInterface;
            sinkVtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint>)&TextAnalysisSinkAddRef;
            sinkVtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint>)&TextAnalysisSinkRelease;
            sinkVtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, DWRITE_SCRIPT_ANALYSIS*, HRESULT>)&TextAnalysisSinkSetScriptAnalysis;
            sinkVtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, DWRITE_LINE_BREAKPOINT*, HRESULT>)&TextAnalysisSinkSetLineBreakpoints;
            sinkVtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, byte, byte, HRESULT>)&TextAnalysisSinkSetBidiLevel;
            sinkVtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, IDWriteNumberSubstitution*, HRESULT>)&TextAnalysisSinkSetNumberSubstitution;

            var source = new TextAnalysisSourceShim
            {
                Vtbl = sourceVtbl,
                RefCount = 1,
                Text = textPtr,
                TextLength = (uint)text.Length,
                Locale = locale,
                ReadingDirection = readingDirection
            };
            var sink = new TextAnalysisSinkShim
            {
                Vtbl = sinkVtbl,
                RefCount = 1,
                TextLength = (uint)text.Length,
                ScriptAnalysis = scriptAnalysis,
                BidiLevels = bidiLevels
            };

            try
            {
                _textAnalyzer->AnalyzeScript((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
                _textAnalyzer->AnalyzeBidi((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
                return true;
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape analysis failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
        }
    }

    private bool HasRightToLeftBidiLevel(int textStart, int textLength)
    {
        var bidiLevels = _shapeBidiLevelScratch.AsSpan(textStart, textLength);
        foreach (var level in bidiLevels)
        {
            if ((level & 1) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryShapeTextRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        if (textLength == 0)
        {
            return true;
        }

        var textEnd = textStart + textLength;
        var position = textStart;
        while (position < textEnd)
        {
            if (GlyphAtlasTextCompositionHelpers.IsTab(text[position]))
            {
                if (!TryAppendShapedControlSegment(style, baseFontFace, position, glyphStart, ref segmentCount, GlyphAtlasTextCompositionHelpers.TabAdvanceSpaceCount))
                {
                    return false;
                }

                position++;
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(text[position]))
            {
                if (!TryAppendShapedControlSegment(style, baseFontFace, position, glyphStart, ref segmentCount, spaceCount: 1))
                {
                    return false;
                }

                position++;
                continue;
            }

            var segmentStart = position;
            while (position < textEnd && !GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(text[position]))
            {
                position++;
            }

            if (!TryShapeTextSpan(text, segmentStart, position - segmentStart, style, baseFontFace, ref glyphStart, ref segmentCount))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryShapeTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        PromoteRtlSpanBaseLevel(text, textStart, textLength);
        if (!TryGetUniformBidiLevel(textStart, textLength, out var bidiLevel))
        {
            return TryShapeBidiLevelRuns(text, textStart, textLength, style, baseFontFace, ref glyphStart, ref segmentCount);
        }

        return TryShapeUniformBidiTextSpan(text, textStart, textLength, style, baseFontFace, bidiLevel, ref glyphStart, ref segmentCount);
    }

    private bool TryShapeBidiLevelRuns(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        var textEnd = textStart + textLength;
        var position = textStart;
        while (position < textEnd)
        {
            var runStart = position;
            var bidiLevel = _shapeBidiLevelScratch[position++];
            while (position < textEnd && _shapeBidiLevelScratch[position] == bidiLevel)
            {
                position++;
            }

            if (!TryShapeUniformBidiTextSpan(text, runStart, position - runStart, style, baseFontFace, bidiLevel, ref glyphStart, ref segmentCount))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryShapeUniformBidiTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, byte bidiLevel, ref int glyphStart, ref int segmentCount)
    {
        var range = text.Slice(textStart, textLength);
        var initialGlyphStart = glyphStart;
        if (!TryShapeTextSegment(range, baseFontFace, style.FontSize, textStart, initialGlyphStart, _shapeScriptScratch[textStart], bidiLevel, out var glyphCount))
        {
            return false;
        }

        if (!HasMissingGlyph(initialGlyphStart, glyphCount))
        {
            if (segmentCount >= MaxShapedRunSegments)
            {
                return false;
            }

            _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(baseFontFace, style.FontSize, textStart, textLength, initialGlyphStart, glyphCount, ControlAdvance: 0, bidiLevel);
            if (!TryAssignShapedTextAdvances(textStart, textLength, initialGlyphStart, glyphCount))
            {
                return false;
            }

            glyphStart = initialGlyphStart + glyphCount;
            return true;
        }

        glyphStart = initialGlyphStart;
        return TryShapeSegmentedFallbackRange(text, textStart, textLength, style, baseFontFace, ref glyphStart, ref segmentCount);
    }

    private bool TryAppendShapedControlSegment(TextStyle style, CachedFontFace baseFontFace, int textStart, int glyphStart, ref int segmentCount, int spaceCount)
    {
        if (segmentCount >= MaxShapedRunSegments || !TryMeasureCharacterAdvance(baseFontFace, style.FontSize, ' ', out var spaceAdvance))
        {
            return false;
        }

        _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(
            baseFontFace,
            style.FontSize,
            textStart,
            TextLength: 1,
            glyphStart,
            GlyphCount: 0,
            spaceAdvance * spaceCount,
            _shapeBidiLevelScratch[textStart]);
        _shapedTextAdvanceScratch[textStart] = spaceAdvance * spaceCount;
        return true;
    }

    private bool TryShapeTextSegment(ReadOnlySpan<char> text, CachedFontFace fontFace, float fontEmSize, int textScratchStart, int glyphStart, DWRITE_SCRIPT_ANALYSIS scriptAnalysis, byte bidiLevel, out int glyphCount)
    {
        glyphCount = 0;
        var isRightToLeft = (bidiLevel & 1) != 0;

        fixed (char* textPtr = text)
        fixed (ushort* clusterMapBase = _shapeClusterScratch)
        fixed (DWRITE_SHAPING_TEXT_PROPERTIES* textPropsBase = _shapeTextPropsScratch)
        fixed (ushort* glyphIndicesBase = _shapeGlyphScratch)
        fixed (DWRITE_SHAPING_GLYPH_PROPERTIES* glyphPropsBase = _shapeGlyphPropsScratch)
        {
            var clusterMap = clusterMapBase + textScratchStart;
            var textProps = textPropsBase + textScratchStart;
            var glyphIndices = glyphIndicesBase + glyphStart;
            var glyphProps = glyphPropsBase + glyphStart;
            uint actualGlyphCount;
            try
            {
                _textAnalyzer->GetGlyphs(
                    new PCWSTR(textPtr),
                    (uint)text.Length,
                    fontFace.Face,
                    false,
                    isRightToLeft,
                    &scriptAnalysis,
                    default,
                    null,
                    null,
                    null,
                    0,
                    (uint)(_shapeGlyphScratch.Length - glyphStart),
                    clusterMap,
                    textProps,
                    glyphIndices,
                    glyphProps,
                    &actualGlyphCount);
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe GetGlyphs failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }

            if (actualGlyphCount == 0 || actualGlyphCount > _shapeGlyphScratch.Length - glyphStart)
            {
                return false;
            }

            fixed (float* advancesBase = _shapeAdvanceScratch)
            fixed (DWRITE_GLYPH_OFFSET* offsetsBase = _shapeOffsetScratch)
            {
                var advances = advancesBase + glyphStart;
                var offsets = offsetsBase + glyphStart;
                try
                {
                    _textAnalyzer->GetGlyphPlacements(
                        new PCWSTR(textPtr),
                        clusterMap,
                        textProps,
                        (uint)text.Length,
                        glyphIndices,
                        glyphProps,
                        actualGlyphCount,
                        fontFace.Face,
                        fontEmSize,
                        false,
                        isRightToLeft,
                        &scriptAnalysis,
                        default,
                        null,
                        null,
                        0,
                        advances,
                        offsets);
                }
                catch (COMException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe GetGlyphPlacements failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                    return false;
                }
            }

            glyphCount = (int)actualGlyphCount;
            ProjectShapedGlyphs(glyphStart, glyphCount);
            return true;
        }
    }

    private bool TryShapeSegmentedFallbackRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        if (_fontFallback == null || textLength == 0 || text.Length > ushort.MaxValue)
        {
            return false;
        }

        var rangeSegmentStart = segmentCount;
        IDWriteFont* mappedFont = null;
        try
        {
            fixed (char* textPtr = text)
            fixed (char* baseFamilyName = ToDirectWriteFontFamily(style.FontFamily))
            {
                var locale = stackalloc char[6];
                locale[0] = 'e';
                locale[1] = 'n';
                locale[2] = '-';
                locale[3] = 'u';
                locale[4] = 's';
                locale[5] = '\0';

                var vtbl = stackalloc void*[8];
                vtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, Guid*, void**, HRESULT>)&TextAnalysisSourceQueryInterface;
                vtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceAddRef;
                vtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceRelease;
                vtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextAtPosition;
                vtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextBeforePosition;
                vtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, DWRITE_READING_DIRECTION>)&TextAnalysisSourceGetParagraphReadingDirection;
                vtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, ushort**, HRESULT>)&TextAnalysisSourceGetLocaleName;
                vtbl[7] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, IDWriteNumberSubstitution**, HRESULT>)&TextAnalysisSourceGetNumberSubstitution;

                var source = new TextAnalysisSourceShim
                {
                    Vtbl = vtbl,
                    RefCount = 1,
                    Text = textPtr,
                    TextLength = (uint)text.Length,
                    Locale = locale,
                    ReadingDirection = DetermineParagraphReadingDirection(text)
                };

                var textPosition = (uint)textStart;
                var textEnd = (uint)(textStart + textLength);
                while (textPosition < textEnd)
                {
                    if (segmentCount >= MaxShapedRunSegments)
                    {
                        return false;
                    }

                    var mappedLength = 0u;
                    var scale = 1f;
                    _fontFallback->MapCharacters(
                        (IDWriteTextAnalysisSource*)&source,
                        textPosition,
                        textEnd - textPosition,
                        _fontCollection,
                        new PCWSTR(baseFamilyName),
                        ToDirectWriteFontWeight(style.FontWeight),
                        ToDirectWriteFontStyle(style.FontStyle),
                        ToDirectWriteFontStretch(style.FontStretch),
                        &mappedLength,
                        &mappedFont,
                        &scale);

                    if (mappedLength == 0 || mappedLength > textEnd - textPosition)
                    {
                        return false;
                    }

                    var fontFace = baseFontFace;
                    var fontEmSize = style.FontSize;
                    if (mappedFont != null)
                    {
                        if (scale <= 0 || !TryGetCachedFallbackFontFace(mappedFont, out fontFace))
                        {
                            return false;
                        }

                        fontEmSize *= scale;
                        mappedFont->Release();
                        mappedFont = null;
                    }

                    var segmentGlyphStart = glyphStart;
                    PromoteRtlSpanBaseLevel(text, (int)textPosition, (int)mappedLength);
                    var segmentBidiLevel = _shapeBidiLevelScratch[(int)textPosition];
                    if (!TryShapeTextSegment(
                        text.Slice((int)textPosition, (int)mappedLength),
                        fontFace,
                        fontEmSize,
                        (int)textPosition,
                        segmentGlyphStart,
                        _shapeScriptScratch[(int)textPosition],
                        segmentBidiLevel,
                        out var glyphCount))
                    {
                        return false;
                    }

                    _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(
                        fontFace,
                        fontEmSize,
                        (int)textPosition,
                        (int)mappedLength,
                        segmentGlyphStart,
                        glyphCount,
                        ControlAdvance: 0,
                        segmentBidiLevel);
                    if (!TryAssignShapedTextAdvances((int)textPosition, (int)mappedLength, segmentGlyphStart, glyphCount))
                    {
                        return false;
                    }

                    glyphStart = segmentGlyphStart + glyphCount;
                    textPosition += mappedLength;
                }
            }

            return segmentCount > rangeSegmentStart;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font fallback failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
            return false;
        }
        catch (GlyphAtlasRecordException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font fallback skipped: {ex.Phase}");
            return false;
        }
        finally
        {
            if (mappedFont != null) mappedFont->Release();
        }
    }

    private bool TryGetCachedFallbackFontFace(IDWriteFont* font, out CachedFontFace fontFace)
    {
        fontFace = default!;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;
        global::Windows.Win32.System.Com.IUnknown* identity = null;
        try
        {
            var iid = IUnknownGuid;
            void* identityObject = null;
            font->QueryInterface(&iid, &identityObject).ThrowOnFailure();
            identity = (global::Windows.Win32.System.Com.IUnknown*)identityObject;
            var key = (nint)identity;
            if (_fallbackFontFaces.TryGetValue(key, out fontFace!))
            {
                identity->Release();
                identity = null;
                return true;
            }

            font->CreateFontFace(&face);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetCachedFallbackFontFace found a missing DirectWrite font face.");
            }

            face->GetMetrics(out var metrics);
            face4 = TryQueryFontFace4(face);
            fontFace = new CachedFontFace(new FontFaceIdentity(_nextFontFaceIdentity++), face, metrics, face4, identity);
            _fallbackFontFaces.Add(key, fontFace);
            face = null;
            face4 = null;
            identity = null;
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.TryGetCachedFallbackFontFace",
                ex);
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (face != null) face->Release();
            if (identity != null) identity->Release();
        }
    }

    private static IDWriteFontFace4* TryQueryFontFace4(IDWriteFontFace* face)
    {
        if (face == null)
        {
            return null;
        }

        try
        {
            face->QueryInterface<IDWriteFontFace4>(out var face4).ThrowOnFailure();
            return face4;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] DirectWrite font face4 query skipped: 0x{unchecked((uint)ex.ErrorCode):X8}");
            return null;
        }
    }

    private bool HasMissingGlyph(int glyphStart, int glyphCount)
    {
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        foreach (ref readonly var glyph in glyphs)
        {
            if (glyph.GlyphIndex == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void PromoteRtlSpanBaseLevel(ReadOnlySpan<char> text, int textStart, int textLength)
    {
        if (textLength > 0 && IsRtlOnlyStrongSpan(text.Slice(textStart, textLength)))
        {
            _shapeBidiLevelScratch.AsSpan(textStart, textLength).Fill(1);
        }
    }

    private static bool IsRtlOnlyStrongSpan(ReadOnlySpan<char> text)
    {
        var hasRightToLeftStrong = false;
        foreach (var character in text)
        {
            if (GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character))
            {
                hasRightToLeftStrong = true;
                continue;
            }

            if (char.IsLetter(character))
            {
                return false;
            }
        }

        return hasRightToLeftStrong;
    }

    private float ComputeShapedGlyphAdvance(int glyphStart, int glyphCount)
    {
        var width = 0f;
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        foreach (ref readonly var glyph in glyphs)
        {
            width += glyph.Advance;
        }

        return width;
    }

    private bool TryGetUniformBidiLevel(int textStart, int textLength, out byte bidiLevel)
    {
        bidiLevel = 0;
        if (textLength == 0)
        {
            return true;
        }

        bidiLevel = _shapeBidiLevelScratch[textStart];
        var end = textStart + textLength;
        for (var i = textStart + 1; i < end; i++)
        {
            if (_shapeBidiLevelScratch[i] != bidiLevel)
            {
                return false;
            }
        }

        return true;
    }

    private float ComputeShapedLineAdvance(int segmentStart, int segmentCount)
    {
        var width = 0f;
        var segments = _shapedSegmentScratch.AsSpan(segmentStart, segmentCount);
        foreach (ref readonly var segment in segments)
        {
            width += ComputeShapedSegmentAdvance(segment);
        }

        return width;
    }

    private float ComputeShapedSegmentAdvance(ShapedGlyphSegment segment) => segment.ControlAdvance + ComputeShapedGlyphAdvance(segment.GlyphStart, segment.GlyphCount);

    private bool TryAssignShapedTextAdvances(int textStart, int textLength, int glyphStart, int glyphCount)
    {
        if ((_shapeBidiLevelScratch[textStart] & 1) != 0)
        {
            _shapedTextAdvanceScratch[textStart] = ComputeShapedGlyphAdvance(glyphStart, glyphCount);
            _shapedTextAdvanceScratch.AsSpan(textStart + 1, textLength - 1).Clear();
            return true;
        }

        for (var offset = 0; offset < textLength; offset++)
        {
            var textIndex = textStart + offset;
            var relativeGlyphStart = _shapeClusterScratch[textIndex];
            if (relativeGlyphStart > glyphCount)
            {
                return false;
            }

            if (offset > 0 && relativeGlyphStart == _shapeClusterScratch[textIndex - 1])
            {
                _shapedTextAdvanceScratch[textIndex] = 0;
                continue;
            }

            var relativeGlyphEnd = glyphCount;
            for (var nextOffset = offset + 1; nextOffset < textLength; nextOffset++)
            {
                var nextRelativeGlyphStart = _shapeClusterScratch[textStart + nextOffset];
                if (nextRelativeGlyphStart == relativeGlyphStart)
                {
                    continue;
                }

                if (nextRelativeGlyphStart < relativeGlyphStart || nextRelativeGlyphStart > glyphCount)
                {
                    return false;
                }

                relativeGlyphEnd = nextRelativeGlyphStart;
                break;
            }

            _shapedTextAdvanceScratch[textIndex] = ComputeShapedGlyphAdvance(glyphStart + relativeGlyphStart, relativeGlyphEnd - relativeGlyphStart);
        }

        return true;
    }

    private bool TryBuildShapedLinesFromLayout(ReadOnlySpan<char> text, int segmentCount, int plannedLineCount, out int lineCount)
    {
        lineCount = 0;
        var segmentIndex = 0;
        for (var i = 0; i < plannedLineCount; i++)
        {
            var plannedLine = _shapedLayoutLineScratch[i];
            while (segmentIndex < segmentCount && _shapedSegmentScratch[segmentIndex].TextEnd <= plannedLine.Start)
            {
                segmentIndex++;
            }

            var lineSegmentStart = segmentIndex;
            var lineGlyphStart = segmentIndex < segmentCount ? _shapedSegmentScratch[segmentIndex].GlyphStart : 0;
            var lineGlyphEnd = lineGlyphStart;
            var hasGlyphSegment = false;
            var firstGlyphSegmentBidiLevel = (byte)0;
            while (segmentIndex < segmentCount && _shapedSegmentScratch[segmentIndex].TextStart < plannedLine.End)
            {
                var segment = _shapedSegmentScratch[segmentIndex];
                if (segment.TextStart < plannedLine.Start || segment.TextEnd > plannedLine.End)
                {
                    return false;
                }

                if (segment.GlyphCount > 0)
                {
                    if (!hasGlyphSegment)
                    {
                        firstGlyphSegmentBidiLevel = segment.BidiLevel;
                    }

                    hasGlyphSegment = true;
                }

                lineGlyphEnd = Math.Max(lineGlyphEnd, segment.GlyphStart + segment.GlyphCount);
                segmentIndex++;
            }

            var lineSegmentCount = segmentIndex - lineSegmentStart;
            var lineIsRightToLeft = TryDetermineRangeReadingDirection(text, plannedLine.Start, plannedLine.End, out var lineDirection)
                ? lineDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT
                : hasGlyphSegment && (firstGlyphSegmentBidiLevel & 1) != 0;
            ApplyShapedLineVisualOrder(lineSegmentStart, lineSegmentCount, lineIsRightToLeft);

            _shapedLineScratch[lineCount++] = new ShapedGlyphLine(
                lineSegmentStart,
                lineSegmentCount,
                lineGlyphStart,
                lineGlyphEnd - lineGlyphStart,
                plannedLine.Width,
                lineIsRightToLeft ? (byte)1 : (byte)0);
        }

        return true;
    }

    private void ApplyShapedLineVisualOrder(int segmentStart, int segmentCount, bool lineIsRightToLeft)
    {
        if (segmentCount <= 1)
        {
            return;
        }

        var segments = _shapedSegmentScratch.AsSpan(segmentStart, segmentCount);
        Span<byte> bidiLevels = stackalloc byte[MaxShapedRunSegments];
        var lineBidiLevels = bidiLevels.Slice(0, segmentCount);
        for (var i = 0; i < segments.Length; i++)
        {
            lineBidiLevels[i] = segments[i].BidiLevel;
        }

        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(segments, lineBidiLevels);
        if (lineIsRightToLeft)
        {
            ReverseShapedSegments(segmentStart, segmentStart + segmentCount - 1);
        }
    }

    private void ReverseShapedSegments(int start, int end)
    {
        while (start < end)
        {
            (_shapedSegmentScratch[start], _shapedSegmentScratch[end]) = (_shapedSegmentScratch[end], _shapedSegmentScratch[start]);
            start++;
            end--;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceQueryInterface(TextAnalysisSourceShim* source, Guid* riid, void** ppvObject)
    {
        if (ppvObject == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var iUnknown = IUnknownGuid;
        if (riid != null && (*riid == iUnknown || *riid == IDWriteTextAnalysisSource.IID_Guid))
        {
            *ppvObject = source;
            source->RefCount++;
            return (HRESULT)0;
        }

        *ppvObject = null;
        return (HRESULT)unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSourceAddRef(TextAnalysisSourceShim* source) => (uint)++source->RefCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSourceRelease(TextAnalysisSourceShim* source)
    {
        if (source->RefCount > 0)
        {
            source->RefCount--;
        }

        return (uint)source->RefCount;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetTextAtPosition(TextAnalysisSourceShim* source, uint textPosition, ushort** textString, uint* textLength)
    {
        if (textString == null || textLength == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        if (textPosition >= source->TextLength)
        {
            *textString = null;
            *textLength = 0;
            return (HRESULT)0;
        }

        *textString = (ushort*)(source->Text + textPosition);
        *textLength = source->TextLength - textPosition;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetTextBeforePosition(TextAnalysisSourceShim* source, uint textPosition, ushort** textString, uint* textLength)
    {
        if (textString == null || textLength == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        if (textPosition == 0 || textPosition > source->TextLength)
        {
            *textString = null;
            *textLength = 0;
            return (HRESULT)0;
        }

        *textString = (ushort*)source->Text;
        *textLength = textPosition;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static DWRITE_READING_DIRECTION TextAnalysisSourceGetParagraphReadingDirection(TextAnalysisSourceShim* source) => source->ReadingDirection;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetLocaleName(TextAnalysisSourceShim* source, uint textPosition, uint* textLength, ushort** localeName)
    {
        if (textLength == null || localeName == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        *textLength = textPosition < source->TextLength ? source->TextLength - textPosition : 0;
        *localeName = (ushort*)source->Locale;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetNumberSubstitution(TextAnalysisSourceShim* source, uint textPosition, uint* textLength, IDWriteNumberSubstitution** numberSubstitution)
    {
        if (textLength == null || numberSubstitution == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        *textLength = textPosition < source->TextLength ? source->TextLength - textPosition : 0;
        *numberSubstitution = null;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkQueryInterface(TextAnalysisSinkShim* sink, Guid* riid, void** ppvObject)
    {
        if (ppvObject == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var iUnknown = IUnknownGuid;
        if (riid != null && (*riid == iUnknown || *riid == IDWriteTextAnalysisSink.IID_Guid))
        {
            *ppvObject = sink;
            sink->RefCount++;
            return (HRESULT)0;
        }

        *ppvObject = null;
        return (HRESULT)unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSinkAddRef(TextAnalysisSinkShim* sink) => (uint)++sink->RefCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSinkRelease(TextAnalysisSinkShim* sink)
    {
        if (sink->RefCount > 0)
        {
            sink->RefCount--;
        }

        return (uint)sink->RefCount;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetScriptAnalysis(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_SCRIPT_ANALYSIS* scriptAnalysis)
    {
        if (scriptAnalysis == null || sink->ScriptAnalysis == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        var end = (int)(textPosition + textLength);
        for (var i = start; i < end; i++)
        {
            sink->ScriptAnalysis[i] = *scriptAnalysis;
        }

        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetLineBreakpoints(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_LINE_BREAKPOINT* lineBreakpoints) => (HRESULT)0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetBidiLevel(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, byte explicitLevel, byte resolvedLevel)
    {
        if (sink->BidiLevels == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        var end = (int)(textPosition + textLength);
        for (var i = start; i < end; i++)
        {
            sink->BidiLevels[i] = resolvedLevel;
        }

        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetNumberSubstitution(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, IDWriteNumberSubstitution* numberSubstitution) => (HRESULT)0;

    private void ProjectShapedGlyphs(int glyphStart, int glyphCount)
    {
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        var glyphIndices = _shapeGlyphScratch.AsSpan(glyphStart, glyphCount);
        var glyphProps = _shapeGlyphPropsScratch.AsSpan(glyphStart, glyphCount);
        var advances = _shapeAdvanceScratch.AsSpan(glyphStart, glyphCount);
        var offsets = _shapeOffsetScratch.AsSpan(glyphStart, glyphCount);
        for (var i = 0; i < glyphCount; i++)
        {
            glyphs[i] = ShapedGlyph.FromDirectWrite(glyphIndices[i], advances[i], offsets[i], glyphProps[i]);
        }
    }

    private void EnsureShapeScratch(int textLength, int glyphCapacity, int segmentCapacity, int lineCapacity)
    {
        var resized = false;
        if (_shapeClusterScratch.Length < textLength)
        {
            _shapeClusterScratch = new ushort[textLength];
            _shapeTextPropsScratch = new DWRITE_SHAPING_TEXT_PROPERTIES[textLength];
            _shapeScriptScratch = new DWRITE_SCRIPT_ANALYSIS[textLength];
            _shapeBidiLevelScratch = new byte[textLength];
            resized = true;
        }

        if (_shapeGlyphScratch.Length < glyphCapacity)
        {
            _shapeGlyphScratch = new ushort[glyphCapacity];
            _shapeGlyphPropsScratch = new DWRITE_SHAPING_GLYPH_PROPERTIES[glyphCapacity];
            _shapeAdvanceScratch = new float[glyphCapacity];
            _shapeOffsetScratch = new DWRITE_GLYPH_OFFSET[glyphCapacity];
            _shapedGlyphScratch = new ShapedGlyph[glyphCapacity];
            resized = true;
        }

        if (_shapedSegmentScratch.Length < segmentCapacity)
        {
            _shapedSegmentScratch = new ShapedGlyphSegment[segmentCapacity];
            resized = true;
        }

        if (_shapedLineScratch.Length < lineCapacity)
        {
            _shapedLineScratch = new ShapedGlyphLine[lineCapacity];
            resized = true;
        }

        if (_shapedLayoutLineScratch.Length < lineCapacity)
        {
            _shapedLayoutLineScratch = new GlyphAtlasLayoutLine[lineCapacity];
            resized = true;
        }

        if (_shapedTextAdvanceScratch.Length < textLength)
        {
            _shapedTextAdvanceScratch = new float[textLength];
            resized = true;
        }

        if (resized)
        {
            _shapeScratchResizeCount++;
        }
    }

    private int GetShapeScratchByteCount()
    {
        return checked(
            _shapeClusterScratch.Length * sizeof(ushort)
            + _shapeTextPropsScratch.Length * sizeof(DWRITE_SHAPING_TEXT_PROPERTIES)
            + _shapeScriptScratch.Length * sizeof(DWRITE_SCRIPT_ANALYSIS)
            + _shapeBidiLevelScratch.Length
            + _shapeGlyphScratch.Length * sizeof(ushort)
            + _shapeGlyphPropsScratch.Length * sizeof(DWRITE_SHAPING_GLYPH_PROPERTIES)
            + _shapeAdvanceScratch.Length * sizeof(float)
            + _shapeOffsetScratch.Length * sizeof(DWRITE_GLYPH_OFFSET)
            + _shapedGlyphScratch.Length * sizeof(ShapedGlyph)
            + _shapedSegmentScratch.Length * (IntPtr.Size + sizeof(float) * 2 + sizeof(int) * 4)
            + _shapedLineScratch.Length * (sizeof(int) * 4 + sizeof(float))
            + _shapedLayoutLineScratch.Length * (sizeof(int) * 2 + sizeof(float))
            + _shapedTextAdvanceScratch.Length * sizeof(float));
    }

    private Span<byte> RentClearTypeScratch(int byteCount)
    {
        if (_clearTypeScratch.Length < byteCount)
        {
            _clearTypeScratch = new byte[byteCount];
            _rasterScratchResizeCount++;
        }

        return _clearTypeScratch.AsSpan(0, byteCount);
    }

    private Span<byte> RentGrayscaleScratch(int byteCount)
    {
        if (_grayscaleScratch.Length < byteCount)
        {
            _grayscaleScratch = new byte[byteCount];
            _rasterScratchResizeCount++;
        }

        return _grayscaleScratch.AsSpan(0, byteCount);
    }

}

