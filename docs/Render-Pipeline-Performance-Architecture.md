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

## C# Systems Techniques To Reuse

The Irix renderer can borrow several ideas from systems-style C# work such as
Nockawa's database-engine notes without turning the codebase into unsafe-first
code. The useful pattern is to spend C#'s type system where it can make hot-path
data shape explicit, then reserve unsafe or byte-level access for narrow,
guarded bridges.

### `where T : unmanaged`

`where T : unmanaged` is important because it lets a generic API state that `T`
contains no managed references. That unlocks a different class of render-pipeline
helpers:

- `sizeof(T)` / `Unsafe.SizeOf<T>()` can be used behind one strongly typed helper
  instead of scattered per-type constants.
- `Span<T>` / `ReadOnlySpan<T>` payloads can be copied, hashed, compared, or
  uploaded as bytes through a single constrained path.
- The JIT can specialize the generic method for the concrete value type used by
  a hot path.
- APIs can reject accidental managed-reference fields at compile time instead of
  discovering them through runtime allocation regressions.

For Irix, this is a good fit for compact render payloads and keys, not for every
domain object. Strong candidates are value-only records such as `NodeKey`,
`ActionId`, layout records, command-range records, material records, composition
layer records, and future slab records. Poor candidates are values that own text,
managed arrays, delegates, resources with disposal semantics, or backend COM
objects.

The rule should be: use `where T : unmanaged` on internal helpers that operate on
contiguous value payloads, not as a public API promise and not as a reason to
erase domain names into raw bytes.

### Byte Views With Typed Owners

Database engines care about reading and writing structured pages without
per-record allocation. The render pipeline has a similar shape when publishing
layout, draw, hit-test, and composition payloads. The reusable idea is:

```csharp
internal static ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> values)
    where T : unmanaged
{
    return MemoryMarshal.AsBytes(values);
}
```

The helper should live behind an internal boundary and be covered by size/layout
guards. Callers should still speak in terms of `LayoutElement`,
`LayoutTreeNode`, `DrawCommand`, `DrawMaterial`, or future slab records. The byte
view is an implementation detail for hashing, equality, serialization-like
diagnostics, upload staging, or cache keys.

### Explicit Layout And Size Guards

Irix already uses explicit/sequential layout and size guards for compact values
such as color, paint, content resource, and property values. Keep expanding that
style only for payloads that cross hot boundaries:

- records stored in large contiguous arrays;
- values copied into frame resources;
- payloads hashed for layer/material/cache identity;
- values uploaded to backend buffers;
- future virtual-node/layout publication slab records.

Every such value needs tests for size, layout intent, and semantic round-trip
behavior. A small layout is not useful if it makes invalid states easier to
construct.

### Data-Oriented Slabs Before Object Graphs

The database-engine lesson maps especially well to `VirtualNode`: when a
published structure is traversed often and mutated only through rebuild/diff
phases, a contiguous owner can be better than many small objects or per-node
arrays. That is the rationale behind the future `VirtualNodeTree` slab in P2.

The slab should stay typed:

- node records are not raw bytes;
- child/property/content ranges are explicit ranges, not unstructured offsets;
- readers expose domain operations, not arbitrary pointer arithmetic;
- publication owns the arrays and readers cannot outlive that publication.

### Ref Structs And Stack-Scoped Builders

The repo already uses `ref struct` scratch helpers such as property readers,
layout contexts, and scratch lists. Continue that direction for build-only APIs:

- property partitioning;
- child staging;
- dirty classification;
- temporary layout vectors;
- command-range projection.

Do not store those builders in retained snapshots, async state machines, fields,
or backend-owned objects. If something must cross the frame boundary, freeze it
into an owned publication first.

### Pinning And Native Upload Boundaries

Pinning and fixed pointers are useful at the backend edge, but they are expensive
architecture tools if they leak upward. Keep pinning rules narrow:

- pin only for the duration of a backend/source-data call;
- prefer unmanaged contiguous payloads for upload staging;
- never let a pinned managed object become the lifetime owner of a retained
  render resource;
- prefer backend-owned upload rings or native resources once data is handed to
  D3D12.

### Intrinsics And SIMD

Intrinsics belong after layout and ownership are stable. Potential future uses:

- compare/hash unmanaged payload spans for cache identity;
- transform batches of rectangles or layer values;
- compact/cull large retained command ranges.

They should sit behind scalar fallbacks or guarded helper methods. Do not use
SIMD to compensate for a structure that is still allocating the wrong shape.

### Analyzer Guards For Domain Lifetimes

C# cannot express every Rust-like lifetime rule. Irix can compensate with source
guards and analyzers around its own invariants:

- no managed-reference fields in unmanaged hot payload records;
- no `Span<T>`, `ReadOnlySpan<T>`, or `ref struct` leakage into retained state;
- no pooled mutable arrays inside retained publications;
- no public `VirtualNode` authoring surface;
- no backend resource ownership above platform backends.

This is often better than forcing every API into unsafe code. The language covers
the simple lifetime cases; guards cover the project-specific ones.

## Evidence Snapshot

Baseline refreshed on 2026-06-25 after the `VirtualNode` IR was normalized to
`Container`/`Content` nodes, the first production control/root publication
slices were applied, and `LayoutTreeResult` became a readonly value publication
shell.

