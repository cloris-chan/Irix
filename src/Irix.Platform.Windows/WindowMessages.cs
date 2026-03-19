namespace Irix.Platform.Windows;

internal static class WindowMessages
{
    public const uint FocusGained = 0x0007;
    public const uint FocusLost = 0x0008;
    public const uint DisplayChange = 0x007E;
    public const uint Paint = 0x000F;
    public const uint Size = 0x0005;
    public const uint Character = 0x0102;
    public const uint MouseMove = 0x0200;
    public const uint LeftButtonDown = 0x0201;
    public const uint LeftButtonUp = 0x0202;
    public const uint RightButtonDown = 0x0204;
    public const uint RightButtonUp = 0x0205;
    public const uint MiddleButtonDown = 0x0207;
    public const uint MiddleButtonUp = 0x0208;
    public const uint MouseWheel = 0x020A;
    public const uint KeyDown = 0x0100;
    public const uint KeyUp = 0x0101;
    public const uint Destroy = 0x0002;
}
