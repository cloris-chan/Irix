# V1 MVP/GA Candidate Summary

Date: 2026-05-13

## Decision

V1 core is complete, default-on, and frozen for the MVP/GA candidate. No new V1 core features are being opened for this candidate. The current scope is validation, accepted-risk documentation, and post-GA follow-up tracking.

## Done

| Area | Status |
|------|--------|
| Default-on partial apply | Done; `--no-partial-apply` remains rollback |
| D3D12 segmented ownership | Done for the V1 default path |
| D2D text overlay sync | Done; default `SyncTextOverlay=true` remains enabled |
| Device-lost recovery | Done; compositor uses `IDeviceRecovery` and renderer rebuilds device resources |
| GPU resource failure handling | Done for V1 scope; runtime resize/recovery failures produce explicit device error reasons, including `E_OUTOFMEMORY` |
| Command allocator reset guard | Done; reset retries once after `WaitForGpu`, then escalates to device-lost/recovery |
| Windows version boundary | Done; target SDK 10.0.26100.0, runtime minimum 10.0.15063.0 |
| CI / Release baseline | Done; Release build, test lanes, D3D12 smoke, performance lane, and AOT publish pass |
| Platform integration smoke | Done for current machine: minimize/restore, occlusion/restore, resize, scroll, click, 100% / 150% / 200%, runtime scale switch |
| Glyph atlas | Post-GA design only; no current-GA implementation |

## Accepted Risks

| Risk | Candidate decision |
|------|--------------------|
| Text overlay sync wait exceeds the old provisional 2ms target on some refresh/runtime combinations | Accepted. Correctness wins: default sync remains enabled, manual smokes show no text lag, and `--no-sync-text-overlay` remains A/B only. |
| `D3D11Query` sync strategy improves some modes but regresses 120Hz | Not adopted as default. Kept as internal diagnostic/spike strategy. |
| 144Hz not tested | Removed from current GA scope because local hardware does not expose 144Hz. |
| Single-display hardware coverage | Accepted for this candidate; broader hardware remains post-GA validation. |
| Explicit glyph atlas/cache absent | Accepted. Windows V1 text path remains DirectWrite / Direct2D. |

## Release Baseline

| Command | Result |
|---------|--------|
| `dotnet build -c Release` | Passed; only two pre-existing xUnit analyzer warnings |
| `dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance"` | Passed 496/496 |
| `dotnet test --no-build -c Release --filter "Category=D3D12"` | Passed 6/6 |
| `dotnet test --no-build -c Release --filter "Category=Performance"` | Passed 2/2 |
| `dotnet publish src/Irix.Poc/Irix.Poc.csproj -c Release -r win-x64 --self-contained` | Passed |

Pre-existing warnings:

- `ConcurrentInputRenderTests.cs`: xUnit1031 blocking task operation warning.
- `ConcurrentInputRenderTests.cs`: xUnit1051 missing `TestContext.Current.CancellationToken` warning.

## Platform Smoke Evidence

| Scenario | Result |
|----------|--------|
| Default partial apply: minimize/restore, occlusion/restore, repeated resize, scroll, click | Passed; no crash, no device-lost, no black screen, no stale frame, no text lag |
| `--no-partial-apply` rollback: resize + scroll + click + text sync | Passed |
| 100% startup scale | Passed; text/rect/hit-test/clip/scroll correct |
| 150% startup scale | Passed; text/rect/hit-test/clip/scroll correct |
| 200% startup scale | Passed; text/rect/hit-test/clip/scroll correct |
| Runtime scale switch 200% -> 100% -> 150% with resize/scroll/click | Passed; `DPI changed` events observed, no visual mismatch |

## Post-GA Follow-Up

| Item | Notes |
|------|-------|
| Renderer-level sync optimization | Preserve 60Hz text correctness; do not disable default sync for performance. |
| 144Hz hardware validation | Run only when hardware exposes 144Hz. |
| Glyph atlas/cache issue #2 | Design exists in `Glyph-Atlas-Post-GA-Design.md`; implementation remains post-GA. |
| Broader hardware and multi-monitor validation | Outside this candidate. |
| Translator promotion / typed ids / scroll extraction | Framework promotion work, not candidate scope. |

## Candidate Tag Readiness

The workspace is ready for a V1 MVP/GA candidate tag after the current changes are reviewed and committed. Suggested tag name: `v1-mvp-ga-candidate.1`.
