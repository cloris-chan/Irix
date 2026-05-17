# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-17.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, phase boundaries, ADR index, and v1/v2 scope. |
| [GA-Hardening-Plan.md](GA-Hardening-Plan.md) | Current GA/MVP hardening state, accepted risks, display/stability/performance evidence. |
| [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md) | Remaining post-GA renderer and framework-promotion backlog. |
| [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md) | Post-GA D3D12-only glyph atlas text renderer design. |
| [ADR-Scissor-Clipping-v0.md](ADR-Scissor-Clipping-v0.md) | Clip/scissor/text-clip v0 decision and frozen behavior. |
| [LayoutDirtyV1-Design.md](LayoutDirtyV1-Design.md) | Layout dirty classification and StyleOnly planning boundary. |
| [Diagnostics-Snapshot-v0.md](Diagnostics-Snapshot-v0.md) | Diagnostics snapshot v0 boundary and formatter contract. |
| [Project_Status_and_Todo.md](Project_Status_and_Todo.md) | This short current status page. |

Removed historical prep/checkpoint docs were already absorbed into the canonical docs above or into tests. Do not recreate `*-Prep.md`, checkpoint, release-note, or smoke-checklist docs unless there is a new active design line.

---

## Current State

| Area | Status |
|------|--------|
| V1 core | Complete / regression-only. Do not reopen core feature scope for GA cleanup. |
| Windows backend | D3D12 is the active v1 PoC backend; D3D11On12/D2D text overlay remains accepted until the post-GA glyph atlas path lands. |
| Partial apply | Default-on, with `--no-partial-apply` rollback. Existing segmented ownership path and guards are test-covered. |
| Display scale | Complete / regression-only for current evidence: 100%, 150%, 200%; 60Hz, 120Hz, 240Hz. |
| Text/value IR | Complete. `VirtualNode -> LayoutElement -> DrawCommandRecorder` uses `TextNodeContent` and `TextBufferSnapshot.ResolveRequired`; no string text property path. |
| Style/property model | Complete after Round 15 cleanup. Public authoring uses one typed property helper surface. Metadata/support/diagnostics remain internal. |
| Ref struct boundary | Complete for Round 16. `ref struct` is limited to synchronous builders/readers/layout context; retained IR and batches stay ordinary storable types. |
| Record struct boundary | Complete for Round 18. Framework/internal primitives, IR, render hot paths, platform types, and PoC backend/diagnostics do not use `record struct`. |
| Profiler allocation pass | Complete for retained/input first pass. The 2026-05-17 VS profiler GCDump drove targeted cleanup in canonical retained apply, input hit-test routing, input ownership diagnostics, retained metadata projection, and text snapshot comparison. |
| Docs | Trimmed to canonical docs only; obsolete prep/checkpoint docs deleted. |

---

## Property / Node API Rules

Public authoring uses semantic helpers:

```csharp
VirtualNodeProperty.Width(160);
VirtualNodeProperty.Height(40);
VirtualNodeProperty.ScrollY(scrollY);
VirtualNodeProperty.Action(actionId);
VirtualNodeProperty.Hovered(isHovered);
VirtualNodeProperty.Pressed(isPressed);
VirtualNodeProperty.Focused(isFocused);
```

Current public keys:

| Property | Value kind | Effects | Supported nodes |
|----------|------------|---------|-----------------|
| `Width`, `Height` | Number | Layout | Rectangle, Button, ScrollContainer |
| `ScrollY` | Number | Layout | ScrollContainer |
| `ActionId` | ActionId | Interaction | Button |
| `IsHovered`, `IsPressed`, `IsFocused` | Boolean | Interaction + Visual | Button |

Current exclusions:

- No public `Opacity` until it is wired to real draw/composite behavior.
- No public `FillColor` / `TextColor` until a pure typed color value exists.
- No string style properties such as `FontFamily`.
- No global layout metrics as node properties.
- No control-specific size keys such as `ButtonHeight` or `RectangleHeight`.
- No `VirtualAttribute*` / `AttributeValue*` compatibility aliases.

Round 15 hardening state:

- `VirtualNodeKind.None` is the default node kind.
- `VirtualNode.Properties` and `VirtualNode.Children` are immutable snapshots exposed as `ReadOnlySpan<T>`; no per-node `IReadOnlyList` wrapper is allocated.
- `VirtualNode`, `VirtualNodeTree`, and `VirtualNodePatch` do not expose public value equality; explicit structural comparison is internal.
- `VirtualNodeProperty` constructor is private; normal construction goes through helpers.
- `PropertyValue` exposes `TryGetX` and `GetRequiredX`; silent getters were removed.
- `VirtualNodeFactory.Rectangle(width, height, ...)` was removed; width/height are explicit properties.
- Buttons require an explicit label; layout no longer falls back to `"Button"`.

Round 16 construction/layout state:

- `VirtualNode` remains a normal `readonly struct`; it is safe for retained/diff/patch/batch storage.
- `VirtualNodePropertyListBuilder` is a `ref struct` over caller-provided `Span<VirtualNodeProperty>`; small lists can use `stackalloc`, and callers hand off `Written` to span factories.
- `VirtualNodeChildrenBuilder` is a `ref struct` with an `InlineArray(4)` child buffer and array fallback beyond inline capacity.
- `VirtualNodeFactory.Create(...)` accepts `ReadOnlySpan<VirtualNodeProperty>` and `ReadOnlySpan<VirtualNode>` / children builder overloads.
- Internal owned-array construction is named `CreateFromOwnedArraysUnsafe` and documents the no-mutation ownership handoff.
- Public authoring helpers use `params scoped ReadOnlySpan<T>`, not array `params`; array `params` is guarded against reintroduction.
- `LayoutTreeBuilder` reads node properties through internal `PropertyReader` over `VirtualNode.Properties`; `LayoutContext` is a synchronous `ref struct`.

