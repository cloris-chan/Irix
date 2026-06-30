# Render Pipeline Performance Architecture

> Design note for the private Irix render pipeline. This is not a public API
> contract. It records the current diagnostic baseline, the ownership model, and
> the staged optimization plan for the core render path.

## Purpose

Irix already has the most important performance boundary in place: rendering is
driven by an internal `VirtualNode` IR, retained layout/render publications, and a
D3D12-first compositor path. The next optimization work should not be random
pooling or per-call micro-tuning. The target is stricter: after initialization,
warmup, and explicit capacity reservation, the core render pipeline should be
able to process routine UI tree, layout, draw, hit-test, and composition updates
with zero managed allocations as long as no new resource or capacity expansion is
required. That target only works if the hot path is explicit about ownership:

- Build-time scratch is temporary and can use stack storage, pooled storage, and
  borrowed spans.
- Published render state is immutable to readers, owned by the pipeline, and
  safe to retain across frames through generation-fenced views.
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

Baseline refreshed on 2026-06-29 after the `VirtualNode` IR was normalized to
`Container`/`Content` nodes, the first production control/root publication
slices were applied, `LayoutTreeResult`, `DrawCommandRecordResult`, and
`RenderPipelineRetainedInputSnapshot` became readonly value publication shells,
pipeline attribution counters were aligned to the current-thread steady-state
measurement scope, hit targets publish through `HitTargetList`, empty/single
dirty classifications and dirty ranges publish through inline value lists instead of retained arrays,
common element-command mappings publish through `ElementCommandRangeList`,
common scroll diagnostics publish through `ScrollContainerDiagList`, and common
layout elements/tree nodes/ranges publish through `LayoutElementList`,
`LayoutTreeNodeList`, and `LayoutElementRangeList`. Recorder-created
`DrawCommandBatch` values now use a typed `PooledDrawCommandMemoryOwner` that
retains command backing capacity behind a generation token, so retained handoff
freshness must match both owner identity and generation before a reused owner is
considered current. Clean render
requests also reuse the retained hit-target value publication when tree,
viewport, and dirty state are unchanged. Style-only patch attribution is now a
separate pipeline bucket, and the retained root metadata check on the layout-skip
path validates shape/property safety without projecting and discarding a copied
root.

Primary command:

```powershell
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-text-cache 180
```

Allocation summary:

| Scenario | Total | Bytes/frame | Main buckets |
|----------|-------|-------------|--------------|
| Static | 2159384 bytes | 11996 B/frame | render 10923 B/frame, tree 837 B/frame, translate 136 B/frame, diff 91 B/frame |
| Warm scroll | 239320 bytes | 1329 B/frame | tree 820 B/frame, diff 273 B/frame, render 182 B/frame, translate 91 B/frame |
| Scale change | 234128 bytes | 1300 B/frame | tree 774 B/frame, render 227 B/frame, translate 136 B/frame, diff 91 B/frame |

Warm-scroll details are the key CPU render-pipeline comparison point:

