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

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
    }

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
        public DWRITE_SCRIPT_ANALYSIS* ScriptAnalysis;
        public byte* BidiLevels;
    }

    public readonly struct GlyphAtlasRecordResult(bool Recorded, int AtlasRuns, int DegradedRuns) : IEquatable<GlyphAtlasRecordResult>
    {
        public bool Recorded { get; } = Recorded;
        public int AtlasRuns { get; } = AtlasRuns;
        public int DegradedRuns { get; } = DegradedRuns;

        public static GlyphAtlasRecordResult Empty => new(true, 0, 0);

        public static GlyphAtlasRecordResult DegradedOnly(int degradedRuns) => new(false, 0, degradedRuns);

        public bool Equals(GlyphAtlasRecordResult other)
        {
            return Recorded == other.Recorded
                && AtlasRuns == other.AtlasRuns
                && DegradedRuns == other.DegradedRuns;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasRecordResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Recorded, AtlasRuns, DegradedRuns);

        public static bool operator ==(GlyphAtlasRecordResult left, GlyphAtlasRecordResult right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasRecordResult left, GlyphAtlasRecordResult right) => !left.Equals(right);
    }

    private readonly struct GlyphFrame(
        int VertexCount,
        int BatchCount,
        int AtlasRunCount,
        int DegradedRunCount,
        GlyphAtlasFallbackReasonCounts DegradationReasons,
        int ColorLayerRunCount,
        int ColorBitmapRunCount)
    {
        public int VertexCount { get; } = VertexCount;
        public int BatchCount { get; } = BatchCount;
        public int AtlasRunCount { get; } = AtlasRunCount;
        public int DegradedRunCount { get; } = DegradedRunCount;
        public GlyphAtlasFallbackReasonCounts DegradationReasons { get; } = DegradationReasons;
        public int ColorLayerRunCount { get; } = ColorLayerRunCount;
        public int ColorBitmapRunCount { get; } = ColorBitmapRunCount;
    }

    private enum GlyphAtlasPageFormat : byte
    {
        Alpha,
        Bgra
    }

    private readonly struct GlyphDrawBatch(int StartVertex, int VertexCount, IntegerScissorRect Scissor, GlyphAtlasPageHandle Page)
    {
        public int StartVertex { get; } = StartVertex;
        public int VertexCount { get; } = VertexCount;
        public IntegerScissorRect Scissor { get; } = Scissor;
        public GlyphAtlasPageHandle Page { get; } = Page;
    }

    public enum GlyphAtlasInitializationPhase : byte
    {
        None,
        DirectWriteFactory,
        FontCollection,
        TextAnalyzer,
        FontFallback,
        RootSignature,
        ShaderCompile,
        PSO,
        AtlasTexture,
        UploadBuffer,
        DescriptorHeap,
        ShaderResourceView,
        VertexBuffer
    }

    public enum GlyphAtlasRecordFailurePhase : byte
    {
        None,
        Record,
        DirectWrite,
        VertexBufferMap,
        AtlasUploadMap,
        CommandList,
        AtlasPage,
        Pipeline,
        AtlasDraw
    }

    public sealed class GlyphAtlasInitializationException(
        GlyphAtlasInitializationPhase phase,
        Exception innerException) : InvalidOperationException($"Glyph atlas initialization failed during {phase}.", innerException)
    {
        public GlyphAtlasInitializationPhase Phase { get; } = phase;
    }

    private sealed class GlyphAtlasRecordException(
        GlyphAtlasRecordFailurePhase phase,
        Exception innerException) : InvalidOperationException($"Glyph atlas record failed during {phase}: {innerException.Message}", innerException)
    {
        public GlyphAtlasRecordFailurePhase Phase { get; } = phase;
    }

    [Flags]
    public enum GlyphAtlasFallbackReason
    {
        None = 0,
        NonAscii = 1 << 0,
        Clip = 1 << 1,
        Wrapping = 1 << 2,
        Alignment = 1 << 3,
        AtlasFull = 1 << 4,
        VertexLimit = 1 << 5,
        FontMissing = 1 << 6,
        CompileFailed = 1 << 7,
        BatchLimit = 1 << 8,
        InitializationFailed = 1 << 9,
        RecordFailed = 1 << 10,
        ColorGlyph = 1 << 11,
        ComplexScript = 1 << 12,
        ColorGlyphSvg = 1 << 13,
        ColorGlyphPng = 1 << 14,
        ColorGlyphJpeg = 1 << 15,
        ColorGlyphTiff = 1 << 16,
        ColorGlyphPremultipliedBgra = 1 << 17,
        ColorGlyphPaintTree = 1 << 18
    }

    public readonly struct GlyphAtlasFallbackReasonCounts(
        int NonAscii,
        int ColorGlyph,
        int ComplexScript,
        int ColorGlyphSvg,
        int ColorGlyphPng,
        int ColorGlyphJpeg,
        int ColorGlyphTiff,
        int ColorGlyphPremultipliedBgra,
        int ColorGlyphPaintTree,
        int Clip,
        int Wrapping,
        int Alignment,
        int AtlasFull,
        int VertexLimit,
        int FontMissing,
        int CompileFailed,
        int BatchLimit,
        int InitializationFailed,
        int RecordFailed) : IEquatable<GlyphAtlasFallbackReasonCounts>
    {
        public int NonAscii { get; } = NonAscii;
        public int ColorGlyph { get; } = ColorGlyph;
        public int ComplexScript { get; } = ComplexScript;
        public int ColorGlyphSvg { get; } = ColorGlyphSvg;
        public int ColorGlyphPng { get; } = ColorGlyphPng;
        public int ColorGlyphJpeg { get; } = ColorGlyphJpeg;
        public int ColorGlyphTiff { get; } = ColorGlyphTiff;
        public int ColorGlyphPremultipliedBgra { get; } = ColorGlyphPremultipliedBgra;
        public int ColorGlyphPaintTree { get; } = ColorGlyphPaintTree;
        public int Clip { get; } = Clip;
        public int Wrapping { get; } = Wrapping;
        public int Alignment { get; } = Alignment;
        public int AtlasFull { get; } = AtlasFull;
        public int VertexLimit { get; } = VertexLimit;
        public int FontMissing { get; } = FontMissing;
        public int CompileFailed { get; } = CompileFailed;
        public int BatchLimit { get; } = BatchLimit;
        public int InitializationFailed { get; } = InitializationFailed;
        public int RecordFailed { get; } = RecordFailed;

        public GlyphAtlasFallbackReasonCounts With(GlyphAtlasFallbackReason reason)
        {
            return new GlyphAtlasFallbackReasonCounts(
                NonAscii + (HasReason(reason, GlyphAtlasFallbackReason.NonAscii) ? 1 : 0),
                ColorGlyph + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyph) ? 1 : 0),
                ComplexScript + (HasReason(reason, GlyphAtlasFallbackReason.ComplexScript) ? 1 : 0),
                ColorGlyphSvg + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphSvg) ? 1 : 0),
                ColorGlyphPng + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPng) ? 1 : 0),
                ColorGlyphJpeg + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphJpeg) ? 1 : 0),
                ColorGlyphTiff + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphTiff) ? 1 : 0),
                ColorGlyphPremultipliedBgra + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra) ? 1 : 0),
                ColorGlyphPaintTree + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPaintTree) ? 1 : 0),
                Clip + (HasReason(reason, GlyphAtlasFallbackReason.Clip) ? 1 : 0),
                Wrapping + (HasReason(reason, GlyphAtlasFallbackReason.Wrapping) ? 1 : 0),
                Alignment + (HasReason(reason, GlyphAtlasFallbackReason.Alignment) ? 1 : 0),
                AtlasFull + (HasReason(reason, GlyphAtlasFallbackReason.AtlasFull) ? 1 : 0),
                VertexLimit + (HasReason(reason, GlyphAtlasFallbackReason.VertexLimit) ? 1 : 0),
                FontMissing + (HasReason(reason, GlyphAtlasFallbackReason.FontMissing) ? 1 : 0),
                CompileFailed + (HasReason(reason, GlyphAtlasFallbackReason.CompileFailed) ? 1 : 0),
                BatchLimit + (HasReason(reason, GlyphAtlasFallbackReason.BatchLimit) ? 1 : 0),
                InitializationFailed + (HasReason(reason, GlyphAtlasFallbackReason.InitializationFailed) ? 1 : 0),
                RecordFailed + (HasReason(reason, GlyphAtlasFallbackReason.RecordFailed) ? 1 : 0));
        }

        private static bool HasReason(GlyphAtlasFallbackReason reason, GlyphAtlasFallbackReason flag) => (reason & flag) != 0;

        public GlyphAtlasFallbackReasonCounts Add(GlyphAtlasFallbackReasonCounts other)
        {
            return new GlyphAtlasFallbackReasonCounts(
                NonAscii + other.NonAscii,
                ColorGlyph + other.ColorGlyph,
                ComplexScript + other.ComplexScript,
                ColorGlyphSvg + other.ColorGlyphSvg,
                ColorGlyphPng + other.ColorGlyphPng,
                ColorGlyphJpeg + other.ColorGlyphJpeg,
                ColorGlyphTiff + other.ColorGlyphTiff,
                ColorGlyphPremultipliedBgra + other.ColorGlyphPremultipliedBgra,
                ColorGlyphPaintTree + other.ColorGlyphPaintTree,
                Clip + other.Clip,
                Wrapping + other.Wrapping,
                Alignment + other.Alignment,
                AtlasFull + other.AtlasFull,
                VertexLimit + other.VertexLimit,
                FontMissing + other.FontMissing,
                CompileFailed + other.CompileFailed,
                BatchLimit + other.BatchLimit,
                InitializationFailed + other.InitializationFailed,
                RecordFailed + other.RecordFailed);
        }

        public bool Equals(GlyphAtlasFallbackReasonCounts other)
        {
            return NonAscii == other.NonAscii
                && ColorGlyph == other.ColorGlyph
                && ComplexScript == other.ComplexScript
                && ColorGlyphSvg == other.ColorGlyphSvg
                && ColorGlyphPng == other.ColorGlyphPng
                && ColorGlyphJpeg == other.ColorGlyphJpeg
                && ColorGlyphTiff == other.ColorGlyphTiff
                && ColorGlyphPremultipliedBgra == other.ColorGlyphPremultipliedBgra
                && ColorGlyphPaintTree == other.ColorGlyphPaintTree
                && Clip == other.Clip
                && Wrapping == other.Wrapping
                && Alignment == other.Alignment
                && AtlasFull == other.AtlasFull
                && VertexLimit == other.VertexLimit
                && FontMissing == other.FontMissing
                && CompileFailed == other.CompileFailed
                && BatchLimit == other.BatchLimit
                && InitializationFailed == other.InitializationFailed
                && RecordFailed == other.RecordFailed;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasFallbackReasonCounts other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(NonAscii);
            hash.Add(ColorGlyph);
            hash.Add(ComplexScript);
            hash.Add(ColorGlyphSvg);
            hash.Add(ColorGlyphPng);
            hash.Add(ColorGlyphJpeg);
            hash.Add(ColorGlyphTiff);
            hash.Add(ColorGlyphPremultipliedBgra);
            hash.Add(ColorGlyphPaintTree);
            hash.Add(Clip);
            hash.Add(Wrapping);
            hash.Add(Alignment);
            hash.Add(AtlasFull);
            hash.Add(VertexLimit);
            hash.Add(FontMissing);
            hash.Add(CompileFailed);
            hash.Add(BatchLimit);
            hash.Add(InitializationFailed);
            hash.Add(RecordFailed);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return $"NonAscii={NonAscii}, ColorGlyph={ColorGlyph}, ComplexScript={ComplexScript}, ColorGlyphSvg={ColorGlyphSvg}, ColorGlyphPng={ColorGlyphPng}, ColorGlyphJpeg={ColorGlyphJpeg}, ColorGlyphTiff={ColorGlyphTiff}, ColorGlyphPremultipliedBgra={ColorGlyphPremultipliedBgra}, ColorGlyphPaintTree={ColorGlyphPaintTree}, Clip={Clip}, Wrapping={Wrapping}, Alignment={Alignment}, AtlasFull={AtlasFull}, VertexLimit={VertexLimit}, "
                + $"FontMissing={FontMissing}, CompileFailed={CompileFailed}, BatchLimit={BatchLimit}, InitializationFailed={InitializationFailed}, RecordFailed={RecordFailed}";
        }
    }

    private readonly struct FontFaceKey(TextFontFamily Family, TextFontWeight Weight, TextFontStyle Style, TextFontStretch Stretch, float EmSize) : IEquatable<FontFaceKey>
    {
        public TextFontFamily Family { get; } = Family;
        public TextFontWeight Weight { get; } = Weight;
        public TextFontStyle Style { get; } = Style;
        public TextFontStretch Stretch { get; } = Stretch;
        public float EmSize { get; } = EmSize;

        public bool Equals(FontFaceKey other)
        {
            return Family == other.Family
                && Weight == other.Weight
                && Style == other.Style
                && Stretch == other.Stretch
                && EmSize.Equals(other.EmSize);
        }

        public override bool Equals(object? obj) => obj is FontFaceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Family, Weight, Style, Stretch, EmSize);
    }

    private readonly struct FontFaceIdentity(int Value) : IEquatable<FontFaceIdentity>
    {
        public int Value { get; } = Value;

        public bool Equals(FontFaceIdentity other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is FontFaceIdentity other && Equals(other);

        public override int GetHashCode() => Value;
    }

    private sealed class CachedFontFace(FontFaceIdentity identity, IDWriteFontFace* face, DWRITE_FONT_METRICS metrics, IDWriteFontFace4* face4 = null, global::Windows.Win32.System.Com.IUnknown* fontIdentity = null)
    {
        public FontFaceIdentity Identity { get; } = identity;
        public IDWriteFontFace* Face { get; } = face;
        public IDWriteFontFace4* Face4 { get; } = face4;
        public DWRITE_FONT_METRICS Metrics { get; } = metrics;
        private global::Windows.Win32.System.Com.IUnknown* FontIdentity { get; } = fontIdentity;

        public void Release()
        {
            if (Face != null)
            {
                Face->Release();
            }

            if (Face4 != null)
            {
                Face4->Release();
            }

            if (FontIdentity != null)
            {
                FontIdentity->Release();
            }
        }
    }

    private readonly struct ShapedGlyph(
        ushort GlyphIndex,
        float Advance,
        float AdvanceOffset,
        float AscenderOffset,
        bool IsClusterStart,
        bool IsDiacritic,
        bool IsZeroWidthSpace)
    {
        public ushort GlyphIndex { get; } = GlyphIndex;
        public float Advance { get; } = Advance;
        public float AdvanceOffset { get; } = AdvanceOffset;
        public float AscenderOffset { get; } = AscenderOffset;
        public bool IsClusterStart { get; } = IsClusterStart;
        public bool IsDiacritic { get; } = IsDiacritic;
        public bool IsZeroWidthSpace { get; } = IsZeroWidthSpace;

        public static ShapedGlyph Simple(ushort glyphIndex, float advance) => new(glyphIndex, advance, 0, 0, IsClusterStart: true, IsDiacritic: false, IsZeroWidthSpace: false);

        public static ShapedGlyph FromDirectWrite(
            ushort glyphIndex,
            float advance,
            DWRITE_GLYPH_OFFSET offset,
            DWRITE_SHAPING_GLYPH_PROPERTIES properties) =>
            new(
                glyphIndex,
                advance,
                offset.advanceOffset,
                offset.ascenderOffset,
                properties.isClusterStart,
                properties.isDiacritic,
                properties.isZeroWidthSpace);
    }

    private readonly struct ShapedGlyphSegment(
        CachedFontFace FontFace,
        float FontEmSize,
        int TextStart,
        int TextLength,
        int GlyphStart,
        int GlyphCount,
        float ControlAdvance,
        byte BidiLevel)
    {
        public CachedFontFace FontFace { get; } = FontFace;
        public float FontEmSize { get; } = FontEmSize;
        public int TextStart { get; } = TextStart;
        public int TextLength { get; } = TextLength;
        public int TextEnd => TextStart + TextLength;
        public int GlyphStart { get; } = GlyphStart;
        public int GlyphCount { get; } = GlyphCount;
        public float ControlAdvance { get; } = ControlAdvance;
        public byte BidiLevel { get; } = BidiLevel;
        public bool IsRightToLeft => (BidiLevel & 1) != 0;

        public float ComputeLineHeight() => D3D12GlyphAtlasTextRenderer.ComputeLineHeight(FontFace.Metrics, FontEmSize);

        public float ComputeAscent()
        {
            var scale = FontEmSize / FontFace.Metrics.designUnitsPerEm;
            return FontFace.Metrics.ascent * scale;
        }
    }

    private readonly struct ShapedGlyphLine(int SegmentStart, int SegmentCount, int GlyphStart, int GlyphCount, float Width, byte BidiLevel)
    {
        public int SegmentStart { get; } = SegmentStart;
        public int SegmentCount { get; } = SegmentCount;
        public int GlyphStart { get; } = GlyphStart;
        public int GlyphCount { get; } = GlyphCount;
        public float Width { get; } = Width;
        public byte BidiLevel { get; } = BidiLevel;
        public bool IsRightToLeft => (BidiLevel & 1) != 0;
    }

    private readonly ref struct ShapedGlyphRun(
        ReadOnlySpan<ShapedGlyph> Glyphs,
        ReadOnlySpan<ShapedGlyphSegment> Segments,
        ReadOnlySpan<ShapedGlyphLine> Lines,
        ReadOnlySpan<ushort> ClusterMap,
        int TextLength,
        bool RequiresColorGlyph)
    {
        public ReadOnlySpan<ShapedGlyph> Glyphs { get; } = Glyphs;
        public ReadOnlySpan<ShapedGlyphSegment> Segments { get; } = Segments;
        public ReadOnlySpan<ShapedGlyphLine> Lines { get; } = Lines;
        public ReadOnlySpan<ushort> ClusterMap { get; } = ClusterMap;
        public int TextLength { get; } = TextLength;
        public bool RequiresColorGlyph { get; } = RequiresColorGlyph;
        public int GlyphCount => Glyphs.Length;
        public int LineCount => Lines.Length;

        public float ComputeAdvance()
        {
            var width = 0f;
            foreach (ref readonly var line in Lines)
            {
                width = Math.Max(width, line.Width);
            }

            return width;
        }

        public float ComputeLineHeight()
        {
            var lineHeight = 0f;
            foreach (ref readonly var segment in Segments)
            {
                lineHeight = Math.Max(lineHeight, segment.ComputeLineHeight());
            }

            return lineHeight;
        }

        public float ComputeAscent()
        {
            var ascent = 0f;
            foreach (ref readonly var segment in Segments)
            {
                ascent = Math.Max(ascent, segment.ComputeAscent());
            }

            return ascent;
        }

        public bool HasMissingGlyph()
        {
            foreach (ref readonly var glyph in Glyphs)
            {
                if (glyph.GlyphIndex == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct GlyphAtom(byte Kind, uint CodePoint, ushort GlyphIndex, byte Flags) : IEquatable<GlyphAtom>
    {
        private const byte SimpleCodePointKind = 1;
        private const byte ShapedPlacementKind = 2;
        private const byte ColorLayerKind = 3;
        private const byte BgraGlyphKind = 4;
        private const byte EncodedBitmapGlyphKind = 5;
        private const byte DiacriticFlag = 1 << 0;
        private const byte ZeroWidthSpaceFlag = 1 << 1;

        public byte Kind { get; } = Kind;
        public uint CodePoint { get; } = CodePoint;
        public ushort GlyphIndex { get; } = GlyphIndex;
        public byte Flags { get; } = Flags;

        public static GlyphAtom SimpleCodePoint(uint codePoint, ushort glyphIndex) => new(SimpleCodePointKind, codePoint, glyphIndex, Flags: 0);

        public static GlyphAtom ShapedPlacement(ushort glyphIndex, bool isDiacritic, bool isZeroWidthSpace)
        {
            var flags = (byte)((isDiacritic ? DiacriticFlag : 0) | (isZeroWidthSpace ? ZeroWidthSpaceFlag : 0));
            return new GlyphAtom(ShapedPlacementKind, 0, glyphIndex, flags);
        }

        public static GlyphAtom ColorLayer(ushort glyphIndex) => new(ColorLayerKind, 0, glyphIndex, Flags: 0);

        public static GlyphAtom BgraGlyph(ushort glyphIndex, uint pixelsPerEm) => new(BgraGlyphKind, pixelsPerEm, glyphIndex, Flags: 0);

        public static GlyphAtom EncodedBitmapGlyph(ushort glyphIndex, uint pixelsPerEm, byte formatId) => new(EncodedBitmapGlyphKind, pixelsPerEm, glyphIndex, formatId);

        public bool Equals(GlyphAtom other) => Kind == other.Kind && CodePoint == other.CodePoint && GlyphIndex == other.GlyphIndex && Flags == other.Flags;

        public override bool Equals(object? obj) => obj is GlyphAtom other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, CodePoint, GlyphIndex, Flags);
    }

    private readonly struct GlyphKey(FontFaceIdentity FontFace, float EmSize, GlyphAtom Glyph) : IEquatable<GlyphKey>
    {
        public FontFaceIdentity FontFace { get; } = FontFace;
        public float EmSize { get; } = EmSize;
        public GlyphAtom Glyph { get; } = Glyph;

        public bool Equals(GlyphKey other) => FontFace.Equals(other.FontFace) && EmSize.Equals(other.EmSize) && Glyph.Equals(other.Glyph);

        public override bool Equals(object? obj) => obj is GlyphKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FontFace, EmSize, Glyph);
    }

    private readonly struct GlyphAtlasPageHandle(int Index, int Generation) : IEquatable<GlyphAtlasPageHandle>
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public bool IsNone => Generation == 0;

        public GlyphAtlasPageHandle NextGeneration() => new(Index, checked(Generation + 1));

        public bool Equals(GlyphAtlasPageHandle other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is GlyphAtlasPageHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        public static bool operator ==(GlyphAtlasPageHandle left, GlyphAtlasPageHandle right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasPageHandle left, GlyphAtlasPageHandle right) => !left.Equals(right);
    }

    private readonly struct GlyphAtlasPageUsage(
        int UsedPixels,
        int FragmentedPixels,
        int AlphaPageCount,
        int BgraPageCount,
        int AlphaUsedPixels,
        int BgraUsedPixels,
        int AlphaFragmentedPixels,
        int BgraFragmentedPixels,
        long OldestPageAge,
        long NewestPageAge,
        long OldestAlphaPageAge,
        long OldestBgraPageAge)
    {
        public int UsedPixels { get; } = UsedPixels;
        public int FragmentedPixels { get; } = FragmentedPixels;
        public int AlphaPageCount { get; } = AlphaPageCount;
        public int BgraPageCount { get; } = BgraPageCount;
        public int AlphaUsedPixels { get; } = AlphaUsedPixels;
        public int BgraUsedPixels { get; } = BgraUsedPixels;
        public int AlphaFragmentedPixels { get; } = AlphaFragmentedPixels;
        public int BgraFragmentedPixels { get; } = BgraFragmentedPixels;
        public long OldestPageAge { get; } = OldestPageAge;
        public long NewestPageAge { get; } = NewestPageAge;
        public long OldestAlphaPageAge { get; } = OldestAlphaPageAge;
        public long OldestBgraPageAge { get; } = OldestBgraPageAge;
    }

    private readonly struct GlyphAtlasPageMutationState(
        GlyphAtlasPageHandle Handle,
        int NextX,
        int NextY,
        int RowHeight,
        bool IsDirty,
        int DirtyLeft,
        int DirtyTop,
        int DirtyRight,
        int DirtyBottom,
        int UsedPixels,
        int AllocatedPixels,
        long LastUsedSerial)
    {
        public GlyphAtlasPageHandle Handle { get; } = Handle;
        public int NextX { get; } = NextX;
        public int NextY { get; } = NextY;
        public int RowHeight { get; } = RowHeight;
        public bool IsDirty { get; } = IsDirty;
        public int DirtyLeft { get; } = DirtyLeft;
        public int DirtyTop { get; } = DirtyTop;
        public int DirtyRight { get; } = DirtyRight;
        public int DirtyBottom { get; } = DirtyBottom;
        public int UsedPixels { get; } = UsedPixels;
        public int AllocatedPixels { get; } = AllocatedPixels;
        public long LastUsedSerial { get; } = LastUsedSerial;
    }

    private readonly struct GlyphEntryMutationState(int Index, int Generation, long LastUsedSerial)
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public long LastUsedSerial { get; } = LastUsedSerial;
    }

    private readonly struct GlyphAtlasPageReuseRequest(GlyphAtlasPageHandle Page, long RequestedRecordSerial)
    {
        public GlyphAtlasPageHandle Page { get; } = Page;
        public long RequestedRecordSerial { get; } = RequestedRecordSerial;
        public bool IsNone => Page.IsNone;

        public bool CanApply(long recordSerial, long oldestRetainedRecordSerial) =>
            GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(!IsNone, RequestedRecordSerial, recordSerial, oldestRetainedRecordSerial);
    }

    private sealed unsafe class GlyphAtlasPage(
        GlyphAtlasPageHandle handle,
        GlyphAtlasPageFormat format,
        ID3D12Resource* texture,
        ID3D12Resource*[] uploads,
        ID3D12DescriptorHeap* srvHeap,
        D3D12_RESOURCE_STATES textureState,
        byte[] pixels)
    {
        private GlyphAtlasPageHandle _handle = handle;
        private D3D12_RESOURCE_STATES _textureState = textureState;
        private bool _released;

        public GlyphAtlasPageHandle Handle => _handle;
        public int Generation => Handle.Generation;
        public GlyphAtlasPageFormat Format { get; } = format;
        public int BytesPerPixel => GetAtlasBytesPerPixel(Format);
        public int RowPitch => GetAtlasRowPitch(Format);
        public DXGI_FORMAT DxgiFormat => GetDxgiFormat(Format);
        public ID3D12Resource* Texture { get; private set; } = texture;
        public ID3D12Resource*[] Uploads { get; } = uploads;
        public ID3D12DescriptorHeap* SrvHeap { get; private set; } = srvHeap;
        public byte[] Pixels { get; } = pixels;
        public int NextX { get; set; } = AtlasPadding;
        public int NextY { get; set; } = AtlasPadding;
        public int RowHeight { get; set; }
        public bool IsDirty { get; set; }
        public int DirtyLeft { get; set; } = AtlasWidth;
        public int DirtyTop { get; set; } = AtlasHeight;
        public int DirtyRight { get; set; }
        public int DirtyBottom { get; set; }
        public int UsedPixels { get; set; }
        public int AllocatedPixels { get; set; }
        public long LastUsedSerial { get; private set; }

        public D3D12_RESOURCE_BARRIER TransitionTexture(D3D12_RESOURCE_STATES after)
        {
            var barrier = Transition(Texture, _textureState, after);
            _textureState = after;
            return barrier;
        }

        public void Touch(long serial)
        {
            LastUsedSerial = Math.Max(LastUsedSerial, serial);
        }

        public GlyphAtlasPageMutationState CaptureMutationState() =>
            new(Handle, NextX, NextY, RowHeight, IsDirty, DirtyLeft, DirtyTop, DirtyRight, DirtyBottom, UsedPixels, AllocatedPixels, LastUsedSerial);

        public void RestoreMutationState(GlyphAtlasPageMutationState state)
        {
            NextX = state.NextX;
            NextY = state.NextY;
            RowHeight = state.RowHeight;
            IsDirty = state.IsDirty;
            DirtyLeft = state.DirtyLeft;
            DirtyTop = state.DirtyTop;
            DirtyRight = state.DirtyRight;
            DirtyBottom = state.DirtyBottom;
            UsedPixels = state.UsedPixels;
            AllocatedPixels = state.AllocatedPixels;
            LastUsedSerial = state.LastUsedSerial;
        }

        public GlyphAtlasPageHandle ResetForReuse()
        {
            _handle = _handle.NextGeneration();
            Array.Clear(Pixels);
            var resetState = GlyphAtlasTextCompositionHelpers.CreatePageReuseResetState(AtlasWidth, AtlasHeight, AtlasPadding);
            NextX = resetState.NextX;
            NextY = resetState.NextY;
            RowHeight = resetState.RowHeight;
            IsDirty = resetState.IsDirty;
            DirtyLeft = resetState.DirtyLeft;
            DirtyTop = resetState.DirtyTop;
            DirtyRight = resetState.DirtyRight;
            DirtyBottom = resetState.DirtyBottom;
            UsedPixels = resetState.UsedPixels;
            AllocatedPixels = resetState.AllocatedPixels;
            LastUsedSerial = resetState.LastUsedSerial;
            return _handle;
        }

        public int ComputeAllocatedPixels()
        {
            if (NextY <= AtlasPadding && NextX <= AtlasPadding && RowHeight == 0)
            {
                return 0;
            }

            var committedRows = Math.Max(0, NextY - AtlasPadding);
            var currentRowWidth = Math.Max(0, NextX - AtlasPadding);
            return checked((committedRows * AtlasWidth) + (currentRowWidth * RowHeight));
        }

        public int ComputeAvailablePixels()
        {
            var rowAvailable = Math.Max(0, AtlasWidth - AtlasPadding - NextX) * Math.Max(0, RowHeight);
            var rowsAvailable = Math.Max(0, AtlasHeight - AtlasPadding - NextY - RowHeight) * AtlasWidth;
            return checked(rowAvailable + rowsAvailable);
        }

        public void Release()
        {
            if (_released)
            {
                return;
            }

            for (var i = 0; i < Uploads.Length; i++)
            {
                if (Uploads[i] != null)
                {
                    Uploads[i]->Release();
                    Uploads[i] = null;
                }
            }

            if (Texture != null) Texture->Release();
            if (SrvHeap != null) SrvHeap->Release();
            Texture = null;
            SrvHeap = null;
            _released = true;
        }
    }

    private readonly struct GlyphAtlasEntryHandle(int Index, int Generation) : IEquatable<GlyphAtlasEntryHandle>
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public bool IsNone => Generation == 0;

        public bool Equals(GlyphAtlasEntryHandle other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is GlyphAtlasEntryHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        public static bool operator ==(GlyphAtlasEntryHandle left, GlyphAtlasEntryHandle right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasEntryHandle left, GlyphAtlasEntryHandle right) => !left.Equals(right);
    }

    private readonly struct GlyphEntry(
        GlyphKey Key,
        float Width,
        float Height,
        float OffsetX,
        float OffsetY,
        float Advance,
        float U1,
        float V1,
        float U2,
        float V2,
        GlyphAtlasPageHandle Page,
        int Generation = 0,
        long LastUsedSerial = 0)
    {
        public GlyphKey Key { get; } = Key;
        public float Width { get; } = Width;
        public float Height { get; } = Height;
        public float OffsetX { get; } = OffsetX;
        public float OffsetY { get; } = OffsetY;
        public float Advance { get; } = Advance;
        public float U1 { get; } = U1;
        public float V1 { get; } = V1;
        public float U2 { get; } = U2;
        public float V2 { get; } = V2;
        public GlyphAtlasPageHandle Page { get; } = Page;
        public int Generation { get; } = Generation;
        public long LastUsedSerial { get; } = LastUsedSerial;
        public bool IsLive => LastUsedSerial > 0;

        public GlyphEntry WithGeneration(int generation) => new(Key, Width, Height, OffsetX, OffsetY, Advance, U1, V1, U2, V2, Page, generation, LastUsedSerial);

        public GlyphEntry WithLastUsedSerial(long serial) => new(Key, Width, Height, OffsetX, OffsetY, Advance, U1, V1, U2, V2, Page, Generation, serial);

        public GlyphEntry Clear() => new(Key, 0, 0, 0, 0, 0, 0, 0, 0, 0, Page, Generation, 0);
    }

    public readonly struct GlyphAtlasTextRendererDiagnostics(
        int CachedGlyphs,
        long UploadedBytes,
        int DrawnGlyphs,
        int CacheHits,
        int CacheMisses,
        int FallbackFrames,
        int UnsupportedRuns,
        GlyphAtlasFallbackReasonCounts Reasons,
        GlyphAtlasInitializationPhase InitializationFailurePhase,
        GlyphAtlasRecordFailurePhase RecordFailurePhase,
        int RasterScratchBytes,
        int RasterScratchResizes,
        int AtlasPages = 0,
        int AtlasAlphaPages = 0,
        int AtlasBgraPages = 0,
        int AtlasEvictions = 0,
        int AtlasAlphaEvictions = 0,
        int AtlasBgraEvictions = 0,
        int AtlasUsedPixels = 0,
        int AtlasFragmentedPixels = 0,
        int AtlasAlphaUsedPixels = 0,
        int AtlasBgraUsedPixels = 0,
        int AtlasAlphaFragmentedPixels = 0,
        int AtlasBgraFragmentedPixels = 0,
        long AtlasRecordSerial = 0,
        long AtlasOldestPageAge = 0,
        long AtlasNewestPageAge = 0,
        long AtlasOldestAlphaPageAge = 0,
        long AtlasOldestBgraPageAge = 0,
        int AtlasPendingPageReuses = 0,
        int AtlasPendingAlphaPageReuses = 0,
        int AtlasPendingBgraPageReuses = 0,
        int AtlasPageReuseRequests = 0,
        int AtlasAlphaPageReuseRequests = 0,
        int AtlasBgraPageReuseRequests = 0,
        int AtlasFullWithoutPageReuse = 0,
        int AtlasAlphaFullWithoutPageReuse = 0,
        int AtlasBgraFullWithoutPageReuse = 0,
        int AtlasRuns = 0,
        int DegradedRuns = 0,
        int UploadedGlyphs = 0,
        int ShapedProbeRuns = 0,
        int ShapedProbeGlyphs = 0,
        int ColorLayerRuns = 0,
        int ColorBitmapRuns = 0) : IEquatable<GlyphAtlasTextRendererDiagnostics>
    {
        public int CachedGlyphs { get; } = CachedGlyphs;
        public long UploadedBytes { get; } = UploadedBytes;
        public int DrawnGlyphs { get; } = DrawnGlyphs;
        public int CacheHits { get; } = CacheHits;
        public int CacheMisses { get; } = CacheMisses;
        public int FallbackFrames { get; } = FallbackFrames;
        public int UnsupportedRuns { get; } = UnsupportedRuns;
        public int ShapedProbeRuns { get; } = ShapedProbeRuns;
        public int ShapedProbeGlyphs { get; } = ShapedProbeGlyphs;
        public int AtlasPages { get; } = AtlasPages;
        public int AtlasAlphaPages { get; } = AtlasAlphaPages;
        public int AtlasBgraPages { get; } = AtlasBgraPages;
        public int AtlasBudgetPages => AtlasPageBudget;
        public int AtlasPageWidth => AtlasWidth;
        public int AtlasPageHeight => AtlasHeight;
        public int AtlasCapacityPixels => AtlasBudgetPixels;
        public long AtlasCpuBytes => ComputeAtlasResidentBytes(AtlasAlphaPages, AtlasBgraPages);
        public long AtlasUploadBytes => checked(AtlasGpuBytes * UploadFrameCount);
        public long AtlasGpuBytes => ComputeAtlasResidentBytes(AtlasAlphaPages, AtlasBgraPages);
        public int AtlasEvictions { get; } = AtlasEvictions;
        public int AtlasAlphaEvictions { get; } = AtlasAlphaEvictions;
        public int AtlasBgraEvictions { get; } = AtlasBgraEvictions;
        public int AtlasUsedPixels { get; } = AtlasUsedPixels;
        public int AtlasFragmentedPixels { get; } = AtlasFragmentedPixels;
        public int AtlasAlphaUsedPixels { get; } = AtlasAlphaUsedPixels;
        public int AtlasBgraUsedPixels { get; } = AtlasBgraUsedPixels;
        public int AtlasAlphaFragmentedPixels { get; } = AtlasAlphaFragmentedPixels;
        public int AtlasBgraFragmentedPixels { get; } = AtlasBgraFragmentedPixels;
        public long AtlasRecordSerial { get; } = AtlasRecordSerial;
        public long AtlasOldestPageAge { get; } = AtlasOldestPageAge;
        public long AtlasNewestPageAge { get; } = AtlasNewestPageAge;
        public long AtlasOldestAlphaPageAge { get; } = AtlasOldestAlphaPageAge;
        public long AtlasOldestBgraPageAge { get; } = AtlasOldestBgraPageAge;
        public int AtlasPendingPageReuses { get; } = AtlasPendingPageReuses;
        public int AtlasPendingAlphaPageReuses { get; } = AtlasPendingAlphaPageReuses;
        public int AtlasPendingBgraPageReuses { get; } = AtlasPendingBgraPageReuses;
        public int AtlasPageReuseRequests { get; } = AtlasPageReuseRequests;
        public int AtlasAlphaPageReuseRequests { get; } = AtlasAlphaPageReuseRequests;
        public int AtlasBgraPageReuseRequests { get; } = AtlasBgraPageReuseRequests;
        public int AtlasFullWithoutPageReuse { get; } = AtlasFullWithoutPageReuse;
        public int AtlasAlphaFullWithoutPageReuse { get; } = AtlasAlphaFullWithoutPageReuse;
        public int AtlasBgraFullWithoutPageReuse { get; } = AtlasBgraFullWithoutPageReuse;
        public int AtlasRuns { get; } = AtlasRuns;
        public int DegradedRuns { get; } = DegradedRuns;
        public int UploadedGlyphs { get; } = UploadedGlyphs;
        public int ColorLayerRuns { get; } = ColorLayerRuns;
        public int ColorBitmapRuns { get; } = ColorBitmapRuns;
        public GlyphAtlasFallbackReasonCounts Reasons { get; } = Reasons;
        public GlyphAtlasInitializationPhase InitializationFailurePhase { get; } = InitializationFailurePhase;
        public GlyphAtlasRecordFailurePhase RecordFailurePhase { get; } = RecordFailurePhase;
        public int RasterScratchBytes { get; } = RasterScratchBytes;
        public int RasterScratchResizes { get; } = RasterScratchResizes;

        public GlyphAtlasTextRendererDiagnostics WithCachedGlyphs(int cachedGlyphs) =>
            new(
                cachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithCacheHit() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits + 1,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithCacheMiss() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses + 1,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithDrawnGlyphs(int glyphs) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs + glyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPages(int atlasPages) => WithAtlasPageCounts(atlasPages, AtlasAlphaPages, AtlasBgraPages);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageCounts(int atlasPages, int alphaPages, int bgraPages) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                atlasPages,
                alphaPages,
                bgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasEviction() =>
            WithAtlasPolicy(AtlasEvictions + 1, AtlasAlphaEvictions, AtlasBgraEvictions, AtlasPendingAlphaPageReuses, AtlasPendingBgraPageReuses, AtlasPageReuseRequests, AtlasAlphaPageReuseRequests, AtlasBgraPageReuseRequests, AtlasFullWithoutPageReuse, AtlasAlphaFullWithoutPageReuse, AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasAlphaEviction() => WithAtlasEviction(isBgra: false);

        public GlyphAtlasTextRendererDiagnostics WithAtlasBgraEviction() => WithAtlasEviction(isBgra: true);

        public GlyphAtlasTextRendererDiagnostics WithAtlasEviction(bool isBgra) =>
            WithAtlasPolicy(
                AtlasEvictions + 1,
                AtlasAlphaEvictions + (isBgra ? 0 : 1),
                AtlasBgraEvictions + (isBgra ? 1 : 0),
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPendingPageReuse(int pendingPageReuses) =>
            WithAtlasPolicy(AtlasEvictions, AtlasAlphaEvictions, AtlasBgraEvictions, pendingPageReuses, AtlasPendingBgraPageReuses, AtlasPageReuseRequests, AtlasAlphaPageReuseRequests, AtlasBgraPageReuseRequests, AtlasFullWithoutPageReuse, AtlasAlphaFullWithoutPageReuse, AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPendingPageReuse(int pendingAlphaPageReuses, int pendingBgraPageReuses) =>
            WithAtlasPolicy(AtlasEvictions, AtlasAlphaEvictions, AtlasBgraEvictions, pendingAlphaPageReuses, pendingBgraPageReuses, AtlasPageReuseRequests, AtlasAlphaPageReuseRequests, AtlasBgraPageReuseRequests, AtlasFullWithoutPageReuse, AtlasAlphaFullWithoutPageReuse, AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageReuseRequest() =>
            WithAtlasPolicy(AtlasEvictions, AtlasAlphaEvictions, AtlasBgraEvictions, AtlasPendingAlphaPageReuses, AtlasPendingBgraPageReuses, AtlasPageReuseRequests + 1, AtlasAlphaPageReuseRequests, AtlasBgraPageReuseRequests, AtlasFullWithoutPageReuse, AtlasAlphaFullWithoutPageReuse, AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageReuseRequest(bool isBgra) =>
            WithAtlasPolicy(
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests + 1,
                AtlasAlphaPageReuseRequests + (isBgra ? 0 : 1),
                AtlasBgraPageReuseRequests + (isBgra ? 1 : 0),
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasFullWithoutPageReuse() =>
            WithAtlasPolicy(AtlasEvictions, AtlasAlphaEvictions, AtlasBgraEvictions, AtlasPendingAlphaPageReuses, AtlasPendingBgraPageReuses, AtlasPageReuseRequests, AtlasAlphaPageReuseRequests, AtlasBgraPageReuseRequests, AtlasFullWithoutPageReuse + 1, AtlasAlphaFullWithoutPageReuse, AtlasBgraFullWithoutPageReuse);

        public GlyphAtlasTextRendererDiagnostics WithAtlasFullWithoutPageReuse(bool isBgra) =>
            WithAtlasPolicy(
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse + 1,
                AtlasAlphaFullWithoutPageReuse + (isBgra ? 0 : 1),
                AtlasBgraFullWithoutPageReuse + (isBgra ? 1 : 0));

        private GlyphAtlasTextRendererDiagnostics WithAtlasPolicy(
            int evictions,
            int alphaEvictions,
            int bgraEvictions,
            int pendingAlphaPageReuses,
            int pendingBgraPageReuses,
            int pageReuseRequests,
            int alphaPageReuseRequests,
            int bgraPageReuseRequests,
            int fullWithoutPageReuse,
            int alphaFullWithoutPageReuse,
            int bgraFullWithoutPageReuse) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                evictions,
                alphaEvictions,
                bgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                pendingAlphaPageReuses + pendingBgraPageReuses,
                pendingAlphaPageReuses,
                pendingBgraPageReuses,
                pageReuseRequests,
                alphaPageReuseRequests,
                bgraPageReuseRequests,
                fullWithoutPageReuse,
                alphaFullWithoutPageReuse,
                bgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageUsage(int usedPixels, int fragmentedPixels) =>
            WithAtlasPageUsage(usedPixels, fragmentedPixels, AtlasAlphaUsedPixels, AtlasBgraUsedPixels, AtlasAlphaFragmentedPixels, AtlasBgraFragmentedPixels);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageUsage(int usedPixels, int fragmentedPixels, int alphaUsedPixels, int bgraUsedPixels, int alphaFragmentedPixels, int bgraFragmentedPixels) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                usedPixels,
                fragmentedPixels,
                alphaUsedPixels,
                bgraUsedPixels,
                alphaFragmentedPixels,
                bgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasTouchMetrics(long recordSerial, long oldestPageAge, long newestPageAge) =>
            WithAtlasTouchMetrics(recordSerial, oldestPageAge, newestPageAge, AtlasOldestAlphaPageAge, AtlasOldestBgraPageAge);

        public GlyphAtlasTextRendererDiagnostics WithAtlasTouchMetrics(long recordSerial, long oldestPageAge, long newestPageAge, long oldestAlphaPageAge, long oldestBgraPageAge) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                recordSerial,
                oldestPageAge,
                newestPageAge,
                oldestAlphaPageAge,
                oldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasRuns(int atlasRuns) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns + atlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithUploadedBytes(long bytes) =>
            new(
                CachedGlyphs,
                UploadedBytes + bytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithUploadedGlyph() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs + 1,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithShapedGlyphProbe(int glyphCount) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns + 1,
                ShapedProbeGlyphs + glyphCount,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithColorGlyphRuns(int layerRuns, int bitmapRuns) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns + layerRuns,
                ColorBitmapRuns + bitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithRasterScratch(int bytes, int resizes) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                bytes,
                resizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithInitializationFailure(GlyphAtlasInitializationPhase phase) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                phase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithRecordFailure(GlyphAtlasRecordFailurePhase phase) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                phase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);

        public GlyphAtlasTextRendererDiagnostics WithDegradation(int unsupportedRuns, GlyphAtlasFallbackReason reason)
        {
            var reasons = default(GlyphAtlasFallbackReasonCounts);
            if (reason != GlyphAtlasFallbackReason.None)
            {
                var reasonCount = unsupportedRuns > 0 ? unsupportedRuns : 1;
                for (var i = 0; i < reasonCount; i++)
                {
                    reasons = reasons.With(reason);
                }
            }

            return WithDegradation(unsupportedRuns, reasons);
        }

        public GlyphAtlasTextRendererDiagnostics WithDegradation(int unsupportedRuns, GlyphAtlasFallbackReasonCounts reasons)
        {
            return new GlyphAtlasTextRendererDiagnostics(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames + 1,
                UnsupportedRuns + unsupportedRuns,
                Reasons.Add(reasons),
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasAlphaPages,
                AtlasBgraPages,
                AtlasEvictions,
                AtlasAlphaEvictions,
                AtlasBgraEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasAlphaUsedPixels,
                AtlasBgraUsedPixels,
                AtlasAlphaFragmentedPixels,
                AtlasBgraFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasOldestAlphaPageAge,
                AtlasOldestBgraPageAge,
                AtlasPendingPageReuses,
                AtlasPendingAlphaPageReuses,
                AtlasPendingBgraPageReuses,
                AtlasPageReuseRequests,
                AtlasAlphaPageReuseRequests,
                AtlasBgraPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasAlphaFullWithoutPageReuse,
                AtlasBgraFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns + unsupportedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs,
                ColorLayerRuns,
                ColorBitmapRuns);
        }

        public string FormatSummary()
        {
            return $"cachedGlyphs={CachedGlyphs}, atlasPages={AtlasPages}, atlasAlphaPages={AtlasAlphaPages}, atlasBgraPages={AtlasBgraPages}, atlasBudgetPages={AtlasBudgetPages}, atlasPage={AtlasPageWidth}x{AtlasPageHeight}, atlasCapacity={AtlasCapacityPixels} px, atlasCpuBytes={AtlasCpuBytes} bytes, atlasUploadBytes={AtlasUploadBytes} bytes, atlasGpuBytes={AtlasGpuBytes} bytes, atlasEvictions={AtlasEvictions}, atlasAlphaEvictions={AtlasAlphaEvictions}, atlasBgraEvictions={AtlasBgraEvictions}, atlasPendingPageReuses={AtlasPendingPageReuses}, atlasPendingAlphaPageReuses={AtlasPendingAlphaPageReuses}, atlasPendingBgraPageReuses={AtlasPendingBgraPageReuses}, atlasPageReuseRequests={AtlasPageReuseRequests}, atlasAlphaPageReuseRequests={AtlasAlphaPageReuseRequests}, atlasBgraPageReuseRequests={AtlasBgraPageReuseRequests}, atlasFullWithoutPageReuse={AtlasFullWithoutPageReuse}, atlasAlphaFullWithoutPageReuse={AtlasAlphaFullWithoutPageReuse}, atlasBgraFullWithoutPageReuse={AtlasBgraFullWithoutPageReuse}, atlasUsed={AtlasUsedPixels} px, atlasFragmented={AtlasFragmentedPixels} px, "
                + $"atlasAlphaUsed={AtlasAlphaUsedPixels} px, atlasBgraUsed={AtlasBgraUsedPixels} px, atlasAlphaFragmented={AtlasAlphaFragmentedPixels} px, atlasBgraFragmented={AtlasBgraFragmentedPixels} px, atlasRecordSerial={AtlasRecordSerial}, atlasOldestPageAge={AtlasOldestPageAge}, atlasNewestPageAge={AtlasNewestPageAge}, atlasOldestAlphaPageAge={AtlasOldestAlphaPageAge}, atlasOldestBgraPageAge={AtlasOldestBgraPageAge}, drawnGlyphs={DrawnGlyphs}, atlasRuns={AtlasRuns}, degradedRuns={DegradedRuns}, "
                + $"uploads={UploadedBytes} bytes, uploadedGlyphs={UploadedGlyphs}, shapedProbeRuns={ShapedProbeRuns}, shapedProbeGlyphs={ShapedProbeGlyphs}, colorLayerRuns={ColorLayerRuns}, colorBitmapRuns={ColorBitmapRuns}, hits={CacheHits}, misses={CacheMisses}, fallbacks={FallbackFrames}, unsupportedRuns={UnsupportedRuns}, reasons=[{Reasons}], "
                + $"initFailurePhase={InitializationFailurePhase}, recordFailurePhase={RecordFailurePhase}, rasterScratch={RasterScratchBytes} bytes/{RasterScratchResizes} resizes";
        }

        private static long ComputeAtlasResidentBytes(int alphaPages, int bgraPages) =>
            checked((long)alphaPages * GetAtlasPixelBytes(GlyphAtlasPageFormat.Alpha) + (long)bgraPages * GetAtlasPixelBytes(GlyphAtlasPageFormat.Bgra));

        public bool Equals(GlyphAtlasTextRendererDiagnostics other)
        {
            return CachedGlyphs == other.CachedGlyphs
                && UploadedBytes == other.UploadedBytes
                && DrawnGlyphs == other.DrawnGlyphs
                && CacheHits == other.CacheHits
                && CacheMisses == other.CacheMisses
                && FallbackFrames == other.FallbackFrames
                && UnsupportedRuns == other.UnsupportedRuns
                && AtlasPages == other.AtlasPages
                && AtlasAlphaPages == other.AtlasAlphaPages
                && AtlasBgraPages == other.AtlasBgraPages
                && AtlasEvictions == other.AtlasEvictions
                && AtlasAlphaEvictions == other.AtlasAlphaEvictions
                && AtlasBgraEvictions == other.AtlasBgraEvictions
                && AtlasUsedPixels == other.AtlasUsedPixels
                && AtlasFragmentedPixels == other.AtlasFragmentedPixels
                && AtlasAlphaUsedPixels == other.AtlasAlphaUsedPixels
                && AtlasBgraUsedPixels == other.AtlasBgraUsedPixels
                && AtlasAlphaFragmentedPixels == other.AtlasAlphaFragmentedPixels
                && AtlasBgraFragmentedPixels == other.AtlasBgraFragmentedPixels
                && AtlasRecordSerial == other.AtlasRecordSerial
                && AtlasOldestPageAge == other.AtlasOldestPageAge
                && AtlasNewestPageAge == other.AtlasNewestPageAge
                && AtlasOldestAlphaPageAge == other.AtlasOldestAlphaPageAge
                && AtlasOldestBgraPageAge == other.AtlasOldestBgraPageAge
                && AtlasPendingPageReuses == other.AtlasPendingPageReuses
                && AtlasPendingAlphaPageReuses == other.AtlasPendingAlphaPageReuses
                && AtlasPendingBgraPageReuses == other.AtlasPendingBgraPageReuses
                && AtlasPageReuseRequests == other.AtlasPageReuseRequests
                && AtlasAlphaPageReuseRequests == other.AtlasAlphaPageReuseRequests
                && AtlasBgraPageReuseRequests == other.AtlasBgraPageReuseRequests
                && AtlasFullWithoutPageReuse == other.AtlasFullWithoutPageReuse
                && AtlasAlphaFullWithoutPageReuse == other.AtlasAlphaFullWithoutPageReuse
                && AtlasBgraFullWithoutPageReuse == other.AtlasBgraFullWithoutPageReuse
                && AtlasRuns == other.AtlasRuns
                && DegradedRuns == other.DegradedRuns
                && UploadedGlyphs == other.UploadedGlyphs
                && ShapedProbeRuns == other.ShapedProbeRuns
                && ShapedProbeGlyphs == other.ShapedProbeGlyphs
                && ColorLayerRuns == other.ColorLayerRuns
                && ColorBitmapRuns == other.ColorBitmapRuns
                && Reasons.Equals(other.Reasons)
                && InitializationFailurePhase == other.InitializationFailurePhase
                && RecordFailurePhase == other.RecordFailurePhase
                && RasterScratchBytes == other.RasterScratchBytes
                && RasterScratchResizes == other.RasterScratchResizes;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasTextRendererDiagnostics other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(CachedGlyphs);
            hash.Add(UploadedBytes);
            hash.Add(DrawnGlyphs);
            hash.Add(CacheHits);
            hash.Add(CacheMisses);
            hash.Add(FallbackFrames);
            hash.Add(UnsupportedRuns);
            hash.Add(AtlasPages);
            hash.Add(AtlasAlphaPages);
            hash.Add(AtlasBgraPages);
            hash.Add(AtlasEvictions);
            hash.Add(AtlasAlphaEvictions);
            hash.Add(AtlasBgraEvictions);
            hash.Add(AtlasUsedPixels);
            hash.Add(AtlasFragmentedPixels);
            hash.Add(AtlasAlphaUsedPixels);
            hash.Add(AtlasBgraUsedPixels);
            hash.Add(AtlasAlphaFragmentedPixels);
            hash.Add(AtlasBgraFragmentedPixels);
            hash.Add(AtlasRecordSerial);
            hash.Add(AtlasOldestPageAge);
            hash.Add(AtlasNewestPageAge);
            hash.Add(AtlasOldestAlphaPageAge);
            hash.Add(AtlasOldestBgraPageAge);
            hash.Add(AtlasPendingPageReuses);
            hash.Add(AtlasPendingAlphaPageReuses);
            hash.Add(AtlasPendingBgraPageReuses);
            hash.Add(AtlasPageReuseRequests);
            hash.Add(AtlasAlphaPageReuseRequests);
            hash.Add(AtlasBgraPageReuseRequests);
            hash.Add(AtlasFullWithoutPageReuse);
            hash.Add(AtlasAlphaFullWithoutPageReuse);
            hash.Add(AtlasBgraFullWithoutPageReuse);
            hash.Add(AtlasRuns);
            hash.Add(DegradedRuns);
            hash.Add(UploadedGlyphs);
            hash.Add(ShapedProbeRuns);
            hash.Add(ShapedProbeGlyphs);
            hash.Add(ColorLayerRuns);
            hash.Add(ColorBitmapRuns);
            hash.Add(Reasons);
            hash.Add(InitializationFailurePhase);
            hash.Add(RecordFailurePhase);
            hash.Add(RasterScratchBytes);
            hash.Add(RasterScratchResizes);
            return hash.ToHashCode();
        }
    }
}