Display/input coordinate rules:

- Public input-facing compositor hit testing uses physical pixels: `DrawingBackendCompositor.TryGetActionIdAtPhysicalPixel(...)`.
- Logical hit testing is internal/test/handoff-only: `TryGetActionIdAtLogicalPixel(...)` is not a platform input API.
- Stored `DisplayScale` values are normalized at ingress; default/invalid scale is converted to `1x` before storage or backend frame use.
- Non-uniform draw scale remains supported. Text font size uses the normalized Y-axis text scale, not an average of X/Y.

No-record-struct boundary:

- `Irix.Core`, `Irix.Drawing`, `Irix.Rendering`, `Irix.Platform`, and `Irix.Platform.Windows` must not introduce `record struct`.
- Framework/internal primitives, retained IR, render-frame batches, layout/render diagnostics, handoff structs, platform input/viewport types, and backend hot-path structs are ordinary `readonly struct` types with explicit constructors and value equality.
- `Irix.Poc` backend, diagnostics snapshots, and rendering/window helpers follow the same no-record-struct rule.
- The only exception is UI authoring/MVU state and message shape in `Irix.Poc`, such as `CounterModel` / `CounterMessage`, scroll/input/control state, and feedback state. That exception is for authoring ergonomics only and must not leak into framework or backend hot paths.
- Framework/internal hot paths also avoid record `with` syntax; any rebuild is explicit constructor reconstruction.

Profiler-driven retained/input allocation pass:

- `RetainedTree.Apply` treats differ-created canonical-root batches as authoritative and no longer reconstructs an intermediate tree that is discarded. Dirty indices are still derived from patches: updates mark the node, adds mark the parent in the next tree, and removes mark the parent in the previous tree.
- Runtime input routing uses a value-type hit-test resolver instead of allocating a `Func<int,int,ActionId>` delegate/closure on the native input path. `Program.TryMapInputForRuntime` has no delegate overload.
- Input ownership diagnostics are value events in a bounded 128-event ring buffer. The diagnostic stream is not a retained unbounded object log and does not compact with `RemoveAt(0)`.
- Text content equality uses internal `TryResolve` instead of `ResolveRequired` when diffing/classifying. Mismatched snapshots return `false` for comparison; actual draw recording still uses `ResolveRequired` and continues to fail hard on invalid text ownership.
- Retained root metadata projection and hit-target metadata projection use stack spans/arrays for small dirty/key sets instead of short-lived `HashSet` / `List` objects on the partial-apply path.

Profiler findings intentionally not folded into this pass:

- `List<LayoutElement>`, `List<LayoutTreeNode>`, and dirty range scratch allocations remain the next layout-builder scratch arena / pooling target.
- D3D12 text layout cache and overlay synchronization remain in the post-GA glyph atlas line; this pass does not implement glyph atlas or rewrite renderer ownership.

---

## Current Verification

Last local verification:

```powershell
dotnet build Irix.slnx --no-restore
dotnet test tests/Irix.Core.Tests/Irix.Core.Tests.csproj --no-restore
dotnet test Irix.slnx --no-restore
```

Result: 589 tests passed.

Round 16 allocation probes:

```text
5,000 BuildView -> diff -> layout -> record iterations
builder/span path: must not allocate more than inline helper path by over 128 KB; lower allocation is allowed

5,000 authoring-only VirtualNode iterations
builder/span path: must not allocate more than inline helper path by over 128 KB; lower allocation is allowed
```

Round 18 allocation guard baseline:

| Area | Current guard |
|------|---------------|
| BuildView authoring helpers | Builder/span path must not regress over inline helper path by more than 128 KB over 5,000 iterations. |
| Diff -> layout -> record pipeline | Builder/span root path must not regress over inline helper path by more than 128 KB over 500 retained pipeline iterations. |
| FrameDrawingResources warm pool | Warm rent/add/seal/return stays under 2 MB over 1,000 frames. |
| D3D12 ExecuteCore scale path | 150% / 200% scale executes without allocating a scaled command array. |
| Compositor render loop | Mock backend render loop stays under the 20 ms/frame average guard over 180 frames. |

Source guards currently block:

- Primitive `ActionId.ToString()` and `VirtualPropertyKey.ToString()`.
- Legacy attribute-era API names.
- Old alias helpers.
- String style/value factories.
- Global layout key reintroduction.
- Missing property metadata.
- Public/no-op `Opacity`.
- `VirtualNodeProperty` public/internal construction.
- Silent `PropertyValue` getters.
- Reference-equality `VirtualNode` semantics.
- Ref struct leakage into batch/retained state sources.
- Framework/backend `record struct` and framework record `with` syntax outside MVU authoring state exceptions.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Allocation baseline tightening | Measure/tighten hot path allocations without reopening string property design. |
| P1 | D3D12-only glyph atlas prototype | Follow [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md); do not change public API. |
| P1 | Framework promotion review | Translator/scroll/settings promotion only after a concrete contract is written in the main design/backlog docs. |
| P2 | StyleOnly layout skip | Future fast path only; current layout still rebuilds. |

Do not mix glyph atlas implementation, renderer rewrites, or public API expansion into style/property cleanup.

