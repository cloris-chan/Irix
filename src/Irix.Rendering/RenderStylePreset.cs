using Irix.Drawing;

namespace Irix.Rendering;

internal readonly struct RenderStylePreset(
    LayoutStyle Layout,
    DrawingStyle Drawing,
    ControlVisualStateResolver VisualStates) : IEquatable<RenderStylePreset>
{

    public LayoutStyle Layout { get; } = Layout;
    public DrawingStyle Drawing { get; } = Drawing;
    public ControlVisualStateResolver VisualStates { get; } = VisualStates;

    public static RenderStylePreset Default { get; } = new(
        Layout: new LayoutStyle(
            HorizontalPadding: 16,
            VerticalPadding: 16,
            ItemSpacing: 12,
            TextHeight: 32,
            ButtonHeight: 40,
            RectangleHeight: 48,
            MinimumButtonWidth: 140,
            ButtonTextWidthFactor: 12,
            ButtonHorizontalPadding: 32),
        Drawing: new DrawingStyle(
            TextColor: DrawColor.Opaque(255, 255, 255),
            RectangleFillColor: DrawColor.Opaque(72, 72, 72),
            ButtonFillColor: DrawColor.Opaque(52, 120, 246),
            ButtonHoverFillColor: DrawColor.Opaque(72, 136, 255),
            ButtonPressedFillColor: DrawColor.Opaque(36, 92, 210),
            ButtonFocusedFillColor: DrawColor.Opaque(84, 160, 255),
            ButtonTextColor: DrawColor.Opaque(255, 255, 255),
            TextStyle: TextStyle.Default,
            ButtonTextStyle: TextStyle.Default),
        VisualStates: ControlVisualStateResolver.Default);

    public bool Equals(RenderStylePreset other)
    {
        return Layout == other.Layout
            && Drawing == other.Drawing
            && VisualStates == other.VisualStates;
    }

    public override bool Equals(object? obj) => obj is RenderStylePreset other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Layout, Drawing, VisualStates);

    public static bool operator ==(RenderStylePreset left, RenderStylePreset right) => left.Equals(right);

    public static bool operator !=(RenderStylePreset left, RenderStylePreset right) => !left.Equals(right);
}

internal readonly struct RenderStylePresetId(byte value) : IEquatable<RenderStylePresetId>
{
    public static readonly RenderStylePresetId Default = new(1);

    public byte Value { get; } = value;

    public bool IsDefault => Value == Default.Value;

    public bool Equals(RenderStylePresetId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is RenderStylePresetId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(RenderStylePresetId left, RenderStylePresetId right) => left.Equals(right);

    public static bool operator !=(RenderStylePresetId left, RenderStylePresetId right) => !left.Equals(right);
}
