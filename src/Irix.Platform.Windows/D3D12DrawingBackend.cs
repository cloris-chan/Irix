using System.Runtime.CompilerServices;
using Irix.Drawing;
using Irix.Rendering;

namespace Irix.Platform.Windows;

internal readonly struct D3D12FillRectScissorPlan(EffectiveScissor EffectiveScissor, IntegerScissorRect RenderScissor, bool Skip) : IEquatable<D3D12FillRectScissorPlan>
{

    public EffectiveScissor EffectiveScissor { get; } = EffectiveScissor;
    public IntegerScissorRect RenderScissor { get; } = RenderScissor;
    public bool Skip { get; } = Skip;

    public bool Equals(D3D12FillRectScissorPlan other)
    {
        return EffectiveScissor == other.EffectiveScissor
            && RenderScissor == other.RenderScissor
            && Skip == other.Skip;
    }

    public override bool Equals(object? obj) => obj is D3D12FillRectScissorPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(EffectiveScissor, RenderScissor, Skip);

    public static bool operator ==(D3D12FillRectScissorPlan left, D3D12FillRectScissorPlan right) => left.Equals(right);

    public static bool operator !=(D3D12FillRectScissorPlan left, D3D12FillRectScissorPlan right) => !left.Equals(right);
}

internal readonly struct D3D12TextClipPlan(EffectiveScissor EffectiveClip, bool ClipEnabled, bool Skip) : IEquatable<D3D12TextClipPlan>
{

    public EffectiveScissor EffectiveClip { get; } = EffectiveClip;
    public bool ClipEnabled { get; } = ClipEnabled;
    public bool Skip { get; } = Skip;

    public bool Equals(D3D12TextClipPlan other)
    {
        return EffectiveClip == other.EffectiveClip
            && ClipEnabled == other.ClipEnabled
            && Skip == other.Skip;
    }

    public override bool Equals(object? obj) => obj is D3D12TextClipPlan other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(EffectiveClip, ClipEnabled, Skip);

    public static bool operator ==(D3D12TextClipPlan left, D3D12TextClipPlan right) => left.Equals(right);

    public static bool operator !=(D3D12TextClipPlan left, D3D12TextClipPlan right) => !left.Equals(right);
}

internal readonly struct D3D12FillRectScissorDiagnostics(int ClippedCommandCount, int EmptyIntersectionSkippedCount, int ScissorStateChangeCount, EffectiveScissor LastEffectiveScissor) : IEquatable<D3D12FillRectScissorDiagnostics>
{

    public int ClippedCommandCount { get; } = ClippedCommandCount;
    public int EmptyIntersectionSkippedCount { get; } = EmptyIntersectionSkippedCount;
    public int ScissorStateChangeCount { get; } = ScissorStateChangeCount;
    public EffectiveScissor LastEffectiveScissor { get; } = LastEffectiveScissor;

    public bool Equals(D3D12FillRectScissorDiagnostics other)
    {
        return ClippedCommandCount == other.ClippedCommandCount
            && EmptyIntersectionSkippedCount == other.EmptyIntersectionSkippedCount
            && ScissorStateChangeCount == other.ScissorStateChangeCount
            && LastEffectiveScissor == other.LastEffectiveScissor;
    }

    public override bool Equals(object? obj) => obj is D3D12FillRectScissorDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ClippedCommandCount, EmptyIntersectionSkippedCount, ScissorStateChangeCount, LastEffectiveScissor);

    public static bool operator ==(D3D12FillRectScissorDiagnostics left, D3D12FillRectScissorDiagnostics right) => left.Equals(right);

    public static bool operator !=(D3D12FillRectScissorDiagnostics left, D3D12FillRectScissorDiagnostics right) => !left.Equals(right);
}

internal readonly struct D3D12TextClipDiagnostics(int TextClipSkippedCount, EffectiveScissor LastEffectiveTextClip) : IEquatable<D3D12TextClipDiagnostics>
{

    public int TextClipSkippedCount { get; } = TextClipSkippedCount;
    public EffectiveScissor LastEffectiveTextClip { get; } = LastEffectiveTextClip;

    public bool Equals(D3D12TextClipDiagnostics other)
    {
        return TextClipSkippedCount == other.TextClipSkippedCount
            && LastEffectiveTextClip == other.LastEffectiveTextClip;
    }

    public override bool Equals(object? obj) => obj is D3D12TextClipDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TextClipSkippedCount, LastEffectiveTextClip);

    public static bool operator ==(D3D12TextClipDiagnostics left, D3D12TextClipDiagnostics right) => left.Equals(right);

    public static bool operator !=(D3D12TextClipDiagnostics left, D3D12TextClipDiagnostics right) => !left.Equals(right);
}

internal readonly struct D3D12ExecuteCoreResult(
    D3D12FillRectScissorDiagnostics FillRectDiagnostics,
    D3D12TextClipDiagnostics TextClipDiagnostics,
    DrawMaterialOutputDiagnostics MaterialDiagnostics,
    bool HasBackgroundColor,
    DrawColor BackgroundColor) : IEquatable<D3D12ExecuteCoreResult>
{

    public D3D12FillRectScissorDiagnostics FillRectDiagnostics { get; } = FillRectDiagnostics;
    public D3D12TextClipDiagnostics TextClipDiagnostics { get; } = TextClipDiagnostics;
    public DrawMaterialOutputDiagnostics MaterialDiagnostics { get; } = MaterialDiagnostics;
    public bool HasBackgroundColor { get; } = HasBackgroundColor;
    public DrawColor BackgroundColor { get; } = BackgroundColor;

    public bool Equals(D3D12ExecuteCoreResult other)
    {
        return FillRectDiagnostics == other.FillRectDiagnostics
            && TextClipDiagnostics == other.TextClipDiagnostics
            && MaterialDiagnostics == other.MaterialDiagnostics
            && HasBackgroundColor == other.HasBackgroundColor
            && BackgroundColor == other.BackgroundColor;
    }

    public override bool Equals(object? obj) => obj is D3D12ExecuteCoreResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FillRectDiagnostics, TextClipDiagnostics, MaterialDiagnostics, HasBackgroundColor, BackgroundColor);

    public static bool operator ==(D3D12ExecuteCoreResult left, D3D12ExecuteCoreResult right) => left.Equals(right);

    public static bool operator !=(D3D12ExecuteCoreResult left, D3D12ExecuteCoreResult right) => !left.Equals(right);
}

