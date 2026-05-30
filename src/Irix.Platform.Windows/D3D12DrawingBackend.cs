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
    bool HasBackgroundColor,
    DrawColor BackgroundColor) : IEquatable<D3D12ExecuteCoreResult>
{

    public D3D12FillRectScissorDiagnostics FillRectDiagnostics { get; } = FillRectDiagnostics;
    public D3D12TextClipDiagnostics TextClipDiagnostics { get; } = TextClipDiagnostics;
    public bool HasBackgroundColor { get; } = HasBackgroundColor;
    public DrawColor BackgroundColor { get; } = BackgroundColor;

    public bool Equals(D3D12ExecuteCoreResult other)
    {
        return FillRectDiagnostics == other.FillRectDiagnostics
            && TextClipDiagnostics == other.TextClipDiagnostics
            && HasBackgroundColor == other.HasBackgroundColor
            && BackgroundColor == other.BackgroundColor;
    }

    public override bool Equals(object? obj) => obj is D3D12ExecuteCoreResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FillRectDiagnostics, TextClipDiagnostics, HasBackgroundColor, BackgroundColor);

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
    bool RenderTargetBacked,
    int RenderTargetCacheHits,
    int RenderTargetCacheMisses,
    int CachedRenderTargetCommands,
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
    public bool RenderTargetBacked { get; } = RenderTargetBacked;
    public int RenderTargetCacheHits { get; } = RenderTargetCacheHits;
    public int RenderTargetCacheMisses { get; } = RenderTargetCacheMisses;
    public int CachedRenderTargetCommands { get; } = CachedRenderTargetCommands;
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
            && RenderTargetBacked == other.RenderTargetBacked
            && RenderTargetCacheHits == other.RenderTargetCacheHits
            && RenderTargetCacheMisses == other.RenderTargetCacheMisses
            && CachedRenderTargetCommands == other.CachedRenderTargetCommands
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
        hash.Add(RenderTargetBacked);
        hash.Add(RenderTargetCacheHits);
        hash.Add(RenderTargetCacheMisses);
        hash.Add(CachedRenderTargetCommands);
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
            CachedLayerCommands,
            RenderTargetBacked,
            RenderTargetCacheHits,
            RenderTargetCacheMisses,
            CachedRenderTargetCommands);
    }

    public D3D12CompositionExecuteDiagnostics WithRenderTargetDiagnostics(in D3D12CompositionRenderTargetCacheDiagnostics diagnostics)
    {
        return new D3D12CompositionExecuteDiagnostics(
            D3D12Backed,
            LayerCount,
            CommandCount,
            LayerCommandStart,
            LayerCommandCount,
            TranslatedCommands,
            OpacityAppliedCommands,
            LayerCacheHits,
            LayerCacheMisses,
            CachedLayerCommands,
            diagnostics.RenderTargetBacked,
            diagnostics.RenderTargetCacheHits,
            diagnostics.RenderTargetCacheMisses,
            diagnostics.CachedRenderTargetCommands,
            AppliedTransform,
            AppliedOpacity,
            ExecuteResult);
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
    public bool SupportsRenderTargetCache => _rects.Length > 0 && _texts.Length == 0;

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
                rects[rectIndex++] = new D3D12CompositionLayerRectPayload(command.Rect, command.Color, command.ClipBounds);
            }
            else if (command.Kind == DrawCommandKind.DrawTextRun)
            {
                texts[textIndex++] = new D3D12CompositionLayerTextPayload(
                    command.Rect,
                    command.Color,
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
    DrawColor Color,
    DrawRect ClipBounds) : IEquatable<D3D12CompositionLayerRectPayload>
{
    public DrawRect Rect { get; } = Rect;
    public DrawColor Color { get; } = Color;
    public DrawRect ClipBounds { get; } = ClipBounds;

    public bool Equals(D3D12CompositionLayerRectPayload other) => Rect == other.Rect && Color == other.Color && ClipBounds == other.ClipBounds;

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerRectPayload other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rect, Color, ClipBounds);

    public static bool operator ==(D3D12CompositionLayerRectPayload left, D3D12CompositionLayerRectPayload right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionLayerRectPayload left, D3D12CompositionLayerRectPayload right) => !left.Equals(right);
}

