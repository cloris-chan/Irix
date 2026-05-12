namespace Irix.Rendering;

/// <summary>
/// Internal interface for backends that support device-lost recovery.
/// Implemented by platform-specific backends (e.g., D3D12) that can
/// reconstruct GPU resources after device removal.
/// </summary>
internal interface IDeviceRecovery
{
    /// <summary>
    /// Returns true if the device is in a removed/unrecoverable state.
    /// </summary>
    bool IsDeviceRemoved { get; }

    /// <summary>
    /// Attempt to recover from device-lost. Returns true if recovery succeeded
    /// and the backend is ready to render again.
    /// </summary>
    bool TryRecover();
}
