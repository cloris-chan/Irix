# Irix Project Status

> Current developer handoff note for the Irix Windows PoC.
> Last verified: 2026-05-20.

---

## Canonical Docs

| Doc | Purpose |
|-----|---------|
| [Irix_Framework_Design.md](Irix_Framework_Design.md) | Main architecture design, phase boundaries, ADR index, and v1/v2 scope. |
| [GA-Hardening-Plan.md](GA-Hardening-Plan.md) | Current GA/MVP hardening state, accepted risks, display/stability/performance evidence. |
| [Post-V1-MVP-Backlog.md](Post-V1-MVP-Backlog.md) | Remaining post-GA renderer and framework-promotion backlog. |
| [Glyph-Atlas-Post-GA-Design.md](Glyph-Atlas-Post-GA-Design.md) | Post-GA D3D12-only glyph atlas text renderer design. |
| [Glyph-Atlas-Mixed-Fallback-Smoke-Evidence-2026-05-19.md](Glyph-Atlas-Mixed-Fallback-Smoke-Evidence-2026-05-19.md) | Local evidence for mixed ASCII/NonAscii/clipped glyph-atlas fallback and default long smoke. |
| [Post-GA-Default-Baseline-Evidence-2026-05-18.md](Post-GA-Default-Baseline-Evidence-2026-05-18.md) | Local evidence for the post-GA default switch to GlyphAtlas text composition and Scissor clipping. |
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
| Windows backend | D3D12 is the active v1 PoC backend. GlyphAtlas text composition is the post-GA default on `post-ga-renderer-foundation`; mixed fallback v0 keeps accepted atlas runs on D3D12 and sends unsupported runs to D3D11On12/D2D overlay. `--text-composition overlay` remains rollback while the overlay removal path is built. |
| Backend clip | Scissor is the default backend clip mode; `--disable-scissor` / `--clip-mode diagnostic` remain diagnostic rollback paths. |
| Default renderer baseline | GlyphAtlas + Scissor default baseline enabled. Do not introduce another runtime default switch before shader/resource lifetime and allocation attribution hardening. |
| Partial apply | Default-on, with `--no-partial-apply` rollback. Existing segmented ownership path and guards are test-covered. |
| Shader packaging | D3D12 rectangle and glyph-atlas passes use embedded DXBC bytecode. Runtime `D3DCompile` / `d3dcompiler_47.dll` is no longer required by renderer source. |
| Resource lifetime | D3D12 upload maps, swapchain intermediates, and overlay wrapping intermediates release through `finally`. Core device/queue/RTV/command/fence setup is shared by constructor and recovery with pointer guards and constructor-failure cleanup. |
| Display scale | Complete / regression-only for current evidence: 100%, 150%, 200%; 60Hz, 120Hz, 240Hz. |
| Text/value IR | Complete. `VirtualNode -> LayoutElement -> DrawCommandRecorder` uses `TextNodeContent` and `TextBufferSnapshot.ResolveRequired`; no string text property path. Device error diagnostics use typed `DeviceErrorDiagnostic`; text formatting stays at CLI/debug/report output boundaries. |
| Style/property model | Complete after Round 15 cleanup. Public authoring uses one typed property helper surface. Metadata/support/diagnostics remain internal. |
| Ref struct boundary | Complete for Round 16. `ref struct` is limited to synchronous builders/readers/layout context; retained IR and batches stay ordinary storable types. |
| Record struct boundary | Complete for Round 18. Framework/internal primitives, IR, render hot paths, platform types, and PoC backend/diagnostics do not use `record struct`. |
| Profiler allocation pass | Complete for retained/input first pass. The 2026-05-17 VS profiler GCDump drove targeted cleanup in canonical retained apply, input hit-test routing, input ownership diagnostics, retained metadata projection, and text snapshot comparison. |
| Allocation baseline | In place for MVU BuildView, diff, retained apply, layout full/dirty, draw command record full/dirty, D3D12 ExecuteCore at 100%/150%, render-request reuse, mock backend frame timing, and FrameDrawingResources warm pool allocation. |
| Performance micro-optimization | Pre-GA micro-optimization is closed. `--diagnose-text-cache` now attributes warm glyph-atlas allocation by tree/diff/translate/render stage; optimize from evidence, not guesses. |
| Private GA | Tagged as `v1.0-private-ga`; `post-ga-renderer-foundation` is the active next branch. This is an internal/private milestone, not a public API freeze. |
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
| `Width` | Number | Layout | Rectangle, Button |
| `Height` | Number | Layout | Rectangle, Button, ScrollContainer |
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

