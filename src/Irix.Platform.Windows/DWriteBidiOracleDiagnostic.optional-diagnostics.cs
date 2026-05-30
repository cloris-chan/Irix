#if IRIX_DIAGNOSTICS
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Platform.Windows;

internal static unsafe class DWriteBidiOracleDiagnostic
{
    private static readonly Guid IUnknownGuid = new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);

    private static readonly BidiOracleProbeDefinition[] DefaultProbes =
    [
        new("ltr-arabic-ltr", "abc \u0645\u0631\u062D\u0628\u0627 xyz", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT),
        new("rtl-leading-digits", "123 \u0645\u0631\u062D\u0628\u0627 abc", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT),
        new("hebrew-weak-digits", "\u05E9\u05DC\u05D5\u05DD 123 abc", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT),
        new("nested-mixed", "abc \u05D0\u05D1 12 \u05D2\u05D3 xyz", DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT)
    ];

    internal static BidiOracleDiagnosticSnapshot Capture()
    {
        IDWriteFactory* factory = null;
        IDWriteTextAnalyzer* analyzer = null;

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
                return BidiOracleDiagnosticSnapshot.Failed("TextAnalyzerUnavailable");
            }

            var results = new BidiOracleProbeResult[DefaultProbes.Length];
            for (var i = 0; i < DefaultProbes.Length; i++)
            {
                results[i] = Probe(analyzer, DefaultProbes[i]);
            }

            return BidiOracleDiagnosticSnapshot.Create(factoryAvailable: true, analyzerAvailable: true, results);
        }
        catch (COMException ex)
        {
            return BidiOracleDiagnosticSnapshot.Failed($"DirectWrite=0x{unchecked((uint)ex.ErrorCode):X8}");
        }
        finally
        {
            if (analyzer != null) analyzer->Release();
            if (factory != null) factory->Release();
        }
    }

    private static BidiOracleProbeResult Probe(IDWriteTextAnalyzer* analyzer, BidiOracleProbeDefinition probe)
    {
        var text = probe.Text;
        var levels = new byte[text.Length];
        levels.AsSpan().Fill(probe.BaseDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? (byte)1 : (byte)0);

        try
        {
            AnalyzeBidi(analyzer, text.AsSpan(), probe.BaseDirection, levels);
        }
        catch (COMException ex)
        {
            return BidiOracleProbeResult.Failed(probe.Label, text.Length, probe.BaseDirection, $"0x{unchecked((uint)ex.ErrorCode):X8}");
        }

        var logicalRuns = CreateLevelRuns(levels);
        var visualRuns = new BidiOracleLevelRun[logicalRuns.Length];
        logicalRuns.AsSpan().CopyTo(visualRuns);
        var visualRunLevels = new byte[visualRuns.Length];
        for (var i = 0; i < visualRuns.Length; i++)
        {
            visualRunLevels[i] = visualRuns[i].Level;
        }

        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(visualRuns, visualRunLevels);

        var characterOrder = new int[levels.Length];
        var characterOrderLevels = new byte[levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            characterOrder[i] = i;
            characterOrderLevels[i] = levels[i];
        }

        GlyphAtlasTextCompositionHelpers.ApplyBidiVisualOrder(characterOrder, characterOrderLevels);

        return BidiOracleProbeResult.Create(probe.Label, text.Length, probe.BaseDirection, levels, logicalRuns, visualRuns, characterOrder);
    }

    private static void AnalyzeBidi(IDWriteTextAnalyzer* analyzer, ReadOnlySpan<char> text, DWRITE_READING_DIRECTION readingDirection, byte[] levels)
    {
        if (text.IsEmpty || text.Length > ushort.MaxValue)
        {
            return;
        }

        fixed (char* textPtr = text)
        fixed (byte* levelsPtr = levels)
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
                BidiLevels = levelsPtr
            };

            analyzer->AnalyzeBidi((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
        }
    }

    private static BidiOracleLevelRun[] CreateLevelRuns(byte[] levels)
    {
        if (levels.Length == 0)
        {
            return [];
        }

        var runs = new BidiOracleLevelRun[levels.Length];
        var runCount = 0;
        var runStart = 0;
        var runLevel = levels[0];
        for (var i = 1; i < levels.Length; i++)
        {
            if (levels[i] == runLevel)
            {
                continue;
            }

            runs[runCount++] = new BidiOracleLevelRun(runStart, i - runStart, runLevel);
            runStart = i;
            runLevel = levels[i];
        }

        runs[runCount++] = new BidiOracleLevelRun(runStart, levels.Length - runStart, runLevel);
        Array.Resize(ref runs, runCount);
        return runs;
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
    private static HRESULT TextAnalysisSinkSetScriptAnalysis(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_SCRIPT_ANALYSIS* scriptAnalysis) => (HRESULT)0;

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
        public byte* BidiLevels;
    }
}

