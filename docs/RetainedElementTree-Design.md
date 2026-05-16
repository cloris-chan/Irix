# RetainedElementTree Design Draft

> Status: **Draft** — not yet implemented. Defines the contract for true local patch apply.

## Problem

`VirtualNodeDiffer.CreatePatchBatch(prev, next)` produces a `PatchBatch` containing:

- `Root` — the full new `VirtualNode` tree (always present)
- `Memory<VirtualNodePatch>` — granular patches (`Update`, `Add`, `Remove`, `ReplaceRoot`)

Today consumers ignore the patches and rebuild from `PatchBatch.Root` every frame. This is correct but wasteful — the entire downstream element tree is reconstructed even for small property changes.

## Goal

`RetainedElementTree.Apply(PatchBatch)` mutates a persistent element tree in-place using the patches, falling back to full rebuild from `Root` only when necessary.

## Input

```csharp
public sealed class RetainedElementTree
{
    public RetainedElement Root { get; }

    public ApplyResult Apply(PatchBatch batch);
}
```

`PatchBatch` carries both the patches and the `Root` — the retained tree can always fall back to `Root` rebuild.

## Retained Element Structure

```csharp
public sealed class RetainedElement
{
    public VirtualNodeKind Kind { get; set; }
    public ulong Key { get; set; }
    public NodeContent Content { get; set; }
    public VirtualNodeProperty[] Properties { get; set; }
    public List<RetainedElement> Children { get; }
    public RetainedElement? Parent { get; }
}
```

A mutable mirror of `VirtualNode`. Parent back-references enable efficient child removal/reordering.

## Index Semantics

`VirtualNodePatch.NodeIndex` is a **depth-first pre-order index** into the *previous* tree at diff time.

Example tree with DFS indices:

```
         0 (root)
        / \
       1    4
      / \    \
     2   3    5
```

- `NodeIndex = 0` → root
- `NodeIndex = 3` → root's first child's second child

The retained tree maintains the same index mapping. Apply walks the retained tree by DFS to locate the target node.

### Keyed Nodes

When a node has `Key != 0`, the differ uses key-based reconciliation (matching old↔new by key). The patch's `NodeIndex` still refers to the DFS position in the *previous* tree, but the actual match is by key. The retained tree should prefer key lookup over DFS walk when keys are present.

## Patch Operations

### `Update`

- Target: node at `NodeIndex`
- Action: replace `Content` and `Properties` on the retained node
- Children: untouched
- Failure: if node not found → fallback to full rebuild

### `ReplaceRoot`

- Target: root node (index 0) or a subtree root
- Action: replace the entire subtree at `NodeIndex` with `patch.Node` (reconstruct retained elements from the new `VirtualNode`)
- Failure: always succeeds (it's a full subtree replacement)

### `Add`

- Target: parent at `NodeIndex`, insert `patch.Node` as a new child
- Action: create a new `RetainedElement` from `patch.Node`, append to parent's `Children`
- Insert position: determined by DFS order — insert at the correct index among siblings
- Failure: if parent not found → fallback to full rebuild

### `Remove`

- Target: node at `NodeIndex`
- Action: detach from parent's `Children` list, dispose
- Failure: if node not found → fallback to full rebuild

### `Move` (future)

- Reserved for keyed reconciliation with reordering
- Not emitted by current `VirtualNodeDiffer`

## Apply Algorithm

```
Apply(PatchBatch batch):
    if batch.Count == 0:
        // No patches — check if Root is structurally identical
        // (differ may emit 0 patches when trees are equal)
        return ApplyResult.NoChange

    for each patch in batch:
        match patch.Operation:
            ReplaceRoot when index == 0:
                Rebuild entire retained tree from batch.Root
                return ApplyResult.FullRebuild

            ReplaceRoot when index > 0:
                node = FindByDfsIndex(index)
                if node == null: → Fallback(batch.Root)
                replacement = BuildRetained(patch.Node)
                ReplaceInParent(node, replacement)

            Update:
                node = FindByDfsIndex(index)
                if node == null: → Fallback(batch.Root)
                node.Content = patch.Node.Content
                node.Properties = patch.Node.Properties

            Add:
                parent = FindByDfsIndex(index)
                if parent == null: → Fallback(batch.Root)
                child = BuildRetained(patch.Node)
                InsertChildAtCorrectPosition(parent, child, index)

            Remove:
                node = FindByDfsIndex(index)
                if node == null: → Fallback(batch.Root)
                node.Parent.Children.Remove(node)

    return ApplyResult.Patched

Fallback(VirtualNode newRoot):
    Rebuild entire retained tree from newRoot
    return ApplyResult.FullRebuild
```

## Failure / Fallback

Any patch that cannot be applied (node not found, index out of range, structural mismatch) triggers a **full rebuild** from `PatchBatch.Root`. This is the safety net — correctness over performance.

The caller does not need to distinguish `Patched` from `FullRebuild` for correctness, only for metrics/logging.

```csharp
public enum ApplyResult
{
    NoChange,
    Patched,
    FullRebuild
}
```

## DFS Index Walker

```csharp
private RetainedElement? FindByDfsIndex(int targetIndex)
{
    var current = 0;
    return DfsWalk(Root, targetIndex, ref current);
}

private RetainedElement? DfsWalk(RetainedElement node, int target, ref int current)
{
    if (current == target) return node;
    current++;
    foreach (var child in node.Children)
    {
        var found = DfsWalk(child, target, ref current);
        if (found != null) return found;
    }
    return null;
}
```

## Open Questions

1. **Keyed index optimization**: For heavily keyed trees, should `RetainedElement` maintain a `Dictionary<ulong, RetainedElement>` for O(1) key lookup? Trade-off: memory + maintenance cost vs. DFS walk cost.

2. **Batch atomicity**: If patch N fails, should we roll back patches 0..N-1, or just rebuild from Root? Current design: rebuild from Root (patches are not reversible).

3. **Thread safety**: `RetainedElementTree.Apply` is expected to be called from a single compositor thread. No locking needed.

4. **Memory pooling**: `RetainedElement` instances from removed subtrees could be pooled for reuse. Defer to implementation phase.

## Implementation Phases

| Phase | Scope |
|-------|-------|
| Phase 1 | `RetainedElementTree` + `Apply` with full rebuild only (patches ignored, validates the API shape) |
| Phase 2 | `Update` + `ReplaceRoot` local apply (covers ~90% of real patches) |
| Phase 3 | `Add` + `Remove` with DFS walker |
| Phase 4 | Keyed index, `Move` operation, pooling |
