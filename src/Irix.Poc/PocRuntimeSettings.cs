using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly struct PocRuntimeSettings(
    DrawingBackendClipMode ClipMode,
    TextCompositionMode TextCompositionMode,
    bool EnablePartialApply,
    bool EnableConsoleMirror) : IEquatable<PocRuntimeSettings>
{
    public DrawingBackendClipMode ClipMode { get; } = ClipMode;
    public TextCompositionMode TextCompositionMode { get; } = TextCompositionMode;
    public bool EnablePartialApply { get; } = EnablePartialApply;
    public bool EnableConsoleMirror { get; } = EnableConsoleMirror;

    public static PocRuntimeSettings Default { get; } = new(
        DrawingBackendClipMode.Scissor,
        TextCompositionMode.GlyphAtlas,
        EnablePartialApply: true,
        EnableConsoleMirror: false);

    public RenderPipelineProductionOwnerOptions RenderProductionOwnerOptions =>
        EnablePartialApply
            ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
            : RenderPipelineProductionOwnerOptions.Disabled;

    public DrawingBackendCompositorHandoffOptions CompositorHandoffOptions =>
        EnablePartialApply
            ? DrawingBackendCompositorHandoffOptions.Enabled
            : DrawingBackendCompositorHandoffOptions.Disabled;

    public string PartialApplyConsoleStatus =>
        EnablePartialApply ? "ENABLED (default)" : "DISABLED (--no-partial-apply)";

    public static PocRuntimeSettings Parse(string[] args) =>
        new(
            ParseClipMode(args),
            ParseTextCompositionMode(args),
            EnablePartialApply: !args.Contains("--no-partial-apply"),
            EnableConsoleMirror: args.Contains("--console"));

    internal static TextCompositionMode ParseTextCompositionMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--text-composition").Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return TextCompositionMode.GlyphAtlas;
        }

        return value?.ToLowerInvariant() switch
        {
            "glyph-atlas" or "glyphatlas" or "atlas" => TextCompositionMode.GlyphAtlas,
            _ => throw new ArgumentException($"Unsupported text composition mode '{value}'. GlyphAtlas is the only active text composition mode.")
        };
    }

    internal static DrawingBackendClipMode ParseClipMode(string[] args)
    {
        var value = args.SkipWhile(a => a != "--clip-mode").Skip(1).FirstOrDefault();
        return value?.ToLowerInvariant() switch
        {
            "diagnostic" or "diagnostics" => DrawingBackendClipMode.Diagnostic,
            "scissor" => DrawingBackendClipMode.Scissor,
            _ when args.Contains("--disable-scissor") => DrawingBackendClipMode.Diagnostic,
            _ => DrawingBackendClipMode.Scissor
        };
    }

    public bool Equals(PocRuntimeSettings other)
    {
        return ClipMode == other.ClipMode
            && TextCompositionMode == other.TextCompositionMode
            && EnablePartialApply == other.EnablePartialApply
            && EnableConsoleMirror == other.EnableConsoleMirror;
    }

    public override bool Equals(object? obj) => obj is PocRuntimeSettings other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ClipMode);
        hash.Add(TextCompositionMode);
        hash.Add(EnablePartialApply);
        hash.Add(EnableConsoleMirror);
        return hash.ToHashCode();
    }

    public static bool operator ==(PocRuntimeSettings left, PocRuntimeSettings right) => left.Equals(right);

    public static bool operator !=(PocRuntimeSettings left, PocRuntimeSettings right) => !left.Equals(right);
}
