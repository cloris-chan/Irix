using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly record struct D3D12FillRectScissorPlan(EffectiveScissor EffectiveScissor, IntegerScissorRect RenderScissor, bool Skip);

internal readonly record struct D3D12TextClipPlan(EffectiveScissor EffectiveClip, bool ClipEnabled, bool Skip);

internal readonly record struct D3D12FillRectScissorDiagnostics(int ClippedCommandCount, int EmptyIntersectionSkippedCount, int ScissorStateChangeCount, EffectiveScissor LastEffectiveScissor);

internal readonly record struct D3D12TextClipDiagnostics(int TextClipSkippedCount, EffectiveScissor LastEffectiveTextClip);

internal readonly record struct D3D12ExecuteCoreResult(
    D3D12FillRectScissorDiagnostics FillRectDiagnostics,
    D3D12TextClipDiagnostics TextClipDiagnostics,
    bool HasBackgroundColor,
    DrawColor BackgroundColor);

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
        var clippedCommandCount = 0;
        var emptyIntersectionSkippedCount = 0;
        var scissorStateChangeCount = 0;
        var hasPreviousScissor = false;
        var previousScissor = IntegerScissorRect.Empty;
        var lastEffectiveScissor = EffectiveScissor.Empty;

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
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

    internal static D3D12TextClipDiagnostics ComputeTextClipDiagnostics(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        DisplayScale scale = default)
    {
        scale = scale.Normalize();
        var textClipSkippedCount = 0;
        var lastEffectiveTextClip = EffectiveScissor.Empty;

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
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
        _frameContext = frameContext with { Scale = frameContext.Scale.Normalize() };
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
        var clippedCommandCount = 0;
        var emptyIntersectionSkippedCount = 0;
        var scissorStateChangeCount = 0;
        var hasPreviousScissor = false;
        var previousScissor = IntegerScissorRect.Empty;
        var lastEffectiveScissor = EffectiveScissor.Empty;
        var textClipSkippedCount = 0;
        var lastEffectiveTextClip = EffectiveScissor.Empty;
        var hasBackgroundColor = false;
        var backgroundColor = default(DrawColor);

        foreach (var logicalCommand in commands)
        {
            var command = ScaleCommandToPhysicalPixels(logicalCommand, scale);
            if (command.ClipBounds.Width > 0 && command.ClipBounds.Height > 0)
            {
                clippedCommandCount++;
            }

            switch (command.Kind)
            {
                case DrawCommandKind.FillRect:
                    var scissorPlan = ResolveFillRectScissor(clipMode, viewport, command.ClipBounds);
                    lastEffectiveScissor = scissorPlan.EffectiveScissor;
                    if (scissorPlan.Skip)
                    {
                        emptyIntersectionSkippedCount++;
                        break;
                    }

                    if (clipMode == DrawingBackendClipMode.Scissor && (!hasPreviousScissor || scissorPlan.RenderScissor != previousScissor))
                    {
                        scissorStateChangeCount++;
                        previousScissor = scissorPlan.RenderScissor;
                        hasPreviousScissor = true;
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
                    lastEffectiveTextClip = textClipPlan.EffectiveClip;
                    if (textClipPlan.Skip)
                    {
                        textClipSkippedCount++;
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
            new D3D12FillRectScissorDiagnostics(clippedCommandCount, emptyIntersectionSkippedCount, scissorStateChangeCount, lastEffectiveScissor),
            new D3D12TextClipDiagnostics(textClipSkippedCount, lastEffectiveTextClip),
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

        var scaleFactor = (scale.ScaleX + scale.ScaleY) / 2f;
        return style with { FontSize = style.FontSize * scaleFactor };
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
