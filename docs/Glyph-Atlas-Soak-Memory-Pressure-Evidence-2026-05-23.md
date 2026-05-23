# Glyph Atlas Soak / Memory Pressure Evidence - 2026-05-23

Scope: local validation that glyph atlas page growth, resident bytes, cold-page reuse, and fragmentation stay observable during a longer mixed matrix/wrap/reuse/pressure run without reintroducing D3D11On12/D2D overlay.

Commands:

```powershell
dotnet test -c Release --no-restore --filter "FullyQualifiedName~ProgramDiagnosticsTests"
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-soak 60 --pressure-every 6 --diagnose-scale 150
.\scripts\glyph-atlas-regression.ps1 -Mode Local
.\scripts\glyph-atlas-regression.ps1 -Mode Nightly
```

Observed validation:

```text
ProgramDiagnosticsTests: Passed, 98
```

Soak smoke key output:

```text
Frames: 60
Pressure cadence: every 6 frame(s)
Page policy: budgetPages=48, pageReuse=FormatScopedColdPage, retainedFloorGate=True, currentRecordColdReuse=True, sameRecordTouchedReuse=False, entryLru=False, subRectFreeList=False
Final: frameSerial=60, presentSerial=60, syncWaits=0
Soak summary: frames=60, pressureFrames=10, matrixFrames=15, wrapFrames=10, reuseFrames=25, maxAtlasPages=48, maxAlphaPages=48, maxBgraPages=0, maxAtlasCpuBytes=50331648 bytes, maxAtlasGpuBytes=50331648 bytes, maxAtlasUsed=34777238 px, maxAtlasFragmented=13369251 px, atlasEvictions=231, atlasAlphaEvictions=231, atlasBgraEvictions=0, atlasPendingPageReuses=0, atlasPendingAlphaPageReuses=0, atlasPendingBgraPageReuses=0, atlasPageReuseRequests=0, atlasAlphaPageReuseRequests=0, atlasBgraPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasAlphaFullWithoutPageReuse=0, atlasBgraFullWithoutPageReuse=0, maxDegradedRuns=49
Glyph atlas: cachedGlyphs=3852, atlasPages=48, atlasAlphaPages=48, atlasBgraPages=0, atlasBudgetPages=48, atlasPage=1024x1024, atlasCapacity=50331648 px, atlasCpuBytes=50331648 bytes, atlasUploadBytes=100663296 bytes, atlasGpuBytes=50331648 bytes, atlasEvictions=231, atlasAlphaEvictions=231, atlasBgraEvictions=0, atlasPendingPageReuses=0, atlasPendingAlphaPageReuses=0, atlasPendingBgraPageReuses=0, atlasPageReuseRequests=0, atlasAlphaPageReuseRequests=0, atlasBgraPageReuseRequests=0, atlasFullWithoutPageReuse=0, atlasAlphaFullWithoutPageReuse=0, atlasBgraFullWithoutPageReuse=0, atlasUsed=33910562 px, atlasFragmented=12890703 px, atlasAlphaUsed=33910562 px, atlasBgraUsed=0 px, atlasAlphaFragmented=12890703 px, atlasBgraFragmented=0 px, atlasRecordSerial=60, atlasOldestPageAge=5, atlasNewestPageAge=0, atlasOldestAlphaPageAge=5, atlasOldestBgraPageAge=0, drawnGlyphs=31239, atlasRuns=666, degradedRuns=49
```

Interpretation:

- The soak runner alternates broad matrix frames, wrap frames, reuse frames, and pressure frames. Pressure frames intentionally vary font sizes enough to fill the bounded 48-page Alpha atlas pool.
- The current policy is page-level and format-scoped: cold pages not touched in the current record may be reused immediately; otherwise retained-floor-gated next-record reuse remains the safety contract. Touched same-record reuse is not allowed.
- `entryLru=False` and `subRectFreeList=False` are explicit in the page policy output. Full entry-level LRU / sub-rect eviction remains deferred.
- The soak summary now reports pending reuse and hard full-without-reuse counters split by Alpha/Bgra, matching the final glyph-atlas diagnostic summary.
- This run reached the full 48 Alpha page budget and reused cold pages 231 times without pending reuse buildup, hard full-without-reuse, device removal, record failure, or overlay sync waits.
- `degradedRuns=49` / `AtlasFull=49` is accepted pressure behavior after budget saturation; it is non-overlay degradation and remains observable through diagnostics.
- The fixed regression script promotes the soak checks into recurring lanes: `-Mode Local` is the 300-frame local cadence, and `-Mode Nightly` is the 900-frame large-change cadence. Both are expected to keep `deviceLost=False`, `syncWaits=0`, `hardFullWithoutReuse=0`, `RecordFailed=0`, and `recordFailurePhase=None`; pressure degradation is accepted only while it remains explicit `AtlasFull`/budget behavior.
