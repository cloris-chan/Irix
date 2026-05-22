# Glyph Atlas Residency Smoke Evidence - 2026-05-23

Scope: local validation that glyph atlas diagnostics expose resident CPU shadow bytes, resident D3D12 upload-buffer bytes, resident D3D12 atlas texture bytes, and Alpha/Bgra-split page pressure while the renderer remains D3D12-only.

Commands:

```powershell
dotnet test -c Release --no-restore --filter "FullyQualifiedName~ProgramDiagnosticsTests"
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance"
dotnet test --no-build -c Release --filter "Category=D3D12"
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-wrap 3 --diagnose-scale 150
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-stress --reuse-page
```

Observed validation:

```text
ProgramDiagnosticsTests: Passed, 91
Release build: Passed
Non-D3D12/non-performance tests: Passed, 667
D3D12 tests: Passed, 6
```

Wrap smoke key output:

```text
Final: frameSerial=3, presentSerial=3, syncWaits=0
Glyph atlas: cachedGlyphs=99, atlasPages=1, atlasAlphaPages=1, atlasBgraPages=0, atlasBudgetPages=48, atlasPage=1024x1024, atlasCapacity=50331648 px, atlasCpuBytes=1048576 bytes, atlasUploadBytes=2097152 bytes, atlasGpuBytes=1048576 bytes, atlasEvictions=0, atlasPendingPageReuses=0, atlasPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasUsed=22575 px, atlasFragmented=17977 px, atlasAlphaUsed=22575 px, atlasBgraUsed=0 px, atlasAlphaFragmented=17977 px, atlasBgraFragmented=0 px, atlasRecordSerial=3, atlasOldestPageAge=0, atlasNewestPageAge=0, atlasOldestAlphaPageAge=0, atlasOldestBgraPageAge=0, drawnGlyphs=732, atlasRuns=45, degradedRuns=0, uploads=90880 bytes, uploadedGlyphs=99, shapedProbeRuns=24, shapedProbeGlyphs=327, colorLayerRuns=30, colorBitmapRuns=0
```

Stress reuse smoke key output:

```text
Scenario: MixedAtlasFullReuse
Frame serial: frameSerial=2, presentSerial=2, syncWaits=0
Glyph atlas: cachedGlyphs=3059, atlasPages=38, atlasAlphaPages=38, atlasBgraPages=0, atlasBudgetPages=48, atlasPage=1024x1024, atlasCapacity=50331648 px, atlasCpuBytes=39845888 bytes, atlasUploadBytes=79691776 bytes, atlasGpuBytes=39845888 bytes, atlasEvictions=0, atlasPendingPageReuses=0, atlasPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasUsed=26837719 px, atlasFragmented=10374823 px, atlasAlphaUsed=26837719 px, atlasBgraUsed=0 px, atlasAlphaFragmented=10374823 px, atlasBgraFragmented=0 px, atlasRecordSerial=2, atlasOldestPageAge=1, atlasNewestPageAge=0, atlasOldestAlphaPageAge=1, atlasOldestBgraPageAge=0, drawnGlyphs=3030, atlasRuns=34, degradedRuns=0
```

Interpretation:

- The glyph atlas cache has three resident stores: a CPU shadow `byte[]` per page used for glyph raster/decode staging, per-frame D3D12 upload buffers used to copy dirty page pixels, and a D3D12 texture/SRV per page used for final composition.
- `atlasCpuBytes`, `atlasUploadBytes`, and `atlasGpuBytes` are derived from live Alpha/Bgra page counts and page format bytes-per-pixel. One Alpha page reports `1048576` CPU bytes, `2097152` upload bytes, and `1048576` GPU texture bytes; thirty-eight Alpha pages report `39845888`, `79691776`, and `39845888` bytes respectively.
- `atlasAlphaUsed` / `atlasBgraUsed`, `atlasAlphaFragmented` / `atlasBgraFragmented`, and `atlasOldestAlphaPageAge` / `atlasOldestBgraPageAge` split page pressure by atlas format. These smokes exercised only Alpha pages, so the BGRA split remains zero while total usage equals Alpha usage.
- Local wrap and stress smokes remained atlas-only with `degradedRuns=0`, `RecordFailed=0`, and `syncWaits=0`; no D3D11On12/D2D overlay path is involved.
