# ADR: Scissor Clipping v0

Status: Accepted; v0 complete and default-on

Date: 2026-05-10

## Context

The layout pipeline already carries clip information from `ScrollContainer` into `LayoutElement.ClipBounds`, `DrawCommand.ClipBounds`, and `HitTestTarget.ClipBounds`. Hit testing rejects clipped-out targets, and the D3D12 PoC backend counts clipped commands for diagnostics. FillRect GPU scissor and D3D12 GlyphAtlas text clipping are default-on in the renderer-foundation branch.

Current default behavior: clipped FillRect content is clipped by the D3D12 rasterizer scissor; accepted DrawTextRun content is clipped in the D3D12 GlyphAtlas text pass, and unsupported text degrades explicitly. `--disable-scissor` and `--clip-mode diagnostic` remain rollback/diagnostic paths. The old `--enable-scissor` switch is a temporary no-op alias because scissor is already the default; it is not a compatibility promise.

The current interaction and style preset behavior is guarded while scissor remains the selected target. Scissor work must not accidentally change scroll pumping, input ownership, button visual state, style preset behavior, theme behavior, or retained partial redraw behavior.

## Decision

Introduce backend clip capability as a diagnostic boundary before implementing GPU clipping:

| Mode | Meaning |
|------|---------|
| `None` | Backend ignores `DrawCommand.ClipBounds` entirely. |
| `Diagnostic` | Backend observes/counts clipped commands but renders exactly as before. |
| `Scissor` | Backend applies backend-native scissor/clipping for supported commands. |

D3D12 defaults to `Scissor`: it applies FillRect scissor and DrawTextRun GlyphAtlas clipping for accepted text runs. `Diagnostic` mode remains available through `--disable-scissor` or `--clip-mode diagnostic`; it receives `ClipBounds`, counts clipped commands, and renders without applying backend clipping.

## Per-command Scissor Shape

`Scissor` mode applies clip as an axis-aligned per-command rasterizer scissor for FillRect commands.

Rules:

- `DrawCommand.ClipBounds == default` means use the full viewport scissor.
- Non-default `ClipBounds` is intersected with the current viewport before issuing D3D12 scissor state.
- Empty intersections skip that FillRect command.
- Effective scissor calculation remains a pure Drawing-layer function before any D3D12 state changes.
- Scissor state changes must preserve draw order. The first implementation should batch only consecutive FillRect commands with the same effective clip, not globally reorder by clip.
- Scissor diagnostics should report clip mode, clipped command count, skipped empty-intersection count, and scissor state change count once implemented.

## Batch-by-clip Boundary

Batching by clip is allowed only as a run-length optimization over already ordered commands:

1. Walk commands in order.
2. Compute effective clip for each FillRect.
3. Flush the current rectangle batch whenever command kind or effective clip changes.
4. Set `RSSetScissorRects` once per flushed FillRect run.

Global grouping by clip is not allowed because it can change visual stacking once overlapping commands exist.

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
- Pipeline text: `source=ScrollContainerButton`, `textClip=True`, `layoutClip=True`, `effectiveClip=(0,0,960,20)`, `clippedCommands=2`, `textClipSkipped=0`, `deviceRemoved=False`, `passed=True`.
- Empty: `kind=FillRect`, `clippedCommands=1`, `emptyIntersectionSkipped=1`, `scissorStateChanges=0`, `deviceRemoved=False`.
- Text: `kind=DrawTextRun`, `textClip=True`, `layoutClip=True`, `effectiveClip=(32,32,80,40)`, `textClipSkipped=1`, `deviceRemoved=False`.

`textClip=False` remains part of the FillRect-only smoke baseline. `textClip=True` in the text smoke proves DrawTextRun clipping independently of FillRect scissor. `passed=True` in the pipeline smoke means the FillRect-only pipeline scissor smoke passed; it must not be read as complete control-system validation.

## Non-goals

- GPU partial redraw.
- Nested clip stack redesign.
- Removing the diagnostic rollback path.
- Theme system.
- Generic control abstraction.
- Reworking scroll/input/button/style preset foundations.

## Follow-up

Scissor is the default. Keep `Diagnostic` rollback available while future work avoids expanding the clip scope into nested clip stacks, retained partial redraw, text batching, theme work, or generic control abstraction.
