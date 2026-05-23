# Glyph Atlas BiDi Oracle Evidence - 2026-05-23

Scope: local DirectWrite `IDWriteTextAnalyzer.AnalyzeBidi` oracle for validating that the glyph atlas resolved-level visual-order helper is exercised against real DirectWrite bidi levels, without reintroducing `IDWriteTextLayout`, Direct2D, D3D11On12, or overlay fallback.

Command:

```powershell
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-bidi-oracle
```

Observed output:

```text
=== Glyph Atlas BiDi Oracle Diagnostic ===
bidi-oracle.expected probes=4 labels=ltr-arabic-ltr|rtl-leading-digits|hebrew-weak-digits|nested-mixed fields=levels|logicalRuns|visualRuns|charOrder layoutOracle=False pixelOracle=False overlayFallback=False
BiDi oracle: factory=True, analyzer=True, probes=4, mixedLevelProbes=4, visualReorderedProbes=4, failedProbes=0
bidi-oracle.actual probes=4 labels=ltr-arabic-ltr|rtl-leading-digits|hebrew-weak-digits|nested-mixed mixedLevelProbes=4 visualReorderedProbes=4 failedProbes=0 layoutOracle=False pixelOracle=False overlayFallback=False
Probe: ltr-arabic-ltr base=LTR textLength=13 levels=0,0,0,0,1,1,1,1,1,0,0,0,0 logicalRuns=[0..4@0|4..9@1|9..13@0] visualRuns=[0..4@0|4..9@1|9..13@0] charOrder=[0,1,2,3,8,7,6,5,4,9,10,11,12]
Probe: rtl-leading-digits base=RTL textLength=13 levels=2,2,2,1,1,1,1,1,1,1,2,2,2 logicalRuns=[0..3@2|3..10@1|10..13@2] visualRuns=[10..13@2|3..10@1|0..3@2] charOrder=[10,11,12,9,8,7,6,5,4,3,0,1,2]
Probe: hebrew-weak-digits base=RTL textLength=12 levels=1,1,1,1,1,2,2,2,1,2,2,2 logicalRuns=[0..5@1|5..8@2|8..9@1|9..12@2] visualRuns=[9..12@2|8..9@1|5..8@2|0..5@1] charOrder=[9,10,11,8,5,6,7,4,3,2,1,0]
Probe: nested-mixed base=LTR textLength=16 levels=0,0,0,0,1,1,1,2,2,1,1,1,0,0,0,0 logicalRuns=[0..4@0|4..7@1|7..9@2|9..12@1|12..16@0] visualRuns=[0..4@0|9..12@1|7..9@2|4..7@1|12..16@0] charOrder=[0,1,2,3,11,10,9,7,8,6,5,4,12,13,14,15]
=== glyph atlas bidi oracle diagnostic complete ===
```

Interpretation:

- DirectWrite factory and text analyzer are available, and all four probes produced resolved levels with no failures.
- All four probes have mixed resolved levels and non-identity character visual order, so the diagnostic exercises the current nested resolved-level reordering helper against DirectWrite-generated inputs.
- The expected/actual machine lines intentionally describe the structural snapshot contract only: probe labels plus levels/logical-runs/visual-runs/character-order fields. They do not claim full layout or pixel oracle coverage.
- The probe intentionally uses `IDWriteTextAnalyzer.AnalyzeBidi` only. It does not add `IDWriteTextLayout`, Direct2D rendering, D3D11On12, or any overlay fallback.
- This closes the immediate structural oracle gap for resolved-level ordering. A production pixel/glyph-run oracle against full DirectWrite layout output remains a later correctness task for cases beyond the current resolved-level projection.
