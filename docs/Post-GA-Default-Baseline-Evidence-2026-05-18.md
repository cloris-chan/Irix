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
```

Copy-paste default and rollback smokes:

```powershell
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --text-composition overlay
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --diagnose-sync-non-ascii

dotnet run --no-build -c Release --project src/Irix.Poc
dotnet run --no-build -c Release --project src/Irix.Poc -- --disable-scissor
dotnet run --no-build -c Release --project src/Irix.Poc -- --clip-mode diagnostic
dotnet run --no-build -c Release --project src/Irix.Poc -- --text-composition overlay
```

Expected smoke headers:

| Command | Expected header |
|---------|-----------------|
| `dotnet run --no-build -c Release --project src/Irix.Poc` | `Backend clip mode: Scissor`, `Text composition mode: GlyphAtlas`, `Partial apply: ENABLED (default)` |
| `dotnet run --no-build -c Release --project src/Irix.Poc -- --text-composition overlay` | `Text composition mode: Overlay` |
| `dotnet run --no-build -c Release --project src/Irix.Poc -- --disable-scissor` | `Backend clip mode: Diagnostic` |
| `dotnet run --no-build -c Release --project src/Irix.Poc -- --clip-mode diagnostic` | `Backend clip mode: Diagnostic` |

## Results

- Release build: passed.
- Normal tests: `604` passed.
- D3D12 tests: `6` passed.
- Performance tests: `6` passed.
- Default sync diagnostic: `Text composition mode: GlyphAtlas`, `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, `fallbacks=0`, all fallback reasons `0`, `initFailurePhase=None`.
- Overlay rollback sync diagnostic: `Text composition mode: Overlay`, `frameSerial=900`, `presentSerial=900`, `syncWaits=900`; no device lost.
- Historical pre-mixed-fallback Non-ASCII default diagnostic: `Text composition mode: GlyphAtlas`, `frameSerial=900`, `presentSerial=900`, `syncWaits=900`, `fallbacks=900`, `unsupportedRuns=900`, `NonAscii=900`, `initFailurePhase=None`; no device lost.
- Default UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Scissor`, `Partial apply: ENABLED (default)`, `Sync text overlay: ENABLED (default)`, and `Text composition mode: GlyphAtlas`.
- `--disable-scissor` UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Diagnostic`.
- `--clip-mode diagnostic` UI smoke stayed running for 5 seconds before manual stop and printed `Backend clip mode: Diagnostic`.
- `--text-composition overlay` UI smoke stayed running for 5 seconds before manual stop and printed `Text composition mode: Overlay`.

## Baseline Notes

- The default ASCII path now avoids overlay sync waits.
- Overlay sync is still required and enabled for fallback frames.
- `GlyphAtlas` remains a prototype renderer path with no eviction, no complex shaping, and warm scroll allocation around `6.2 KB/frame`. Runtime shader compile has since been removed in favor of embedded DXBC bytecode, `--diagnose-text-cache` now prints tree/diff/translate/render allocation attribution, and mixed fallback v0 has superseded the whole-frame fallback behavior recorded earlier in this evidence file.
- `Scissor` is the default backend clip mode; `Diagnostic` remains available for rollback and A/B diagnostics.

## P1 Hardening Evidence

