# ADR: Scissor Clipping v0

Status: Accepted

Date: 2026-05-10

## Context

The layout pipeline already carries clip information from `ScrollContainer` into `LayoutElement.ClipBounds`, `DrawCommand.ClipBounds`, and `HitTestTarget.ClipBounds`. Hit testing rejects clipped-out targets, and the D3D12 PoC backend counts clipped commands for diagnostics. FillRect GPU scissor is available behind an explicit opt-in mode; text clipping remains out of scope.

The current interaction and style preset phase is frozen. Scissor work must not change scroll pumping, input ownership, button visual state, style preset behavior, theme behavior, or retained partial redraw behavior.

## Decision

Introduce backend clip capability as a diagnostic boundary before implementing GPU clipping:

| Mode | Meaning |
|------|---------|
| `None` | Backend ignores `DrawCommand.ClipBounds` entirely. |
| `Diagnostic` | Backend observes/counts clipped commands but renders exactly as before. |
| `Scissor` | Backend applies backend-native scissor/clipping for supported commands. |

D3D12 defaults to `Diagnostic`: it receives `ClipBounds`, counts clipped commands, and continues rendering existing FillRect/Text paths unchanged. `Scissor` mode is available behind an explicit backend flag and through the PoC `--enable-scissor` switch; it currently applies only to FillRect commands. The normal PoC path still constructs the backend in `Diagnostic` mode.

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

These smokes prove D3D12 scissor does not break the existing FillRect path while keeping text clipping out of scope.

## Non-goals

- GPU partial redraw.
- Nested clip stack redesign.
- Text clipping.
- Theme system.
- Generic control abstraction.
- Reworking scroll/input/button/style preset foundations.

## Follow-up

When implementing `Scissor` mode, keep the first code change limited to FillRect scissor for D3D12Renderer2D and extend diagnostics before expanding to text or nested clipping.