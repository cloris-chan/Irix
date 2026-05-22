# Glyph Atlas Color Format Smoke Evidence - 2026-05-23

Scope: local DirectWrite color glyph image-format probe for explaining whether the current machine can naturally exercise the D3D12 `Bgra` atlas path through installed emoji/color fonts.

Command:

```powershell
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-color-formats 64
```

Observed output:

```text
=== Glyph Atlas Color Glyph Format Diagnostic ===
Family: Segoe UI Emoji
PixelsPerEm: 64
Color glyph formats: face4=True, probes=8, glyphs=8, layerCandidates=0, bgraCandidates=0, encodedBitmapCandidates=0, unsupportedColorCandidates=0, bitmapRenderableCandidates=0
Probe: U+2764 heart glyph=4016 status=Ok formats=None route=None
Probe: U+1F600 grinning glyph=2266 status=Ok formats=None route=None
Probe: U+1F603 smiling glyph=2269 status=Ok formats=None route=None
Probe: U+1F680 rocket glyph=2463 status=Ok formats=None route=None
Probe: U+1F3AF target glyph=891 status=Ok formats=None route=None
Probe: U+1F525 fire glyph=2125 status=Ok formats=None route=None
Probe: U+1F469 woman glyph=1462 status=Ok formats=None route=None
Probe: U+1F1FA flag-us glyph=293 status=Ok formats=None route=None
=== glyph atlas color glyph format diagnostic complete ===
```

Interpretation:

- The default Segoe UI Emoji probes resolve to glyph indices and `IDWriteFontFace4` is available.
- This machine does not expose DirectWrite `PREMULTIPLIED_B8G8R8A8`, PNG, JPEG, or TIFF image data for these probes at `ppem=64`.
- Therefore `--diagnose-glyph-atlas-wrap` reporting `atlasBgraPages=0` on this machine is expected and does not imply that the D3D12 BGRA atlas route has regressed.
- The BGRA/encoded bitmap route remains code-covered through format selector, raster/decode, page-format, diagnostics, and source guards; natural local smoke needs an installed color font that exposes DirectWrite bitmap image formats.
