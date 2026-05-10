# ADR: Scissor Clipping v0

Status: Accepted

Date: 2026-05-10

## Context

The layout pipeline already carries clip information from `ScrollContainer` into `LayoutElement.ClipBounds`, `DrawCommand.ClipBounds`, and `HitTestTarget.ClipBounds`. Hit testing rejects clipped-out targets, and the D3D12 PoC backend counts clipped commands for diagnostics. FillRect GPU scissor and Direct2D text clipping are available behind the explicit `--enable-scissor` opt-in mode.

Current behavior under `--enable-scissor`: clipped FillRect content is clipped by the D3D12 rasterizer scissor; DrawTextRun content is clipped by a per-run Direct2D axis-aligned clip around `DrawTextLayout`. The default PoC path remains `Diagnostic`, so this behavior is still explicit opt-in while it gets one hand-test pass.

The current interaction and style preset phase is frozen. Scissor work must not change scroll pumping, input ownership, button visual state, style preset behavior, theme behavior, or retained partial redraw behavior.

## Decision

Introduce backend clip capability as a diagnostic boundary before implementing GPU clipping:

| Mode | Meaning |
|------|---------|
| `None` | Backend ignores `DrawCommand.ClipBounds` entirely. |
| `Diagnostic` | Backend observes/counts clipped commands but renders exactly as before. |
| `Scissor` | Backend applies backend-native scissor/clipping for supported commands. |

D3D12 defaults to `Diagnostic`: it receives `ClipBounds`, counts clipped commands, and continues rendering existing FillRect/Text paths unchanged. `Scissor` mode is available behind an explicit backend flag and through the PoC `--enable-scissor` switch; it currently applies FillRect scissor and DrawTextRun Direct2D clipping. The normal PoC path still constructs the backend in `Diagnostic` mode, and `--enable-scissor` must not become the default until this text clip v0 path has been hand-tested.

## Per-command Scissor Shape

`Scissor` mode applies clip as an axis-aligned per-command rasterizer scissor for FillRect commands.

Rules:

- `DrawCommand.ClipBounds == default` means use the full viewport scissor.
- Non-default `ClipBounds` is intersected with the current viewport before issuing D3D12 scissor state.
- Empty intersections skip that FillRect command.
- Effective scissor calculation is frozen as a pure Drawing-layer function before any D3D12 state changes.
- Scissor state changes must preserve draw order. The first implementation should batch only consecutive FillRect commands with the same effective clip, not globally reorder by clip.
- Scissor diagnostics should report clip mode, clipped command count, skipped empty-intersection count, and scissor state change count once implemented.

## Batch-by-clip Boundary

Batching by clip is allowed only as a run-length optimization over already ordered commands:

1. Walk commands in order.
2. Compute effective clip for each FillRect.
3. Flush the current rectangle batch whenever command kind or effective clip changes.
4. Set `RSSetScissorRects` once per flushed FillRect run.

Global grouping by clip is not allowed because it can change visual stacking once overlapping commands exist.

## D2D Text Clip v0

Text is drawn through the Direct2D/DirectWrite overlay path after D3D12 rectangles. D3D12 rasterizer scissor does not automatically clip these D2D overlay draw calls, so text clipping uses a separate Direct2D clip scope.

The minimal text clip step clips each `DrawTextRun` in the Direct2D overlay path with the same effective rectangle model used by FillRect scissor:

1. Preserve `DrawCommand.ClipBounds` through the text translation path. `D3D12DrawingBackend` resolves the DrawTextRun clip and passes `EffectiveScissor` into `D3D12TextRenderer.TextData`.
2. Resolve `viewport + ClipBounds/default` with `DrawingScissor.ResolveEffectiveScissor` so FillRect and text agree on default clip, viewport intersection, and empty-intersection semantics.
3. If the effective text clip is empty, skip that DrawTextRun.
4. If the effective text clip is the full viewport/default target, draw through the original text path without pushing an extra D2D clip.
5. Otherwise, before `ID2D1DeviceContext2.DrawTextLayout`, push one axis-aligned D2D clip matching the effective clip bounds; immediately pop it after that DrawTextRun. The scope must be per text run so later runs cannot inherit stale clip state.
6. Keep `D2D1_DRAW_TEXT_OPTIONS_CLIP` for layout-box clipping. The new D2D axis-aligned clip is for scroll/container `ClipBounds`; the existing layout clip is only the text layout rectangle.
7. Diagnostics report `textClip=True`, the effective text clip, and empty text clip skip count.

This intentionally avoids nested clip stacks, global text batching by clip, or changing DirectWrite layout cache keys. It is a per-run Direct2D state scope around existing DrawTextRun rendering.

## Minimal Smoke

The first scissor smoke uses exactly one clipped FillRect command:

- Rect: `(16,16,160,80)`
- Clip: `(32,32,80,40)`
- No nested clip stack.
- No text command.
- No retained partial redraw.

Current `--diagnose` smokes switch the backend to `Scissor` for FillRect and text clip checks. Expected stable fields:

- Direct: `effectiveClip=(32,32,80,40)`, `textClip=False`, `gpuScissor=True`, `clippedCommands=1`, `emptyIntersectionSkipped=0`, `scissorStateChanges=1`, `deviceRemoved=False`.
- Pipeline: `source=ScrollContainerRectangle`, `textClip=False`, `clippedCommands=1`, `emptyIntersectionSkipped=0`, `scissorStateChanges=1`, `deviceRemoved=False`, `passed=True`.
- Empty: `kind=FillRect`, `clippedCommands=1`, `emptyIntersectionSkipped=1`, `scissorStateChanges=0`, `deviceRemoved=False`.
- Text: `kind=DrawTextRun`, `textClip=True`, `layoutClip=True`, `effectiveClip=(32,32,80,40)`, `textClipSkipped=1`, `deviceRemoved=False`.

`textClip=False` remains part of the FillRect-only smoke baseline. `textClip=True` in the text smoke proves DrawTextRun clipping independently of FillRect scissor. `passed=True` in the pipeline smoke means the FillRect-only pipeline scissor smoke passed; it must not be read as complete control-system validation.

## Non-goals

- GPU partial redraw.
- Nested clip stack redesign.
- Default-enabling `--enable-scissor` before a hand-test pass with text clip v0.
- Theme system.
- Generic control abstraction.
- Reworking scroll/input/button/style preset foundations.

## Follow-up

Before making `--enable-scissor` the default, hand-test the normal UI, `--debug-ui`, scrolling, and button hover/press with text clip v0 enabled.