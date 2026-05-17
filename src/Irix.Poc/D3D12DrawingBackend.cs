using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

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

/// <summary>
/// D3D12 backend: renders FillRect commands as colored rectangles via D3D12Renderer2D.
/// Falls back to clear color for the background.
/// </summary>
internal sealed class D3D12DrawingBackend(D3D12Renderer renderer, DrawingBackendClipMode clipMode = DrawingBackendClipMode.Scissor) : IDrawingBackend, IDirtyRangeAware, IClipScissorCapability, IDeviceRecovery
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
    private readonly FrameRenderList<D3D12TextRenderer.TextData> _texts = new();
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
            throw new InvalidOperationException($"D3D12 begin frame failed: {_renderer.DeviceErrorReason ?? "unknown device error"}");
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

        if (result.HasBackgroundColor)
        {
            _bgR = result.BackgroundColor.R / 255f;
            _bgG = result.BackgroundColor.G / 255f;
            _bgB = result.BackgroundColor.B / 255f;
            _bgA = result.BackgroundColor.A / 255f;
        }
    }

    internal static D3D12ExecuteCoreResult ExecuteCore(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRenderer.TextData> texts)
    {
        scale = scale.Normalize();
        var diagnostics = new ExecuteDiagnosticsAccumulator();
        var hasBackgroundColor = false;
        var backgroundColor = default(DrawColor);

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
            diagnostics.AddCommandClip(command);

            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    var scissorPlan = ResolveFillRectScissor(clipMode, viewport, command.ClipBounds);
                    diagnostics.AddFillRectPlan(clipMode, scissorPlan);
                    if (scissorPlan.Skip)
                    {
                        break;
                    }

                    if (rects.Count == 0 && !hasBackgroundColor)
                    {
                        backgroundColor = command.Color;
                        hasBackgroundColor = true;
                    }

                    rects.Add(new D3D12Renderer2D.RectData(
                        command.Rect.X, command.Rect.Y,
                        command.Rect.Width, command.Rect.Height,
                        command.Color.R / 255f, command.Color.G / 255f,
                        command.Color.B / 255f, command.Color.A / 255f,
                        scissorPlan.RenderScissor));
                    break;
                case DrawCommandKind.DrawTextRun:
                    var textClipPlan = ResolveTextClip(clipMode, viewport, command.ClipBounds);
                    diagnostics.AddTextClipPlan(textClipPlan);
                    if (textClipPlan.Skip)
                    {
                        break;
                    }

                    if (resources.Resolve(command.Text).IsEmpty)
                    {
                        break;
                    }

                    texts.Add(new D3D12TextRenderer.TextData(
                        command.Rect.X,
                        command.Rect.Y,
                        command.Rect.Width,
                        command.Rect.Height,
                        command.Color.R / 255f,
                        command.Color.G / 255f,
                        command.Color.B / 255f,
                        command.Color.A / 255f,
                        command.Text,
                        command.Resource,
                        textClipPlan.EffectiveClip,
                        textClipPlan.ClipEnabled,
                        ResolvePhysicalTextStyle(resources, command.Resource, scale),
                        resources));
                    break;
            }
        }

        return new D3D12ExecuteCoreResult(
            diagnostics.FillRectDiagnostics,
            diagnostics.TextClipDiagnostics,
            hasBackgroundColor,
            backgroundColor);
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

    public bool TryRecover() => _renderer.TryRecover();

    public void Dispose()
    {
        _rects.Dispose();
        _texts.Dispose();
        _renderer.Dispose();
    }
}
