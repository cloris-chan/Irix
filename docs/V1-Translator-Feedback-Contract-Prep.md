# v1 Translator Feedback Contract Prep

> Design-only contract draft for `WindowDrawCommandTranslator` feedback and promotion gates. This document does not move the translator, introduce framework APIs, replace the legacy max-scroll callback, change runtime behavior, or enable StyleOnly fast-path.

## 1. Scope

Current status:

- Diagnostics consolidation is regression-only: Program diagnostics runner split, diagnostics snapshot v0, debug UI bridge v0, and formatter contracts are sealed.
- Controls-boundary helpers are regression-only: `ControlVisualState`, `ControlActionAttributeAdapter`, and `ButtonAttributeBundle` remain PoC-owned internal code.
- Scroll feedback vocabulary v0 is regression-only: `ScrollFeedback` / `ScrollContainerMetrics` are side-channel translator feedback; the legacy `Action<double>` max-scroll callback still drives runtime behavior.
- `WindowDrawCommandTranslator` is not ready for promotion. It still embeds Counter style defaults, private retained-tree ownership, private pipeline creation, and sample-shaped feedback.
- Contract naming is sufficient for the next implementation planning step; this line remains design-only unless a regression test is added to preserve existing behavior.

Non-goals:

- Do not move `WindowDrawCommandTranslator` out of `Irix.Poc`.
- Do not introduce public framework APIs.
- Do not replace `Action<double>` or change `CounterMessage.UpdateMaxScrollY` flow.
- Do not move `ScrollController`, `ScrollState`, or `ScrollFramePump`.
- Do not enable StyleOnly fast-path.
- Do not introduce a unified diagnostics channel / event bus / registry.

## 2. Current Translator Shape

| Area | Current shape | Contract issue |
|------|---------------|----------------|
| Style source | Private `RenderPipeline(CounterStylePreset.Default)` inside `WindowDrawCommandTranslator` | Framework promotion would bake Counter style into a reusable bridge. |
| Viewport source | `INativeWindow.Region.PhysicalBounds` or optional `Func<PixelRectangle>` | The contract does not name pixel units, resize timing, or fallback behavior. |
| Retained-tree owner | Private `RetainedTree` inside translator | Patch application ownership and retained tree lifetime are implicit. |
| Render-pipeline creation | Private `RenderPipeline` instance | Pipeline style/options/factory are hidden and sample-shaped. |
| Prepare-frame hook | Optional `Action? prepareFrame` | Resource/resize preparation semantics are not named. |
| Post-frame feedback | Legacy `Action<double>` max-scroll callback plus `LastScrollFeedback` side-channel | Runtime still consumes a positional max-scroll double; typed feedback is not a promoted contract. |
| Local diagnostics | `LastViewport`, `LastLayoutViewport`, layout rebuild count/reason, dirty classifications, `LastScrollFeedback` | Useful as local diagnostics, but not a global diagnostics channel. |
| App isolation | No direct Counter messages or input router dependency inside translator | Mostly satisfied; style and feedback remain sample-shaped. |

## 3. Draft Contract Names

These names are design vocabulary only. Do not implement them in this line.

| Draft contract | Boundary | Notes |
|----------------|----------|-------|
| `TranslatorStyleSource` | Provides `RenderStylePreset` or equivalent style inputs for render-pipeline creation. | Must replace hidden `CounterStylePreset.Default` before promotion. |
| `TranslatorViewportSource` | Provides physical-pixel `PixelRectangle` viewport and names resize timing/fallback behavior. | Current `Func<PixelRectangle>` is a precursor, not a contract. |
| `TranslatorRetainedTreeOwner` | Owns patch application and retained-tree lifetime, or explicitly delegates both to the translator. | Promotion must decide whether the bridge owns retained state. |
| `RenderPipelineFactory` | Creates `RenderPipeline` with explicit style/options. | Must avoid hidden Counter defaults. |
| `FramePreparationHook` | Names backend/resource preparation before layout/render build. | Keep only if resize/backend lifetime needs a pre-frame hook. |
| `TranslatorFrameFeedback` | Typed post-frame result carrying layout diagnostics and scroll feedback. | Should include `ScrollFeedback`, viewport, layout rebuild reason, dirty classifications, and future layout diagnostics. |
| `TranslatorDiagnosticsSnapshot` | Optional local snapshot for tests/debug readers. | Must remain local; do not introduce a global diagnostics channel. |

Naming sufficiency check:

| Required boundary | Chosen name | Status |
|-------------------|-------------|--------|
| Style source | `TranslatorStyleSource` | Clear enough for design; implementation still blocked by hidden `CounterStylePreset.Default`. |
| Viewport source | `TranslatorViewportSource` | Clear enough for design; implementation must still define pixel units, resize timing, and fallback behavior. |
| Retained-tree owner | `TranslatorRetainedTreeOwner` | Clear enough for design; ownership decision remains blocked. |
| Render-pipeline creation | `RenderPipelineFactory` | Clear enough for design; implementation must remove hidden pipeline construction. |
| Post-frame feedback | `TranslatorFrameFeedback` | Clear enough for design; implementation must preserve legacy callback until migration is explicit. |

