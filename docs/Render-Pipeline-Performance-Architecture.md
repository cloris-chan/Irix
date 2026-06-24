# Render Pipeline Performance Architecture

> Design note for the private Irix render pipeline. This is not a public API
> contract. It records the current diagnostic baseline, the ownership model, and
> the staged optimization plan for the core render path.

## Purpose

Irix already has the most important performance boundary in place: rendering is
driven by an internal `VirtualNode` IR, retained layout/render publications, and a
D3D12-first compositor path. The next optimization work should not be random
pooling or per-call micro-tuning. It should make the hot path explicit about
ownership:

- Build-time scratch is temporary and can use stack storage, pooled storage, and
  borrowed spans.
- Published render state is immutable, owned, and safe to retain across frames.
- Platform resources are owned by the backend and referenced through stable
  handles or frame resources.

This is close to Rust's useful design lesson, but expressed in C#: keep mutation
single-owner, keep borrowed data inside the frame/build lifetime, and freeze or
copy before anything becomes retained state. The goal is not to make the code
look like Rust; the goal is to make invalid lifetimes hard to represent.

## Evidence Snapshot

Baseline collected on 2026-06-25 after the `VirtualNode` IR was normalized to
`Container` and `Content` nodes.

Primary command:

```powershell
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-text-cache 180
```

Allocation summary:

| Scenario | Total | Bytes/frame | Main buckets |
|----------|-------|-------------|--------------|
| Static | 2272008 bytes | 12622 B/frame | render 11060 B/frame, tree 728 B/frame, translate 745 B/frame |
| Warm scroll | 551688 bytes | 3064 B/frame | translate 1742 B/frame, tree 728 B/frame, render 364 B/frame |
| Scale change | 346048 bytes | 1922 B/frame | tree 911 B/frame, translate 637 B/frame, render 364 B/frame |

Warm-scroll details are the key CPU render-pipeline comparison point:

| Bucket | Bytes/frame | Notes |
|--------|-------------|-------|
| `tree.buildRoot.button.childrenArray` | 364 | Cost of publishing per-button child arrays after the button becomes a container with rectangle/text content children. |
| `layout.elementsArray` | 364 | Retained `LayoutTreeResult` publication array. |
| `layout.treeNodesArray` | 182 | Retained `LayoutTreeResult` publication array. |
| `layout.result` | 182 | Retained result object. |
| `drawRecord` | 147 | Command recording is visible but not the largest bucket. |
| `tree.buildRoot.button.propertyArray` | 136 | Control/state/style property publication. |
| `layout.nodeWalk` | 0 | The layout bucket is publication cost, not node-walk allocation. |

The older warm-scroll comparison point was about 2204 B/frame before the
Container/Content split. The new shape is expected: button-like controls now
lower into a container plus content nodes, which makes ownership clearer but
exposes child-array publication as a measured hotspot.

Composition and scroll diagnostics were also checked:

- `--diagnose-composition-transform`
- `--diagnose-composition-scroll`
- `--diagnose-composition-multilayer`
- `--diagnose-composition-layer-cache`
- `--diagnose-composition-skip`
- `--diagnose-composition-marker-runtime`
- `--diagnose-scroll-presentation-runtime`
- `--diagnose-scroll-presentation-hittest`
- `--diagnose-scroll-presentation-interaction`

Those diagnostics stayed on the intended path: D3D12-backed transform/opacity,
fixed-clip scroll presentation, multi-layer execution, layer-cache hits and
invalidations, explicit skip reasons, marker dispatch, active hit-test remapping,
and scroll presentation lifecycle behavior are healthy. The next performance
work should therefore reduce UI/control lowering and render publication cost
before inventing a new compositor fallback.

## Architecture Rules

### Render IR Boundary

`VirtualNode` is an internal render IR, not the final user-facing authoring API.
The current semantic shape remains:

- `Container` nodes own ordered child nodes and no content.
- `Content` nodes own exactly one content resource and no children.
- Content resources represent concrete draw/layout payloads such as text,
  rectangle shape, future image resources, or other content records.
