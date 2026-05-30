#if IRIX_DIAGNOSTICS
namespace Irix.Poc;

internal interface IDiagnosticsProvider<out TSnapshot>
{
    TSnapshot Capture();
}
#endif
