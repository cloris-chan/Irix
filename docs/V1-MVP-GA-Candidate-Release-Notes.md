# V1 MVP/GA Candidate Release Notes

Candidate: `v1-mvp-ga-candidate.1`
Date: 2026-05-13

## Scope

This candidate freezes V1 core. It does not add new public APIs, rewrite the renderer, introduce a new default sync strategy, add new CI matrices, or implement a glyph atlas.

## Highlights

- Partial apply is default-on; rollback remains available via `--no-partial-apply`.
- D3D12 rendering path includes rectangle rendering plus DirectWrite / Direct2D text overlay.
- Text overlay synchronization remains default-on with `SyncTextOverlay=true`.
- Device-lost recovery is implemented through `IDeviceRecovery` and renderer resource reinitialization.
- Runtime resize/resource recreation failures now report explicit device error reasons instead of continuing with undefined pointers.
- Command allocator/list reset failures retry once after `WaitForGpu`, then escalate to device-lost/recovery.
- Sync diagnostics can compare internal strategies with `--text-overlay-sync-strategy`, but `D3D12FenceAfterOverlay` remains default.
- `scripts/ga-baseline.ps1` supports sync/text-cache/smoke baselines and strategy-labeled sync outputs.

## Validation

- Release build passed.
- Ordinary tests passed: 496/496.
- D3D12 smoke tests passed: 6/6.
- Performance lane passed: 2/2.
- AOT publish passed for `Irix.Poc` win-x64 self-contained.
- Platform integration smokes passed on the current machine:
  - minimize/restore
  - full occlusion/restore
  - repeated resize
  - scroll/click
  - default partial apply
  - `--no-partial-apply` rollback
  - 100% / 150% / 200% startup scale
  - runtime scale switch 200% -> 100% -> 150%

## Known Limitations / Accepted Risks

- Text overlay sync wait is accepted for this candidate even though it can exceed the old provisional 2ms target. Correctness takes priority; default synchronization stays enabled and manual smokes show no text lag.
- `--no-sync-text-overlay` is only for A/B diagnostics and must not be used as the default path.
- `D3D11Query` is diagnostic-only. It improves 60Hz / 240Hz in local measurements but regresses 120Hz, so it is not adopted as default.
- 144Hz is not part of the current GA matrix because the local display does not expose a 144Hz mode.
- Glyph atlas/cache is not implemented in this candidate. The Windows text path remains DirectWrite / Direct2D; post-GA design is tracked in `Glyph-Atlas-Post-GA-Design.md`.
- Broader hardware, multi-monitor, and high-contrast/accessibility validation remain post-GA work.

## Suggested Tag

`v1-mvp-ga-candidate.1`

Create the tag only after reviewing and committing this candidate snapshot.