| Bucket | Bytes/frame | Notes |
|--------|-------------|-------|
| `tree.buildRoot.childPublication` | 410 | Button template children now publish into a tree-publication child array range; the remaining cost is the per-frame publication backing array until the future retained-capacity owner is installed. |
| `tree.buildRoot.children` | 227 | Root children still publish through a per-frame owned array because `VirtualNode` contains managed references and cannot be stackalloced. |
| `tree.buildRoot.container` | 136 | Remaining root/container publication cost before the future retained-capacity `VirtualNodeTree` publication owner. |
| `tree.buildRoot.button.childrenList` | 0 | The per-button child-list backing allocation is removed for the measured button-template path. |
| `tree.buildRoot.button.propertyList` | 0 | Button control metadata now publishes through compact `VirtualNodePropertyList` storage instead of per-button property arrays. |
| `drawRecord` | 0 | Recorder-owned command batches now reuse typed command owners that retain backing capacity through a generation token; dirty range mapping still publishes through `IndexRangeList`, and common element-command mapping publishes through `ElementCommandRangeList`, so `record.dirtyRanges=0`. |
| `hitTargets` | 0 | Common hit targets are retained through `HitTargetList` without publishing an array; clean render requests reuse the retained value publication, and dirty retained input snapshots patch common hit-target metadata inline. |
| `layout.elementsArray` | 0 | Common layout elements publish through `LayoutElementList` without a retained array. |
| `layout.treeNodesArray` | 0 | Common layout tree nodes publish through `LayoutTreeNodeList` without a retained array. |
| `layout.scrollDiagnosticsArray` | 0 | Empty and single scroll diagnostics are retained through `ScrollContainerDiagList` without publishing an array. |
| `layout.dirtyRanges` | 0 | Empty and single dirty ranges are retained through `IndexRangeList` without publishing an array. |
| `pipeline.classify` | 0 | Empty and single dirty classifications are retained through `LayoutDirtyClassificationList` without publishing an array. |
| `retainedFrame` | 0 | The retained frame stores the `HitTargetList` value publication without an additional copy. |
| `pipeline.snapshot.retainedInput` | 0 | The retained input snapshot shell is now a readonly value, not a heap object. |
| `layout.result` | 0 | The retained result shell is now a readonly value, not a heap object. |
| `layout.nodeWalk` | 0 | The layout bucket is publication cost, not node-walk allocation. |

Core-pipeline steady-state diagnostics over prebuilt trees, known resources, and
reserved capacity now prove the inner render loop is allocation-free:

| Scenario | Thread bytes/frame | Pipeline snapshot |
|----------|--------------------|-------------------|
| Warm reuse | 0 | `pipelineBytes=0 total`, `snapshot=0`, `retainedInput=0`, `classify=0`, `layout=0`, `styleOnlyPatch=0`, `hitTargets=0`, `record=0 total`, `retainedFrame=0 total` |
| Style only | 0 | `pipelineBytes=0 total`, `snapshot=0`, `retainedInput=0`, `classify=0`, `layout=0`, `styleOnlyPatch=0`, `hitTargets=0`, `dirtyRanges=0`, `record=0 total` |
| Layout change | 0 | `pipelineBytes=0 total`, `snapshot=0`, `retainedInput=0`, `classify=0`, `layout=0`, `styleOnlyPatch=0`, `hitTargets=0`, `dirtyRanges=0`, `scrollDiagnostics=0`, `record=0 total` |

The `styleOnlyPatch=0` bucket matters: `layout=0` is no longer the only signal
for layout-skip cost. If retained metadata validation or style-only layout
patching starts allocating again, it should now appear under `styleOnlyPatch`
instead of hiding behind the scenario total.

The older warm-scroll comparison point was about 2204 B/frame before the
Container/Content split. The current shape is expected: button-like controls now
lower into a container plus content nodes, while common small layout publication
stays inline and the remaining visible cost has moved back to tree/control
publication. Recorder command-owner shell and command-array backing capacity are
no longer warm-scroll buckets; after diagnostic capacity warmup, the inner
resource/style/command-build record buckets are also zero. The remaining visible
allocation work is outside the prebuilt-tree core loop, primarily control/tree
publication and larger retained publication pages.

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

## Steady-State No-Allocation Target

The long-term performance contract is a warm steady state with zero managed
allocation inside the core render pipeline. "Steady state" means the renderer has
completed initialization and warmup, the app has reserved enough capacity for its
current scene scale, backend caches have the needed pages/resources, and a frame
does not introduce new resource identities.

The no-allocation hot path includes:

- control composition lowering into the internal render IR;
- `VirtualNode` publication and retained diff/projection;
- layout rebuilds and style-only layout patches;
- draw command recording, command-range projection, and hit-target publication;
- retained frame handoff and composition-plan refresh;
- compositor ticks that consume already-published retained state.

Allowed allocation remains explicit and outside the steady-state budget:

| Event | Allocation rule |
|-------|-----------------|
| Process/app initialization | Allowed. |
| First-use warmup and explicit capacity reservation | Allowed. |
| Capacity page/slab/ring expansion | Allowed, recorded as capacity growth. |
| New `ContentResourceRef`, text buffer, image, glyph, material, or backend resource identity | Allowed, recorded as resource introduction or cache growth. |
| Glyph atlas/image cache page growth and device recovery | Allowed, backend-owned and diagnostic-visible. |
| Diagnostics formatting, logging, exceptions, tests, and CLI output | Outside the core hot-path budget. |
| Routine UI tree/layout/style/content updates that fit existing capacity and reuse known resources | No managed allocation. |