- Runtime shader compile removal: D3D12 rectangle and glyph-atlas passes now use embedded DXBC bytecode; `D3DCompile` is removed from renderer source generation.
- Self-contained publish after shader packaging removal: `dotnet publish src/Irix.Poc/Irix.Poc.csproj -c Release -r win-x64 --self-contained` passed.
- Default GlyphAtlas sync smoke after embedded bytecode fix: `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, `fallbacks=0`, `initFailurePhase=None`.
- Overlay rollback sync smoke remains available: `--text-composition overlay` produced `frameSerial=900`, `presentSerial=900`, `syncWaits=900`.
- Historical pre-mixed-fallback Non-ASCII result: `--diagnose-sync-non-ascii` produced `fallbacks=900`, `unsupportedRuns=900`, `NonAscii=900`, `syncWaits=900`, `initFailurePhase=None`. Current mixed fallback v0 evidence is recorded below.
- Warm scroll allocation attribution from `--diagnose-text-cache 30`: total `193584 bytes`, `6452 bytes/frame`; attribution `tree=144440 bytes (4814/frame)`, `diff=3752 bytes (125/frame)`, `translate=49200 bytes (1640/frame)`, `render=8200 bytes (273/frame)`.
- D3D12 failure diagnostics hardening: glyph-atlas initialization failures use `initFailurePhase`; runtime record/upload/map failures now use `recordFailurePhase` and `RecordFailed` fallback reason before overlay fallback. This keeps runtime fallback distinct from constructor-time atlas setup failure.
- Latest resource/failure hardening validation: Release build passed; normal tests `608` passed; D3D12 tests `6` passed; performance tests `6` passed; self-contained publish passed.

## Mixed Fallback v0 Evidence

- Release build: passed.
- Normal tests: `610` passed.
- D3D12 tests: `6` passed.
- Performance tests: `6` passed.
- Default GlyphAtlas short sync smoke: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 30 1` produced `frameSerial=30`, `presentSerial=30`, `syncWaits=0`, `atlasRuns=90`, `overlayFallbackRuns=0`, `fallbacks=0`, `unsupportedRuns=0`, `initFailurePhase=None`, `recordFailurePhase=None`.
- Mixed NonAscii short sync smoke: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 30 1 --diagnose-sync-non-ascii` produced `frameSerial=30`, `presentSerial=30`, `syncWaits=30`, `atlasRuns=60`, `overlayFallbackRuns=30`, `fallbacks=30`, `unsupportedRuns=30`, `NonAscii=30`, `initFailurePhase=None`, `recordFailurePhase=None`.
- Acceptance: mixed NonAscii no longer forces every text run in the frame through overlay. The two ASCII runs per frame stayed on atlas and the one NonAscii run per frame used overlay.
- Remaining limitation: because overlay still renders the fallback run, the mixed NonAscii smoke still has one sync wait per frame. This is expected until overlay removal has a separate replacement and smoke evidence.

## 2026-05-19 Mixed Fallback Extended Evidence

- Extended mixed command: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150`.
- Scenario order: `ASCII atlas -> NonAscii fallback -> clipped ASCII atlas -> clipped NonAscii fallback`.
- Result: `frameSerial=30`, `presentSerial=30`, `syncWaits=30`, `atlasRuns=60`, `overlayFallbackRuns=60`, `fallbacks=30`, `unsupportedRuns=60`, `NonAscii=60`, `textClipSkipped=0`, `lastEffectiveTextClip=(36,264,168,39)`, `initFailurePhase=None`, `recordFailurePhase=None`.
- Whole-frame overlay comparison command: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150 --text-composition overlay`.
  It completed with `frameSerial=30`, `presentSerial=30`, `syncWaits=30`, `textClipSkipped=0`, and the same final effective text clip.
- Default long command: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3`.
  It produced `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, `atlasRuns=2700`, `overlayFallbackRuns=0`, `fallbacks=0`, `unsupportedRuns=0`, `initFailurePhase=None`, `recordFailurePhase=None`.
- Mixed AtlasFull command: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-stress --mixed-fallback`.
  It produced `frameSerial=1`, `presentSerial=1`, `syncWaits=1`, `atlasRuns=5`, `overlayFallbackRuns=30`, `fallbacks=1`, `unsupportedRuns=30`, `AtlasFull=29`, `NonAscii=1`, `RecordFailed=0`, `initFailurePhase=None`, `recordFailurePhase=None`, and no device removal.
- Record-failure contract tests pin all-renderable-run fallback for runtime record failures such as `AtlasUploadMap`; this is unit contract coverage rather than a forced GPU upload-failure smoke.
- Z-order limitation remains explicit: mixed fallback draws all overlay fallback runs after atlas accepted runs. The extended smoke does not claim full relative text-command order preservation.
