#if IRIX_DIAGNOSTICS
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Platform.Windows;

internal static unsafe class DWriteGlyphOracleDiagnostic
{
    private static readonly Guid IUnknownGuid = new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
    private const float FontEmSize = 18f;
    private const int GlyphCapacityMultiplier = 3;

    private static readonly GlyphOracleProbeDefinition[] DefaultProbes =
    [
        new("ascii", "Atlas oracle 123", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT),
        new("cjk-fallback", "CJK \u6E2C\u8A66 fallback", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT),
        new("arabic-rtl", "\u0645\u0631\u062D\u0628\u0627 123", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT),
        new("mixed-bidi", "abc \u0645\u0631\u062D\u0628\u0627 xyz", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT),
        new("tab-crlf", "tab\tstop\r\nnext", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT)
    ];

    internal static GlyphOracleDiagnosticSnapshot Capture()
    {
        IDWriteFactory* factory = null;
        IDWriteFactory4* factory4 = null;
        IDWriteTextAnalyzer* analyzer = null;
        IDWriteFontCollection* fontCollection = null;
        IDWriteFontFallback* fontFallback = null;
        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* baseFace = null;

        try
        {
            PInvoke.DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                typeof(IDWriteFactory).GUID,
                out var factoryObject).ThrowOnFailure();
            factory = (IDWriteFactory*)factoryObject;
            factory->CreateTextAnalyzer(&analyzer);
            if (analyzer == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("TextAnalyzerUnavailable");
            }

            factory->QueryInterface<IDWriteFactory4>(out factory4).ThrowOnFailure();
            if (factory4 == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("Factory4Unavailable");
            }

            factory->GetSystemFontCollection(&fontCollection, false);
            if (fontCollection == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontCollectionUnavailable");
            }

            factory4->GetSystemFontFallback(&fontFallback);
            if (fontFallback == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontFallbackUnavailable");
            }

            fontCollection->FindFamilyName("Segoe UI", out var familyIndex, out var exists);
            if (!exists)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontFamilyMissing");
            }

            fontCollection->GetFontFamily(familyIndex, &family);
            if (family == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontFamilyUnavailable");
            }

            family->GetFirstMatchingFont(
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                &font);
            if (font == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontUnavailable");
            }

            font->CreateFontFace(&baseFace);
            if (baseFace == null)
            {
                return GlyphOracleDiagnosticSnapshot.Failed("FontFaceUnavailable");
            }

            var results = new GlyphOracleProbeResult[DefaultProbes.Length];
            for (var i = 0; i < DefaultProbes.Length; i++)
            {
                results[i] = Probe(analyzer, fontFallback, fontCollection, baseFace, DefaultProbes[i]);
            }

