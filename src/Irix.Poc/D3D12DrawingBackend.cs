using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct D3D12FillRectScissorPlan(EffectiveScissor EffectiveScissor, IntegerScissorRect RenderScissor, bool Skip);

internal readonly record struct D3D12TextClipPlan(EffectiveScissor EffectiveClip, bool ClipEnabled, bool Skip);

internal readonly record struct D3D12FillRectScissorDiagnostics(int ClippedCommandCount, int EmptyIntersectionSkippedCount, int ScissorStateChangeCount, EffectiveScissor LastEffectiveScissor);

internal readonly record struct D3D12TextClipDiagnostics(int TextClipSkippedCount, EffectiveScissor LastEffectiveTextClip);

/// <summary>
/// D3D12 backend: renders FillRect commands as colored rectangles via D3D12Renderer2D.
/// Falls back to clear color for the background.
/// </summary>
internal sealed class D3D12DrawingBackend(D3D12Renderer renderer, DrawingBackendClipMode clipMode = DrawingBackendClipMode.Diagnostic) : IDrawingBackend, IDirtyRangeAware, IClipScissorCapability, IDeviceRecovery
{
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

    internal static D3D12FillRectScissorDiagnostics ComputeFillRectScissorDiagnostics(DrawingBackendClipMode clipMode, in DrawRect viewport, ReadOnlySpan<DrawCommand> commands)
    {
        var clippedCommandCount = 0;
        var emptyIntersectionSkippedCount = 0;
        var scissorStateChangeCount = 0;
        var hasPreviousScissor = false;
        var previousScissor = IntegerScissorRect.Empty;
        var lastEffectiveScissor = EffectiveScissor.Empty;

        foreach (var command in commands)
        {
            if (command.ClipBounds.Width > 0 && command.ClipBounds.Height > 0)
            {
                clippedCommandCount++;
            }

            if (command.Kind != DrawCommandKind.FillRect)
            {
                continue;
            }

            var scissorPlan = ResolveFillRectScissor(clipMode, viewport, command.ClipBounds);
            lastEffectiveScissor = scissorPlan.EffectiveScissor;
            if (scissorPlan.Skip)
            {
                emptyIntersectionSkippedCount++;
                continue;
            }

            if (clipMode == DrawingBackendClipMode.Scissor && (!hasPreviousScissor || scissorPlan.RenderScissor != previousScissor))
            {
                scissorStateChangeCount++;
                previousScissor = scissorPlan.RenderScissor;
                hasPreviousScissor = true;
            }
        }

        return new D3D12FillRectScissorDiagnostics(clippedCommandCount, emptyIntersectionSkippedCount, scissorStateChangeCount, lastEffectiveScissor);
    }

    internal static D3D12TextClipDiagnostics ComputeTextClipDiagnostics(DrawingBackendClipMode clipMode, in DrawRect viewport, ReadOnlySpan<DrawCommand> commands)
    {
        var textClipSkippedCount = 0;
        var lastEffectiveTextClip = EffectiveScissor.Empty;

        foreach (var command in commands)
        {
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            var textClipPlan = ResolveTextClip(clipMode, viewport, command.ClipBounds);
            lastEffectiveTextClip = textClipPlan.EffectiveClip;
            if (textClipPlan.Skip)
            {
                textClipSkippedCount++;
            }
        }

        return new D3D12TextClipDiagnostics(textClipSkippedCount, lastEffectiveTextClip);
    }

    public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
    {
        _dirtyCommandRanges = ranges;
    }

    public void BeginFrame(in FrameContext frameContext)
    {
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
        var diagnostics = ComputeFillRectScissorDiagnostics(ClipMode, viewport, commands);
        var textDiagnostics = ComputeTextClipDiagnostics(ClipMode, viewport, commands);
        _clippedCommandCount = diagnostics.ClippedCommandCount;
        _emptyIntersectionSkippedCount = diagnostics.EmptyIntersectionSkippedCount;
        _scissorStateChangeCount = diagnostics.ScissorStateChangeCount;
        _lastEffectiveScissor = diagnostics.LastEffectiveScissor;
        _textClipSkippedCount = textDiagnostics.TextClipSkippedCount;
        _lastEffectiveTextClip = textDiagnostics.LastEffectiveTextClip;

        foreach (var command in commands)
        {
            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    var scissorPlan = ResolveFillRectScissor(ClipMode, viewport, command.ClipBounds);
                    if (scissorPlan.Skip)
                    {
                        break;
                    }

                    if (_rects.Count == 0)
                    {
                        _bgR = command.Color.R / 255f;
                        _bgG = command.Color.G / 255f;
                        _bgB = command.Color.B / 255f;
                        _bgA = command.Color.A / 255f;
                    }
                    _rects.Add(new D3D12Renderer2D.RectData(
                        command.Rect.X, command.Rect.Y,
                        command.Rect.Width, command.Rect.Height,
                        command.Color.R / 255f, command.Color.G / 255f,
                        command.Color.B / 255f, command.Color.A / 255f,
                        scissorPlan.RenderScissor));
                    break;
                case DrawCommandKind.DrawTextRun:
                    if (resources.Resolve(command.Text).IsEmpty)
                    {
                        break;
                    }

                    var textClipPlan = ResolveTextClip(ClipMode, viewport, command.ClipBounds);
                    if (textClipPlan.Skip)
                    {
                        break;
                    }

                    _texts.Add(new D3D12TextRenderer.TextData(
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
                        resources.ResolveTextStyle(command.Resource),
                        resources));
                    break;
            }
        }
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
