using System.Runtime.InteropServices;

namespace Irix.Platform;

internal enum DeviceErrorSite : byte
{
    None,
    ResizeResourceRecreation,
    BeginFrameCommandReset,
    Present,
    MoveToNextFrame,
    WaitForGpu,
    RecoveryReinitialize,
    GlyphAtlasInitialization,
    GlyphAtlasRecord,
    LayerRenderTargetRecord
}

internal enum DeviceErrorKind : byte
{
    None,
    Failure,
    ComException,
    OutOfMemory,
    HResult,
    InvalidOperation,
    Unknown
}

internal readonly struct DeviceErrorDiagnostic : IEquatable<DeviceErrorDiagnostic>
{
    private const int EOutOfMemory = unchecked((int)0x8007000E);

    public DeviceErrorDiagnostic(DeviceErrorSite site, DeviceErrorKind kind, int errorCode = 0)
    {
        Site = site;
        Kind = kind;
        ErrorCode = kind is DeviceErrorKind.ComException or DeviceErrorKind.OutOfMemory or DeviceErrorKind.HResult ? errorCode : 0;
    }

    public DeviceErrorSite Site { get; }
    public DeviceErrorKind Kind { get; }
    public int ErrorCode { get; }

    public static DeviceErrorDiagnostic None => default;

    public bool IsNone => Site == DeviceErrorSite.None && Kind == DeviceErrorKind.None;

    public bool HasErrorCode => Kind is DeviceErrorKind.ComException or DeviceErrorKind.OutOfMemory or DeviceErrorKind.HResult;

    public static DeviceErrorDiagnostic FromFailure(DeviceErrorSite site) => new(site, DeviceErrorKind.Failure);

    public static DeviceErrorDiagnostic FromInvalidOperation(DeviceErrorSite site) => new(site, DeviceErrorKind.InvalidOperation);

    public static DeviceErrorDiagnostic FromUnknown(DeviceErrorSite site) => new(site, DeviceErrorKind.Unknown);

    public static DeviceErrorDiagnostic FromHResult(DeviceErrorSite site, int errorCode) => new(site, DeviceErrorKind.HResult, errorCode);

    public static DeviceErrorDiagnostic FromComException(DeviceErrorSite site, int errorCode)
    {
        return errorCode == EOutOfMemory
            ? new DeviceErrorDiagnostic(site, DeviceErrorKind.OutOfMemory, errorCode)
            : new DeviceErrorDiagnostic(site, DeviceErrorKind.ComException, errorCode);
    }

    public static DeviceErrorDiagnostic FromException(DeviceErrorSite site, Exception? ex)
    {
        return ex switch
        {
            COMException comException => FromComException(site, comException.ErrorCode),
            InvalidOperationException => FromInvalidOperation(site),
            null => FromUnknown(site),
            _ => FromUnknown(site)
        };
    }

    public bool Equals(DeviceErrorDiagnostic other)
    {
        return Site == other.Site
            && Kind == other.Kind
            && ErrorCode == other.ErrorCode;
    }

    public override bool Equals(object? obj) => obj is DeviceErrorDiagnostic other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Site, Kind, ErrorCode);

    public override string ToString()
    {
        if (IsNone)
        {
            return "(none)";
        }

        var label = FormatSite(Site);
        return Kind switch
        {
            DeviceErrorKind.Failure => $"{label} failed",
            DeviceErrorKind.InvalidOperation => $"{label}: InvalidOperationException",
            DeviceErrorKind.ComException => $"{label}: COMException 0x{unchecked((uint)ErrorCode):X8}",
            DeviceErrorKind.OutOfMemory => $"{label}: E_OUTOFMEMORY (0x{unchecked((uint)ErrorCode):X8})",
            DeviceErrorKind.HResult => $"{label}: HRESULT 0x{unchecked((uint)ErrorCode):X8}",
            DeviceErrorKind.Unknown => $"{label}: unknown device error",
            _ => "(none)"
        };
    }

    private static string FormatSite(DeviceErrorSite site)
    {
        return site switch
        {
            DeviceErrorSite.ResizeResourceRecreation => "Resize resource recreation",
            DeviceErrorSite.BeginFrameCommandReset => "BeginFrame command reset",
            DeviceErrorSite.Present => "Present",
            DeviceErrorSite.MoveToNextFrame => "MoveToNextFrame",
            DeviceErrorSite.WaitForGpu => "WaitForGpu",
            DeviceErrorSite.RecoveryReinitialize => "Recovery reinitialize",
            DeviceErrorSite.GlyphAtlasInitialization => "Glyph atlas initialization",
            DeviceErrorSite.GlyphAtlasRecord => "Glyph atlas record",
            DeviceErrorSite.LayerRenderTargetRecord => "Layer render target record",
            _ => "unknown device error"
        };
    }

    public static bool operator ==(DeviceErrorDiagnostic left, DeviceErrorDiagnostic right) => left.Equals(right);

    public static bool operator !=(DeviceErrorDiagnostic left, DeviceErrorDiagnostic right) => !left.Equals(right);
}
