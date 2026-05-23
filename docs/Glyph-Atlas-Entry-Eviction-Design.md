# Glyph Atlas Entry Eviction Design

Scope: design-only follow-up for POST-011. Do not implement entry-level LRU or a sub-rect free-list until retained atlas command ownership is explicit and the current page-level reuse policy remains stable under the fixed regression/soak lane.

## Current Policy

- Page pool: bounded 48 pages, split by `Alpha` and `Bgra` format.
- Reuse unit: full atlas page.
- Reuse gate: format-scoped cold page, current-record reuse only if the page was not touched by the current record, otherwise retained-floor-gated next-record reuse.
- Mutation safety: failed text runs roll back newly cached glyph entries, page packing state, dirty state, and pages created during the run.
- Diagnostics: resident CPU/upload/GPU bytes, used/fragmented pixels, page ages, pending/completed reuse, reuse requests, and full-without-reuse counters are observable.

## Entry Eviction Preconditions

- Retained atlas command ownership must expose the oldest retained atlas record serial. If retained command caching is added, page/entry reuse must use that retained floor, not the current record serial.
- Glyph entries must have an explicit owner/lifetime state: live, evicting, reusable, or stale. A generation check alone is not enough for sub-rect reuse because stale UVs can target a newly packed glyph within the same page.
- The packer must expose a sub-rect allocator/free-list with coalescing and fragmentation metrics before entry-level LRU can safely free rectangles.
- Cache lookup must reject evicting/stale entries before draw-batch construction, and draw batches must bind page/entry generations that are validated at record time.
- Rollback must restore both entry state and sub-rect allocator state for entries allocated during a failed run.

## Deferred Implementation Shape

- Keep page-level cold reuse as the first safety valve.
- Add an entry metadata table with `LastUsedSerial`, `GlyphAtlasEntryHandle`, page handle, rectangle, format, and state.
- Add a per-format LRU candidate scan that ignores entries touched by the current record or retained floor.
- Free entry rectangles into a page-local free-list only after no retained draw batch can reference them.
- Reuse freed rectangles before allocating a new shelf row; coalesce adjacent free rectangles only within the same row until a stronger allocator is justified.
- Keep full-page generation bumps for page reuse; entry-level reuse needs entry-generation bumps and stale-entry cleanup without page-generation bumps.

## Non-goals

- Do not add same-record reuse of entries that were touched by the current record.
- Do not add retained atlas command caching as part of eviction.
- Do not change the renderer coverage surface during the coverage freeze.
- Do not use D3D11On12, Direct2D final composition, or overlay fallback to hide eviction failures.
