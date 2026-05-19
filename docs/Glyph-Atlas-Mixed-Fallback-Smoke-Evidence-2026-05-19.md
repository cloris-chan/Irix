# Glyph Atlas Mixed Fallback Smoke Evidence - 2026-05-19

Scope: follow-up evidence for mixed glyph-atlas / overlay fallback behavior after `b80965b`. Public API remains unchanged. `--text-composition overlay` remains the whole-frame overlay rollback.

## Commands

```powershell
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "FullyQualifiedName~ProgramDiagnosticsTests" --verbosity normal
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150 --text-composition overlay
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-stress --mixed-fallback
```

Validation result: Release build passed, and `ProgramDiagnosticsTests` passed with `55` tests.

## Mixed Fallback Scenario

`--diagnose-glyph-atlas-mixed-fallback` builds this fixed text order each frame:

```text
ASCII atlas -> NonAscii fallback -> clipped ASCII atlas -> clipped NonAscii fallback
```

Expected `GlyphAtlas` classification per frame:

| Field | Expected |
|-------|----------|
| Text runs | 4 |
| Atlas runs | 2 |
| Overlay fallback runs | 2 |
| NonAscii fallback runs | 2 |
| Clipped atlas runs | 1 |
| Clipped overlay fallback runs | 1 |

`GlyphAtlas` result at 150% diagnostic scale:

- `frameSerial=30`, `presentSerial=30`, `syncWaits=30`.
- `atlasRuns=60`, `overlayFallbackRuns=60`, `fallbacks=30`, `unsupportedRuns=60`, `NonAscii=60`.
- `Overlay subset parity`: `fallbackRuns=2`, `wholeFrameOverlayRuns=4`, `matchesWholeFrame=True`, `resolver=True`, `style=True`, `clip=True`, `scale=True`, `color=True`.
- `textClipSkipped=0`, `lastEffectiveTextClip=(36,264,168,39)`.
- `initFailurePhase=None`, `recordFailurePhase=None`.
- Device-lost did not occur.

The whole-frame overlay comparison command completed the same 30 frames with `syncWaits=30`, `textClipSkipped=0`, the same final effective text clip, and the same overlay subset parity line.
The subset path uses the same `D3D12TextRenderer.Render(...)` primitive but passes only the fallback text runs.

## Mixed AtlasFull Stress

`--diagnose-glyph-atlas-stress --mixed-fallback` builds a single frame with two small ASCII prefix runs, the AtlasFull stress set, and one trailing NonAscii fallback run.

Result:

- `Scenario: MixedAtlasFull`.
- `frameSerial=1`, `presentSerial=1`, `syncWaits=1`.
- `atlasRuns=5`, `overlayFallbackRuns=30`, `fallbacks=1`, `unsupportedRuns=30`.
- Reasons: `AtlasFull=29`, `NonAscii=1`, `RecordFailed=0`.
- `cachedGlyphs=407`, `drawnGlyphs=306`, `uploads=1041408 bytes`, `hits=316`, `misses=436`.
- `initFailurePhase=None`, `recordFailurePhase=None`.
- Device-lost did not occur.

Acceptance: AtlasFull did not force whole-frame overlay fallback. Earlier accepted atlas runs still recorded as atlas runs, while later AtlasFull and NonAscii runs used the overlay fallback subset.

## Z-Order Limitation

The mixed smoke intentionally has a fallback text run before a later atlas text run in command order. Current `GlyphAtlas` pass order is:

```text
rects -> atlas accepted text runs -> overlay fallback text runs -> present
```

Therefore the fallback overlay run draws after the later atlas run. This is the current v0 limitation and is not full text-command z-order preservation. The overlay rollback path is still whole-frame overlay and does not have this mixed atlas/fallback z-order split.

## Overlay Subset Correctness

The added unit coverage checks the fallback subset keeps the original `TextData` fields used by the overlay renderer: frame resource resolver, resolved physical text style, effective clip, clip enablement, color, and scale-adjusted coordinates.
The 150% mixed smoke now prints the parity contract directly: two fallback subset runs match their whole-frame overlay inputs out of four text runs, including the clipped fallback run.

This is not an automated pixel-equivalence oracle. It verifies that the fallback subset is drawn by the same overlay renderer with the same per-run inputs that whole-frame overlay would use for those fallback runs.

## Failure-Path Contract

The new unit coverage pins the runtime record-failure degradation contract without adding a renderer test hook.
When a record failure such as `AtlasUploadMap` occurs, the fallback list is every renderable text run in that frame, empty or zero-size text is ignored, diagnostics count `RecordFailed` per fallback run, and `recordFailurePhase` records the failing phase.

This is contract coverage, not a forced GPU upload failure smoke. The real renderer path still owns the failure trigger and disables the atlas instance after a runtime record failure.

## Default Long Smoke

Default `GlyphAtlas` long smoke:

- Command: `dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3`
- `frameSerial=900`, `presentSerial=900`, `syncWaits=0`.
- `atlasRuns=2700`, `overlayFallbackRuns=0`, `fallbacks=0`, `unsupportedRuns=0`.
- `cachedGlyphs=29`, `drawnGlyphs=41070`, `uploads=45568 bytes`, `hits=89311`, `misses=29`.
- `initFailurePhase=None`, `recordFailurePhase=None`.
- Device-lost did not occur.
