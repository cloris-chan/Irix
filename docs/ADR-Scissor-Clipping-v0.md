# ADR: Scissor Clipping v0

Status: Accepted

Date: 2026-05-10

## Context

The layout pipeline already carries clip information from `ScrollContainer` into `LayoutElement.ClipBounds`, `DrawCommand.ClipBounds`, and `HitTestTarget.ClipBounds`. Hit testing rejects clipped-out targets, and the D3D12 PoC backend counts clipped commands for diagnostics, but GPU scissor is not applied to individual draw commands yet.

The current interaction and style preset phase is frozen. Scissor work must not change scroll pumping, input ownership, button visual state, style preset behavior, theme behavior, or retained partial redraw behavior.

## Decision

Introduce backend clip capability as a diagnostic boundary before implementing GPU clipping:

| Mode | Meaning |
|------|---------|
| `None` | Backend ignores `DrawCommand.ClipBounds` entirely. |
| `Diagnostic` | Backend observes/counts clipped commands but renders exactly as before. |
| `Scissor` | Backend applies backend-native scissor/clipping for supported commands. |

D3D12 is currently `Diagnostic` only: it receives `ClipBounds`, counts clipped commands, and continues rendering existing FillRect/Text paths unchanged. The diagnostic path computes and reports the effective scissor separately, proving that clip metadata reaches the backend before rasterizer scissor is enabled.

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

Current `Diagnostic` mode validates that the backend receives and counts the clipped FillRect without changing rendering behavior. It also reports `effectiveClip=(32,32,80,40)`, `emptyIntersectionSkipped=0`, `scissorStateChanges=0`, and `gpuScissor=False`; these fields are format-stable placeholders until `Scissor` mode is implemented. Future `Scissor` mode should use the same smoke to verify that enabling D3D12 scissor does not break the existing FillRect path.

## Non-goals

- GPU partial redraw.
- Nested clip stack redesign.
- Text clipping.
- Theme system.
- Generic control abstraction.
- Reworking scroll/input/button/style preset foundations.

## Follow-up

When implementing `Scissor` mode, keep the first code change limited to FillRect scissor for D3D12Renderer2D and extend diagnostics before expanding to text or nested clipping.