internal readonly struct BidiOracleProbeDefinition(string Label, string Text, DWRITE_READING_DIRECTION BaseDirection)
{
    public string Label { get; } = Label;
    public string Text { get; } = Text;
    public DWRITE_READING_DIRECTION BaseDirection { get; } = BaseDirection;
}

internal readonly struct BidiOracleLevelRun(int TextStart, int TextLength, byte Level) : IEquatable<BidiOracleLevelRun>
{
    public int TextStart { get; } = TextStart;
    public int TextLength { get; } = TextLength;
    public int TextEnd => TextStart + TextLength;
    public byte Level { get; } = Level;

    public bool Equals(BidiOracleLevelRun other) => TextStart == other.TextStart && TextLength == other.TextLength && Level == other.Level;

    public override bool Equals(object? obj) => obj is BidiOracleLevelRun other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TextStart, TextLength, Level);

    public static bool operator ==(BidiOracleLevelRun left, BidiOracleLevelRun right) => left.Equals(right);

    public static bool operator !=(BidiOracleLevelRun left, BidiOracleLevelRun right) => !left.Equals(right);
}

internal readonly struct BidiOracleProbeResult(
    string Label,
    int TextLength,
    DWRITE_READING_DIRECTION BaseDirection,
    string Failure,
    byte[] Levels,
    BidiOracleLevelRun[] LogicalRuns,
    BidiOracleLevelRun[] VisualRuns,
    int[] CharacterVisualOrder) : IEquatable<BidiOracleProbeResult>
{
    public string Label { get; } = Label;
    public int TextLength { get; } = TextLength;
    public DWRITE_READING_DIRECTION BaseDirection { get; } = BaseDirection;
    public string Failure { get; } = Failure;
    public IReadOnlyList<byte> Levels { get; } = Levels;
    public IReadOnlyList<BidiOracleLevelRun> LogicalRuns { get; } = LogicalRuns;
    public IReadOnlyList<BidiOracleLevelRun> VisualRuns { get; } = VisualRuns;
    public IReadOnlyList<int> CharacterVisualOrder { get; } = CharacterVisualOrder;
    public bool Succeeded => string.IsNullOrEmpty(Failure);
    public bool HasMixedLevels => CountDistinctLevels(Levels) > 1;
    public bool VisualOrderChanged => !IsIdentity(CharacterVisualOrder);

    public static BidiOracleProbeResult Failed(string label, int textLength, DWRITE_READING_DIRECTION baseDirection, string failure) =>
        new(label, textLength, baseDirection, failure, [], [], [], []);

    public static BidiOracleProbeResult Create(string label, int textLength, DWRITE_READING_DIRECTION baseDirection, byte[] levels, BidiOracleLevelRun[] logicalRuns, BidiOracleLevelRun[] visualRuns, int[] characterVisualOrder) =>
        new(label, textLength, baseDirection, "", levels, logicalRuns, visualRuns, characterVisualOrder);

    public bool Equals(BidiOracleProbeResult other)
    {
        return Label == other.Label
            && TextLength == other.TextLength
            && BaseDirection == other.BaseDirection
            && Failure == other.Failure
            && ReferenceEquals(Levels, other.Levels)
            && ReferenceEquals(LogicalRuns, other.LogicalRuns)
            && ReferenceEquals(VisualRuns, other.VisualRuns)
            && ReferenceEquals(CharacterVisualOrder, other.CharacterVisualOrder);
    }

    public override bool Equals(object? obj) => obj is BidiOracleProbeResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label);
        hash.Add(TextLength);
        hash.Add(BaseDirection);
        hash.Add(Failure);
        hash.Add(Levels.Count);
        hash.Add(LogicalRuns.Count);
        hash.Add(VisualRuns.Count);
        hash.Add(CharacterVisualOrder.Count);
        return hash.ToHashCode();
    }

    public static bool operator ==(BidiOracleProbeResult left, BidiOracleProbeResult right) => left.Equals(right);

    public static bool operator !=(BidiOracleProbeResult left, BidiOracleProbeResult right) => !left.Equals(right);

    private static int CountDistinctLevels(IReadOnlyList<byte> levels)
    {
        var mask = 0;
        for (var i = 0; i < levels.Count; i++)
        {
            var bit = levels[i] < 31 ? 1 << levels[i] : 0;
            mask |= bit;
        }

        return BitOperations.PopCount((uint)mask);
    }

    private static bool IsIdentity(IReadOnlyList<int> order)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] != i)
            {
                return false;
            }
        }

        return true;
    }
}

