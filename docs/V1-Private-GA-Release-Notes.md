# Irix v1.0 Private GA Release Notes

> Private/internal GA milestone for the Irix Windows PoC. This is not a public API freeze.

Tag: `v1.0-private-ga`
Date: 2026-05-17
Scope: private repository, Windows PoC, D3D12 backend with D3D11On12/D2D/DirectWrite text overlay at the tag. The active post-GA branch has since removed that overlay path.

---

## Status

Irix V1 is architecture-complete for the private GA milestone. The core MVU, typed node/property IR, retained diff/apply path, layout, draw command recording, D3D12 backend smoke coverage, display scale handling, input hit testing, and diagnostics guards are in place.

This tag freezes a private candidate snapshot for continued renderer work. It does not freeze the public authoring API, backend API, renderer internals, or future package shape.

---

## Validation

Local CI parity passed on 2026-05-17:

```powershell
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance" --verbosity normal
dotnet test --no-build -c Release --filter "Category=D3D12" --verbosity normal
dotnet test --no-build -c Release --filter "Category=Performance" --verbosity normal
dotnet publish src/Irix.Poc/Irix.Poc.csproj -c Release -r win-x64 --self-contained
```

Results:

| Lane | Result |
|------|--------|
| Release build | Passed, 0 warnings, 0 errors |
| Normal tests | 590 passed |
| Headless D3D12 tests | 6 passed |
| Performance tests | 6 passed |
| AOT publish | Passed |

Manual smoke passed:

| Smoke | Result |
|-------|--------|
| Default run | Passed |
| 100% scale | Passed |
| 150% scale | Passed |
| 200% scale | Passed |
| Runtime scale switch | Passed |

Refresh evidence for this milestone is 60Hz / 120Hz / 240Hz. 144Hz is removed from the current evidence matrix because no 144Hz hardware is available.

---

## Accepted Constraints

| Item | Private GA decision |
|------|---------------------|
| Sync wait | Accepted temporarily for the tag. `D3D12FenceAfterOverlay` was the private-GA default to preserve text/rect synchronization. |
| Text renderer | The D3D11On12/D2D/DirectWrite overlay remained in place for this tag only; post-GA active source has removed it. |
| Glyph atlas | Post-GA renderer foundation work, not part of this tag. |
| Public API | Not frozen. Repository is private and incompatible cleanup remains allowed. |
| Performance micro-optimization | Stop before tag. Do not chase the remaining ~2 KB render-request reuse allocation in this milestone. |

---

## Next Branch

Open `post-ga-renderer-foundation` from this tag. The first line of work is renderer foundation for the future D3D12-only glyph atlas text path, following `Glyph-Atlas-Post-GA-Design.md`.