internal readonly struct D3D12CompositionExecuteDiagnostics(
    bool D3D12Backed,
    int LayerCount,
    int CommandCount,
    int LayerCommandStart,
    int LayerCommandCount,
    int TranslatedCommands,
    int OpacityAppliedCommands,
    int LayerCacheHits,
    int LayerCacheMisses,
    int CachedLayerCommands,
    CompositionTransform AppliedTransform,
    CompositionOpacity AppliedOpacity,
    D3D12ExecuteCoreResult ExecuteResult) : IEquatable<D3D12CompositionExecuteDiagnostics>
{
    public bool D3D12Backed { get; } = D3D12Backed;
    public int LayerCount { get; } = LayerCount;
    public int CommandCount { get; } = CommandCount;
    public int LayerCommandStart { get; } = LayerCommandStart;
    public int LayerCommandCount { get; } = LayerCommandCount;
    public int TranslatedCommands { get; } = TranslatedCommands;
    public int OpacityAppliedCommands { get; } = OpacityAppliedCommands;
    public int LayerCacheHits { get; } = LayerCacheHits;
    public int LayerCacheMisses { get; } = LayerCacheMisses;
    public int CachedLayerCommands { get; } = CachedLayerCommands;
    public CompositionTransform AppliedTransform { get; } = AppliedTransform;
    public CompositionOpacity AppliedOpacity { get; } = AppliedOpacity;
    public D3D12ExecuteCoreResult ExecuteResult { get; } = ExecuteResult;

    public bool Equals(D3D12CompositionExecuteDiagnostics other)
    {
        return D3D12Backed == other.D3D12Backed
            && LayerCount == other.LayerCount
            && CommandCount == other.CommandCount
            && LayerCommandStart == other.LayerCommandStart
            && LayerCommandCount == other.LayerCommandCount
            && TranslatedCommands == other.TranslatedCommands
            && OpacityAppliedCommands == other.OpacityAppliedCommands
            && LayerCacheHits == other.LayerCacheHits
            && LayerCacheMisses == other.LayerCacheMisses
            && CachedLayerCommands == other.CachedLayerCommands
            && AppliedTransform == other.AppliedTransform
            && AppliedOpacity == other.AppliedOpacity
            && ExecuteResult == other.ExecuteResult;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionExecuteDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(D3D12Backed);
        hash.Add(LayerCount);
        hash.Add(CommandCount);
        hash.Add(LayerCommandStart);
        hash.Add(LayerCommandCount);
        hash.Add(TranslatedCommands);
        hash.Add(OpacityAppliedCommands);
        hash.Add(LayerCacheHits);
        hash.Add(LayerCacheMisses);
        hash.Add(CachedLayerCommands);
        hash.Add(AppliedTransform);
        hash.Add(AppliedOpacity);
        hash.Add(ExecuteResult);
        return hash.ToHashCode();
    }

    public CompositionBackendExecutionResult ToBackendExecutionResult()
    {
        return new CompositionBackendExecutionResult(
            D3D12Backed,
            LayerCount,
            CommandCount,
            TranslatedCommands,
            OpacityAppliedCommands,
            LayerCacheHits,
            LayerCacheMisses,
            CachedLayerCommands);
    }

    public static bool operator ==(D3D12CompositionExecuteDiagnostics left, D3D12CompositionExecuteDiagnostics right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionExecuteDiagnostics left, D3D12CompositionExecuteDiagnostics right) => !left.Equals(right);
}

internal sealed class D3D12CompositionLayerContentCache
{
    private const int MaxEntries = 32;
    private readonly D3D12CompositionLayerContentCacheEntry[] _entries = new D3D12CompositionLayerContentCacheEntry[MaxEntries];
    private int _count;
    private int _nextReplaceIndex;

    public D3D12CompositionLayerContent GetOrBuild(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionLayer layer,
        DisplayScale scale,
        out bool hit)
    {
        var key = D3D12CompositionLayerContentCacheKey.Create(commands, resources, layer, scale);
        for (var i = 0; i < _count; i++)
        {
            var entry = _entries[i];
            if (entry.Key == key && entry.Content.SourceEquals(commands.Slice(layer.CommandStart, layer.CommandCount)))
            {
                hit = true;
                return entry.Content;
            }
        }

        hit = false;
        var content = D3D12CompositionLayerContent.Build(commands.Slice(layer.CommandStart, layer.CommandCount), resources, scale);
        Store(key, content);
        return content;
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
        _nextReplaceIndex = 0;
    }

    private void Store(in D3D12CompositionLayerContentCacheKey key, D3D12CompositionLayerContent content)
    {
        if (_count < _entries.Length)
        {
            _entries[_count++] = new D3D12CompositionLayerContentCacheEntry(key, content);
            return;
        }

        _entries[_nextReplaceIndex] = new D3D12CompositionLayerContentCacheEntry(key, content);
        _nextReplaceIndex = (_nextReplaceIndex + 1) % _entries.Length;
    }
}

internal sealed class D3D12CompositionLayerContent
{
    private readonly DrawCommand[] _sourceCommands;
    private readonly D3D12CompositionLayerRectPayload[] _rects;
    private readonly D3D12CompositionLayerTextPayload[] _texts;

    public D3D12CompositionLayerContent(
        DrawCommand[] sourceCommands,
        D3D12CompositionLayerRectPayload[] rects,
        D3D12CompositionLayerTextPayload[] texts,
        int sourceCommandCount)
    {
        _sourceCommands = sourceCommands;
        _rects = rects;
        _texts = texts;
        SourceCommandCount = sourceCommandCount;
    }

    public ReadOnlySpan<D3D12CompositionLayerRectPayload> Rects => _rects;
    public ReadOnlySpan<D3D12CompositionLayerTextPayload> Texts => _texts;
    public int SourceCommandCount { get; }

    public bool SourceEquals(ReadOnlySpan<DrawCommand> commands)
    {
        return commands.SequenceEqual(_sourceCommands);
    }

    public static D3D12CompositionLayerContent Build(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        DisplayScale scale)
    {
        scale = scale.Normalize();
        var rectCount = 0;
        var textCount = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            if (command.Kind == DrawCommandKind.FillRect)
            {
                rectCount++;
            }
            else if (command.Kind == DrawCommandKind.DrawTextRun)
            {
                textCount++;
            }
        }

        var rects = rectCount == 0 ? [] : new D3D12CompositionLayerRectPayload[rectCount];
        var texts = textCount == 0 ? [] : new D3D12CompositionLayerTextPayload[textCount];
        var rectIndex = 0;
        var textIndex = 0;
        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            if (command.Kind == DrawCommandKind.FillRect)
            {
                rects[rectIndex++] = new D3D12CompositionLayerRectPayload(command.Rect, command.Material, command.ClipBounds);
            }
            else if (command.Kind == DrawCommandKind.DrawTextRun)
            {
                texts[textIndex++] = new D3D12CompositionLayerTextPayload(
                    command.Rect,
                    command.Material,
                    command.Text,
                    command.Resource,
                    command.ClipBounds,
                    D3D12DrawingBackend.ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(command.Resource), scale),
                    !resources.Resolve(command.Text).IsEmpty);
            }
        }

        return new D3D12CompositionLayerContent(commands.ToArray(), rects, texts, commands.Length);
    }
}

internal readonly struct D3D12CompositionLayerRectPayload(
    DrawRect Rect,
    DrawMaterial Material,
    DrawRect ClipBounds) : IEquatable<D3D12CompositionLayerRectPayload>
{
    public DrawRect Rect { get; } = Rect;
    public DrawMaterial Material { get; } = Material;
    public DrawRect ClipBounds { get; } = ClipBounds;

    public bool Equals(D3D12CompositionLayerRectPayload other) => Rect == other.Rect && Material == other.Material && ClipBounds == other.ClipBounds;

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerRectPayload other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rect, Material, ClipBounds);

    public static bool operator ==(D3D12CompositionLayerRectPayload left, D3D12CompositionLayerRectPayload right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionLayerRectPayload left, D3D12CompositionLayerRectPayload right) => !left.Equals(right);
}

