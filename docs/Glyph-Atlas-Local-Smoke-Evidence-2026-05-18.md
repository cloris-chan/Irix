# Glyph Atlas Local Smoke Evidence - 2026-05-18

Branch: `post-ga-renderer-foundation`

Scope: opt-in D3D12 glyph atlas text composition smoke evidence. Public API remains unchanged. Overlay remains the default composition path and DirectWrite remains the shaping/raster source plus overlay fallback.

## Environment

- Date/timezone: 2026-05-18, Asia/Taipei
- OS display scale observed by diagnostics: `1.5x1.5`
- Display refresh observed by diagnostics: `240Hz`
- D3D12 test environment: local Windows/D3D12

## Commands

Baseline validation:

```powershell
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance" --verbosity normal
dotnet test --no-build -c Release --filter "Category=D3D12" --verbosity normal
dotnet test --no-build -c Release --filter "Category=Performance" --verbosity normal
dotnet publish src/Irix.Poc/Irix.Poc.csproj -c Release -r win-x64 --self-contained
```

Composition/sync smoke:

```powershell
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --text-composition overlay
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --text-composition glyph-atlas
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 5 1 --text-composition glyph-atlas --diagnose-sync-non-ascii
```

Resize/scale/cache smoke:

```powershell
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-resize --text-composition glyph-atlas
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-text-cache 30 --text-composition glyph-atlas --diagnose-scale 100
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-text-cache 30 --text-composition glyph-atlas --diagnose-scale 150
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-text-cache 30 --text-composition glyph-atlas --diagnose-scale 200
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-stress
```

Notes:

- `--diagnose-scale` is a diagnostic override for translator/compositor scale. It does not change OS DPI.
- The local OS scale for this run was 150%. The 100% and 200% evidence is from forced diagnostic scale plus existing scale tests.

## Expected Evidence To Preserve

- Overlay sync path reports `syncWaits=900` for `300 x 3` and uses `Text composition mode: Overlay`.
- Glyph atlas ASCII path reports `syncWaits=0`, `fallbacks=0`, nonzero `drawnGlyphs`, and fallback reasons all zero.
- Non-ASCII glyph atlas smoke reports safe overlay fallback, `fallbacks=5`, `unsupportedRuns=5`, and `NonAscii=5`; no device lost.
- Resize glyph atlas smoke reports `Device removed: False` and a `Glyph atlas:` diagnostics line.
- Scale partition smoke records `cachedGlyphs`, `uploads`, `misses`, `initFailurePhase`, and `rasterScratch` for `--diagnose-scale 100`, `150`, and `200`.
- Init failure diagnostics must include `initFailurePhase`, with phases covering `RootSignature`, `ShaderCompile`, `PSO`, `AtlasTexture`, `UploadBuffer`, and `VertexBuffer`.
- Atlas stress reports `AtlasFull` fallback without device removal; eviction remains deferred.

## 2026-05-18 Local Results

Validation:

- `dotnet restore`: passed
- `dotnet build --no-restore -c Release`: passed
- normal tests: `594` passed
- D3D12 tests: `6` passed
- performance tests: `6` passed
- self-contained publish: passed

Sync comparison:

- Overlay `300 x 3`: `frameSerial=900`, `presentSerial=900`, `syncWaits=900`; avg sync wait range `1.991ms..2.346ms`; no device lost.
- Glyph atlas `300 x 3`: `frameSerial=900`, `presentSerial=900`, `syncWaits=0`; `cachedGlyphs=29`, `drawnGlyphs=41070`, `uploads=45568 bytes`, `misses=29`, `fallbacks=0`, all fallback reasons `0`, `initFailurePhase=None`, `rasterScratch=1088 bytes/8 resizes`.
- Glyph atlas non-ASCII fallback `5 x 1`: `frameSerial=5`, `presentSerial=5`, `syncWaits=5`; `drawnGlyphs=0`, `uploads=0 bytes`, `fallbacks=5`, `unsupportedRuns=5`, `NonAscii=5`, `initFailurePhase=None`; no device lost.
- Final short glyph atlas smoke `30 x 1`: `frameSerial=30`, `presentSerial=30`, `syncWaits=0`; `cachedGlyphs=29`, `drawnGlyphs=1340`, `uploads=45568 bytes`, `fallbacks=0`, all fallback reasons `0`, `initFailurePhase=None`, `rasterScratch=1088 bytes/8 resizes`.

Default UI smoke:

- Published `Irix.Poc.exe --text-composition glyph-atlas` stayed running for 5 seconds and printed `Text composition mode: GlyphAtlas`, `Display scale: 1.5x1.5`; the process was stopped manually after the smoke window.

Resize smoke:

- `--diagnose-resize --text-composition glyph-atlas`: `Device removed: False`, `swapchain=929x454`, `scale=1.5x1.5`, `cachedGlyphs=24`, `drawnGlyphs=3920`, `uploads=10240 bytes`, `misses=24`, `fallbacks=0`, `initFailurePhase=None`, `rasterScratch=1020 bytes/4 resizes`.

Scale partition smoke:

| Diagnostic scale | static cached/upload/miss | scroll cached/upload/miss | scale-change cached/upload/miss | Fallbacks |
| --- | --- | --- | --- | --- |
| `100` | `15 / 3328 bytes / 15` | `18 / 3328 bytes / 3` | `36 / 9472 bytes / 18` | `0` |
| `150` | `15 / 4864 bytes / 15` | `18 / 4864 bytes / 3` | `38 / 11008 bytes / 20` | `0` |
| `200` | `15 / 6400 bytes / 15` | `18 / 6400 bytes / 3` | `36 / 9216 bytes / 18` | `0` |

The upload sizes and scratch sizes differ by diagnostic scale, which gives evidence that the atlas key includes resolved physical font size rather than stretching a 100% raster at 150% or 200%.

Atlas full stress:

- `--diagnose-glyph-atlas-stress`: `Device removed: False`, `frameSerial=1`, `presentSerial=1`, `syncWaits=1`; `cachedGlyphs=344`, `misses=345`, `fallbacks=1`, `unsupportedRuns=1`, `AtlasFull=1`, `initFailurePhase=None`, `rasterScratch=22680 bytes/26 resizes`.
- The stress frame intentionally falls back to overlay after atlas allocation fails. The single sync wait is expected for the fallback frame and proves fallback composition stayed available.

## State Ownership

The glyph atlas pass records before command-list close/execute. It owns D3D12 PSO, root signature, descriptor heap, viewport/scissor, vertex buffer, and primitive topology state until the command list ends. The current frame has no later D3D12 draw pass after glyph atlas; a future pass must explicitly rebind its own state.

## Deferred Work

- Atlas eviction and complex shaping are still deferred.
- Non-ASCII, fallback font identity, color glyphs, SDF/MSDF, and atlas packing improvements remain post-prototype work.
- Glyph miss scratch buffers are reusable and attributed in diagnostics, but more detailed allocation telemetry can be added later if miss-path churn becomes a measured issue.