The required model is capacity-owned publication, not ad hoc object pooling:

- Each hot publication owns reusable pages, slabs, or ring-buffer segments with
  explicit capacity and generation.
- Writers mutate only the current build generation.
- Commit publishes read-only typed views: indices, spans, ranges, or handles
  that cannot outlive the owning publication.
- Readers never observe mutable pooled storage; they observe a frozen generation.
- Reuse is legal only after all retained readers for the older generation are
  gone or after a double/triple-buffered owner proves the old generation is not
  visible.
- Empty publications use static empty views; non-empty publications use reserved
  owner capacity.
- If capacity is insufficient, the operation may grow once, record the growth,
  and is not counted as a zero-allocation steady-state frame.

The current implementation has reached zero managed allocation for the reserved
inner `core-render-pipeline` diagnostic, but the full pipeline is not there yet.
The measured outer buckets still show the gap: control child arrays and
root/container publication remain visible in the synthetic tree builder, and
larger non-inline layout, hit-target, dirty-classification, dirty-range, and
command-range publications can still allocate when they exceed the
value-publication inline capacities. The recent value-publication slices remove
result/snapshot object identity, common small array publications, warm-scroll
draw-command backing allocation, and reserved inner record allocation, but the
remaining work is to replace new-array publication with capacity-owned pages and
generation-fenced views.

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
| Publication | Render/core retained state | Owned immutable arrays, capacity-owned pages/slabs/ring segments, value records, stable handles, frozen generation views | Stack memory, rented mutable arrays visible to readers, borrowed spans |
| Backend resources | Platform backend | Device resources, descriptor/upload rings, glyph/image/material caches with handles | App/runtime state ownership, public renderer API leakage |

Copying is acceptable during initialization, growth, and final publication. The
steady-state optimization target is to stop allocating and repeatedly copying
inside one zone when capacity already exists.

### Retained Publication Safety

`LayoutTreeResult`, retained snapshots, draw resources, hit targets, and any
future published virtual-node slab must be safe to retain after the build call
returns. They must not expose mutable pooled arrays. Empty/static arrays are fine;
non-empty published buffers must have one clear owner. Future no-allocation
publications may reuse storage only behind generation fences or equivalent
ownership rules that keep readers on immutable views while writers advance a new
generation.

### D3D12-First Constraint

The active renderer remains D3D12-first. Normal retained-frame rendering is the
explicit secondary path when a written blocker appears. Performance work must not
create a generic CPU compositor, public scheduler, or public `VirtualNode` API.

## Target Shape

The long-term target is a pipeline with explicit build, reserve, and publish
phases:

```text
App/control runtime
  -> Control composition builder
  -> VirtualNode build scratch
  -> capacity-owned VirtualNode publication view
  -> diff / retained tree patch
  -> layout build scratch
  -> capacity-owned layout publication view
  -> draw/hit/resource publication views
  -> D3D12 compositor/backend resources
```

Each arrow either borrows from the current owner for the duration of one build
phase, writes into already-reserved owner capacity, or grows capacity outside the
steady-state budget before committing the next retained publication view. That is
the C# version of the useful Rust-style idea: ownership is explicit, borrowing is
scoped, retained values cannot point back into scratch memory, and reuse is gated
by owner generation rather than convention.

## Staged Plan

### P0 - Keep The Baseline Written

This document is the baseline. Future implementation commits should update the
numbers only when a measured change lands.

Acceptance:

- `--diagnose-text-cache 180` remains the main allocation comparison.
- `--diagnose-render-steady-state-allocation 30` remains the core render
  pipeline allocation baseline for warm reused trees, style-only patches, and
  layout-affecting changes over prebuilt trees and known content resources.
- Composition and scroll presentation diagnostics above stay green for broad
  render-pipeline changes.