## 4. Promotion Gates

| Gate | Requirement | Current status | Decision |
|------|-------------|----------------|----------|
| Style source | Style is injected or resolved outside the translator. | Blocked: translator constructs `RenderPipeline(CounterStylePreset.Default)`. | Do not promote. |
| Viewport source | Physical-pixel source contract names resize timing and fallback behavior. | Partial: optional viewport provider exists but is only a delegate. | Draft contract first. |
| Retained-tree ownership | Patch application and retained-tree lifetime are owned by an explicit component. | Blocked: private `RetainedTree` is embedded. | Do not move. |
| Render-pipeline creation | Pipeline creation uses explicit style/options/factory. | Blocked: private pipeline creation hides Counter defaults. | Do not move. |
| Prepare-frame semantics | Pre-frame hook has named backend/resource responsibilities or is removed. | Partial: optional `Action` exists without semantic name. | Draft contract first. |
| Post-frame feedback | Feedback is typed and can carry layout diagnostics plus scroll metrics. | Partial: `LastScrollFeedback` side-channel exists; legacy `Action<double>` still drives runtime. | Keep side-channel; do not replace callback. |
| Scroll metrics | Container id, viewport extent, content extent, and max scroll are named. | Mostly satisfied for v0 side-channel via `ScrollFeedback` / `ScrollContainerMetrics`. | Regression-only unless tests fail. |
| Diagnostics exposure | Local diagnostics are either exposed through typed feedback or remain local accessors. | Partial: local accessors exist; no typed frame feedback record. | Do not create global channel. |
| App isolation | Translator has no Counter messages, input router, or app command dependency. | Mostly satisfied, except style and feedback are sample-shaped. | Keep in PoC. |

## 5. Why It Stays In Irix.Poc

`WindowDrawCommandTranslator` remains in `Irix.Poc` because the bridge still has unresolved framework contracts:

- Style source is sample-shaped through `CounterStylePreset.Default`.
- Viewport source is a delegate/window fallback, not a named physical-pixel source contract.
- Retained tree ownership is embedded privately, so patch application lifetime is not a framework decision yet.
- Render-pipeline creation is private and carries hidden style defaults.
- Post-frame feedback is split between legacy `Action<double>` and `LastScrollFeedback`; no promoted typed feedback record exists.
- Prepare-frame semantics are unnamed and may represent backend/resource lifetime details.
- Local diagnostics are useful for tests/debugging, but they are not a diagnostics channel.

Moving the translator before these contracts are explicit would promote PoC composition policy as framework API.

## 6. Promotion Precondition Order

Future implementation should follow this order:

1. Preserve feedback compatibility with tests for `LastScrollFeedback`, `LastMaxScrollY`, and legacy max-scroll callback alignment.
2. Define `TranslatorStyleSource` and remove hidden Counter style construction from the bridge.
3. Define `TranslatorViewportSource` with physical-pixel units, resize timing, and fallback semantics.
4. Decide `TranslatorRetainedTreeOwner`: embedded bridge ownership or external render service ownership.
5. Introduce a `RenderPipelineFactory` or equivalent explicit pipeline creation contract.
6. Define `FramePreparationHook` semantics or remove the hook from the promoted shape.
7. Define `TranslatorFrameFeedback` with scroll feedback and local layout diagnostics.
8. Only then reconsider moving `WindowDrawCommandTranslator` out of `Irix.Poc`.

## 7. Allowed Next Work

Allowed:

- Refine this contract vocabulary in documentation.
- Add focused tests that prove existing `LastScrollFeedback`, `LastMaxScrollY`, and legacy callback values do not diverge.
- Add tests for viewport source / layout diagnostics if they do not change behavior.

Not allowed in this line:

- Moving `WindowDrawCommandTranslator`.
- Adding framework interfaces or public APIs.
- Replacing `Action<double>` with typed feedback.
- Moving scroll controller/state/pump.
- Starting typed id wrappers.
- Enabling StyleOnly fast-path.
- Adding unified diagnostics channel / event bus / registry.

## 8. Completion Standard

This line is complete when:

- Promotion gates are documented as blocked / partial / mostly satisfied.
- Style source, viewport source, retained-tree owner, render-pipeline creation, and post-frame feedback names are recorded.
- Existing runtime behavior is unchanged.
- `WindowDrawCommandTranslator` remains in `Irix.Poc`.
- Scroll scheduler/message flow remains unchanged.

Current status: complete as design contract prep. Future work must reopen this line explicitly before adding framework APIs or moving code.

Guardrail: this document does not imply runtime, API, or file-move permission.
