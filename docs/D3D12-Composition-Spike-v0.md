# D3D12 Composition Spike v0

> Narrow implementation contract for the first composition/GPU-offload code path. This is not a generic compositor design; it validates a D3D12-backed layer update path for translation and opacity.

## Goal

Prove the first composition IR can be handed to the active D3D12 backend without changing the normal draw-command renderer path.

The first spike owns only:

- One composition layer referencing a contiguous draw-command range.
- Layer translation in logical pixels.
- Layer opacity in normalized `[0, 1]`.
- D3D12 backend consumption through an explicit diagnostic execution path.
- Stable machine-readable diagnostics.

## Non-Goals

- No public composition API.
- No scheduler or animation clock.
- No scroll presentation model.
- No hit-test coordinate remapping.
- No retained layer cache or intermediate render target.
- No Vulkan/Metal work.
- No replacement of `ICompositor.RenderAsync`.

## IR Contract

`CompositionFrame` is platform-neutral and lives above D3D12:

| Type | Role |
|------|------|
| `CompositionLayerId` | Stable layer identity for diagnostics and future retained mapping. |
| `CompositionTransform` | Translation-only v0 transform. |
| `CompositionOpacity` | Strong normalized opacity value. |
| `CompositionLayer` | Layer id, command range, transform, and opacity. |
| `CompositionFrame` | Single-layer v0 frame wrapper. |

The layer references existing `RenderFrameBatch` command ranges; it does not copy commands or own frame resources.

## D3D12 Execution

The D3D12 spike materializes translation and opacity at backend execution time:

```text
RenderFrameBatch commands/resources
  + CompositionFrame single layer
  -> D3D12DrawingBackend.ExecuteComposition
  -> transformed rect/text payloads
  -> existing D3D12 rectangle/GlyphAtlas passes
  -> Present
```

This is intentionally a D3D12-backed diagnostic path. The normal compositor path remains unchanged until layer identity, animation ticking, and hit-test mapping contracts are ready.

## Diagnostics

`--diagnose-composition-transform` must prove:

- `finalComposition=D3D12`
- `d3d12Backed=True`
- one layer was consumed
- command range accounting is stable
- translated command count is nonzero
- opacity-applied command count is nonzero
- no layout rebuild or draw-command regeneration is required inside the backend path

## Next Gate

After this spike is stable, the next implementation step is compositor-owned ticking for transform/opacity. That should update `CompositionFrame` properties between frames without rebuilding `VirtualNode`, layout, or draw commands.
