namespace Irix.Poc;

internal interface IInputHitTestService
{
    bool TryHitTestPhysicalPixel(int x, int y, out ActionId actionId);
}