- Button-like controls are resolved before this layer into a container with
  rectangle/text content children and interaction metadata on the control owner.
- Z-order is represented by emitted child order. Any z-index, cascade, or
  CSS-like sorting belongs to control/composition builders before the final node
  sequence is published.

### Three Lifetime Zones

| Zone | Owner | Allowed storage | Forbidden storage |
|------|-------|-----------------|-------------------|
| Build scratch | The current frame/build call | `stackalloc`, `ref struct` builders, pooled buffers, mutable lists, borrowed spans | Retained references, async storage, backend ownership |
| Publication | Render/core retained state | Owned immutable arrays, value records, stable handles, frozen slabs | Stack memory, rented mutable arrays, borrowed spans |
| Backend resources | Platform backend | Device resources, descriptor/upload rings, glyph/image/material caches with handles | App/runtime state ownership, public renderer API leakage |

Copying is expected at zone boundaries. The optimization target is to stop
copying several times inside one zone.

### Retained Publication Safety

`LayoutTreeResult`, retained snapshots, draw resources, hit targets, and any
future published virtual-node slab must be safe to retain after the build call
returns. They must not expose mutable pooled arrays. Empty/static arrays are fine;
non-empty published buffers must have one clear owner.

### D3D12-First Constraint

The active renderer remains D3D12-first. Normal retained-frame rendering is the
explicit secondary path when a written blocker appears. Performance work must not
create a generic CPU compositor, public scheduler, or public `VirtualNode` API.

## Target Shape

The long-term target is a pipeline with explicit build and publish phases:

```text
App/control runtime
  -> Control composition builder
  -> VirtualNode build scratch
  -> immutable VirtualNode publication
  -> diff / retained tree patch
  -> layout build scratch
  -> immutable LayoutTreeResult publication
  -> draw/hit/resource publication
  -> D3D12 compositor/backend resources
```

Each arrow either borrows from the current owner for the duration of one build
phase or freezes into the next retained publication. That is the C# version of
the useful Rust-style idea: ownership is explicit, borrowing is scoped, and
retained values cannot point back into scratch memory.

## Staged Plan

### P0 - Keep The Baseline Written

This document is the baseline. Future implementation commits should update the
numbers only when a measured change lands.

Acceptance:

- `--diagnose-text-cache 180` remains the main allocation comparison.
- Composition and scroll presentation diagnostics above stay green for broad
  render-pipeline changes.
- `LayoutTreeResult` and retained snapshots remain immutable publications.

### P1 - Control Composition Scratch Lowering

Current hot signal: button lowering allocates child/property publication arrays
after the control is split into a container plus rectangle/text content nodes.
The first implementation slice should reduce temporary arrays and repeated
partitioning without changing the IR semantics.

Direction:

- Introduce a frame-owned control/virtual-node build context for small child and
  property staging.
- Partition control properties through stack or scratch spans, then publish only
  the arrays that actually become retained node data.
- Keep button semantics outside `VirtualNode`. A button remains a control-level
  composition that emits a container plus content nodes.
- Avoid adding button nodes, z-index properties, or CSS processing names to the
  render IR.

Acceptance:

- Warm-scroll diagnostics show a reduction in `tree.buildRoot.button.*` buckets.
- Any retained `VirtualNode` data is owned after publication.
- Source guards still block public `VirtualNode` authoring leakage.

### P2 - VirtualNode Publication Slab

The measured `childrenArray` bucket is a sign that per-node owned arrays are the
next structural limit. Since `VirtualNode` is internal, the next target design can
move from "each node owns child/property arrays" to "one published tree owns
contiguous node, child-range, property, and content-resource slabs".

Possible shape:

- `VirtualNodeTree` owns published arrays/slabs.
- `VirtualNodeRef` or a reader struct identifies nodes by index plus generation
  or tree owner.
- Node records store kind, key, content range/index, property range, and child
  range.
