# Post-GA Default Baseline Evidence - 2026-05-18

Branch: `post-ga-renderer-foundation`

Scope: default baseline switch evidence for `GlyphAtlas` text composition and `Scissor` backend clipping. Public API remains unchanged. `--text-composition overlay` remains the overlay rollback; `--disable-scissor` and `--clip-mode diagnostic` remain clip rollback/diagnostic paths. Partial apply remains default-on with `--no-partial-apply` rollback. `SyncTextOverlay` remains enabled for overlay fallback correctness.

## Environment

- Date/timezone: 2026-05-18, Asia/Taipei
- OS display scale observed by diagnostics: `1.5x1.5`
- Display refresh observed by diagnostics: `240Hz`
- D3D12 test environment: local Windows/D3D12

## Validation Commands

```powershell
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance" --verbosity normal
dotnet test --no-build -c Release --filter "Category=D3D12" --verbosity normal
dotnet test --no-build -c Release --filter "Category=Performance" --verbosity normal
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --text-composition overlay
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --diagnose-sync-non-ascii
dotnet run --no-build -c Release --project src/Irix.Poc
dotnet run --no-build -c Release --project src/Irix.Poc -- --disable-scissor
dotnet run --no-build -c Release --project src/Irix.Poc -- --clip-mode diagnostic
dotnet run --no-build -c Release --project src/Irix.Poc -- --text-composition overlay
```

## Results

- Release build: passed.
- Normal tests: `604` passed.
- D3D12 tests: `6` passed.
- Performance tests: `6` passed.
- Default sync diagnostic: `Text composition mode: GlyphAtlas`, `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, `fallbacks=0`, all fallback reasons `0`, `initFailurePhase=None`.
- Overlay rollback sync diagnostic: `Text composition mode: Overlay`, `frameSerial=900`, `presentSerial=900`, `syncWaits=900`; no device lost.
- Non-ASCII default fallback diagnostic: `Text composition mode: GlyphAtlas`, `frameSerial=900`, `presentSerial=900`, `syncWaits=900`, `fallbacks=900`, `unsupportedRuns=900`, `NonAscii=900`, `initFailurePhase=None`; no device lost.
- Default UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Scissor`, `Partial apply: ENABLED (default)`, `Sync text overlay: ENABLED (default)`, and `Text composition mode: GlyphAtlas`.
- `--disable-scissor` UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Diagnostic`.
- `--clip-mode diagnostic` UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Diagnostic`.
- `--text-composition overlay` UI smoke stayed running for 5 seconds before manual stop and printed `Text composition mode: Overlay`.

## Baseline Notes

- The default ASCII path now avoids overlay sync waits.
- Overlay sync is still required and enabled for fallback frames.
- `GlyphAtlas` remains a prototype renderer path with whole-frame fallback, runtime shader compile, no eviction, no complex shaping, and warm scroll allocation around `6.2 KB/frame` pending attribution.
- `Scissor` is the default backend clip mode; `Diagnostic` remains available for rollback and A/B diagnostics.
