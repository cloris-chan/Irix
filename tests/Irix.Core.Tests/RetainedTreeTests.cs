using Xunit;

namespace Irix.Core.Tests;

public sealed class RetainedTreeTests
{
    private readonly VirtualTextArena _arena = new();
    [Fact]
    public void Apply_replace_root_swaps_entire_tree()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "old", new NodeKey(1)));
        var newRoot = VirtualNodeBuilder.Text(_arena, "new", new NodeKey(2));
        var batch = new PatchBatch(newRoot, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, newRoot)]), 1);
        var tree = new RetainedTree(oldTree);

        var dirty = tree.Apply(batch).Dirty;

        Assert.Equal(ResolveNodeText(_arena, newRoot.Content), ResolveNodeText(_arena, tree.Tree.Root.Content));
        Assert.Contains(0, dirty);
    }

    [Fact]
    public void Apply_update_changes_node_content()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "before", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "btn", new NodeKey(3)));
        var updated = VirtualNodeBuilder.Text(_arena, "after", new NodeKey(2));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Equal("after", ResolveNodeText(_arena, tree.Tree.Root.Children[0].Content));
        Assert.Equal(VirtualNodeKind.Button, tree.Tree.Root.Children[1].Kind);
        Assert.Contains(1, dirty);
    }

    [Fact]
    public void Apply_add_inserts_child()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)));
        var newChild = VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Add, 2, newChild)]), 1);

        // Verify batch content directly
        Assert.Equal(1, batch.Count);
        var patch = batch.Memory.Span[0];
        Assert.Equal(VirtualNodePatchOperation.Add, patch.Operation);
        Assert.Equal(2, patch.NodeIndex);
        Assert.Equal(VirtualNodeKind.Text, patch.Node.Kind);

        // Verify tree structure
        var tree = new RetainedTree(new VirtualNodeTree(root));
        Assert.Equal(1, tree.Tree.Root.Children.Length);
        Assert.Equal(VirtualNodeKind.Text, tree.Tree.Root.Children[0].Kind);

        // Verify CountNodes
        // Root: index 0, size 2 (root + 1 child)
        // Text("a"): index 1, size 1
        // Add target: index 2 = root's childEnd

        var dirty = tree.Apply(batch).Dirty;

        var rootResult = tree.Tree.Root;
        Assert.True(rootResult.Children.Length >= 2,
            $"Expected �? children, got {rootResult.Children.Length}. dirty=[{string.Join(",", dirty)}]");
        Assert.Equal("a", ResolveNodeText(_arena, rootResult.Children[0].Content));
        Assert.Equal("b", ResolveNodeText(_arena, rootResult.Children[1].Content));
    }

    [Fact]
    public void Apply_remove_deletes_child()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "keep", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "remove", new NodeKey(3)));
        // Remove node at index 2 (first child of root is index 1, second child starts at index 2)
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Remove, 2, default)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Equal(1, tree.Tree.Root.Children.Length);
        Assert.Equal("keep", ResolveNodeText(_arena, tree.Tree.Root.Children[0].Content));
        Assert.Contains(0, dirty);
    }

    [Fact]
    public void Apply_keyed_update_preserves_other_children()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "unchanged", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "old label", new NodeKey(3)),
            VirtualNodeBuilder.Text(_arena, "also unchanged", new NodeKey(4)));
        var updated = VirtualNodeBuilder.Button(_arena, "new label", new NodeKey(3));
        // Button is at index: root(0) + text(1 node) = index 2
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 2, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Equal("unchanged", ResolveNodeText(_arena, tree.Tree.Root.Children[0].Content));
        // Button's content is default (text lives in child Text node); children preserved from old tree
        Assert.Equal(VirtualNodeKind.Button, tree.Tree.Root.Children[1].Kind);
        Assert.Equal("old label", ResolveNodeText(_arena, tree.Tree.Root.Children[1].Children[0].Content));
        Assert.Equal("also unchanged", ResolveNodeText(_arena, tree.Tree.Root.Children[2].Content));
        Assert.Contains(2, dirty);
    }

    [Fact]
    public void Apply_multiple_patches_produces_correct_final_tree()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)));
        var updatedA = VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2));
        var newC = VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4));
        // Update index 1 (first child), Add index 3 (after second child)
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updatedA),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 3, newC)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.True(tree.Tree.Root.Children.Length == 3,
            $"Expected 3 children, got {tree.Tree.Root.Children.Length}. dirty=[{string.Join(",", dirty)}]");
        Assert.Equal("A", ResolveNodeText(_arena, tree.Tree.Root.Children[0].Content));
        Assert.Equal("b", ResolveNodeText(_arena, tree.Tree.Root.Children[1].Content));
        Assert.Equal("c", ResolveNodeText(_arena, tree.Tree.Root.Children[2].Content));
    }

    [Fact]
    public void Apply_empty_batch_preserves_tree()
    {
        var root = VirtualNodeBuilder.Text(_arena, "stable", new NodeKey(1));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>([]), 0);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Equal("stable", ResolveNodeText(_arena, tree.Tree.Root.Content));
        Assert.Empty(dirty);
    }

    [Fact]
    public void Apply_zero_patch_canonical_batch_advances_root_and_snapshot()
    {
        var prev = VirtualNodeBuilder.Text(_arena, "same", new NodeKey(1));
        var prevSnapshot = _arena.GetOrCreateSnapshot();

        _arena.BeginFrame();
        var next = VirtualNodeBuilder.Text(_arena, "same", new NodeKey(1));
        var nextSnapshot = _arena.GetOrCreateSnapshot();

        using var batch = VirtualNodeDiffer.CreatePatchBatch(
            new VirtualNodeTree(prev, prevSnapshot),
            new VirtualNodeTree(next, nextSnapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, prevSnapshot));

        var result = tree.Apply(batch);

        Assert.True(batch.HasCanonicalRoot);
        Assert.Equal(0, batch.Count);
        Assert.Empty(result.Dirty);
        Assert.True(VirtualNodeStructuralComparer.Equals(next, tree.Tree.Root, nextSnapshot, tree.Tree.TextSnapshot));
        Assert.Equal(nextSnapshot, tree.Tree.TextSnapshot);
        Assert.Equal("same", tree.Tree.TextSnapshot.ResolveRequired(tree.Tree.Root.Content.TryGetText(out var text) ? text : default).ToString());
    }

    [Fact]
    public void Apply_manual_patch_batch_does_not_treat_root_as_canonical()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "before", new NodeKey(2)));
        var updated = VirtualNodeBuilder.Text(_arena, "after", new NodeKey(2));
        var misleadingRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(99),
            VirtualNodeBuilder.Text(_arena, "wrong", new NodeKey(100)));
        var snapshot = _arena.GetOrCreateSnapshot();
        var batch = new PatchBatch(misleadingRoot, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1, textSnapshot: snapshot);
        var tree = new RetainedTree(new VirtualNodeTree(root, snapshot));

        tree.Apply(batch);

        Assert.False(batch.HasCanonicalRoot);
        Assert.Equal(new NodeKey(1), tree.Tree.Root.Key);
        Assert.Equal(1, tree.Tree.Root.Children.Length);
        Assert.Equal("after", ResolveNodeText(_arena, tree.Tree.Root.Children[0].Content));
    }

    [Fact]
    public void Apply_diff_batch_then_retained_tree_matches_next_tree()
    {
        // Simulate the real flow: diff(prev, next) �?apply patches to prev �?result == next
        var prev = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));
        var next = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        var snapshot = _arena.GetOrCreateSnapshot();
        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev, snapshot), new VirtualNodeTree(next, snapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, snapshot));

        tree.Apply(batch);

        Assert.True(VirtualNodeStructuralComparer.Equals(next, tree.Tree.Root, snapshot, tree.Tree.TextSnapshot));
    }

    [Fact]
    public void Apply_keyed_add_and_remove_matches_next_tree()
    {
        // Test: remove "b", add "d" via differ �?apply patches �?verify result
        var prev = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(20)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(30)));
        var next = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(30)),
            VirtualNodeBuilder.Text(_arena, "d", new NodeKey(40)));

        var snapshot = _arena.GetOrCreateSnapshot();
        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev, snapshot), new VirtualNodeTree(next, snapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, snapshot));

        tree.Apply(batch);

        // The differ uses new-tree DFS coordinates for Add/Remove indices.
        // After apply: "b" (key 20) is removed, "d" (key 40) is added.
        // The result should have the correct set of children (by key).
        var result = tree.Tree.Root;
        Assert.Equal(3, result.Children.Length);
        Assert.True(ContainsChildKey(result.Children, new NodeKey(10)));  // "a"
        Assert.True(ContainsChildKey(result.Children, new NodeKey(30)));  // "c"
        Assert.True(ContainsChildKey(result.Children, new NodeKey(40)));  // "d"
        Assert.False(ContainsChildKey(result.Children, new NodeKey(20)));  // "b" removed
    }

    [Fact]
    public void Apply_dirty_is_sorted_ascending()
    {
        // Root(0) has 3 children: indices 1, 2, 3
        // Update index 3 (last child) and index 1 (first child) �?dirty should be [1, 3]
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4)));
        var updatedA = VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2));
        var updatedC = VirtualNodeBuilder.Text(_arena, "C", new NodeKey(4));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 3, updatedC),
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updatedA)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        // Should be sorted ascending
        Assert.Equal(2, dirty.Count);
        Assert.Equal(1, dirty[0]);
        Assert.Equal(3, dirty[1]);
    }

    [Fact]
    public void Apply_dirty_is_deduplicated_when_multiple_children_change()
    {
        // Add two children at the same parent �?parent dirty should appear only once
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)));
        var newB = VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3));
        var newC = VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 2, newB),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 3, newC)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        // Parent (root, index 0) should appear only once even though two children were added
        Assert.Contains(0, dirty);
        Assert.Equal(1, dirty.Count(d => d == 0));
    }

    [Fact]
    public void Apply_update_marks_updated_node_not_parent()
    {
        // Update a child �?dirty should contain the child's index, not the parent's
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "before", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "keep", new NodeKey(3)));
        var updated = VirtualNodeBuilder.Text(_arena, "after", new NodeKey(2));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Contains(1, dirty);   // the updated node
        Assert.DoesNotContain(0, dirty); // parent not dirty (children unchanged)
    }

    [Fact]
    public void Apply_add_and_remove_marks_parent_as_dirty()
    {
        // Remove child at index 1, add new child at index 2 �?parent (0) dirty
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "old", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "keep", new NodeKey(3)));
        var newChild = VirtualNodeBuilder.Text(_arena, "new", new NodeKey(4));
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Remove, 1, default),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 2, newChild)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch).Dirty;

        Assert.Contains(0, dirty); // parent dirty
    }

    private static string ResolveNodeText(VirtualTextArena arena, NodeContent content) =>
        content.TryGetText(out var tc) ? arena.ResolveRequired(tc).ToString() : "";

    private static bool ContainsChildKey(ReadOnlySpan<VirtualNode> children, NodeKey key)
    {
        foreach (var child in children)
        {
            if (child.Key == key)
            {
                return true;
            }
        }

        return false;
    }
}