internal readonly struct D3D12CompositionLayerTextPayload(
    DrawRect Rect,
    DrawColor Color,
    TextSlice Text,
    ResourceHandle Resource,
    DrawRect ClipBounds,
    TextStyle ResolvedStyle,
    bool HasText) : IEquatable<D3D12CompositionLayerTextPayload>
{
    public DrawRect Rect { get; } = Rect;
    public DrawColor Color { get; } = Color;
    public TextSlice Text { get; } = Text;
    public ResourceHandle Resource { get; } = Resource;
    public DrawRect ClipBounds { get; } = ClipBounds;
    public TextStyle ResolvedStyle { get; } = ResolvedStyle;
    public bool HasText { get; } = HasText;

    public bool Equals(D3D12CompositionLayerTextPayload other)
    {
        return Rect == other.Rect
            && Color == other.Color
            && Text == other.Text
            && Resource == other.Resource
            && ClipBounds == other.ClipBounds
            && ResolvedStyle == other.ResolvedStyle
            && HasText == other.HasText;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerTextPayload other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rect, Color, Text, Resource, ClipBounds, ResolvedStyle, HasText);

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

        public D3D12FillRectScissorDiagnostics FillRectDiagnostics =>
            new(_clippedCommandCount, _emptyIntersectionSkippedCount, _scissorStateChangeCount, _lastEffectiveScissor);

        public D3D12TextClipDiagnostics TextClipDiagnostics => new(_textClipSkippedCount, _lastEffectiveTextClip);
    }

    private readonly D3D12Renderer _renderer = renderer;
    private float _bgR, _bgG, _bgB, _bgA = 1.0f;
    private readonly FrameRenderList<D3D12Renderer2D.RectData> _rects = new();
    private readonly FrameRenderList<D3D12TextRun> _texts = new();
    private readonly FrameRenderList<D3D12CompositionLayerRenderTargetRequest> _compositionLayerRenderTargets = new();
    private readonly D3D12CompositionLayerContentCache _compositionLayerContentCache = new();
    private IFrameResourceResolver? _resources;
    private IReadOnlyList<(int Start, int Count)> _dirtyCommandRanges = [];
    private int _clippedCommandCount;
    private int _emptyIntersectionSkippedCount;
    private int _scissorStateChangeCount;
    private int _textClipSkippedCount;
    private EffectiveScissor _lastEffectiveScissor = EffectiveScissor.Empty;
    private EffectiveScissor _lastEffectiveTextClip = EffectiveScissor.Empty;
    private FrameContext _frameContext;

    /// <summary>Dirty command ranges from the last SetDirtyCommandRanges call.</summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges => _dirtyCommandRanges;

    /// <summary>Number of commands with non-default clip bounds from the last Execute.</summary>
    public int ClippedCommandCount => _clippedCommandCount;

    public int EmptyIntersectionSkippedCount => _emptyIntersectionSkippedCount;

    public int ScissorStateChangeCount => _scissorStateChangeCount;

    public int TextClipSkippedCount => _textClipSkippedCount;

    public EffectiveScissor LastEffectiveScissor => _lastEffectiveScissor;

    public EffectiveScissor LastEffectiveTextClip => _lastEffectiveTextClip;

    public DrawingBackendClipMode ClipMode { get; private set; } = clipMode;

    public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer | CompositionBackendCapabilities.LayerContentCache | CompositionBackendCapabilities.RenderTargetCache;

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
        _compositionLayerRenderTargets.Reset();
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
        D3D12CompositionExecuteDiagnostics diagnostics;
        if (TryExecuteCompositionWithRenderTargetCache(
            _compositionLayerContentCache,
            ClipMode,
            viewport,
            commands,
            resources,
            compositionFrame,
            _frameContext.Scale,
            _rects,
            _texts,
            _compositionLayerRenderTargets,
            out var renderTargetDiagnostics))
        {
            var cacheDiagnostics = _renderer.PrepareCompositionLayerRenderTargets(_compositionLayerRenderTargets.Span, _frameContext.Scale);
            diagnostics = renderTargetDiagnostics.WithRenderTargetDiagnostics(cacheDiagnostics);
        }
        else
        {
            diagnostics = ExecuteCompositionDiagnosticCore(ClipMode, viewport, commands, resources, compositionFrame, _frameContext.Scale, _rects, _texts, _compositionLayerContentCache);
        }

        var result = diagnostics.ExecuteResult;
        _clippedCommandCount = result.FillRectDiagnostics.ClippedCommandCount;
        _emptyIntersectionSkippedCount = result.FillRectDiagnostics.EmptyIntersectionSkippedCount;
        _scissorStateChangeCount = result.FillRectDiagnostics.ScissorStateChangeCount;
        _lastEffectiveScissor = result.FillRectDiagnostics.LastEffectiveScissor;
        _textClipSkippedCount = result.TextClipDiagnostics.TextClipSkippedCount;
        _lastEffectiveTextClip = result.TextClipDiagnostics.LastEffectiveTextClip;

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
                rects,
                texts,
                ref diagnostics,
                ref hasBackgroundColor,
                ref backgroundColor);
        }

        return new D3D12ExecuteCoreResult(
            diagnostics.FillRectDiagnostics,
            diagnostics.TextClipDiagnostics,
            hasBackgroundColor,
            backgroundColor);
    }

    private static void AppendSourceCommand(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        in DrawCommand logicalCommand,
        IFrameResourceResolver resources,
        DisplayScale scale,
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
                AppendPhysicalFillRect(
                    clipMode,
                    viewport,
                    command.Rect,
                    command.Color,
                    command.ClipBounds,
                    rects,
                    ref diagnostics,
                    ref hasBackgroundColor,
                    ref backgroundColor);
                break;
            case DrawCommandKind.DrawTextRun:
                AppendPhysicalTextRun(
                    clipMode,
                    viewport,
                    command.Rect,
                    command.Color,
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

        rects.Add(new D3D12Renderer2D.RectData(
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f,
            scissorPlan.RenderScissor));
    }

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
            RenderTargetBacked: false,
            RenderTargetCacheHits: 0,
            RenderTargetCacheMisses: 0,
            CachedRenderTargetCommands: 0,
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
            RenderTargetBacked: false,
            RenderTargetCacheHits: 0,
            RenderTargetCacheMisses: 0,
            CachedRenderTargetCommands: 0,
            AppliedTransform: compositionFrame.Layer.Transform,
            AppliedOpacity: compositionFrame.Layer.Opacity,
            ExecuteResult: new D3D12ExecuteCoreResult(
                accumulator.FillRectDiagnostics,
                accumulator.TextClipDiagnostics,
                hasBackgroundColor,
                backgroundColor));
        return true;
    }

    internal static bool TryExecuteCompositionWithRenderTargetCache(
        D3D12CompositionLayerContentCache layerContentCache,
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        FrameRenderList<D3D12CompositionLayerRenderTargetRequest> layerRenderTargets,
        out D3D12CompositionExecuteDiagnostics diagnostics)
    {
        diagnostics = default;
        if (!compositionFrame.IsValidForCommandCount(commands.Length))
        {
            throw new ArgumentException("Composition frame layer range must reference a non-empty range inside the command span.", nameof(compositionFrame));
        }

        if (!HasRenderTargetPresentableOrder(compositionFrame, commands.Length))
        {
            return false;
        }

        var accumulator = new ExecuteDiagnosticsAccumulator();
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
                if (!content.SupportsRenderTargetCache)
                {
                    rects.Reset();
                    texts.Reset();
                    layerRenderTargets.Reset();
                    return false;
                }

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

                AccumulateRenderTargetLayerDiagnostics(content, layer, clipMode, viewport, scale, ref accumulator);
                layerRenderTargets.Add(new D3D12CompositionLayerRenderTargetRequest(
                    D3D12CompositionLayerContentCacheKey.Create(commands, resources, layer, scale),
                    content,
                    layer,
                    clipMode));
                commandIndex += layer.CommandCount;
                continue;
            }

            AppendSourceCommand(
                clipMode,
                viewport,
                commands[commandIndex],
                resources,
                scale,
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
            RenderTargetBacked: false,
            RenderTargetCacheHits: 0,
            RenderTargetCacheMisses: 0,
            CachedRenderTargetCommands: 0,
            AppliedTransform: compositionFrame.Layer.Transform,
            AppliedOpacity: compositionFrame.Layer.Opacity,
            ExecuteResult: new D3D12ExecuteCoreResult(
                accumulator.FillRectDiagnostics,
                accumulator.TextClipDiagnostics,
                hasBackgroundColor,
                backgroundColor));
        return true;
    }

    private static bool HasRenderTargetPresentableOrder(in CompositionFrame compositionFrame, int commandCount)
    {
        if (HasOverlappingLayerCommandRanges(compositionFrame))
        {
            return false;
        }

        var hasLayer = false;
        for (var commandIndex = 0; commandIndex < commandCount;)
        {
            if (TryGetLayerStartingAt(compositionFrame, commandIndex, out var layer))
            {
                hasLayer = true;
                commandIndex += layer.CommandCount;
                continue;
            }

            if (hasLayer)
            {
                return false;
            }

            commandIndex++;
        }

        return hasLayer;
    }

    private static void AccumulateRenderTargetLayerDiagnostics(
        D3D12CompositionLayerContent content,
        in CompositionLayer layer,
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        DisplayScale scale,
        ref ExecuteDiagnosticsAccumulator diagnostics)
    {
        var rectPayloads = content.Rects;
        for (var i = 0; i < rectPayloads.Length; i++)
        {
            var payload = rectPayloads[i];
            var command = ScaleCommandToPhysicalPixels(new DrawCommand(
                DrawCommandKind.FillRect,
                Translate(payload.Rect, layer.Transform),
                ApplyOpacity(payload.Color, layer.Opacity),
                ClipBounds: ResolveComposedClip(payload.ClipBounds, layer)), scale);
            diagnostics.AddCommandClip(command);
            diagnostics.AddFillRectPlan(clipMode, ResolveFillRectScissor(clipMode, viewport, command.ClipBounds));
        }
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
            var command = ScaleCommandToPhysicalPixels(new DrawCommand(
                DrawCommandKind.FillRect,
                Translate(payload.Rect, layer.Transform),
                ApplyOpacity(payload.Color, layer.Opacity),
                ClipBounds: ResolveComposedClip(payload.ClipBounds, layer)), scale);
            diagnostics.AddCommandClip(command);
            AppendPhysicalFillRect(
                clipMode,
                viewport,
                command.Rect,
                command.Color,
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
            var command = ScaleCommandToPhysicalPixels(new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Translate(payload.Rect, layer.Transform),
                ApplyOpacity(payload.Color, layer.Opacity),
                payload.Resource,
                payload.Text,
                ResolveComposedClip(payload.ClipBounds, layer)), scale);
            diagnostics.AddCommandClip(command);
            AppendPhysicalTextRun(
                clipMode,
                viewport,
                command.Rect,
                command.Color,
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

    internal static void AppendLayerSourceRectContent(
        D3D12CompositionLayerContent content,
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects)
    {
        var rectPayloads = content.Rects;
        for (var i = 0; i < rectPayloads.Length; i++)
        {
            var payload = rectPayloads[i];
            var command = ScaleCommandToPhysicalPixels(new DrawCommand(
                DrawCommandKind.FillRect,
                payload.Rect,
                payload.Color,
                ClipBounds: payload.ClipBounds), scale);
            var scissorPlan = ResolveFillRectScissor(clipMode, viewport, command.ClipBounds);
            if (scissorPlan.Skip)
            {
                continue;
            }

            rects.Add(new D3D12Renderer2D.RectData(
                command.Rect.X,
                command.Rect.Y,
                command.Rect.Width,
                command.Rect.Height,
                command.Color.R / 255f,
                command.Color.G / 255f,
                command.Color.B / 255f,
                command.Color.A / 255f,
                scissorPlan.RenderScissor));
        }
    }

    private static DrawCommand ApplyComposition(in DrawCommand command, in CompositionLayer layer)
    {
        var transform = layer.Transform;
        var opacity = layer.Opacity;
        return new DrawCommand(
            command.Kind,
            Translate(command.Rect, transform),
            ApplyOpacity(command.Color, opacity),
            command.Resource,
            command.Text,
            ResolveComposedClip(command.ClipBounds, layer),
            command.StrokeWidth,
            command.Transform,
            command.ZIndex);
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

    private static DrawColor ApplyOpacity(DrawColor color, CompositionOpacity opacity)
    {
        var normalized = opacity.Normalized;
        return normalized == 1f ? color : new DrawColor((byte)Math.Clamp(MathF.Round(color.A * normalized), 0f, 255f), color.R, color.G, color.B);
    }

    private static TextStyle ResolvePhysicalTextStyle(IFrameResourceResolver resources, ResourceHandle handle, DisplayScale scale)
    {
        return ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(handle), scale);
    }

    public void EndFrame()
    {
        var rects = _rects.Span;
        var texts = _texts.Span;
        var layerRenderTargets = _compositionLayerRenderTargets.Span;
        var resources = _resources ?? FrameDrawingResources.Empty;
        _resources = null;

        if (rects.Length > 0 || texts.Length > 0 || layerRenderTargets.Length > 0)
        {
            _renderer.RenderFrame(rects, texts, layerRenderTargets, resources, _frameContext.Scale, _bgR, _bgG, _bgB, _bgA);
        }
        else
        {
            _renderer.ClearAndPresent(_bgR, _bgG, _bgB, _bgA);
        }
    }

    public bool IsDeviceRemoved => _renderer.IsDeviceRemoved;

    public bool TryRecover() => _renderer.TryRecover();

    public void Dispose()
    {
        _compositionLayerContentCache.Clear();
        _compositionLayerRenderTargets.Dispose();
        _rects.Dispose();
        _texts.Dispose();
        _renderer.Dispose();
    }
}