internal readonly struct BidiOracleDiagnosticSnapshot(
    bool FactoryAvailable,
    bool AnalyzerAvailable,
    string Failure,
    BidiOracleProbeResult[] Results,
    int MixedLevelProbes,
    int VisualReorderedProbes,
    int FailedProbes) : IEquatable<BidiOracleDiagnosticSnapshot>
{
    public bool FactoryAvailable { get; } = FactoryAvailable;
    public bool AnalyzerAvailable { get; } = AnalyzerAvailable;
    public string Failure { get; } = Failure;
    public IReadOnlyList<BidiOracleProbeResult> Results { get; } = Results;
    public int ProbeCount => Results.Count;
    public int MixedLevelProbes { get; } = MixedLevelProbes;
    public int VisualReorderedProbes { get; } = VisualReorderedProbes;
    public int FailedProbes { get; } = FailedProbes;

    public static BidiOracleDiagnosticSnapshot Failed(string failure) =>
        new(FactoryAvailable: false, AnalyzerAvailable: false, failure, [], MixedLevelProbes: 0, VisualReorderedProbes: 0, FailedProbes: 0);

    public static BidiOracleDiagnosticSnapshot Create(bool factoryAvailable, bool analyzerAvailable, BidiOracleProbeResult[] results)
    {
        var mixedLevelProbes = 0;
        var visualReorderedProbes = 0;
        var failedProbes = 0;
        foreach (ref readonly var result in results.AsSpan())
        {
            if (!result.Succeeded) failedProbes++;
            if (result.HasMixedLevels) mixedLevelProbes++;
            if (result.VisualOrderChanged) visualReorderedProbes++;
        }

        return new BidiOracleDiagnosticSnapshot(factoryAvailable, analyzerAvailable, "", results, mixedLevelProbes, visualReorderedProbes, failedProbes);
    }

    public bool Equals(BidiOracleDiagnosticSnapshot other)
    {
        return FactoryAvailable == other.FactoryAvailable
            && AnalyzerAvailable == other.AnalyzerAvailable
            && Failure == other.Failure
            && ReferenceEquals(Results, other.Results)
            && MixedLevelProbes == other.MixedLevelProbes
            && VisualReorderedProbes == other.VisualReorderedProbes
            && FailedProbes == other.FailedProbes;
    }

    public override bool Equals(object? obj) => obj is BidiOracleDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FactoryAvailable, AnalyzerAvailable, Failure, Results.Count, MixedLevelProbes, VisualReorderedProbes, FailedProbes);

    public static bool operator ==(BidiOracleDiagnosticSnapshot left, BidiOracleDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(BidiOracleDiagnosticSnapshot left, BidiOracleDiagnosticSnapshot right) => !left.Equals(right);
}
#endif
