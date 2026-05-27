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
            && AppliedTransform == other.AppliedTransform
            && AppliedOpacity == other.AppliedOpacity
            && ExecuteResult == other.ExecuteResult;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionExecuteDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(D3D12Backed, LayerCount, CommandCount, LayerCommandStart, LayerCommandCount, TranslatedCommands, OpacityAppliedCommands, HashCode.Combine(AppliedTransform, AppliedOpacity, ExecuteResult));

    public CompositionBackendExecutionResult ToBackendExecutionResult()
    {
        return new CompositionBackendExecutionResult(
            D3D12Backed,
            LayerCount,
            CommandCount,
            TranslatedCommands,
            OpacityAppliedCommands);
    }

    public static bool operator ==(D3D12CompositionExecuteDiagnostics left, D3D12CompositionExecuteDiagnostics right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionExecuteDiagnostics left, D3D12CompositionExecuteDiagnostics right) => !left.Equals(right);
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

    public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

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
        var diagnostics = ExecuteCompositionDiagnosticCore(ClipMode, viewport, commands, resources, compositionFrame, _frameContext.Scale, _rects, _texts);
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

                    texts.Add(new D3D12TextRun(
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

    internal static D3D12CompositionExecuteDiagnostics ExecuteCompositionDiagnosticCore(
        DrawingBackendClipMode clipMode,
        in DrawRect viewport,
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame,
        DisplayScale scale,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts)
    {
        if (!compositionFrame.IsValidForCommandCount(commands.Length))
        {
            throw new ArgumentException("Composition frame layer range must reference a non-empty range inside the command span.", nameof(compositionFrame));
        }

        scale = scale.Normalize();
        var layerCount = compositionFrame.LayerCount;
        var firstLayer = compositionFrame.Layer;
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
            AppliedTransform: firstLayer.Transform,
            AppliedOpacity: firstLayer.Opacity,
            ExecuteResult: executeResult);
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