- `LayoutTreeResult` and `DrawCommandRecordResult` remain value publications
  instead of heap identities, empty/single dirty classification publication
  remains inline, and retained snapshots remain immutable publications until
  generation-backed snapshot slots replace their shell allocation.
- No slice may claim the full zero-allocation contract until a later reserved
  capacity guard covers control/tree publication, backend resources, and cache
  page growth boundaries in addition to this core baseline.

### P1 - Control Composition Scratch Lowering

Status: production slices implemented. Button/control lowering now writes action
and control-state style properties through a stack-backed bundle path, partitions
button template properties without three full-size temporary arrays, publishes
control metadata through compact `VirtualNodePropertyList` storage, and freezes
only non-compact property publications plus the two-child publication array.
`CounterApplication` root construction now fills the final owned child array
directly instead of staging scroll rows in a temporary array before root
publication. The semantic style bridge remains in use through
`StyleDeclarationMapper`.

Remaining hot signal: button lowering still allocates child publication arrays
after the control is split into a container plus rectangle/text content nodes.
Follow-up slices should reduce retained publication arrays structurally without
changing the IR semantics.

Direction:

- Introduce a frame-owned control/virtual-node build context for small child and
  property staging.
- Partition control properties through stack or scratch spans, then publish into
  capacity-owned node/property storage instead of per-node arrays.
- Keep button semantics outside `VirtualNode`. A button remains a control-level
  composition that emits a container plus content nodes.
- Avoid adding button nodes, z-index properties, or CSS processing names to the
  render IR.

Acceptance:

- Warm-scroll diagnostics show a reduction in `tree.buildRoot.button.*` buckets.
- Any retained `VirtualNode` data is owned after publication and is reader-immutable
  through a generation view.
- Source guards still block public `VirtualNode` authoring leakage.

### P2 - VirtualNode Publication Slab

Status: reader boundary and the first button child-range publication slice are
implemented. `VirtualNodeTree` now exposes an internal reader/cursor contract for
diff, layout traversal, and style-only patching, and measured button template
children can publish into a single tree-publication child array range. The next
migration point is no longer "add a reader"; it is "make publication storage a
retained-capacity owner with a lifetime-safe current/previous handoff."

The old measured `button.childrenList` bucket is now zero for the synthetic
button-template path. The remaining measured `tree.buildRoot.childPublication`
and root `tree.buildRoot.children` buckets show the next structural limit:
publication backing storage is still allocated per frame rather than reserved and
owned by the tree publication lifetime. Since `VirtualNode` is internal, the next
target design can move from "each node or builder owns child/property arrays" to
"one published tree owns contiguous node, child-range, property, and
content-resource slabs".

Possible shape:

- `VirtualNodeTree` owns published arrays/slabs and their retained capacity.
- `VirtualNodeRef` or a reader struct identifies nodes by index plus generation
  or tree owner. The current tree reader is a transition contract, not the slab
  owner itself.
- Node records store kind, key, content range/index, property range, and child
  range.
- Hot node/property/content-resource records that contain no managed references
  should be eligible for `where T : unmanaged` helpers and byte-span hashing or
  upload staging.
- Builders can use scratch vectors and freeze once into contiguous publication
  ranges, reusing reserved slabs when capacity is sufficient. Because
  `VirtualNode` carries managed references, root/child staging cannot rely on
  `stackalloc VirtualNode`; capacity-owned managed arrays/pages need explicit
  lifetime ownership.
- Diff/layout/style-only patch APIs consume readers/spans instead of per-node
  arrays.

This keeps ADR-010's important part: no retained all-`ref struct` tree and no
borrowed scratch in retained state. It does allow replacing the current
per-node-array value model with an owned immutable document/slab if diagnostics
justify the migration.

Acceptance:

- Warm-scroll `button.childrenList` stays at zero and broad tree-build
  publication allocation drops through retained-capacity child pages.
- Under reserved capacity and known resources, tree shape changes do not allocate
  new child/property arrays.
- Diff, layout, and retained tree tests prove stable DFS/index/key behavior.
- No published reader can outlive its owning tree publication.
- `where T : unmanaged` helpers stay internal and guarded; they do not erase
  domain-specific value types into public byte-oriented APIs.

### P3 - Layout Publication Builder

