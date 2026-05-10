namespace Irix.Poc;

internal interface IDebugDiagnosticsSnapshotBridge
{
    DebugUiDiagnosticsSnapshot Capture();
}

internal readonly record struct DebugUiDiagnosticsSnapshot(
    Program.ViewportDiagnosticsSnapshot? Viewport,
    CounterLayoutDiagnostics? Layout,
    Program.ScrollDiagnosticsSnapshot? Scroll,
    Program.InputDiagnosticsSnapshot? Input,
    Program.BackendClipTextDiagnosticSnapshot? BackendClipText);