using System.Runtime.InteropServices;

namespace Irix;

[StructLayout(LayoutKind.Sequential, Size = 16)]
internal readonly struct Color : IEquatable<Color>
{
    private const double SrgbToBt2020Rr = 0.6274038959346994;
    private const double SrgbToBt2020Rg = 0.3292830383778849;
    private const double SrgbToBt2020Rb = 0.0433130666874156;
    private const double SrgbToBt2020Gr = 0.0690972893582320;
    private const double SrgbToBt2020Gg = 0.9195403950754595;
    private const double SrgbToBt2020Gb = 0.0113623168665293;
    private const double SrgbToBt2020Br = 0.0163914388751504;
    private const double SrgbToBt2020Bg = 0.0880133078772250;
    private const double SrgbToBt2020Bb = 0.8955952532476245;

    private const double Bt2020ToSrgbRr = 1.6604910021084345;
    private const double Bt2020ToSrgbRg = -0.5876411387885495;
    private const double Bt2020ToSrgbRb = -0.0728498632198848;
    private const double Bt2020ToSrgbGr = -0.1245504745215907;
    private const double Bt2020ToSrgbGg = 1.1328998971254153;
    private const double Bt2020ToSrgbGb = -0.0083494226038240;
    private const double Bt2020ToSrgbBr = -0.0181507633549052;
    private const double Bt2020ToSrgbBg = -0.1005788980080071;
    private const double Bt2020ToSrgbBb = 1.1187296613629123;

    private readonly float _r;
    private readonly float _g;
    private readonly float _b;
    private readonly float _a;

    private Color(float r, float g, float b, float a)
    {
        _r = r;
        _g = g;
        _b = b;
        _a = a;
    }

    public float LinearBt2020R => _r;
    public float LinearBt2020G => _g;
    public float LinearBt2020B => _b;
    public float A => _a;

    public static Color Transparent => default;

    public static Color FromSrgb(byte r, byte g, byte b) => FromSrgb(255, r, g, b);

    public static Color FromSrgb(byte a, byte r, byte g, byte b)
    {
        var linearR = DecodeSrgb(r / 255.0);
        var linearG = DecodeSrgb(g / 255.0);
        var linearB = DecodeSrgb(b / 255.0);

        return new Color(
            (float)((SrgbToBt2020Rr * linearR) + (SrgbToBt2020Rg * linearG) + (SrgbToBt2020Rb * linearB)),
            (float)((SrgbToBt2020Gr * linearR) + (SrgbToBt2020Gg * linearG) + (SrgbToBt2020Gb * linearB)),
            (float)((SrgbToBt2020Br * linearR) + (SrgbToBt2020Bg * linearG) + (SrgbToBt2020Bb * linearB)),
            a / 255f);
    }

    public static Color FromSrgb(float r, float g, float b, float a = 1)
    {
        var linearR = DecodeSrgb(NormalizeSdrChannel(r));
        var linearG = DecodeSrgb(NormalizeSdrChannel(g));
        var linearB = DecodeSrgb(NormalizeSdrChannel(b));

        return new Color(
            (float)((SrgbToBt2020Rr * linearR) + (SrgbToBt2020Rg * linearG) + (SrgbToBt2020Rb * linearB)),
            (float)((SrgbToBt2020Gr * linearR) + (SrgbToBt2020Gg * linearG) + (SrgbToBt2020Gb * linearB)),
            (float)((SrgbToBt2020Br * linearR) + (SrgbToBt2020Bg * linearG) + (SrgbToBt2020Bb * linearB)),
            NormalizeAlpha(a));
    }

    public SrgbColor ToSrgb()
    {
        var linearR = (Bt2020ToSrgbRr * _r) + (Bt2020ToSrgbRg * _g) + (Bt2020ToSrgbRb * _b);
        var linearG = (Bt2020ToSrgbGr * _r) + (Bt2020ToSrgbGg * _g) + (Bt2020ToSrgbGb * _b);
        var linearB = (Bt2020ToSrgbBr * _r) + (Bt2020ToSrgbBg * _g) + (Bt2020ToSrgbBb * _b);

        return SrgbColor.FromArgb(
            ToByte(_a),
            ToByte(EncodeSrgb(linearR)),
            ToByte(EncodeSrgb(linearG)),
            ToByte(EncodeSrgb(linearB)));
    }

    public bool Equals(Color other)
    {
        return _r.Equals(other._r)
            && _g.Equals(other._g)
            && _b.Equals(other._b)
            && _a.Equals(other._a);
    }

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_r, _g, _b, _a);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    private static double DecodeSrgb(double value)
    {
        value = Clamp01(value);
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static double EncodeSrgb(double value)
    {
        value = Clamp01(value);
        return value <= 0.0031308
            ? 12.92 * value
            : (1.055 * Math.Pow(value, 1.0 / 2.4)) - 0.055;
    }

    private static float NormalizeSdrChannel(float value)
    {
        if (!float.IsFinite(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static float NormalizeAlpha(float value)
    {
        if (!float.IsFinite(value))
        {
            return 1;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }

    private static byte ToByte(double value)
    {
        value = Clamp01(value);
        return (byte)Math.Clamp((int)Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0, 255);
    }
}

internal readonly struct SrgbColor(uint Argb) : IEquatable<SrgbColor>
{
    public uint Argb { get; } = Argb;

    public byte A => (byte)(Argb >> 24);
    public byte R => (byte)(Argb >> 16);
    public byte G => (byte)(Argb >> 8);
    public byte B => (byte)Argb;

    public static SrgbColor Transparent => default;

    public static SrgbColor Opaque(byte r, byte g, byte b) => FromArgb(255, r, g, b);

    public static SrgbColor FromArgb(byte a, byte r, byte g, byte b) =>
        new(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b);

    public static SrgbColor FromArgb(uint argb) => new(argb);

    public bool Equals(SrgbColor other) => Argb == other.Argb;

    public override bool Equals(object? obj) => obj is SrgbColor other && Equals(other);

    public override int GetHashCode() => Argb.GetHashCode();

    public static bool operator ==(SrgbColor left, SrgbColor right) => left.Equals(right);

    public static bool operator !=(SrgbColor left, SrgbColor right) => !left.Equals(right);
}
