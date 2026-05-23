# Glyph Atlas Glyph Oracle Evidence - 2026-05-23

Scope: local DirectWrite analyzer/font-fallback oracle for shaping and layout data only. This diagnostic does not use `IDWriteTextLayout`, Direct2D rendering, D3D11On12, or overlay fallback.

Command:

```powershell
dotnet run --project src/Irix.Poc -c Release -- --diagnose-glyph-atlas-glyph-oracle
```

Local result:

```text
=== Glyph Atlas Glyph Oracle Diagnostic ===
Glyph oracle: factory=True, analyzer=True, fontFallback=True, probes=5, failedProbes=0, fallbackFontProbes=5, mixedBidiProbes=2, lineBreakProbes=5, totalGlyphs=67
Probe: ascii base=LTR textLength=16 glyphCount=16 ...
Probe: cjk-fallback base=LTR textLength=15 glyphCount=15 ...
Probe: arabic-rtl base=RTL textLength=9 glyphCount=9 ...
Probe: mixed-bidi base=LTR textLength=13 glyphCount=13 ...
Probe: tab-crlf base=LTR textLength=14 glyphCount=14 ...
=== glyph atlas glyph oracle diagnostic complete ===
```

Expected contract:

- The summary line is `Glyph oracle: ...` with factory, analyzer, font fallback, probe, fallback-font, mixed-BiDi, line-break, and total-glyph counters.
- Each probe line reports glyph count, resolved bidi levels, line-break flags, fallback-font segments, glyph indices, advances, and offsets.
- The oracle covers ASCII, CJK fallback, Arabic RTL, mixed BiDi, and tab/CRLF probes without changing renderer coverage.

This evidence is structural. It is intended to anchor oracle/regression work before any new script or glyph-image-format support is accepted into the renderer.
