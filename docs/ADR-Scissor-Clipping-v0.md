# ADR: Scissor Clipping v0

Status: Accepted

Date: 2026-05-10

## Context

The layout pipeline already carries clip information from `ScrollContainer` into `LayoutElement.ClipBounds`, `DrawCommand.ClipBounds`, and `HitTestTarget.ClipBounds`. Hit testing rejects clipped-out targets, and the D3D12 PoC backend counts clipped commands for diagnostics. FillRect GPU scissor is available behind the explicit `--enable-scissor` opt-in mode; text clipping remains out of scope for this phase.

Current manual-test behavior under `--enable-scissor`: clipped FillRect content is clipped by the D3D12 rasterizer scissor, while DrawTextRun content is still drawn by the Direct2D/DirectWrite overlay and is not clipped. For controls such as buttons, this means the button FillRect can be clipped while its text can still render outside the same `ClipBounds`. That is expected v0 behavior, not a claim that full control clipping is complete.

The current interaction and style preset phase is frozen. Scissor work must not change scroll pumping, input ownership, button visual state, style preset behavior, theme behavior, or retained partial redraw behavior.

## Decision

Introduce backend clip capability as a diagnostic boundary before implementing GPU clipping:

| Mode | Meaning |
|------|---------|
| `None` | Backend ignores `DrawCommand.ClipBounds` entirely. |
| `Diagnostic` | Backend observes/counts clipped commands but renders exactly as before. |
| `Scissor` | Backend applies backend-native scissor/clipping for supported commands. |

D3D12 defaults to `Diagnostic`: it receives `ClipBounds`, counts clipped commands, and continues rendering existing FillRect/Text paths unchanged. `Scissor` mode is available behind an explicit backend flag and through the PoC `--enable-scissor` switch; it currently applies only to FillRect commands. The normal PoC path still constructs the backend in `Diagnostic` mode, and `--enable-scissor` must not become the default until the text clip path has a minimum closed loop.

## Per-command Scissor Shape

Future `Scissor` mode should apply clip as an axis-aligned per-command rasterizer scissor for FillRect commands only.

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

## Text Clip Boundary

Text rendering is out of scope for scissor v0.

Reason: current text is drawn through the Direct2D/DirectWrite overlay path after D3D12 rectangles. D3D12 rasterizer scissor does not automatically clip the D2D overlay draw calls. Text clipping should be designed separately, likely with D2D axis-aligned clip push/pop around text runs or a text-specific backend batching step.

Until that design exists:

- Text commands may keep carrying `ClipBounds` for layout and hit-test consistency.
- D3D12 `Scissor` mode must not claim text clipping support.
- The first smoke must avoid text commands.

## D2D Text Clip v0 Design

The next minimal text clip step should clip each `DrawTextRun` in the Direct2D overlay path with the same effective rectangle model used by FillRect scissor:

1. Preserve `DrawCommand.ClipBounds` through the text translation path. `D3D12DrawingBackend` should pass the DrawTextRun clip into `D3D12TextRenderer.TextData`, either as the original `DrawRect` or as an already resolved effective clip.
2. Resolve `viewport + ClipBounds/default` with `DrawingScissor.ResolveEffectiveScissor` so FillRect and text agree on default clip, viewport intersection, and empty-intersection semantics.
3. If the effective text clip is empty, skip that DrawTextRun.
4. If the effective text clip is the full viewport/default target, draw the text as today.
5. Otherwise, before `ID2D1DeviceContext2.DrawTextLayout`, push one axis-aligned D2D clip matching the effective clip bounds; immediately pop it after that DrawTextRun. The scope must be per text run so later runs cannot inherit stale clip state.
6. Keep `D2D1_DRAW_TEXT_OPTIONS_CLIP` for layout-box clipping. The new D2D axis-aligned clip is for scroll/container `ClipBounds`; the existing layout clip is only the text layout rectangle.
7. Add diagnostics and tests before considering default enablement: text smoke should report `textClip=True`, empty text clip skip count, and FillRect/Text agreement for the same `ClipBounds`.

This design intentionally avoids nested clip stacks, global text batching by clip, or changing DirectWrite layout cache keys. It is a per-run Direct2D state scope around existing DrawTextRun rendering.

## Minimal Smoke

The first scissor smoke uses exactly one clipped FillRect command:

- Rect: `(16,16,160,80)`
- Clip: `(32,32,80,40)`
- No nested clip stack.
- No text command.
- No retained partial redraw.

Current `--diagnose` smokes switch the backend to `Scissor` for FillRect-only checks. Expected stable fields:

- Direct: `effectiveClip=(32,32,80,40)`, `gpuScissor=True`, `clippedCommands=1`, `emptyIntersectionSkipped=0`, `scissorStateChanges=1`, `deviceRemoved=False`.
- Pipeline: `source=ScrollContainerRectangle`, `textClip=False`, `clippedCommands=1`, `emptyIntersectionSkipped=0`, `scissorStateChanges=1`, `deviceRemoved=False`, `passed=True`.
- Empty: `kind=FillRect`, `clippedCommands=1`, `emptyIntersectionSkipped=1`, `scissorStateChanges=0`, `deviceRemoved=False`.

`textClip=False` is part of the expected diagnostic baseline. `passed=True` means the FillRect-only scissor smoke passed; it must not be read as full button/control clipping support. These smokes prove D3D12 scissor does not break the existing FillRect path while keeping text clipping out of scope.

## Non-goals

- GPU partial redraw.
- Nested clip stack redesign.
- Implementing text clipping in this phase.
- Theme system.
- Generic control abstraction.
- Reworking scroll/input/button/style preset foundations.

## Follow-up

Before making `--enable-scissor` the default, implement the D2D text clip v0 closed loop and update diagnostics from `textClip=False` to an explicit text-clip smoke that proves DrawTextRun clipping independently of FillRect clipping.