internal readonly struct D3D12CompositionLayerTextPayload(
    DrawRect Rect,
    DrawMaterial Material,
    TextSlice Text,
    ResourceHandle Resource,
    DrawRect ClipBounds,
    TextStyle ResolvedStyle,
    bool HasText) : IEquatable<D3D12CompositionLayerTextPayload>
{
    public DrawRect Rect { get; } = Rect;
    public DrawMaterial Material { get; } = Material;
    public TextSlice Text { get; } = Text;
    public ResourceHandle Resource { get; } = Resource;
    public DrawRect ClipBounds { get; } = ClipBounds;
    public TextStyle ResolvedStyle { get; } = ResolvedStyle;
    public bool HasText { get; } = HasText;

    public bool Equals(D3D12CompositionLayerTextPayload other)
    {
        return Rect == other.Rect
            && Material == other.Material
            && Text == other.Text
            && Resource == other.Resource
            && ClipBounds == other.ClipBounds
            && ResolvedStyle == other.ResolvedStyle
            && HasText == other.HasText;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerTextPayload other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rect, Material, Text, Resource, ClipBounds, ResolvedStyle, HasText);

    public static bool operator ==(D3D12CompositionLayerTextPayload left, D3D12CompositionLayerTextPayload right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionLayerTextPayload left, D3D12CompositionLayerTextPayload right) => !left.Equals(right);
}