Latest local default-baseline verification:

```powershell
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --filter "Category!=D3D12&Category!=Performance" --verbosity normal
dotnet test --no-build -c Release --filter "Category=D3D12" --verbosity normal
dotnet test --no-build -c Release --filter "Category=Performance" --verbosity normal
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --text-composition overlay
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-sync 300 3 --diagnose-sync-non-ascii
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150
dotnet run --no-build -c Release --project src/Irix.Poc -- --diagnose-glyph-atlas-mixed-fallback 30 --diagnose-scale 150 --text-composition overlay
```

Result: Release build passed; normal tests `608` passed; D3D12 tests `6` passed; performance tests `6` passed; default GlyphAtlas sync smoke reported `syncWaits=0`; overlay rollback and NonAscii fallback smokes presented normally. Self-contained publish passed after shader packaging removal. Glyph-atlas diagnostics now keep constructor-time `initFailurePhase` separate from runtime `recordFailurePhase`.

Mixed fallback v0 update: Release build passed; normal tests `618` passed; D3D12 tests `6` passed; performance tests `6` passed.
Program diagnostics tests `55` passed. Short default GlyphAtlas smoke reported `atlasRuns=90`, `overlayFallbackRuns=0`, `syncWaits=0`.
Short NonAscii mixed fallback smoke reported `atlasRuns=60`, `overlayFallbackRuns=30`, `NonAscii=30`, `syncWaits=30`.

Mixed fallback extended smoke: `ASCII / NonAscii / clipped ASCII / clipped NonAscii` at 150% scale reported `atlasRuns=60`, `overlayFallbackRuns=60`, `NonAscii=60`, `textClipSkipped=0`, `lastEffectiveTextClip=(36,264,168,39)`, and `syncWaits=30`.
The same scene in `--text-composition overlay` completed as whole-frame overlay with the same final effective text clip.
Overlay subset parity reported `fallbackRuns=2`, `wholeFrameOverlayRuns=4`, and resolver/style/clip/scale/color all preserved.
Default long GlyphAtlas `300 x 3` reported `frameSerial=900`, `presentSerial=900`, `syncWaits=0`, `atlasRuns=2700`, and `overlayFallbackRuns=0`.
Mixed AtlasFull stress reported `atlasRuns=5`, `overlayFallbackRuns=30`, `AtlasFull=29`, `NonAscii=1`, `RecordFailed=0`, `initFailurePhase=None`, `recordFailurePhase=None`, and no device removal.
Record-failure contract tests now pin all-renderable-run fallback with `recordFailurePhase=AtlasUploadMap`; this is unit contract coverage, not a forced GPU upload-failure smoke.

Manual smoke status:

| Smoke | Status |
|-------|--------|
| Default run | Passed |
| 100% scale | Passed |
| 150% scale | Passed |
| 200% scale | Passed |
| Runtime scale switch | Passed |
| Refresh evidence | 60Hz / 120Hz / 240Hz accepted; 144Hz removed from the current matrix because no hardware is available. |

Current allocation/performance guards:

| Area | Current guard |
|------|---------------|
| VirtualNode authoring helpers | Builder/span path must not regress over inline helper path by more than 128 KB over 5,000 authoring iterations. |
| Diff -> layout -> record pipeline parity | Builder/span root path must not regress over inline helper path by more than 128 KB over 500 retained pipeline iterations. |
| Frame-stage warm path baseline | `BuildView`, diff, retained apply, layout full/dirty, record full/dirty, render-request reuse, and D3D12 `ExecuteCore` are measured as separate stages. |
| Stage allocation budgets | `BuildView` < 128 KB; diff < 16 KB; retained apply < 8 KB; layout full/dirty < 32 KB each; record full/dirty < 64 KB each; render-request reuse < 64 KB. |
| D3D12 ExecuteCore scale path | 100% and 150% scale execute with zero command-scaling allocation. |
| FrameDrawingResources warm pool | Warm rent/add/seal/return stays under 2 MB over 1,000 frames. |
| Compositor render loop | Mock backend render loop stays under the 20 ms/frame average guard over 180 frames. |

Latest local frame-stage allocation output (2026-05-17, Debug build, .NET 10.0.8, Windows x64):