- Builders can use scratch vectors and freeze once into contiguous publication
  arrays.
- Diff/layout APIs consume readers/spans instead of per-node arrays.

This keeps ADR-010's important part: no retained all-`ref struct` tree and no
borrowed scratch in retained state. It does allow replacing the current
per-node-array value model with an owned immutable document/slab if diagnostics
justify the migration.

Acceptance:

- Warm-scroll `button.childrenArray` and broad tree-build publication allocation
  drop materially.
- Diff, layout, and retained tree tests prove stable DFS/index/key behavior.
- No published reader can outlive its owning tree publication.

### P3 - Layout Publication Builder

`LayoutTreeBuilder` already uses stack-backed scratch lists before publishing
owned arrays. The next slice is not "pool the arrays"; it is to make the
publication freeze boundary first-class.

Direction:

- Introduce an explicit `LayoutBuildScratch` or `LayoutPublicationBuilder`
  boundary around `elements`, `treeNodes`, `elementRanges`, dirty ranges, and
  scroll diagnostics.
- Keep stack/pool usage fully inside the build phase.
- Freeze once into `LayoutTreeResult` or a future `PublishedLayoutFrame`.
- Preserve current `StyleOnly` layout skip and retained geometry reuse rules.

Acceptance:

- Warm-scroll `layout.elementsArray`, `layout.treeNodesArray`, and `layout.result`
  buckets are reduced only by a design that preserves retained publication
  ownership.
- `layout.nodeWalk` allocation remains zero.
- Style-only and partial-apply guards stay green.

### P4 - Dirty Classification, Hit Targets, And Snapshot Shells

After the larger tree/layout publication buckets are addressed, review smaller
publication costs:

- Dirty classification arrays created by `RenderPipeline`.
- Hit-target arrays for retained input snapshots.
- `RenderPipelineRetainedInputSnapshot` shell allocation.
- Scroll composition target lists and command-range projection helpers.

Direction:

- Use inline/scratch buffers while classifying.
- Publish compact immutable arrays only when consumers need retained data.
- Avoid reusing snapshot objects unless generation and text/resource ownership
  make stale reads impossible.

Acceptance:

- Small-bucket reductions do not add aliasing or stale retained-snapshot risks.
- Active hit-test remapping and scroll presentation diagnostics stay green.

### P5 - Backend-Side Batching And Persistent Upload Rings

The current evidence does not point to D3D12 composition as the first CPU
allocation bottleneck. Backend work should follow CPU-side publication cleanup.

Future slices:

- Persistent upload rings for repeated rect/text/material payloads.
- Descriptor/resource-table reuse under explicit frame ownership.
- GPU-side culling or compaction for large retained scenes.
- Content-space offscreen surfaces only after bounds, origin, clip, invalidation,
  and hit-test semantics are written.

Acceptance:

- D3D12 diagnostics report no device removal, no unexpected sync waits, and no
  hidden fallback path.
- Secondary paths remain diagnostic-visible and written-blocker-driven.

## Measurement Gates

Run for every substantial render-pipeline performance implementation:

```powershell
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-text-cache 180
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-transform
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-scroll
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-multilayer
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-layer-cache
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-skip
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-composition-marker-runtime
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-scroll-presentation-runtime
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-scroll-presentation-hittest
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-scroll-presentation-interaction
```

Use `.\scripts\validate.ps1 -Mode Quick` for routine doc/source validation and
`.\scripts\validate.ps1 -Mode Focused` after source changes that touch
architecture guards, diagnostics, partial-apply, composition, or scroll/input
behavior.

## Non-Goals

- No public `VirtualNode` authoring API.
- No button node or other control-specific node kinds in the render IR.
- No z-index property on render nodes.
- No CSS/cascade processing in `VirtualNode`; builders may interpret those ideas
  before emitting ordered render nodes.
- No pooled mutable arrays inside retained layout/render publications.
- No generic CPU compositor as the first performance answer.
- No optimization that weakens text/resource lifetime boundaries.

