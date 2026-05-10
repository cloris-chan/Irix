namespace Irix.Poc;

internal interface IDiagnosticsProvider<out TSnapshot>
{
    TSnapshot Capture();
}