| Stage | Allocated bytes | Allocation type | Current reading |
|-------|-----------------|-----------------|-----------------|
| MVU `BuildView` | 17,408 | Necessary authoring/result allocation plus remaining temporary arrays in the PoC view builder | Highest measured stage; do not optimize blind yet. Next investigation should separate `CounterApplication` authoring arrays from framework node freezing cost. |
| `VirtualNodeDiffer.CreatePatchBatch` | 1,288 | Necessary patch result boundary, with small retained temporary overhead | Already low; not the next target unless patch payload grows. |
| `RetainedTree.Apply` canonical path | 80 | Necessary `ApplyResult` / dirty result boundary | Canonical retained apply is effectively clean after the profiler pass. |
| `LayoutTreeBuilder.BuildLayoutTree` full | 544 | Necessary result arrays | Flat layout tree removed the main temporary collection cost; remaining bytes should mostly be returned result arrays. |
| `LayoutTreeBuilder.BuildLayoutTree` dirty | 576 | Necessary result arrays plus dirty range result | Dirty path is near full path; no obvious temporary collection issue. |
| `DrawCommandRecorder.Record` full | 192 | Necessary command/resource result boundary | Full record is low; current command/resource handoff dominates. |
| `DrawCommandRecorder.Record` dirty | 1,752 | Necessary command/resource result boundary plus dirty command range result | Higher than full because dirty range/result handoff is included; verify before changing recorder internals. |
| D3D12 `ExecuteCore` 100% | 0 | No allocation | Scale-free backend execute path is clean. |
| D3D12 `ExecuteCore` 150% | 0 | No allocation | On-the-fly scale path does not allocate command arrays. |
| Render-request reuse | 2,280 | Re-record result boundary plus retained input snapshot copies; no layout rebuild | Attribution below shows record/result handoff is the largest residual source, not hit targets. |

Render-request reuse allocation attribution (same local environment):

| Component | Allocated bytes | Reading |
|-----------|-----------------|---------|
| `DrawCommandRecorder.Record` | 1,688 | Main residual cost. This includes command owner/resource result handoff and element-command range result arrays even when layout is reused. |
| `BuildHitTargets` | 152 | One small `HitTestTarget[]`; not the primary optimization target for this measured tree. |
| `RenderPipelineRetainedInputSnapshot` spread/copy | 448 | Snapshot copies are visible but smaller than record. Reusing retained arrays may help, but it should follow record/result analysis. |
| `RenderFrameBatch` construction | 24 | Effectively noise; not worth targeting. |
| Total render-request reuse path | 2,280 | Layout rebuild count stayed unchanged and `LastLayoutRebuildReason == None`. |

CI currently enforces the guards above but does not persist exact per-stage byte output as an artifact. Treat the table as the latest local measurement, not a CI average.

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
- Device error diagnostic regression back to retained `string` state in platform/PoC paths.

---

## Next Reasonable Work

| Priority | Work | Boundary |
|----------|------|----------|
| P0 | Renderer foundation hardening | GlyphAtlas + Scissor default baseline enabled; do not flip another default. Continue resource lifetime hardening from the embedded-shader baseline. |
| P1 | Resource cache / stable global handles | Start POST-011 in the D3D12 renderer line so glyph atlas resources can survive frames and move toward non-overlay fallback handling. |
| P1 | Shader packaging follow-up | Runtime shader compile is removed. Decide later whether inline embedded DXBC is enough or a build-time shader asset pipeline is worth adding. |
| P1 | Attribute warm glyph atlas allocation | Run `--diagnose-text-cache` and use tree/diff/translate/render attribution before attempting allocation cleanup. Measure first; do not blind-optimize. |
| P1 | Framework promotion review | Translator/scroll/settings promotion only after a concrete contract is written in the main design/backlog docs. |
| P1 | Mixed fallback extended smoke evidence | Mixed ASCII/NonAscii/clipped, overlay subset parity, mixed AtlasFull, record-failure contract, and default long smoke are recorded. Eviction remains deferred before widening atlas text coverage. |
| P1 | Overlay removal path | Active migration target. Remove D3D11On12/D2D from final composition as soon as unsupported text, atlas-full/eviction, diagnostics, and rollback have non-overlay behavior or an explicit degradation contract. |
| P2 | StyleOnly layout skip | Future fast path only; current layout still rebuilds. |

Do not mix glyph atlas implementation, renderer rewrites, or public API expansion into style/property cleanup.