            return GlyphOracleDiagnosticSnapshot.Create(factoryAvailable: true, analyzerAvailable: true, fontFallbackAvailable: true, results);
        }
        catch (COMException ex)
        {
            return GlyphOracleDiagnosticSnapshot.Failed($"DirectWrite=0x{unchecked((uint)ex.ErrorCode):X8}");
        }
        finally
        {
            if (baseFace != null) baseFace->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
            if (fontFallback != null) fontFallback->Release();
            if (fontCollection != null) fontCollection->Release();
            if (analyzer != null) analyzer->Release();
            if (factory4 != null) factory4->Release();
            if (factory != null) factory->Release();
        }
    }

    private static GlyphOracleProbeResult Probe(
        IDWriteTextAnalyzer* analyzer,
        IDWriteFontFallback* fontFallback,
        IDWriteFontCollection* fontCollection,
        IDWriteFontFace* baseFace,
        GlyphOracleProbeDefinition probe)
    {
        var text = probe.Text;
        if (text.Length == 0 || text.Length > ushort.MaxValue)
        {
            return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, "UnsupportedTextLength");
        }

        var scripts = new DWRITE_SCRIPT_ANALYSIS[text.Length];
        var bidiLevels = new byte[text.Length];
        var lineBreaks = new GlyphOracleLineBreak[text.Length];
        bidiLevels.AsSpan().Fill(probe.BaseDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? (byte)1 : (byte)0);
        try
        {
            Analyze(analyzer, text.AsSpan(), probe.BaseDirection, scripts, bidiLevels, lineBreaks);
        }
        catch (COMException ex)
        {
            return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, $"Analyze=0x{unchecked((uint)ex.ErrorCode):X8}");
        }

        var segments = new GlyphOracleSegment[text.Length];
        var segmentCount = 0;
        var glyphs = new GlyphOracleGlyph[Math.Max(16, text.Length * GlyphCapacityMultiplier)];
        var glyphCount = 0;
        IDWriteFont* mappedFont = null;
        IDWriteFontFace* mappedFace = null;
        try
        {
            fixed (char* textPtr = text)
            {
                var sourceVtbl = stackalloc void*[8];
                sourceVtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, Guid*, void**, HRESULT>)&TextAnalysisSourceQueryInterface;
                sourceVtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceAddRef;
                sourceVtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceRelease;
                sourceVtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextAtPosition;
                sourceVtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextBeforePosition;
                sourceVtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, DWRITE_READING_DIRECTION>)&TextAnalysisSourceGetParagraphReadingDirection;
                sourceVtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, ushort**, HRESULT>)&TextAnalysisSourceGetLocaleName;
                sourceVtbl[7] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, IDWriteNumberSubstitution**, HRESULT>)&TextAnalysisSourceGetNumberSubstitution;

                var locale = stackalloc char[6];
                WriteLocale(locale);
                var source = new TextAnalysisSourceShim
                {
                    Vtbl = sourceVtbl,
                    RefCount = 1,
                    Text = textPtr,
                    TextLength = (uint)text.Length,
                    Locale = locale,
                    ReadingDirection = probe.BaseDirection
                };

                var textPosition = 0u;
                while (textPosition < text.Length)
                {
                    var mappedLength = 0u;
                    var scale = 1f;
                    fontFallback->MapCharacters(
                        (IDWriteTextAnalysisSource*)&source,
                        textPosition,
                        (uint)text.Length - textPosition,
                        fontCollection,
                        "Segoe UI",
                        DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                        DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                        DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                        ref mappedLength,
                        &mappedFont,
                        out scale);

                    if (mappedLength == 0 || mappedLength > text.Length - textPosition)
                    {
                        return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, "MapCharactersInvalidLength");
                    }

                    var face = baseFace;
                    var fontEmSize = FontEmSize;
                    var usedFallback = mappedFont != null;
                    if (mappedFont != null)
                    {
                        mappedFont->CreateFontFace(&mappedFace);
                        if (mappedFace == null || scale <= 0)
                        {
                            return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, "MappedFontFaceUnavailable");
                        }

                        face = mappedFace;
                        fontEmSize *= scale;
                    }

                    var segmentGlyphStart = glyphCount;
                    var segmentTextStart = (int)textPosition;
                    var segmentTextLength = (int)mappedLength;
                    if (!TryShapeSegment(
                        analyzer,
                        text.AsSpan(segmentTextStart, segmentTextLength),
                        face,
                        fontEmSize,
                        scripts[segmentTextStart],
                        bidiLevels[segmentTextStart],
                        glyphs,
                        ref glyphCount,
                        out var failure))
                    {
                        return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, failure);
                    }

                    segments[segmentCount++] = new GlyphOracleSegment(
                        segmentTextStart,
                        segmentTextLength,
                        segmentGlyphStart,
                        glyphCount - segmentGlyphStart,
                        scripts[segmentTextStart].script,
                        bidiLevels[segmentTextStart],
                        usedFallback,
                        fontEmSize);

                    if (mappedFace != null)
                    {
                        mappedFace->Release();
                        mappedFace = null;
                    }

                    if (mappedFont != null)
                    {
                        mappedFont->Release();
                        mappedFont = null;
                    }

                    textPosition += mappedLength;
                }
            }
        }
        catch (COMException ex)
        {
            return GlyphOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, $"Shape=0x{unchecked((uint)ex.ErrorCode):X8}");
        }
        finally
        {
            if (mappedFace != null) mappedFace->Release();
            if (mappedFont != null) mappedFont->Release();
        }

        Array.Resize(ref segments, segmentCount);
        Array.Resize(ref glyphs, glyphCount);
        return GlyphOracleProbeResult.Create(probe.Label, text.Length, probe.BaseDirection, bidiLevels, lineBreaks, segments, glyphs);
    }

    private static void Analyze(
        IDWriteTextAnalyzer* analyzer,
        ReadOnlySpan<char> text,
        DWRITE_READING_DIRECTION readingDirection,
        DWRITE_SCRIPT_ANALYSIS[] scripts,
        byte[] bidiLevels,
        GlyphOracleLineBreak[] lineBreaks)
    {
        fixed (char* textPtr = text)
        fixed (DWRITE_SCRIPT_ANALYSIS* scriptsPtr = scripts)
        fixed (byte* bidiLevelsPtr = bidiLevels)
        fixed (GlyphOracleLineBreak* lineBreaksPtr = lineBreaks)
        {
            var locale = stackalloc char[6];
            WriteLocale(locale);

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
                Scripts = scriptsPtr,
                BidiLevels = bidiLevelsPtr,
                LineBreaks = lineBreaksPtr
            };

            analyzer->AnalyzeScript((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
            analyzer->AnalyzeBidi((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
            analyzer->AnalyzeLineBreakpoints((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
        }
    }

    private static bool TryShapeSegment(
        IDWriteTextAnalyzer* analyzer,
        ReadOnlySpan<char> text,
        IDWriteFontFace* face,
        float fontEmSize,
        DWRITE_SCRIPT_ANALYSIS scriptAnalysis,
        byte bidiLevel,
        GlyphOracleGlyph[] glyphs,
        ref int glyphCount,
        out string failure)
    {
        failure = "";
        var localGlyphCapacity = Math.Min(text.Length * GlyphCapacityMultiplier + 8, glyphs.Length - glyphCount);
        if (localGlyphCapacity <= 0)
        {
            failure = "GlyphCapacityExceeded";
            return false;
        }

        var clusterMap = new ushort[text.Length];
        var textProps = new DWRITE_SHAPING_TEXT_PROPERTIES[text.Length];
        var glyphIndices = new ushort[localGlyphCapacity];
        var glyphProps = new DWRITE_SHAPING_GLYPH_PROPERTIES[localGlyphCapacity];
        var advances = new float[localGlyphCapacity];
        var offsets = new DWRITE_GLYPH_OFFSET[localGlyphCapacity];
        var isRightToLeft = (bidiLevel & 1) != 0;

        fixed (char* textPtr = text)
        fixed (ushort* clusterPtr = clusterMap)
        fixed (DWRITE_SHAPING_TEXT_PROPERTIES* textPropsPtr = textProps)
        fixed (ushort* glyphIndicesPtr = glyphIndices)
        fixed (DWRITE_SHAPING_GLYPH_PROPERTIES* glyphPropsPtr = glyphProps)
        fixed (float* advancesPtr = advances)
        fixed (DWRITE_GLYPH_OFFSET* offsetsPtr = offsets)
        {
            uint actualGlyphCount;
            try
            {
                analyzer->GetGlyphs(
                    new PCWSTR(textPtr),
                    (uint)text.Length,
                    face,
                    false,
                    isRightToLeft,
                    &scriptAnalysis,
                    default,
                    null,
                    null,
                    null,
                    0,
                    (uint)localGlyphCapacity,
                    clusterPtr,
                    textPropsPtr,
                    glyphIndicesPtr,
                    glyphPropsPtr,
                    &actualGlyphCount);
            }
            catch (COMException ex)
            {
                failure = $"GetGlyphs=0x{unchecked((uint)ex.ErrorCode):X8}";
                return false;
            }

            if (actualGlyphCount == 0 || actualGlyphCount > localGlyphCapacity)
            {
                failure = "GlyphCountInvalid";
                return false;
            }

            try
            {
                analyzer->GetGlyphPlacements(
                    new PCWSTR(textPtr),
                    clusterPtr,
                    textPropsPtr,
                    (uint)text.Length,
                    glyphIndicesPtr,
                    glyphPropsPtr,
                    actualGlyphCount,
                    face,
                    fontEmSize,
                    false,
                    isRightToLeft,
                    &scriptAnalysis,
                    default,
                    null,
                    null,
                    0,
                    advancesPtr,
                    offsetsPtr);
            }
            catch (COMException ex)
            {
                failure = $"GetGlyphPlacements=0x{unchecked((uint)ex.ErrorCode):X8}";
                return false;
            }

            for (var i = 0; i < actualGlyphCount; i++)
            {
                glyphs[glyphCount++] = new GlyphOracleGlyph(
                    glyphIndices[i],
                    advances[i],
                    offsets[i].advanceOffset,
                    offsets[i].ascenderOffset,
                    glyphProps[i].isClusterStart,
                    glyphProps[i].isDiacritic,
                    glyphProps[i].isZeroWidthSpace);
            }
        }

        return true;
    }

    private static void WriteLocale(char* locale)
    {
        locale[0] = 'e';
        locale[1] = 'n';
        locale[2] = '-';
        locale[3] = 'u';
        locale[4] = 's';
        locale[5] = '\0';
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
        if (sink->Scripts == null || scriptAnalysis == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        var end = (int)(textPosition + textLength);
        for (var i = start; i < end; i++)
        {
            sink->Scripts[i] = *scriptAnalysis;
        }

        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetLineBreakpoints(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_LINE_BREAKPOINT* lineBreakpoints)
    {
        if (sink->LineBreaks == null || lineBreakpoints == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        for (var i = 0; i < textLength; i++)
        {
            var breakpoint = lineBreakpoints[i];
            sink->LineBreaks[start + i] = new GlyphOracleLineBreak(
                breakpoint.breakConditionBefore,
                breakpoint.breakConditionAfter,
                breakpoint.isWhitespace,
                breakpoint.isSoftHyphen);
        }

        return (HRESULT)0;
    }

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

    private struct TextAnalysisSourceShim
    {
        public void** Vtbl;
        public int RefCount;
        public char* Text;
        public uint TextLength;
        public char* Locale;
        public DWRITE_READING_DIRECTION ReadingDirection;
    }

    private struct TextAnalysisSinkShim
    {
        public void** Vtbl;
        public int RefCount;
        public uint TextLength;
        public DWRITE_SCRIPT_ANALYSIS* Scripts;
        public byte* BidiLevels;
        public GlyphOracleLineBreak* LineBreaks;
    }
}

internal readonly struct GlyphOracleProbeDefinition(string Label, string Text, DWRITE_READING_DIRECTION BaseDirection)
{
    public string Label { get; } = Label;
    public string Text { get; } = Text;
    public DWRITE_READING_DIRECTION BaseDirection { get; } = BaseDirection;
}

internal readonly struct GlyphOracleLineBreak(byte Before, byte After, bool Whitespace, bool SoftHyphen) : IEquatable<GlyphOracleLineBreak>
{
    public byte Before { get; } = Before;
    public byte After { get; } = After;
    public bool Whitespace { get; } = Whitespace;
    public bool SoftHyphen { get; } = SoftHyphen;
    public bool CanBreak => Before == 1 || After == 1 || Before == 3 || After == 3;
    public bool MustBreak => Before == 3 || After == 3;

    public bool Equals(GlyphOracleLineBreak other) => Before == other.Before && After == other.After && Whitespace == other.Whitespace && SoftHyphen == other.SoftHyphen;

    public override bool Equals(object? obj) => obj is GlyphOracleLineBreak other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Before, After, Whitespace, SoftHyphen);

    public static bool operator ==(GlyphOracleLineBreak left, GlyphOracleLineBreak right) => left.Equals(right);

    public static bool operator !=(GlyphOracleLineBreak left, GlyphOracleLineBreak right) => !left.Equals(right);
}

internal readonly struct GlyphOracleGlyph(
    ushort GlyphIndex,
    float Advance,
    float AdvanceOffset,
    float AscenderOffset,
    bool ClusterStart,
    bool Diacritic,
    bool ZeroWidthSpace) : IEquatable<GlyphOracleGlyph>
{
    public ushort GlyphIndex { get; } = GlyphIndex;
    public float Advance { get; } = Advance;
    public float AdvanceOffset { get; } = AdvanceOffset;
    public float AscenderOffset { get; } = AscenderOffset;
    public bool ClusterStart { get; } = ClusterStart;
    public bool Diacritic { get; } = Diacritic;
    public bool ZeroWidthSpace { get; } = ZeroWidthSpace;

    public bool Equals(GlyphOracleGlyph other) => GlyphIndex == other.GlyphIndex && Advance.Equals(other.Advance) && AdvanceOffset.Equals(other.AdvanceOffset) && AscenderOffset.Equals(other.AscenderOffset) && ClusterStart == other.ClusterStart && Diacritic == other.Diacritic && ZeroWidthSpace == other.ZeroWidthSpace;

    public override bool Equals(object? obj) => obj is GlyphOracleGlyph other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GlyphIndex, Advance, AdvanceOffset, AscenderOffset, ClusterStart, Diacritic, ZeroWidthSpace);

    public static bool operator ==(GlyphOracleGlyph left, GlyphOracleGlyph right) => left.Equals(right);

    public static bool operator !=(GlyphOracleGlyph left, GlyphOracleGlyph right) => !left.Equals(right);
}

internal readonly struct GlyphOracleSegment(
    int TextStart,
    int TextLength,
    int GlyphStart,
    int GlyphCount,
    ushort Script,
    byte BidiLevel,
    bool FallbackFont,
    float FontEmSize) : IEquatable<GlyphOracleSegment>
{
    public int TextStart { get; } = TextStart;
    public int TextLength { get; } = TextLength;
    public int TextEnd => TextStart + TextLength;
    public int GlyphStart { get; } = GlyphStart;
    public int GlyphCount { get; } = GlyphCount;
    public int GlyphEnd => GlyphStart + GlyphCount;
    public ushort Script { get; } = Script;
    public byte BidiLevel { get; } = BidiLevel;
    public bool FallbackFont { get; } = FallbackFont;
    public float FontEmSize { get; } = FontEmSize;

    public bool Equals(GlyphOracleSegment other) => TextStart == other.TextStart && TextLength == other.TextLength && GlyphStart == other.GlyphStart && GlyphCount == other.GlyphCount && Script == other.Script && BidiLevel == other.BidiLevel && FallbackFont == other.FallbackFont && FontEmSize.Equals(other.FontEmSize);

    public override bool Equals(object? obj) => obj is GlyphOracleSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TextStart, TextLength, GlyphStart, GlyphCount, Script, BidiLevel, FallbackFont, FontEmSize);

    public static bool operator ==(GlyphOracleSegment left, GlyphOracleSegment right) => left.Equals(right);

    public static bool operator !=(GlyphOracleSegment left, GlyphOracleSegment right) => !left.Equals(right);
}

internal readonly struct GlyphOracleProbeResult(
    string Label,
    int TextLength,
    DWRITE_READING_DIRECTION BaseDirection,
    string Failure,
    byte[] BidiLevels,
    GlyphOracleLineBreak[] LineBreaks,
    GlyphOracleSegment[] Segments,
    GlyphOracleGlyph[] Glyphs) : IEquatable<GlyphOracleProbeResult>
{
    public string Label { get; } = Label;
    public int TextLength { get; } = TextLength;
    public DWRITE_READING_DIRECTION BaseDirection { get; } = BaseDirection;
    public string Failure { get; } = Failure;
    public IReadOnlyList<byte> BidiLevels { get; } = BidiLevels;
    public IReadOnlyList<GlyphOracleLineBreak> LineBreaks { get; } = LineBreaks;
    public IReadOnlyList<GlyphOracleSegment> Segments { get; } = Segments;
    public IReadOnlyList<GlyphOracleGlyph> Glyphs { get; } = Glyphs;
    public bool Succeeded => string.IsNullOrEmpty(Failure);
    public int GlyphCount => Glyphs.Count;
    public bool HasFallbackFont => HasFallback(Segments);
    public bool HasMixedBidiLevels => CountDistinctLevels(BidiLevels) > 1;
    public bool HasLineBreakOpportunity => HasBreak(LineBreaks);

    public static GlyphOracleProbeResult Failed(string label, int textLength, DWRITE_READING_DIRECTION baseDirection, string failure) =>
        new(label, textLength, baseDirection, failure, [], [], [], []);

    public static GlyphOracleProbeResult Create(string label, int textLength, DWRITE_READING_DIRECTION baseDirection, byte[] bidiLevels, GlyphOracleLineBreak[] lineBreaks, GlyphOracleSegment[] segments, GlyphOracleGlyph[] glyphs) =>
        new(label, textLength, baseDirection, "", bidiLevels, lineBreaks, segments, glyphs);

    public bool Equals(GlyphOracleProbeResult other)
    {
        return Label == other.Label
            && TextLength == other.TextLength
            && BaseDirection == other.BaseDirection
            && Failure == other.Failure
            && ReferenceEquals(BidiLevels, other.BidiLevels)
            && ReferenceEquals(LineBreaks, other.LineBreaks)
            && ReferenceEquals(Segments, other.Segments)
            && ReferenceEquals(Glyphs, other.Glyphs);
    }

    public override bool Equals(object? obj) => obj is GlyphOracleProbeResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Label, TextLength, BaseDirection, Failure, BidiLevels.Count, LineBreaks.Count, Segments.Count, Glyphs.Count);

    public static bool operator ==(GlyphOracleProbeResult left, GlyphOracleProbeResult right) => left.Equals(right);

    public static bool operator !=(GlyphOracleProbeResult left, GlyphOracleProbeResult right) => !left.Equals(right);

    private static bool HasFallback(IReadOnlyList<GlyphOracleSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].FallbackFont)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBreak(IReadOnlyList<GlyphOracleLineBreak> lineBreaks)
    {
        for (var i = 0; i < lineBreaks.Count; i++)
        {
            if (lineBreaks[i].CanBreak || lineBreaks[i].Whitespace)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountDistinctLevels(IReadOnlyList<byte> levels)
    {
        var mask = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            mask |= levels[i] < 31 ? 1 << levels[i] : 0;
        }

        return BitOperations.PopCount((uint)mask);
    }
}

internal readonly struct GlyphOracleDiagnosticSnapshot(
    bool FactoryAvailable,
    bool AnalyzerAvailable,
    bool FontFallbackAvailable,
    string Failure,
    GlyphOracleProbeResult[] Results,
    int FailedProbes,
    int FallbackFontProbes,
    int MixedBidiProbes,
    int LineBreakProbes,
    int TotalGlyphs) : IEquatable<GlyphOracleDiagnosticSnapshot>
{
    public bool FactoryAvailable { get; } = FactoryAvailable;
    public bool AnalyzerAvailable { get; } = AnalyzerAvailable;
    public bool FontFallbackAvailable { get; } = FontFallbackAvailable;
    public string Failure { get; } = Failure;
    public IReadOnlyList<GlyphOracleProbeResult> Results { get; } = Results;
    public int ProbeCount => Results.Count;
    public int FailedProbes { get; } = FailedProbes;
    public int FallbackFontProbes { get; } = FallbackFontProbes;
    public int MixedBidiProbes { get; } = MixedBidiProbes;
    public int LineBreakProbes { get; } = LineBreakProbes;
    public int TotalGlyphs { get; } = TotalGlyphs;

    public static GlyphOracleDiagnosticSnapshot Failed(string failure) =>
        new(FactoryAvailable: false, AnalyzerAvailable: false, FontFallbackAvailable: false, failure, [], FailedProbes: 0, FallbackFontProbes: 0, MixedBidiProbes: 0, LineBreakProbes: 0, TotalGlyphs: 0);

    public static GlyphOracleDiagnosticSnapshot Create(bool factoryAvailable, bool analyzerAvailable, bool fontFallbackAvailable, GlyphOracleProbeResult[] results)
    {
        var failedProbes = 0;
        var fallbackFontProbes = 0;
        var mixedBidiProbes = 0;
        var lineBreakProbes = 0;
        var totalGlyphs = 0;
        foreach (ref readonly var result in results.AsSpan())
        {
            if (!result.Succeeded) failedProbes++;
            if (result.HasFallbackFont) fallbackFontProbes++;
            if (result.HasMixedBidiLevels) mixedBidiProbes++;
            if (result.HasLineBreakOpportunity) lineBreakProbes++;
            totalGlyphs += result.GlyphCount;
        }

        return new GlyphOracleDiagnosticSnapshot(factoryAvailable, analyzerAvailable, fontFallbackAvailable, "", results, failedProbes, fallbackFontProbes, mixedBidiProbes, lineBreakProbes, totalGlyphs);
    }

    public bool Equals(GlyphOracleDiagnosticSnapshot other)
    {
        return FactoryAvailable == other.FactoryAvailable
            && AnalyzerAvailable == other.AnalyzerAvailable
            && FontFallbackAvailable == other.FontFallbackAvailable
            && Failure == other.Failure
            && ReferenceEquals(Results, other.Results)
            && FailedProbes == other.FailedProbes
            && FallbackFontProbes == other.FallbackFontProbes
            && MixedBidiProbes == other.MixedBidiProbes
            && LineBreakProbes == other.LineBreakProbes
            && TotalGlyphs == other.TotalGlyphs;
    }

    public override bool Equals(object? obj) => obj is GlyphOracleDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FactoryAvailable);
        hash.Add(AnalyzerAvailable);
        hash.Add(FontFallbackAvailable);
        hash.Add(Failure);
        hash.Add(Results.Count);
        hash.Add(FailedProbes);
        hash.Add(FallbackFontProbes);
        hash.Add(MixedBidiProbes);
        hash.Add(LineBreakProbes);
        hash.Add(TotalGlyphs);
        return hash.ToHashCode();
    }

    public static bool operator ==(GlyphOracleDiagnosticSnapshot left, GlyphOracleDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(GlyphOracleDiagnosticSnapshot left, GlyphOracleDiagnosticSnapshot right) => !left.Equals(right);
}
#endif
