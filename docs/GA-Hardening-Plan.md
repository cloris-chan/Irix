# GA Hardening Baseline

> Current private-GA baseline for the Irix Windows PoC. This is a retained milestone summary, not an active process log.

## Scope

`v1.0-private-ga` is an internal/private milestone. It is not a public API freeze. The active renderer foundation has since moved to D3D12-only GlyphAtlas text composition; D3D11On12 / Direct2D overlay final composition is historical only and must not be reintroduced.

## Current Baseline

| Area | Baseline |
|------|----------|
| V1 core | Architecture-complete and regression-only. Do not reopen core feature scope for GA cleanup. |
| Partial apply | Default-on; `--no-partial-apply` remains rollback. |
| Display scale | 100%, 150%, and 200% local evidence accepted; PerMonitorV2 floor remains Windows 10 1703 / 10.0.15063.0. |
| Refresh matrix | 60Hz, 120Hz, and 240Hz local evidence accepted. 144Hz is not required without hardware. |
| Device resilience | Device-lost detection, recovery, device-removed diagnostics, GPU resource creation failure diagnostics including `E_OUTOFMEMORY`, and command allocator reset retry/escalation are complete for v1 scope. |
| Stability | 1000-frame soak, long-run memory growth, resize stress, concurrent input/render, minimize/restore, occlusion, scroll/click, and runtime scale-switch smokes are complete for current hardware. |
| Performance | Mock backend frame timing, warm `FrameDrawingResources`, split frame-stage allocation, D3D12 `ExecuteCore` 100% / 150% allocation guards, and `--diagnose-text-cache` attribution are in place. Latest local attribution lives in [Project_Status_and_Todo.md](Project_Status_and_Todo.md). |
| Text/value IR | Framework/core render paths use typed ids and `TextNodeContent` / `TextBufferSnapshot` / `TextSlice` boundaries; diagnostics strings are output-boundary only. |
| Renderer | Active Windows text composition is D3D12 GlyphAtlas. DirectWrite/WIC are source-data paths only. Unsupported/failure text cases are explicit D3D12-only degradation. |
| CI | Workflow configuration remains, but GitHub Actions quota is exhausted. Local guard evidence is authoritative until quota returns. |

## Guarded Invariants

- No D3D11On12 / Direct2D final text composition, overlay sync strategy, explicit overlay mode, or hidden overlay CLI alias.
- No runtime shader compile in active D3D12 renderer source.
- No `IDWriteTextLayout` or Direct2D renderer fallback path.
- No retained raw string text in framework/core layout, rendering, or drawing state.
- No public string style/value factory path or string-keyed domain model.
- No entry-level glyph atlas eviction implementation until retained atlas command ownership is explicit.

## Local Validation

Use the current local gates rather than historical private-GA sync evidence:

```powershell
dotnet test --no-build -c Release --filter "FullyQualifiedName~ProgramDiagnosticsTests" --verbosity normal
.\scripts\glyph-atlas-regression.ps1 -Mode Smoke
```

Run `.\scripts\glyph-atlas-regression.ps1 -Mode Local` after glyph/page/shaping changes. Reserve `-Mode Nightly` for page-policy, eviction, or shaping overhauls.

## Accepted Constraints

| Constraint | Current handling |
|------------|------------------|
| Public API stability | Not frozen; repository remains private and incompatible cleanup is allowed. |
| Multi-monitor / hot-plug | Follow-up beyond the current v1 Windows PoC baseline. |
| Fractional DPI beyond current evidence | Follow-up when hardware/settings are available. |
| HDR / wide color gamut | Not required for v1 baseline. |
| Accessibility / screen reader support | Not required for v1 baseline. |
| SVG/COLR paint-tree-only glyphs and BiDi beyond resolved-level projection | Explicit D3D12-only degradation until future coverage is designed and guarded. |

## Historical Note

The private-GA tag used a D3D11On12 / Direct2D / DirectWrite overlay to bootstrap text rendering. That path was intentionally removed after the renderer-foundation work. Historical sync-wait measurements and overlay strategy names are no longer active configuration or optimization targets.