Status: first slices implemented. `LayoutTreeResult` is now a readonly value
publication shell around typed retained publications, and `RenderPipeline`
tracks retained layout through a single nullable result state instead of a
separate layout-list cache. This removes the result object's identity from the
contract while preserving retained publication ownership. `LayoutElementList`,
`LayoutTreeNodeList`, and `LayoutElementRangeList` now inline the common small
layout result, and `ScrollContainerDiagList` keeps empty and single scroll
diagnostics inline, so the warm-scroll `--diagnose-text-cache 180` run reports
`layout.result=0 B/frame`, `layout.elementsArray=0 B/frame`,
`layout.treeNodesArray=0 B/frame`, and `layout.scrollDiagnosticsArray=0 B/frame`.

`LayoutTreeBuilder` already uses stack-backed scratch lists before publishing
typed value lists. The next slice is not "pool the arrays"; it is to give the
publication freeze boundary retained capacity that can publish a new read-only
generation without allocating, including style-only patched element snapshots
that currently copy to preserve older retained snapshots.

Direction:

- Introduce an explicit `LayoutBuildScratch` or `LayoutPublicationBuilder`
  boundary around `elements`, `treeNodes`, `elementRanges`, dirty ranges, and
  scroll diagnostics.
- Keep stack/pool usage fully inside the build phase.
- Freeze once into `LayoutTreeResult` or a future `PublishedLayoutFrame`, then
  migrate large or non-inline patched frozen storage to reserved publication
  pages.
- Preserve current `StyleOnly` layout skip and retained geometry reuse rules.

Acceptance:

- Warm-scroll `layout.result`, `layout.elementsArray`, `layout.treeNodesArray`,
  and `layout.scrollDiagnosticsArray` remain zero for the common small
  publication path.
- Under reserved capacity, large full layout rebuilds and non-inline style-only
  layout patches do not allocate layout publication arrays.
- `layout.nodeWalk` allocation remains zero.
- Style-only and partial-apply guards stay green.

### P4 - Draw Ranges, Dirty Classification, Hit Targets, And Retained Frame Handoff

Status: first draw-record, retained-input snapshot shell, hit-target
value-publication, clean retained hit-target reuse, typed layout hit-target
scanning, dirty range value-publication, element-command range value-publication, and
dirty-classification value-publication slices
implemented. `DrawCommandRecordResult` and
`RenderPipelineRetainedInputSnapshot` are now readonly value publications, so
command recording and retained input publication no longer allocate per-frame
result/snapshot shell objects. `HitTargetList` keeps up to four common hit
targets inline and preserves an owned array only for larger hit-target
publications; public `RenderFrameBatch` construction still copies caller-owned
lists into this value publication. `RetainedRenderFrame.ApplyFull` stores the
value publication directly. When a render request reuses the same retained tree,
viewport, and clean state, `RenderPipeline` reuses the retained `HitTargetList`
instead of rebuilding an equivalent publication. Dirty style-only patches also
publish patched hit-target metadata through `HitTargetList`, so the warm-scroll
`hitTargets` bucket is now zero for the common path. `LayoutDirtyClassificationList` keeps
empty and single dirty classifications inline and uses an owned array only when
multiple retained classifications must be published. Hit-target construction now
uses the retained `LayoutTreeResult` typed element publication directly inside
the rendering layer, so style-only and layout-change scenarios with no action
targets no longer pay an interface-enumeration allocation just to prove the
hit-target publication is empty. `IndexRangeList` now keeps empty and single dirty element
and dirty command ranges inline, while preserving owned arrays only for
multi-range publication. `ElementCommandRangeList` now keeps up to four
element-command range records inline, preserving an owned array only for larger
draw publications. Recorder-created `DrawCommandBatch` values now publish a
generation token for their typed command owner; the owner object and command
backing array can be reused after disposal, and stale retained-frame handoff
state must match both owner identity and generation before it is considered
fresh. Frame resources, multi-classification publication, larger hit-target
publication, and large element-command range publication remain explicit
outputs.

After the larger tree/layout publication buckets are addressed, review smaller
publication costs:

- Larger element-command range arrays created by `DrawCommandRecorder`.
- Multi-classification dirty publication when more than one retained
  classification is present.
