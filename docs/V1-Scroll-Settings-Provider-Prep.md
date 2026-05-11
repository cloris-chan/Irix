# v1 Scroll Settings Provider Prep

> Design-only boundary draft for future `SystemScrollSettings` provider ownership. This document does not wire a provider into `ScrollController`, change scroll delta conversion, move scroll state/controller/pump, or extract scroll from `Irix.Poc`.

## 1. Scope

Current shape:

- `SystemScrollSettings` lives in `Irix.Poc` and has `LinesPerWheelNotch` plus `WheelUnitsPerNotch`.
- `SystemScrollSettings.Default` remains `LinesPerWheelNotch = 3` and `WheelUnitsPerNotch = 120`.
- `ScrollController.ConvertToPixels` already receives settings explicitly, so the future provider can be introduced without changing the controller math.
- Horizontal wheel chars are not part of the current vertical scroll runtime, but the provider boundary should name them before pure controller extraction.

Current decision:

- Continue postponed for runtime wiring.
- If implementation is reopened, first add only a fallback-only internal provider returning `SystemScrollSettings.Default`.
- Do not read Win32 settings before the provider shape has tests and a platform ownership decision.

Non-goals:

- Do not connect a provider to `CounterApplication`, `Program`, or `ScrollController`.
- Do not change wheel delta direction, pixel conversion, line extent, page extent, or accumulator behavior.
- Do not move `ScrollController`, `ScrollState`, or `ScrollFramePump`.
- Do not introduce platform-specific runtime behavior in this line.

## 2. Draft Boundary Names

| Draft concept | Meaning | Current status |
|---------------|---------|----------------|
| `SystemScrollSettingsProvider` | Platform-neutral provider that returns effective wheel settings for the current host. | Design only. |
| `WindowsScrollSettingsSource` | Windows implementation that reads OS mouse wheel preferences. | Design only; no Win32 call added. |
| `FallbackScrollSettings` | Stable defaults used when platform settings are unavailable or unsupported. | Current defaults are already used directly in PoC code. |
| `EffectiveScrollSettings` | Normalized settings handed to scroll conversion after fallback and validation. | Design only; current record can remain the payload shape. |

## 3. Windows Source Rules

Future Windows provider ownership:

- `LinesPerWheelNotch` comes from the Windows vertical wheel setting, currently represented by `SPI_GETWHEELSCROLLLINES`.
- Future horizontal scroll support should read wheel chars from `SPI_GETWHEELSCROLLCHARS` and keep it separate from vertical line settings.
- `WheelUnitsPerNotch` remains the wheel delta unit size, currently `120`; it is not a user preference.
- Page-scroll sentinel values must be represented explicitly in a future contract instead of being squeezed into line count arithmetic.
- Failed, unavailable, or unsupported platform reads must return fallback settings without changing current behavior.

## 4. Fallback Rules

| Setting | Fallback | Notes |
|---------|----------|-------|
| Vertical lines per wheel notch | `3` | Matches current `SystemScrollSettings.Default`. |
| Wheel units per notch | `120` | Matches current raw wheel delta convention. |
| Horizontal chars per wheel notch | `3` when horizontal scrolling is introduced | Design placeholder only; not used by current vertical scroll path. |
| Page scroll mode | Preserve as explicit mode in a future contract | Do not silently convert page-scroll sentinel values to a large line count. |

Non-Windows behavior:

- Return fallback settings until a platform-specific source exists.
- Keep behavior deterministic for tests and headless diagnostics.
- Do not make scroll conversion depend on host availability during this design line.

## 5. Extraction Gate

This design is a precondition for pure controller extraction, not an extraction itself.

Before moving scroll controller code, future work must decide:

1. Whether `SystemScrollSettings` remains the public payload or becomes `EffectiveScrollSettings`.
2. How page-scroll mode is represented.
3. Where horizontal wheel chars live once horizontal scrolling exists.
4. Which platform layer owns Windows setting reads.
5. How tests inject deterministic fallback settings.

Until those decisions are made, current PoC code should keep passing `SystemScrollSettings.Default` explicitly.

## 6. Implementation Prep Decision

Decision: keep runtime postponed, but the first safe implementation is a fallback-only internal provider.

| Option | Decision | Reason |
|--------|----------|--------|
| Keep direct `SystemScrollSettings.Default` usage | Current runtime behavior | It is explicit, deterministic, and already tested. |
| Add fallback-only internal provider | First safe implementation when reopened | It can prove injection/testing shape without Win32 reads or delta changes. |
| Read Windows settings now | Postponed | It would introduce host-dependent behavior before provider ownership and page-scroll semantics are settled. |
| Connect provider to controller/runtime now | Postponed | It would change the runtime wiring surface without a need for current v1 guardrails. |

The fallback-only provider should return the exact current defaults and should be tested for equivalence before any runtime call site is changed.
