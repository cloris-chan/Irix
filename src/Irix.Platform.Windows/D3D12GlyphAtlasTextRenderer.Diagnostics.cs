using System.Runtime.InteropServices;
using Irix.Platform;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private void RecordDegradation(int unsupportedRuns, GlyphAtlasFallbackReasonCounts reasons)
    {
        if (unsupportedRuns > 0)
        {
            _diagnostics = _diagnostics.WithDegradation(unsupportedRuns, reasons);
        }
    }

    private void RecordDegradation(int unsupportedRuns, GlyphAtlasFallbackReason reason)
    {
        if (unsupportedRuns > 0)
        {
            _diagnostics = _diagnostics.WithDegradation(unsupportedRuns, reason);
        }
    }

    private void DisableGlyphAtlasDegradation(
        GlyphAtlasFallbackReason reason,
        GlyphAtlasRecordFailurePhase phase,
        DeviceErrorDiagnostic diagnostic,
        int degradedRunCount)
    {
        _disabled = true;
        _deviceError = diagnostic.IsNone ? DeviceErrorDiagnostic.FromFailure(DeviceErrorSite.GlyphAtlasRecord) : diagnostic;
        _diagnostics = _diagnostics
            .WithDegradation(degradedRunCount, reason)
            .WithRecordFailure(phase);
        System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] {_deviceError}");
    }

    private static COMException WrapD3D12Exception(string context, COMException ex)
    {
        return new COMException($"{context} failed: 0x{unchecked((uint)ex.ErrorCode):X8}", ex.ErrorCode);
    }

    private static GlyphAtlasRecordException CreateRecordException(
        GlyphAtlasRecordFailurePhase phase,
        string context,
        COMException ex)
    {
        return new GlyphAtlasRecordException(phase, WrapD3D12Exception(context, ex));
    }

    private static GlyphAtlasRecordException CreateRecordException(
        GlyphAtlasRecordFailurePhase phase,
        string message)
    {
        return new GlyphAtlasRecordException(phase, new InvalidOperationException(message));
    }
}

