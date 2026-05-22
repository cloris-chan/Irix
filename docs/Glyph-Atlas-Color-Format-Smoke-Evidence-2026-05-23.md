# Glyph Atlas Color Format Smoke Evidence - 2026-05-23

Scope: local DirectWrite color glyph image-format probe for explaining whether the current machine can naturally exercise D3D12 glyph-atlas color routes. This evidence covers the default installed `Segoe UI Emoji` layer route plus a downloaded/installed Noto Color Emoji Windows-compatible font file that exposes PNG bitmap glyph image data through DirectWrite.

Default installed-font command:

```powershell
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-color-formats 64
```

Observed default output:

```text
=== Glyph Atlas Color Glyph Format Diagnostic ===
Family: Segoe UI Emoji
PixelsPerEm: 64
Color glyph formats: factory4=True, face4=True, probes=8, glyphs=8, colorRunCandidates=7, layerCandidates=7, bgraCandidates=0, encodedBitmapCandidates=0, unsupportedColorCandidates=0, bitmapRenderableCandidates=0, imageDataCandidates=0, decodedBitmapCandidates=0
Color glyph natural coverage: status=LayerOnly, layerRoute=True, bgraRoute=False, encodedBitmapRoute=False, bitmapRenderableRoute=False, imageDataRoute=False, decodedBitmapRoute=False, naturalBgraSmoke=False
Probe: U+2764 heart glyph=4016 status=Ok formats=None route=None colorRuns=4 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F600 grinning glyph=2266 status=Ok formats=None route=None colorRuns=6 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F603 smiling glyph=2269 status=Ok formats=None route=None colorRuns=7 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F680 rocket glyph=2463 status=Ok formats=None route=None colorRuns=7 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F3AF target glyph=891 status=Ok formats=None route=None colorRuns=6 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F525 fire glyph=2125 status=Ok formats=None route=None colorRuns=2 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F469 woman glyph=1462 status=Ok formats=None route=None colorRuns=18 runFormats=TRUETYPE|COLR runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
Probe: U+1F1FA flag-us glyph=293 status=Ok formats=None route=None colorRuns=0 runFormats=None runRoute=None imageDataRoute=None imageDataBytes=0 imageDataPpem=0 imageDataSize=0x0 decodeBytes=0 decodeSize=0x0
=== glyph atlas color glyph format diagnostic complete ===
```

Font-file command:

```powershell
dotnet run --no-build -c Release --project src\Irix.Poc -- --diagnose-glyph-atlas-color-formats 64 --diagnose-color-glyph-font-file "C:\Users\Cloris\AppData\Local\Microsoft\Windows\Fonts\NotoColorEmoji_WindowsCompatible.ttf"
```

Observed font-file output:

```text
=== Glyph Atlas Color Glyph Format Diagnostic ===
Family: FontFile:NotoColorEmoji_WindowsCompatible.ttf
PixelsPerEm: 64
Color glyph formats: factory4=True, face4=True, probes=8, glyphs=8, colorRunCandidates=8, layerCandidates=0, bgraCandidates=0, encodedBitmapCandidates=8, unsupportedColorCandidates=0, bitmapRenderableCandidates=8, imageDataCandidates=8, decodedBitmapCandidates=8
Color glyph natural coverage: status=BitmapRenderableAvailable, layerRoute=False, bgraRoute=False, encodedBitmapRoute=True, bitmapRenderableRoute=True, imageDataRoute=True, decodedBitmapRoute=True, naturalBgraSmoke=True
Probe: U+2764 heart glyph=168 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=1145 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F600 grinning glyph=883 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=3390 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F603 smiling glyph=886 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=3406 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F680 rocket glyph=963 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=3275 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F3AF target glyph=414 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=2535 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F525 fire glyph=784 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=3203 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F469 woman glyph=597 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=1970 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
Probe: U+1F1FA flag-us glyph=225 status=Ok formats=None route=None colorRuns=1 runFormats=PNG runRoute=Png imageDataRoute=Png imageDataBytes=583 imageDataPpem=109 imageDataSize=136x128 decodeBytes=69632 decodeSize=136x128
=== glyph atlas color glyph format diagnostic complete ===
```

Interpretation:

- Default `Segoe UI Emoji` resolves the probe glyphs, `IDWriteFactory4` and `IDWriteFontFace4` are available, and `IDWriteFactory4.TranslateColorGlyphRun` exposes DirectWrite-renderable `TRUETYPE|COLR` layer runs for seven probes.
- Default `Segoe UI Emoji` still exposes no DirectWrite BGRA/PNG/JPEG/TIFF glyph image data for these probes at `ppem=64`, so ordinary local wrap smoke reporting `atlasBgraPages=0` is expected for the default font.
- `--diagnose-color-glyph-font-file` bypasses Windows font collection/cache availability by creating a DirectWrite font-face directly from the `.ttf` file. This made the downloaded Noto Color Emoji Windows-compatible file usable even before the system font collection reported the family.
- Noto Color Emoji naturally exposes PNG color glyph runs for all eight probes. `GetGlyphImageData(PNG)` returns non-empty image data for all eight probes, and WIC decodes each image into `32bppPBGRA` bytes with `decodeSize=136x128` and `decodeBytes=69632`.
- This naturally exercises the encoded bitmap color-glyph route needed by the D3D12 `Bgra` atlas path: DirectWrite source data is PNG, CPU decode is WIC, and final composition remains the D3D12 glyph atlas.
- No local font in this evidence exposes DirectWrite `PREMULTIPLIED_B8G8R8A8` or TIFF image data. Those branches remain covered by selector/raster/page-format tests and guards, while the shared encoded bitmap PNG/JPEG/TIFF path now has natural PNG coverage.