Primary command:

```powershell
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-text-cache 180
```

Allocation summary:

| Scenario | Total | Bytes/frame | Main buckets |
|----------|-------|-------------|--------------|
| Static | 2271552 bytes | 12619 B/frame | render 11014 B/frame, translate 770 B/frame, tree 702 B/frame |
| Warm scroll | 547368 bytes | 3040 B/frame | translate 1867 B/frame, tree 876 B/frame, diff 182 B/frame, render 182 B/frame |
| Scale change | 351696 bytes | 1953 B/frame | tree 911 B/frame, translate 592 B/frame, render 364 B/frame |

Warm-scroll details are the key CPU render-pipeline comparison point:

| Bucket | Bytes/frame | Notes |
|--------|-------------|-------|
| `layout.elementsArray` | 501 | Retained layout publication array. |
| `layout.treeNodesArray` | 318 | Retained layout tree publication array. |
| `tree.buildRoot.button.childrenArray` | 284 | Cost of publishing per-button child arrays after the button becomes a container with rectangle/text content children. |
| `tree.buildRoot.button.propertyArray` | 227 | Control/state/style property publication. |
| `drawRecord` | 227 | Command recording is visible but not the largest bucket. |
| `layout.scrollDiagnosticsArray` | 91 | Retained scroll observation publication array. |
| `layout.result` | 0 | The retained result shell is now a readonly value, not a heap object. |
| `layout.nodeWalk` | 0 | The layout bucket is publication cost, not node-walk allocation. |

The older warm-scroll comparison point was about 2204 B/frame before the
Container/Content split. The current shape is expected: button-like controls now
lower into a container plus content nodes, and layout publication exposes owned
arrays rather than a retained result-object allocation.

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

Status: production slices implemented. Button/control lowering now writes action
and control-state style properties through a stack-backed bundle path, partitions
button template properties without three full-size temporary arrays, and freezes
only the final container/content property arrays plus the two-child publication
array. `CounterApplication` root construction now fills the final owned child
array directly instead of staging scroll rows in a temporary array before root
publication. The semantic style bridge remains in use through
`StyleDeclarationMapper`.

Remaining hot signal: button lowering allocates child/property publication arrays
after the control is split into a container plus rectangle/text content nodes.
Follow-up slices should reduce retained publication arrays structurally without
changing the IR semantics.

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
- Hot node/property/content-resource records that contain no managed references
  should be eligible for `where T : unmanaged` helpers and byte-span hashing or
  upload staging.
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
- `where T : unmanaged` helpers stay internal and guarded; they do not erase
  domain-specific value types into public byte-oriented APIs.

### P3 - Layout Publication Builder

Status: first slice implemented. `LayoutTreeResult` is now a readonly value
publication shell around owned retained arrays/lists, and `RenderPipeline`
tracks retained layout through a single nullable result state instead of a
separate layout-list cache. This removes the result object's identity from the
contract while preserving retained publication array ownership. The 2026-06-25
`--diagnose-text-cache 180` warm-scroll run reports `layout.result=0 B/frame`;
the remaining layout buckets are retained publication arrays.

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

- Warm-scroll `layout.result` remains zero, and `layout.elementsArray`,
  `layout.treeNodesArray`, plus `layout.scrollDiagnosticsArray` are reduced only
  by a design that preserves retained publication ownership.
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
- Unmanaged payload upload helpers for compact value arrays such as materials,
  rectangles, layer transforms, and command metadata.
- GPU-side culling or compaction for large retained scenes.
- Content-space offscreen surfaces only after bounds, origin, clip, invalidation,
  and hit-test semantics are written.

Acceptance:

- D3D12 diagnostics report no device removal, no unexpected sync waits, and no
  hidden fallback path.
- Secondary paths remain diagnostic-visible and written-blocker-driven.
- Upload helpers keep pinning/native access inside the backend handoff and do not
  move resource ownership above `Irix.Platform.Windows`.

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

For unmanaged-payload work, add focused guard coverage for:

- `Unsafe.SizeOf<T>()` / layout expectations of every new hot payload value.
- Compile-time `where T : unmanaged` constraints on generic byte/hash/upload
  helpers.
- Negative source-shape checks that retained publications do not store spans,
  ref structs, or pooled mutable buffers.
- Semantic tests proving byte-level helpers do not replace domain equality where
  padding, normalization, or resource identity matters.

Measurement note: `--diagnose-text-cache 180` uses a narrow synthetic measured
button builder to isolate retained-publication buckets. It remains the comparison
for layout/tree publication cost, but it does not fully capture production
control call-site savings from stack-backed `ButtonPropertyBundle` lowering or
the exact-array `CounterApplication` root publication path.

## Non-Goals

- No public `VirtualNode` authoring API.
- No button node or other control-specific node kinds in the render IR.
- No z-index property on render nodes.
- No CSS/cascade processing in `VirtualNode`; builders may interpret those ideas
  before emitting ordered render nodes.
- No pooled mutable arrays inside retained layout/render publications.
- No generic CPU compositor as the first performance answer.
- No optimization that weakens text/resource lifetime boundaries.
- No public byte-oriented render APIs just because an internal value is
  unmanaged.
- No intrinsics or unsafe pointer paths before the data layout and ownership
  boundary are stable and measured.
