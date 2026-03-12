namespace Irix.Platform.Windows;

internal static class WindowMessages
{
    public const uint Paint = 0x000F;
    public const uint Size = 0x0005;
    public const uint MouseMove = 0x0200;
    public const uint LeftButtonDown = 0x0201;
    public const uint LeftButtonUp = 0x0202;
    public const uint KeyDown = 0x0100;
    public const uint KeyUp = 0x0101;
    public const uint Destroy = 0x0002;
}
