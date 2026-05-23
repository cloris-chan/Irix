# Glyph Atlas Regression Matrix Evidence - 2026-05-23

Scope: local validation that the D3D12-only glyph atlas path has a fixed smoke matrix for ASCII, CJK, Latin Extended, Greek, Cyrillic, Arabic, Hebrew, mixed BiDi, emoji, wrap, tab, and CRLF text, plus an explicit non-overlay degradation contract for remaining unsupported cases.

Commands:

```powershell
dotnet test -c Release --no-restore --filter "FullyQualifiedName~ProgramDiagnosticsTests"
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-matrix 3 --diagnose-scale 150
.\scripts\glyph-atlas-regression.ps1
.\scripts\glyph-atlas-regression.ps1 -Mode Local
.\scripts\glyph-atlas-regression.ps1 -Mode Nightly
```

Current local gate:

```powershell
.\scripts\glyph-atlas-regression.ps1 -Mode Smoke
```

CI quota note:

- `.github/workflows/ci.yml` still configures a `Glyph atlas regression lane` that runs `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke`.
- CI lane configured but currently not runnable because quota is exhausted.
- GitHub Actions quota is currently exhausted, so Actions cannot be used as the status source.
- Until quota is available again, run the Smoke lane locally before and after broad changes and use `TestResults\glyph-atlas-regression-*-*.guard.summary.txt` as the gate evidence.
- Run `-Mode Local` manually after glyph/page/shaping changes. Run `-Mode Nightly` only as a manual long run after page-policy, eviction, or shaping overhauls.

Observed validation:

```text
ProgramDiagnosticsTests: Passed, 107
```

Matrix smoke key output:

```text
Expected matrix: textRuns=13, atlasRuns=13, degradedRuns=0, wrappedRuns=2, tabRuns=1, explicitLineRuns=1, simpleBmpRuns=3, shapedRuns=5, cjkRuns=1, arabicRuns=2, hebrewRuns=1, mixedBidiRuns=1, emojiRuns=1
Degradation contract: svgColorGlyph=True, colrPaintTreeColorGlyph=True, bidiBeyondResolvedLevels=True, atlasFullAfterBudget=True, recordFailure=True, initializationFailure=True, overlayFallback=False
Matrix cases: ASCII=True LatinExtended=True Greek=True Cyrillic=True CJK=True Arabic=True Hebrew=True MixedBidi=True Emoji=True Wrap=True Tab=True CRLF=True
Accepted degradation: overlay=False svgColorGlyph=True colrPaintTreeColorGlyph=True bidiBeyondResolvedLevels=True atlasFullAfterBudget=True recordFailure=True initializationFailure=True
Final: frameSerial=3, presentSerial=3, syncWaits=0
Glyph atlas: cachedGlyphs=106, atlasPages=1, atlasAlphaPages=1, atlasBgraPages=0, atlasBudgetPages=48, atlasPage=1024x1024, atlasCapacity=50331648 px, atlasCpuBytes=1048576 bytes, atlasUploadBytes=2097152 bytes, atlasGpuBytes=1048576 bytes, atlasEvictions=0, atlasAlphaEvictions=0, atlasBgraEvictions=0, atlasPendingPageReuses=0, atlasPendingAlphaPageReuses=0, atlasPendingBgraPageReuses=0, atlasPageReuseRequests=0, atlasAlphaPageReuseRequests=0, atlasBgraPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasAlphaFullWithoutPageReuse=0, atlasBgraFullWithoutPageReuse=0, atlasUsed=27709 px, atlasFragmented=18189 px, atlasAlphaUsed=27709 px, atlasBgraUsed=0 px, atlasAlphaFragmented=18189 px, atlasBgraFragmented=0 px, atlasRecordSerial=3, atlasOldestPageAge=0, atlasNewestPageAge=0, atlasOldestAlphaPageAge=0, atlasOldestBgraPageAge=0, drawnGlyphs=555, atlasRuns=39, degradedRuns=0, uploads=64000 bytes, uploadedGlyphs=106, shapedProbeRuns=15, shapedProbeGlyphs=177, colorLayerRuns=30, colorBitmapRuns=0
```

Interpretation:

- `--diagnose-glyph-atlas-matrix` is now the fixed broad glyph-atlas smoke. It covers the script/control matrix in one scene and keeps expected per-frame classification machine-readable.
- `scripts/glyph-atlas-regression.ps1` is the canonical local smoke entry. `-Mode Smoke` uses a 60-frame soak and should be run before/after broad changes while Actions quota is unavailable; `-Mode Local` uses 300 frames after glyph/page/shaping changes; `-Mode Nightly` uses 900 frames only for manual long runs after page-policy, eviction, or shaping overhauls. All modes run matrix, soak, color-format natural coverage, BiDi oracle, and glyph oracle diagnostics into `TestResults`.
- The fixed lane emits machine-readable `matrix.expected`, `matrix.actual`, `soak.actual`, `bidi-oracle.expected`, `bidi-oracle.actual`, `glyph-oracle.expected`, and `glyph-oracle.actual` lines, then writes a `glyph-atlas-regression-*-*.guard.summary.txt` file only after validating the contract.
- The summary guard fails fast if matrix actual drifts from `degradedRuns=0`, `glyphAtlasInitialized=True`, or `overlaySync=False`; if soak reports device loss, overlay sync, sync waits, hard full-without-reuse, `RecordFailed`, or a non-`None` record failure phase; or if oracle expected/actual probe labels/counts drift.
- Windows CI is configured to run `.\scripts\glyph-atlas-regression.ps1 -Mode Smoke`, but current GitHub Actions quota exhaustion means local guard summaries are the authoritative status until quota returns.
- The matrix remains D3D12-only: `syncWaits=0`, `degradedRuns=0`, and `overlayFallback=False`.
- The explicit accepted-degradation contract is: SVG color glyphs, COLR paint-tree-only color glyphs, BiDi cases beyond current resolved-level projection, AtlasFull after the bounded page budget, record failure, and initialization failure may degrade without invoking D3D11On12/D2D overlay.
- Natural BGRA/encoded-bitmap font coverage remains separate from this matrix because local Segoe UI Emoji exposes COLR/layer runs but no bitmap glyph image data on this machine.