internal readonly struct D3D12CompositionLayerContentCacheKey(
    CompositionLayerId LayerId,
    int CommandStart,
    int CommandCount,
    int CommandHash,
    IFrameResourceResolver Resources,
    ulong ResourceFrameId,
    DisplayScale Scale) : IEquatable<D3D12CompositionLayerContentCacheKey>
{
    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public int CommandHash { get; } = CommandHash;
    public IFrameResourceResolver Resources { get; } = Resources;
    public ulong ResourceFrameId { get; } = ResourceFrameId;
    public DisplayScale Scale { get; } = Scale;

    public static D3D12CompositionLayerContentCacheKey Create(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionLayer layer,
        DisplayScale scale)
    {
        return new D3D12CompositionLayerContentCacheKey(
            layer.Id,
            layer.CommandStart,
            layer.CommandCount,
            ComputeCommandHash(commands.Slice(layer.CommandStart, layer.CommandCount)),
            resources,
            resources is FrameDrawingResources frameResources ? frameResources.FrameId : 0,
            scale.Normalize());
    }

    public bool Equals(D3D12CompositionLayerContentCacheKey other)
    {
        return LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && CommandHash == other.CommandHash
            && ReferenceEquals(Resources, other.Resources)
            && ResourceFrameId == other.ResourceFrameId
            && Scale == other.Scale;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerContentCacheKey other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(LayerId);
        hashCode.Add(CommandStart);
        hashCode.Add(CommandCount);
        hashCode.Add(CommandHash);
        hashCode.Add(RuntimeHelpers.GetHashCode(Resources));
        hashCode.Add(ResourceFrameId);
        hashCode.Add(Scale);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(D3D12CompositionLayerContentCacheKey left, D3D12CompositionLayerContentCacheKey right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionLayerContentCacheKey left, D3D12CompositionLayerContentCacheKey right) => !left.Equals(right);

    private static int ComputeCommandHash(ReadOnlySpan<DrawCommand> commands)
    {
        var hashCode = new HashCode();
        for (var i = 0; i < commands.Length; i++)
        {
            hashCode.Add(commands[i]);
        }

        return hashCode.ToHashCode();
    }
}

internal readonly struct D3D12CompositionLayerContentCacheEntry(
    D3D12CompositionLayerContentCacheKey Key,
    D3D12CompositionLayerContent Content)
{
    public D3D12CompositionLayerContentCacheKey Key { get; } = Key;
    public D3D12CompositionLayerContent Content { get; } = Content;
}

/// <summary>
/// D3D12 backend: renders FillRect commands as colored rectangles via D3D12Renderer2D.
/// Falls back to clear color for the background.
/// </summary>
internal sealed class D3D12DrawingBackend(D3D12Renderer renderer, DrawingBackendClipMode clipMode = DrawingBackendClipMode.Scissor) : IDrawingBackend, IDirtyRangeAware, IClipScissorCapability, IDeviceRecovery, ICompositionDrawingBackend
{
    private const int LinearGradientClampFallbackSegmentCount = 16;
    private const float LinearGradientClampEpsilon = 0.00001f;

    private struct ExecuteDiagnosticsAccumulator
    {
        private int _clippedCommandCount;
        private int _emptyIntersectionSkippedCount;
        private int _scissorStateChangeCount;
        private bool _hasPreviousScissor;
        private IntegerScissorRect _previousScissor;
        private EffectiveScissor _lastEffectiveScissor;
        private int _textClipSkippedCount;
        private EffectiveScissor _lastEffectiveTextClip;
        private DrawMaterialKind _selectedMaterialKind;
        private DrawMaterialFallbackReason _materialFallbackReason;
        private int _materialCommandCount;
        private int _solidColorMaterialCommandCount;
        private int _linearGradientMaterialCommandCount;
        private int _linearGradientSingleRectCommandCount;
        private int _linearGradientSegmentedCommandCount;
        private int _linearGradientSegmentRectCount;
        private int _materialFallbackCommandCount;

        public void AddCommandClip(in DrawCommand command)
        {
            if (command.ClipBounds.Width > 0 && command.ClipBounds.Height > 0)
            {
                _clippedCommandCount++;
            }
        }

        public void AddFillRectPlan(DrawingBackendClipMode clipMode, in D3D12FillRectScissorPlan scissorPlan)
        {
            _lastEffectiveScissor = scissorPlan.EffectiveScissor;
            if (scissorPlan.Skip)
            {
                _emptyIntersectionSkippedCount++;
                return;
            }

            if (clipMode == DrawingBackendClipMode.Scissor && (!_hasPreviousScissor || scissorPlan.RenderScissor != _previousScissor))
            {
                _scissorStateChangeCount++;
                _previousScissor = scissorPlan.RenderScissor;
                _hasPreviousScissor = true;
            }
        }

        public void AddTextClipPlan(in D3D12TextClipPlan textClipPlan)
        {
            _lastEffectiveTextClip = textClipPlan.EffectiveClip;
            if (textClipPlan.Skip)
            {
                _textClipSkippedCount++;
            }
        }

        public void AddMaterialOutput(in DrawMaterialOutputMappingResult result)
        {
            _materialCommandCount++;
            if (_selectedMaterialKind == DrawMaterialKind.None
                || result.FallbackApplied
                || result.MaterialKind > _selectedMaterialKind)
            {
                _selectedMaterialKind = result.MaterialKind;
            }

            if (_materialFallbackReason == DrawMaterialFallbackReason.None && result.FallbackApplied)
            {
                _materialFallbackReason = result.FallbackReason;
            }

            switch (result.MaterialKind)
            {
                case DrawMaterialKind.SolidColor:
                    _solidColorMaterialCommandCount++;
                    break;
                case DrawMaterialKind.LinearGradient:
                    _linearGradientMaterialCommandCount++;
                    break;
            }

            if (result.FallbackApplied)
            {
                _materialFallbackCommandCount++;
            }
        }

        public void AddLinearGradientRasterization(bool segmented, int segmentRectCount)
        {
            if (segmented)
            {
                _linearGradientSegmentedCommandCount++;
                _linearGradientSegmentRectCount += segmentRectCount;
            }
            else
            {
                _linearGradientSingleRectCommandCount++;
            }
        }

        public D3D12FillRectScissorDiagnostics FillRectDiagnostics =>
            new(_clippedCommandCount, _emptyIntersectionSkippedCount, _scissorStateChangeCount, _lastEffectiveScissor);

        public D3D12TextClipDiagnostics TextClipDiagnostics => new(_textClipSkippedCount, _lastEffectiveTextClip);

        public DrawMaterialOutputDiagnostics MaterialDiagnostics => new(
            ColorOutputKind.SdrSrgb,
            D3D12MaterialCapabilities,
            _selectedMaterialKind,
            _materialFallbackReason,
            _materialCommandCount,
            _solidColorMaterialCommandCount,
            _linearGradientMaterialCommandCount,
            _linearGradientSingleRectCommandCount,
            _linearGradientSegmentedCommandCount,
            _linearGradientSegmentRectCount,
            _materialFallbackCommandCount);
    }

    private readonly D3D12Renderer _renderer = renderer;
    private float _bgR, _bgG, _bgB, _bgA = 1.0f;
    private readonly FrameRenderList<D3D12Renderer2D.RectData> _rects = new();
    private readonly FrameRenderList<D3D12TextRun> _texts = new();
    private readonly D3D12CompositionLayerContentCache _compositionLayerContentCache = new();
    private IFrameResourceResolver? _resources;
    private IReadOnlyList<(int Start, int Count)> _dirtyCommandRanges = [];
    private int _clippedCommandCount;
    private int _emptyIntersectionSkippedCount;
    private int _scissorStateChangeCount;
    private int _textClipSkippedCount;
    private EffectiveScissor _lastEffectiveScissor = EffectiveScissor.Empty;
    private EffectiveScissor _lastEffectiveTextClip = EffectiveScissor.Empty;
    private DrawMaterialOutputDiagnostics _materialOutputDiagnostics;
    private FrameContext _frameContext;

    internal static DrawMaterialBackendCapabilities D3D12MaterialCapabilities =>
        DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient;

    private static DrawMaterialBackendCapabilities D3D12TextMaterialCapabilities => DrawMaterialBackendCapabilities.SolidColor;

    /// <summary>Dirty command ranges from the last SetDirtyCommandRanges call.</summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges => _dirtyCommandRanges;

    /// <summary>Number of commands with non-default clip bounds from the last Execute.</summary>
    public int ClippedCommandCount => _clippedCommandCount;

    public int EmptyIntersectionSkippedCount => _emptyIntersectionSkippedCount;

    public int ScissorStateChangeCount => _scissorStateChangeCount;

    public int TextClipSkippedCount => _textClipSkippedCount;

    public EffectiveScissor LastEffectiveScissor => _lastEffectiveScissor;

    public EffectiveScissor LastEffectiveTextClip => _lastEffectiveTextClip;

    internal DrawMaterialOutputDiagnostics MaterialOutputDiagnostics => _materialOutputDiagnostics;

    public DrawingBackendClipMode ClipMode { get; private set; } = clipMode;

    public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer | CompositionBackendCapabilities.LayerContentCache;

    public CompositionFramePacing FramePacing => CompositionFramePacing.BackendPresentation;

    /// <summary>Frame serial diagnostics from the D3D12 renderer (sync wait count, timing, etc.).</summary>
    internal D3D12Renderer.FrameSerialDiagnostics FrameSerialDiagnostics => _renderer.GetFrameSerialDiagnostics();

    public void SetClipMode(DrawingBackendClipMode clipMode)
    {
        ClipMode = clipMode;
    }

    internal static D3D12FillRectScissorPlan ResolveFillRectScissor(DrawingBackendClipMode clipMode, in DrawRect viewport, in DrawRect clipBounds)
    {
        var viewportWidth = (int)viewport.Width;
        var viewportHeight = (int)viewport.Height;
        var fullScissor = DrawingScissor.ToIntegerScissorRect(new EffectiveScissor(viewport, false), viewportWidth, viewportHeight);
        var effectiveScissor = DrawingScissor.ResolveEffectiveScissor(viewport, clipBounds);

        if (clipMode != DrawingBackendClipMode.Scissor)
        {
            return new D3D12FillRectScissorPlan(effectiveScissor, fullScissor, false);
        }

        if (effectiveScissor.IsEmpty)
        {
            return new D3D12FillRectScissorPlan(effectiveScissor, IntegerScissorRect.Empty, true);
        }

        var renderScissor = DrawingScissor.ToIntegerScissorRect(effectiveScissor, viewportWidth, viewportHeight);
        return new D3D12FillRectScissorPlan(effectiveScissor, renderScissor, false);
    }

    internal static D3D12TextClipPlan ResolveTextClip(DrawingBackendClipMode clipMode, in DrawRect viewport, in DrawRect clipBounds)
    {
        var effectiveClip = DrawingScissor.ResolveEffectiveScissor(viewport, clipBounds);
        if (clipMode != DrawingBackendClipMode.Scissor)
        {
            return new D3D12TextClipPlan(effectiveClip, false, false);
        }

        if (effectiveClip.IsEmpty)
        {
            return new D3D12TextClipPlan(effectiveClip, false, true);
        }

        return new D3D12TextClipPlan(effectiveClip, effectiveClip.Bounds != viewport, false);
    }

    internal static D3D12FillRectScissorDiagnostics ComputeFillRectScissorDiagnostics(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        DisplayScale scale = default)
    {
        scale = scale.Normalize();
        var diagnostics = new ExecuteDiagnosticsAccumulator();

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
            diagnostics.AddCommandClip(command);

            if (command.Kind != DrawCommandKind.FillRect)
            {
                continue;
            }

            var scissorPlan = ResolveFillRectScissor(clipMode, viewport, command.ClipBounds);
            diagnostics.AddFillRectPlan(clipMode, scissorPlan);
        }

        return diagnostics.FillRectDiagnostics;
    }

    internal static D3D12TextClipDiagnostics ComputeTextClipDiagnostics(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        DisplayScale scale = default)
    {
        scale = scale.Normalize();
        var diagnostics = new ExecuteDiagnosticsAccumulator();

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            var textClipPlan = ResolveTextClip(clipMode, viewport, command.ClipBounds);
            diagnostics.AddTextClipPlan(textClipPlan);
        }

        return diagnostics.TextClipDiagnostics;
    }

    public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
    {
        _dirtyCommandRanges = ranges;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
        _frameContext = new FrameContext(frameContext.Width, frameContext.Height, frameContext.Scale.Normalize(), frameContext.Timestamp);
        if (!_renderer.BeginFrame())
        {
            var deviceError = _renderer.DeviceError;
            throw new InvalidOperationException($"D3D12 begin frame failed: {(deviceError.IsNone ? "unknown device error" : deviceError.ToString())}");
        }

        _rects.Reset();
        _texts.Reset();
    }

    public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
    {
        _resources = resources;
        var viewportWidth = _renderer.Width;
        var viewportHeight = _renderer.Height;
        var viewport = new DrawRect(0, 0, viewportWidth, viewportHeight);
        var result = ExecuteCore(ClipMode, viewport, commands, resources, _frameContext.Scale, _rects, _texts);
        _clippedCommandCount = result.FillRectDiagnostics.ClippedCommandCount;
        _emptyIntersectionSkippedCount = result.FillRectDiagnostics.EmptyIntersectionSkippedCount;
        _scissorStateChangeCount = result.FillRectDiagnostics.ScissorStateChangeCount;
        _lastEffectiveScissor = result.FillRectDiagnostics.LastEffectiveScissor;
        _textClipSkippedCount = result.TextClipDiagnostics.TextClipSkippedCount;
        _lastEffectiveTextClip = result.TextClipDiagnostics.LastEffectiveTextClip;
        _materialOutputDiagnostics = result.MaterialDiagnostics;

        if (result.HasBackgroundColor)
        {
            _bgR = result.BackgroundColor.R / 255f;
            _bgG = result.BackgroundColor.G / 255f;
            _bgB = result.BackgroundColor.B / 255f;
            _bgA = result.BackgroundColor.A / 255f;
        }
    }

    public CompositionBackendExecutionResult ExecuteComposition(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame)
    {
        return ExecuteCompositionCore(commands, resources, compositionFrame).ToBackendExecutionResult();
    }

    internal D3D12CompositionExecuteDiagnostics ExecuteCompositionDiagnostic(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame)
    {
        return ExecuteCompositionCore(commands, resources, compositionFrame);
    }

    private D3D12CompositionExecuteDiagnostics ExecuteCompositionCore(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame)
    {
        _resources = resources;
        var viewportWidth = _renderer.Width;
        var viewportHeight = _renderer.Height;
        var viewport = new DrawRect(0, 0, viewportWidth, viewportHeight);
        var diagnostics = ExecuteCompositionDiagnosticCore(ClipMode, viewport, commands, resources, compositionFrame, _frameContext.Scale, _rects, _texts, _compositionLayerContentCache);

        var result = diagnostics.ExecuteResult;
        _clippedCommandCount = result.FillRectDiagnostics.ClippedCommandCount;
        _emptyIntersectionSkippedCount = result.FillRectDiagnostics.EmptyIntersectionSkippedCount;
        _scissorStateChangeCount = result.FillRectDiagnostics.ScissorStateChangeCount;
        _lastEffectiveScissor = result.FillRectDiagnostics.LastEffectiveScissor;
        _textClipSkippedCount = result.TextClipDiagnostics.TextClipSkippedCount;
        _lastEffectiveTextClip = result.TextClipDiagnostics.LastEffectiveTextClip;
        _materialOutputDiagnostics = result.MaterialDiagnostics;

        if (result.HasBackgroundColor)
        {
            _bgR = result.BackgroundColor.R / 255f;
            _bgG = result.BackgroundColor.G / 255f;
            _bgB = result.BackgroundColor.B / 255f;
            _bgA = result.BackgroundColor.A / 255f;
        }

        return diagnostics;
    }

    internal static D3D12ExecuteCoreResult ExecuteCore(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts)
    {
        scale = scale.Normalize();
        var diagnostics = new ExecuteDiagnosticsAccumulator();
        var outputMapping = ColorOutputMapping.SdrSrgb;
        var hasBackgroundColor = false;
        var backgroundColor = default(DrawColor);

        foreach (var logicalCommand in commands)
        {
            AppendSourceCommand(
                clipMode,
                viewport,
                logicalCommand,
                resources,
                scale,
                outputMapping,
                rects,
                texts,
                ref diagnostics,
                ref hasBackgroundColor,
                ref backgroundColor);
        }

        return new D3D12ExecuteCoreResult(
            diagnostics.FillRectDiagnostics,
            diagnostics.TextClipDiagnostics,
            diagnostics.MaterialDiagnostics,
            hasBackgroundColor,
            backgroundColor);
    }

    private static void AppendSourceCommand(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawCommand logicalCommand,
        IFrameResourceResolver resources,
        DisplayScale scale,
        ColorOutputMapping outputMapping,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        ref bool hasBackgroundColor,
        ref DrawColor backgroundColor)
    {
        var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
        diagnostics.AddCommandClip(command);

        switch (command.Kind)
        {
            case DrawCommandKind.FillRect:
                var fillMaterial = outputMapping.MapToSdr(command.Material, D3D12MaterialCapabilities);
                diagnostics.AddMaterialOutput(fillMaterial);
                AppendPhysicalFillMaterialRect(
                    clipMode,
                    viewport,
                    command.Rect,
                    command.Material,
                    fillMaterial,
                    outputMapping,
                    command.ClipBounds,
                    rects,
                    ref diagnostics,
                    ref hasBackgroundColor,
                    ref backgroundColor);
                break;
            case DrawCommandKind.DrawTextRun:
                var textMaterial = outputMapping.MapToSdr(command.Material, D3D12TextMaterialCapabilities);
                diagnostics.AddMaterialOutput(textMaterial);
                AppendPhysicalTextRun(
                    clipMode,
                    viewport,
                    command.Rect,
                    textMaterial.Color,
                    command.Text,
                    command.Resource,
                    command.ClipBounds,
                    ResolvePhysicalTextStyle(resources, command.Resource, scale),
                    resources,
                    texts,
                    ref diagnostics,
                    !resources.Resolve(command.Text).IsEmpty);
                break;
        }
    }

    private static void AppendPhysicalFillMaterialRect(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawRect rect,
        in DrawMaterial material,
        in DrawMaterialOutputMappingResult mappedMaterial,
        ColorOutputMapping outputMapping,
        in DrawRect clipBounds,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        ref bool hasBackgroundColor,
        ref DrawColor backgroundColor)
    {
        if (material.Kind == DrawMaterialKind.LinearGradient && !mappedMaterial.FallbackApplied)
        {
            AppendPhysicalLinearGradientRect(
                clipMode,
                viewport,
                rect,
                material,
                mappedMaterial.Color,
                outputMapping,
                clipBounds,
                rects,
                ref diagnostics,
                ref hasBackgroundColor,
                ref backgroundColor);
            return;
        }

        AppendPhysicalFillRect(
            clipMode,
            viewport,
            rect,
            mappedMaterial.Color,
            clipBounds,
            rects,
            ref diagnostics,
            ref hasBackgroundColor,
            ref backgroundColor);
    }

    private static void AppendPhysicalFillRect(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawRect rect,
        in DrawColor color,
        in DrawRect clipBounds,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        ref bool hasBackgroundColor,
        ref DrawColor backgroundColor)
    {
        var scissorPlan = ResolveFillRectScissor(clipMode, viewport, clipBounds);
        diagnostics.AddFillRectPlan(clipMode, scissorPlan);
        if (scissorPlan.Skip)
        {
            return;
        }

        if (rects.Count == 0 && !hasBackgroundColor)
        {
            backgroundColor = color;
            hasBackgroundColor = true;
        }

        AddPhysicalRectData(rect, color, scissorPlan.RenderScissor, rects);
    }

    private static void AppendPhysicalLinearGradientRect(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawRect rect,
        in DrawMaterial material,
        in DrawColor fallbackColor,
        ColorOutputMapping outputMapping,
        in DrawRect clipBounds,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        ref bool hasBackgroundColor,
        ref DrawColor backgroundColor)
    {
        var scissorPlan = ResolveFillRectScissor(clipMode, viewport, clipBounds);
        diagnostics.AddFillRectPlan(clipMode, scissorPlan);
        if (scissorPlan.Skip)
        {
            return;
        }

        var representativeColor = ResolveLinearGradientRepresentativeColor(material, fallbackColor, outputMapping);
        if (rects.Count == 0 && !hasBackgroundColor)
        {
            backgroundColor = representativeColor;
            hasBackgroundColor = true;
        }

        if (rect.Width <= 0f || rect.Height <= 0f)
        {
            diagnostics.AddLinearGradientRasterization(segmented: false, segmentRectCount: 1);
            AddPhysicalRectData(rect, representativeColor, scissorPlan.RenderScissor, rects);
            return;
        }

        if (CanRepresentLinearGradientAsSingleVertexRect(material, rect.Width, rect.Height))
        {
            AddPhysicalGradientRectData(
                rect,
                representativeColor,
                outputMapping.MapToSdr(SampleLinearGradient(material, 0f, 0f)),
                outputMapping.MapToSdr(SampleLinearGradient(material, rect.Width, 0f)),
                outputMapping.MapToSdr(SampleLinearGradient(material, rect.Width, rect.Height)),
                outputMapping.MapToSdr(SampleLinearGradient(material, 0f, rect.Height)),
                scissorPlan.RenderScissor,
                rects);
            diagnostics.AddLinearGradientRasterization(segmented: false, segmentRectCount: 1);
            return;
        }

        AppendPhysicalSegmentedLinearGradientRect(
            rect,
            material,
            outputMapping,
            scissorPlan.RenderScissor,
            rects);
        diagnostics.AddLinearGradientRasterization(segmented: true, segmentRectCount: LinearGradientClampFallbackSegmentCount);
    }

    private static DrawColor ResolveLinearGradientRepresentativeColor(
        in DrawMaterial material,
        in DrawColor fallbackColor,
        ColorOutputMapping outputMapping) =>
        IsDegenerateLinearGradient(material)
            ? outputMapping.MapToSdr(material.Color)
            : fallbackColor;

    private static Color SampleLinearGradient(in DrawMaterial material, float x, float y)
    {
        var dx = material.EndPoint.X - material.StartPoint.X;
        var dy = material.EndPoint.Y - material.StartPoint.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= float.Epsilon
            ? 0f
            : Math.Clamp(ProjectLinearGradient(material, x, y, dx, dy, lengthSquared), 0f, 1f);

        return Color.FromLinearBt2020(
            Lerp(material.Color.LinearBt2020R, material.EndColor.LinearBt2020R, t),
            Lerp(material.Color.LinearBt2020G, material.EndColor.LinearBt2020G, t),
            Lerp(material.Color.LinearBt2020B, material.EndColor.LinearBt2020B, t),
            Lerp(material.Color.A, material.EndColor.A, t));
    }

    private static bool CanRepresentLinearGradientAsSingleVertexRect(in DrawMaterial material, float width, float height)
    {
        var dx = material.EndPoint.X - material.StartPoint.X;
        var dy = material.EndPoint.Y - material.StartPoint.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= float.Epsilon)
        {
            return true;
        }

        var topLeft = ProjectLinearGradient(material, 0f, 0f, dx, dy, lengthSquared);
        var topRight = ProjectLinearGradient(material, width, 0f, dx, dy, lengthSquared);
        var bottomRight = ProjectLinearGradient(material, width, height, dx, dy, lengthSquared);
        var bottomLeft = ProjectLinearGradient(material, 0f, height, dx, dy, lengthSquared);
        var min = MathF.Min(MathF.Min(topLeft, topRight), MathF.Min(bottomRight, bottomLeft));
        var max = MathF.Max(MathF.Max(topLeft, topRight), MathF.Max(bottomRight, bottomLeft));

        return (min >= -LinearGradientClampEpsilon && max <= 1f + LinearGradientClampEpsilon)
            || max <= LinearGradientClampEpsilon
            || min >= 1f - LinearGradientClampEpsilon;
    }

    private static bool IsDegenerateLinearGradient(in DrawMaterial material)
    {
        var dx = material.EndPoint.X - material.StartPoint.X;
        var dy = material.EndPoint.Y - material.StartPoint.Y;
        return dx * dx + dy * dy <= float.Epsilon;
    }

    private static float ProjectLinearGradient(
        in DrawMaterial material,
        float x,
        float y,
        float dx,
        float dy,
        float lengthSquared) =>
        ((x - material.StartPoint.X) * dx + (y - material.StartPoint.Y) * dy) / lengthSquared;

    private static void AppendPhysicalSegmentedLinearGradientRect(
        in DrawRect rect,
        in DrawMaterial material,
        ColorOutputMapping outputMapping,
        in IntegerScissorRect scissor,
        FrameRenderList<D3D12Renderer2D.RectData> rects)
    {
        var dx = material.EndPoint.X - material.StartPoint.X;
        var dy = material.EndPoint.Y - material.StartPoint.Y;
        var splitHorizontally = MathF.Abs(dx) >= MathF.Abs(dy);
        for (var segment = 0; segment < LinearGradientClampFallbackSegmentCount; segment++)
        {
            var start = segment / (float)LinearGradientClampFallbackSegmentCount;
            var end = (segment + 1) / (float)LinearGradientClampFallbackSegmentCount;
            float x0;
            float y0;
            float x1;
            float y1;
            DrawRect segmentRect;
            if (splitHorizontally)
            {
                x0 = rect.Width * start;
                y0 = 0f;
                x1 = rect.Width * end;
                y1 = rect.Height;
                segmentRect = new DrawRect(rect.X + x0, rect.Y, x1 - x0, rect.Height);
            }
            else
            {
                x0 = 0f;
                y0 = rect.Height * start;
                x1 = rect.Width;
                y1 = rect.Height * end;
                segmentRect = new DrawRect(rect.X, rect.Y + y0, rect.Width, y1 - y0);
            }

            var representativeColor = outputMapping.MapToSdr(SampleLinearGradient(
                material,
                (x0 + x1) * 0.5f,
                (y0 + y1) * 0.5f));
            AddPhysicalGradientRectData(
                segmentRect,
                representativeColor,
                outputMapping.MapToSdr(SampleLinearGradient(material, x0, y0)),
                outputMapping.MapToSdr(SampleLinearGradient(material, x1, y0)),
                outputMapping.MapToSdr(SampleLinearGradient(material, x1, y1)),
                outputMapping.MapToSdr(SampleLinearGradient(material, x0, y1)),
                scissor,
                rects);
        }
    }

    private static float Lerp(float start, float end, float t) => start + (end - start) * t;

    private static void AddPhysicalRectData(
        in DrawRect rect,
        in DrawColor color,
        in IntegerScissorRect scissor,
        FrameRenderList<D3D12Renderer2D.RectData> rects)
    {
        rects.Add(new D3D12Renderer2D.RectData(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f,
            scissor));
    }

    private static void AddPhysicalGradientRectData(
        in DrawRect rect,
        in DrawColor representativeColor,
        in DrawColor topLeft,
        in DrawColor topRight,
        in DrawColor bottomRight,
        in DrawColor bottomLeft,
        in IntegerScissorRect scissor,
        FrameRenderList<D3D12Renderer2D.RectData> rects)
    {
        rects.Add(new D3D12Renderer2D.RectData(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            representativeColor.R / 255f,
            representativeColor.G / 255f,
            representativeColor.B / 255f,
            representativeColor.A / 255f,
            scissor,
            ToVector4(topLeft),
            ToVector4(topRight),
            ToVector4(bottomRight),
            ToVector4(bottomLeft)));
    }

    private static System.Numerics.Vector4 ToVector4(in DrawColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private static void AppendPhysicalTextRun(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawRect rect,
        in DrawColor color,
        TextSlice text,
        ResourceHandle resource,
        in DrawRect clipBounds,
        in TextStyle resolvedStyle,
        IFrameResourceResolver resources,
        FrameRenderList<D3D12TextRun> texts,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        bool addTextRun = true)
    {
        var textClipPlan = ResolveTextClip(clipMode, viewport, clipBounds);
        diagnostics.AddTextClipPlan(textClipPlan);
        if (textClipPlan.Skip || !addTextRun)
        {
            return;
        }

        texts.Add(new D3D12TextRun(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f,
            text,
            resource,
            textClipPlan.EffectiveClip,
            textClipPlan.ClipEnabled,
            resolvedStyle,
            resources));
    }

    internal static D3D12CompositionExecuteDiagnostics ExecuteCompositionDiagnosticCore(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        D3D12CompositionLayerContentCache? layerContentCache = null)
    {
        if (!compositionFrame.IsValidForCommandCount(commands.Length))
        {
            throw new ArgumentException("Composition frame layer range must reference a non-empty range inside the command span.", nameof(compositionFrame));
        }

        scale = scale.Normalize();
        var layerCount = compositionFrame.LayerCount;
        var firstLayer = compositionFrame.Layer;
        if (layerContentCache is not null
            && TryExecuteCompositionWithLayerCache(
                layerContentCache,
                clipMode,
                viewport,
                commands,
                resources,
                compositionFrame,
                scale,
                rects,
                texts,
                out var cachedDiagnostics))
        {
            return cachedDiagnostics;
        }

        Span<DrawCommand> inlineCommands = stackalloc DrawCommand[64];
        Span<DrawCommand> composedCommands = commands.Length <= inlineCommands.Length ? inlineCommands[..commands.Length] : new DrawCommand[commands.Length];
        var translatedCommands = 0;
        var opacityAppliedCommands = 0;

        for (var i = 0; i < commands.Length; i++)
        {
            var command = commands[i];
            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var layer = compositionFrame.GetLayer(layerIndex);
                if ((uint)(i - layer.CommandStart) >= (uint)layer.CommandCount)
                {
                    continue;
                }

                command = ApplyComposition(command, layer);
                if (!layer.Transform.IsIdentity)
                {
                    translatedCommands++;
                }

                if (!layer.Opacity.IsOpaque)
                {
                    opacityAppliedCommands++;
                }
            }

            composedCommands[i] = command;
        }

        var executeResult = ExecuteCore(clipMode, viewport, composedCommands, resources, scale, rects, texts);
        return new D3D12CompositionExecuteDiagnostics(
            D3D12Backed: true,
            LayerCount: layerCount,
            CommandCount: commands.Length,
            LayerCommandStart: firstLayer.CommandStart,
            LayerCommandCount: firstLayer.CommandCount,
            TranslatedCommands: translatedCommands,
            OpacityAppliedCommands: opacityAppliedCommands,
            LayerCacheHits: 0,
            LayerCacheMisses: 0,
            CachedLayerCommands: 0,
            AppliedTransform: firstLayer.Transform,
            AppliedOpacity: firstLayer.Opacity,
            ExecuteResult: executeResult);
    }

    private static bool TryExecuteCompositionWithLayerCache(
        D3D12CompositionLayerContentCache layerContentCache,
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        out D3D12CompositionExecuteDiagnostics diagnostics)
    {
        diagnostics = default;
        if (HasOverlappingLayerCommandRanges(compositionFrame))
        {
            return false;
        }

        var accumulator = new ExecuteDiagnosticsAccumulator();
        var outputMapping = ColorOutputMapping.SdrSrgb;
        var hasBackgroundColor = false;
        var backgroundColor = default(DrawColor);
        var translatedCommands = 0;
        var opacityAppliedCommands = 0;
        var layerCacheHits = 0;
        var layerCacheMisses = 0;
        var cachedLayerCommands = 0;

        for (var commandIndex = 0; commandIndex < commands.Length;)
        {
            if (TryGetLayerStartingAt(compositionFrame, commandIndex, out var layer))
            {
                var content = layerContentCache.GetOrBuild(commands, resources, layer, scale, out var cacheHit);
                if (cacheHit)
                {
                    layerCacheHits++;
                }
                else
                {
                    layerCacheMisses++;
                }

                cachedLayerCommands += content.SourceCommandCount;
                if (!layer.Transform.IsIdentity)
                {
                    translatedCommands += layer.CommandCount;
                }

                if (!layer.Opacity.IsOpaque)
                {
                    opacityAppliedCommands += layer.CommandCount;
                }

                AppendCachedLayerContent(
                    content,
                    layer,
                    clipMode,
                    viewport,
                    resources,
                    scale,
                    outputMapping,
                    rects,
                    texts,
                    ref accumulator,
                    ref hasBackgroundColor,
                    ref backgroundColor);
                commandIndex += layer.CommandCount;
                continue;
            }

            AppendSourceCommand(
                clipMode,
                viewport,
                commands[commandIndex],
                resources,
                scale,
                outputMapping,
                rects,
                texts,
                ref accumulator,
                ref hasBackgroundColor,
                ref backgroundColor);
            commandIndex++;
        }

        diagnostics = new D3D12CompositionExecuteDiagnostics(
            D3D12Backed: true,
            LayerCount: compositionFrame.LayerCount,
            CommandCount: commands.Length,
            LayerCommandStart: compositionFrame.Layer.CommandStart,
            LayerCommandCount: compositionFrame.Layer.CommandCount,
            TranslatedCommands: translatedCommands,
            OpacityAppliedCommands: opacityAppliedCommands,
            LayerCacheHits: layerCacheHits,
            LayerCacheMisses: layerCacheMisses,
            CachedLayerCommands: cachedLayerCommands,
            AppliedTransform: compositionFrame.Layer.Transform,
            AppliedOpacity: compositionFrame.Layer.Opacity,
            ExecuteResult: new D3D12ExecuteCoreResult(
                accumulator.FillRectDiagnostics,
                accumulator.TextClipDiagnostics,
                accumulator.MaterialDiagnostics,
                hasBackgroundColor,
                backgroundColor));
        return true;
    }

    private static bool HasOverlappingLayerCommandRanges(in CompositionFrame compositionFrame)
    {
        var layerCount = compositionFrame.LayerCount;
        for (var i = 0; i < layerCount; i++)
        {
            var left = compositionFrame.GetLayer(i);
            var leftEnd = left.CommandStart + left.CommandCount;
            for (var j = i + 1; j < layerCount; j++)
            {
                var right = compositionFrame.GetLayer(j);
                var rightEnd = right.CommandStart + right.CommandCount;
                if (left.CommandStart < rightEnd && right.CommandStart < leftEnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetLayerStartingAt(in CompositionFrame compositionFrame, int commandIndex, out CompositionLayer layer)
    {
        for (var i = 0; i < compositionFrame.LayerCount; i++)
        {
            layer = compositionFrame.GetLayer(i);
            if (layer.CommandStart == commandIndex)
            {
                return true;
            }
        }

        layer = default;
        return false;
    }

    private static void AppendCachedLayerContent(
        D3D12CompositionLayerContent content,
        in CompositionLayer layer,
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        IFrameResourceResolver resources,
        DisplayScale scale,
        ColorOutputMapping outputMapping,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        ref ExecuteDiagnosticsAccumulator diagnostics,
        ref bool hasBackgroundColor,
        ref DrawColor backgroundColor)
    {
        var rectPayloads = content.Rects;
        for (var i = 0; i < rectPayloads.Length; i++)
        {
            var payload = rectPayloads[i];
            var command = ScaleCommandToPhysicalPixels(DrawCommand.FromMaterial(
                DrawCommandKind.FillRect,
                Rect: Translate(payload.Rect, layer.Transform),
                Material: ApplyOpacity(payload.Material, layer.Opacity),
                ClipBounds: ResolveComposedClip(payload.ClipBounds, layer)), scale);
            diagnostics.AddCommandClip(command);
            var material = outputMapping.MapToSdr(command.Material, D3D12MaterialCapabilities);
            diagnostics.AddMaterialOutput(material);
            AppendPhysicalFillMaterialRect(
                clipMode,
                viewport,
                command.Rect,
                command.Material,
                material,
                outputMapping,
                command.ClipBounds,
                rects,
                ref diagnostics,
                ref hasBackgroundColor,
                ref backgroundColor);
        }

        var textPayloads = content.Texts;
        for (var i = 0; i < textPayloads.Length; i++)
        {
            var payload = textPayloads[i];
            var command = ScaleCommandToPhysicalPixels(DrawCommand.FromMaterial(
                DrawCommandKind.DrawTextRun,
                Rect: Translate(payload.Rect, layer.Transform),
                Material: ApplyOpacity(payload.Material, layer.Opacity),
                Resource: payload.Resource,
                Text: payload.Text,
                ClipBounds: ResolveComposedClip(payload.ClipBounds, layer)), scale);
            diagnostics.AddCommandClip(command);
            var material = outputMapping.MapToSdr(command.Material, D3D12TextMaterialCapabilities);
            diagnostics.AddMaterialOutput(material);
            AppendPhysicalTextRun(
                clipMode,
                viewport,
                command.Rect,
                material.Color,
                payload.Text,
                payload.Resource,
                command.ClipBounds,
                payload.ResolvedStyle,
                resources,
                texts,
                ref diagnostics,
                payload.HasText);
        }
    }

    private static DrawCommand ApplyComposition(in DrawCommand command, in CompositionLayer layer)
    {
        var transform = layer.Transform;
        var opacity = layer.Opacity;
        return DrawCommand.FromMaterial(
            command.Kind,
            Rect: Translate(command.Rect, transform),
            Material: ApplyOpacity(command.Material, opacity),
            Resource: command.Resource,
            Text: command.Text,
            ClipBounds: ResolveComposedClip(command.ClipBounds, layer),
            StrokeWidth: command.StrokeWidth,
            Transform: command.Transform,
            ZIndex: command.ZIndex);
    }

    private static DrawRect ResolveComposedClip(in DrawRect clipBounds, in CompositionLayer layer)
    {
        if (!layer.HasFixedClip)
        {
            return Translate(clipBounds, layer.Transform);
        }

        if (clipBounds == default)
        {
            return layer.ClipBounds;
        }

        if (clipBounds.Width <= 0f || clipBounds.Height <= 0f)
        {
            return clipBounds;
        }

        return Intersect(clipBounds, layer.ClipBounds);
    }

    internal static TextStyle ScaleTextStyleToPhysicalPixels(TextStyle style, DisplayScale scale)
    {
        scale = scale.Normalize();
        if (scale.IsIdentity)
        {
            return style;
        }

        return new TextStyle(
            style.FontFamily,
            style.FontSize * scale.TextScale,
            style.FontWeight,
            style.FontStyle,
            style.FontStretch,
            style.HorizontalAlignment,
            style.VerticalAlignment,
            style.Wrapping);
    }

    private static DrawCommand ScaleCommandToPhysicalPixels(in DrawCommand command, DisplayScale scale)
    {
        return scale.IsIdentity ? command : command.Scale(scale);
    }

    private static DrawRect Translate(in DrawRect rect, in CompositionTransform transform)
    {
        return rect.Width == 0f && rect.Height == 0f
            ? rect
            : new DrawRect(rect.X + transform.TranslateX, rect.Y + transform.TranslateY, rect.Width, rect.Height);
    }

    private static DrawRect Intersect(in DrawRect left, in DrawRect right)
    {
        var x0 = MathF.Max(left.X, right.X);
        var y0 = MathF.Max(left.Y, right.Y);
        var x1 = MathF.Min(left.X + left.Width, right.X + right.Width);
        var y1 = MathF.Min(left.Y + left.Height, right.Y + right.Height);
        return x1 <= x0 || y1 <= y0 ? new DrawRect(x0, y0, -1f, -1f) : new DrawRect(x0, y0, x1 - x0, y1 - y0);
    }

    private static Color ApplyOpacity(Color color, CompositionOpacity opacity)
    {
        var normalized = opacity.Normalized;
        return normalized == 1f ? color : color.WithOpacity(normalized);
    }

    private static DrawMaterial ApplyOpacity(DrawMaterial material, CompositionOpacity opacity)
    {
        var normalized = opacity.Normalized;
        return normalized == 1f ? material : material.WithOpacity(normalized);
    }

    private static TextStyle ResolvePhysicalTextStyle(IFrameResourceResolver resources, ResourceHandle handle, DisplayScale scale)
    {
        return ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(handle), scale);
    }

    public void EndFrame()
    {
        var rects = _rects.Span;
        var texts = _texts.Span;
        var resources = _resources ?? FrameDrawingResources.Empty;
        _resources = null;

        if (rects.Length > 0 || texts.Length > 0)
        {
            _renderer.RenderFrame(rects, texts, resources, _bgR, _bgG, _bgB, _bgA);
        }
        else
        {
            _renderer.ClearAndPresent(_bgR, _bgG, _bgB, _bgA);
        }
    }

    public bool IsDeviceRemoved => _renderer.IsDeviceRemoved;

    public bool TryRecover()
    {
        if (!_renderer.TryRecover())
        {
            return false;
        }

        _compositionLayerContentCache.Clear();
        return true;
    }

    public void Dispose()
    {
        _compositionLayerContentCache.Clear();
        _rects.Dispose();
        _texts.Dispose();
        _renderer.Dispose();
    }
}