- Hit-target arrays only when a retained input snapshot publishes more than the
  inline `HitTargetList` capacity.
- Scroll composition target lists and command-range projection helpers.

Direction:

- Keep inline/scratch buffers while classifying and publish empty/single
  classifications through `LayoutDirtyClassificationList`, and empty/single
  ranges through `IndexRangeList`.
- Publish compact immutable views only when consumers need retained data.
- Move element-command range publication toward capacity-owned render-frame
  publication pages after the inline value-publication fast path; do not replace
  it with mutable pooled arrays.
- Keep `RenderPipelineRetainedInputSnapshot` as a value publication and move the
  referenced retained arrays/lists toward generation-backed publication pages
  when generation and text/resource ownership make stale reads impossible.

Acceptance:

- Small-bucket reductions do not add aliasing or stale retained-snapshot risks.
- Under reserved capacity, dirty classification, hit-target, and command-range
  projection publication does not allocate.
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
- Backend cache/page expansion is recorded separately from core steady-state
  allocation.
- Secondary paths remain diagnostic-visible and written-blocker-driven.
- Upload helpers keep pinning/native access inside the backend handoff and do not
  move resource ownership above `Irix.Platform.Windows`.

## Measurement Gates

Run for every substantial render-pipeline performance implementation:

```powershell
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-text-cache 180
dotnet run --project src\Irix.Poc\Irix.Poc.csproj -c Release -p:IrixDiagnostics=true -- --diagnose-render-steady-state-allocation 30
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

The current `--diagnose-render-steady-state-allocation 30` command is the first
reserved-capacity inner-loop guard. It warms the renderer and capacity-owning
recording paths, measures with `GC.GetAllocatedBytesForCurrentThread()`, uses
prebuilt `VirtualNodeTree` instances, reuses known text content resources, and
reports warm reuse, style-only, and layout-affecting scenarios under the
`core-render-pipeline` scope with `capacityReserved=true`.

Before declaring the full render pipeline no-allocation complete, keep this core
baseline and add a broader reserved-capacity guard. That later guard should
reserve capacity for the scenario, reuse existing content and backend resources,
then measure routine control/tree publication, diff/retained tree patching, full
layout rebuilds, style-only layout patches, retained-frame handoff, and
composition refresh with zero `GC.GetAllocatedBytesForCurrentThread()` deltas.
Separate diagnostics should report capacity growth, new resource identity, and
backend cache page expansion so legitimate growth does not masquerade as a
hot-path regression.

Measurement note: `--diagnose-text-cache 180` uses a narrow synthetic measured
button builder to isolate retained-publication buckets. It remains the comparison
for layout/tree publication cost, but it does not fully capture production
control call-site savings from stack-backed `ButtonPropertyBundle` lowering or
the exact-array `CounterApplication` root publication path.

Attribution note: the text-cache scenario total, top-level
tree/diff/translate/render row, and tree publication detail rows are broad
process-wide diagnostic counters and are labelled `processWide` in diagnostic
output. The dedicated translate, pipeline, layout, and record detail lines are
explicitly reported as current-thread counters. The inner
`--diagnose-render-steady-state-allocation` command uses the same
`GC.GetAllocatedBytesForCurrentThread()` scope for both scenario totals and
pipeline phase buckets, so phase buckets are comparable to `threadBytes`.

## Non-Goals

- No public `VirtualNode` authoring API.
- No button node or other control-specific node kinds in the render IR.
- No z-index property on render nodes.
- No CSS/cascade processing in `VirtualNode`; builders may interpret those ideas
  before emitting ordered render nodes.
- No pooled mutable arrays inside retained layout/render publications.
- No generic array pooling as a substitute for typed capacity-owned publication
  pages with generation fences.
- No generic CPU compositor as the first performance answer.
- No optimization that weakens text/resource lifetime boundaries.
- No claim of zero-allocation steady state until a dedicated allocation guard
  proves warm updates under reserved capacity allocate zero managed bytes.
- No public byte-oriented render APIs just because an internal value is
  unmanaged.
- No intrinsics or unsafe pointer paths before the data layout and ownership
  boundary are stable and measured.
