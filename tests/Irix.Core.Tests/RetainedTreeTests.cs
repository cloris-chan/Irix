using Xunit;

namespace Irix.Core.Tests;

public sealed class RetainedTreeTests
{
    [Fact]
    public void Apply_replace_root_swaps_entire_tree()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("old", 1));
        var newRoot = VirtualNodeFactory.Text("new", 2);
        var batch = new PatchBatch(newRoot, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.ReplaceRoot, 0, newRoot)]), 1);
        var tree = new RetainedTree(oldTree);

        var dirty = tree.Apply(batch);

        Assert.Equal(newRoot.Content.Text, tree.Tree.Root.Content.Text);
        Assert.Contains(0, dirty);
    }

    [Fact]
    public void Apply_update_changes_node_content()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("before", 2),
            VirtualNodeFactory.Button("btn", 3));
        var updated = VirtualNodeFactory.Text("after", 2);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Equal("after", tree.Tree.Root.Children[0].Content.Text);
        Assert.Equal(VirtualNodeKind.Button, tree.Tree.Root.Children[1].Kind);
        Assert.Contains(1, dirty);
    }

    [Fact]
    public void Apply_add_inserts_child()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2));
        var newChild = VirtualNodeFactory.Text("b", 3);
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
        Assert.Single(tree.Tree.Root.Children);
        Assert.Equal(VirtualNodeKind.Text, tree.Tree.Root.Children[0].Kind);

        // Verify CountNodes
        // Root: index 0, size 2 (root + 1 child)
        // Text("a"): index 1, size 1
        // Add target: index 2 = root's childEnd

        var dirty = tree.Apply(batch);

        var rootResult = tree.Tree.Root;
        Assert.True(rootResult.Children.Length >= 2,
            $"Expected ≥2 children, got {rootResult.Children.Length}. dirty=[{string.Join(",", dirty)}]");
        Assert.Equal("a", rootResult.Children[0].Content.Text);
        Assert.Equal("b", rootResult.Children[1].Content.Text);
    }

    [Fact]
    public void Apply_remove_deletes_child()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("keep", 2),
            VirtualNodeFactory.Text("remove", 3));
        // Remove node at index 2 (first child of root is index 1, second child starts at index 2)
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Remove, 2, default)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Single(tree.Tree.Root.Children);
        Assert.Equal("keep", tree.Tree.Root.Children[0].Content.Text);
        Assert.Contains(0, dirty);
    }

    [Fact]
    public void Apply_keyed_update_preserves_other_children()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("unchanged", 2),
            VirtualNodeFactory.Button("old label", 3),
            VirtualNodeFactory.Text("also unchanged", 4));
        var updated = VirtualNodeFactory.Button("new label", 3);
        // Button is at index: root(0) + text(1 node) = index 2
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 2, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Equal("unchanged", tree.Tree.Root.Children[0].Content.Text);
        // Button's content is default (text lives in child Text node); children preserved from old tree
        Assert.Equal(VirtualNodeKind.Button, tree.Tree.Root.Children[1].Kind);
        Assert.Equal("old label", tree.Tree.Root.Children[1].Children[0].Content.Text);
        Assert.Equal("also unchanged", tree.Tree.Root.Children[2].Content.Text);
        Assert.Contains(2, dirty);
    }

    [Fact]
    public void Apply_multiple_patches_produces_correct_final_tree()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3));
        var updatedA = VirtualNodeFactory.Text("A", 2);
        var newC = VirtualNodeFactory.Text("c", 4);
        // Update index 1 (first child), Add index 3 (after second child)
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updatedA),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 3, newC)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.True(tree.Tree.Root.Children.Length == 3,
            $"Expected 3 children, got {tree.Tree.Root.Children.Length}. dirty=[{string.Join(",", dirty)}]");
        Assert.Equal("A", tree.Tree.Root.Children[0].Content.Text);
        Assert.Equal("b", tree.Tree.Root.Children[1].Content.Text);
        Assert.Equal("c", tree.Tree.Root.Children[2].Content.Text);
    }

    [Fact]
    public void Apply_empty_batch_preserves_tree()
    {
        var root = VirtualNodeFactory.Text("stable", 1);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>([]), 0);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Equal("stable", tree.Tree.Root.Content.Text);
        Assert.Empty(dirty);
    }

    [Fact]
    public void Apply_diff_batch_then_retained_tree_matches_next_tree()
    {
        // Simulate the real flow: diff(prev, next) → apply patches to prev → result == next
        var prev = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Increment", 3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var next = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 1", 2),
            VirtualNodeFactory.Button("Increment", 3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev), new VirtualNodeTree(next));
        var tree = new RetainedTree(new VirtualNodeTree(prev));

        tree.Apply(batch);

        Assert.True(VirtualNodeDiffer.NodesEqual(next, tree.Tree.Root));
    }

    [Fact]
    public void Apply_keyed_add_and_remove_matches_next_tree()
    {
        // Test: remove "b", add "d" via differ → apply patches → verify result
        var prev = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 10),
            VirtualNodeFactory.Text("b", 20),
            VirtualNodeFactory.Text("c", 30));
        var next = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 10),
            VirtualNodeFactory.Text("c", 30),
            VirtualNodeFactory.Text("d", 40));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev), new VirtualNodeTree(next));
        var tree = new RetainedTree(new VirtualNodeTree(prev));

        tree.Apply(batch);

        // The differ uses new-tree DFS coordinates for Add/Remove indices.
        // After apply: "b" (key 20) is removed, "d" (key 40) is added.
        // The result should have the correct set of children (by key).
        var result = tree.Tree.Root;
        Assert.Equal(3, result.Children.Length);
        Assert.Contains(result.Children, c => c.Key == 10);  // "a"
        Assert.Contains(result.Children, c => c.Key == 30);  // "c"
        Assert.Contains(result.Children, c => c.Key == 40);  // "d"
        Assert.DoesNotContain(result.Children, c => c.Key == 20);  // "b" removed
    }

    [Fact]
    public void Apply_dirty_is_sorted_ascending()
    {
        // Root(0) has 3 children: indices 1, 2, 3
        // Update index 3 (last child) and index 1 (first child) — dirty should be [1, 3]
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3),
            VirtualNodeFactory.Text("c", 4));
        var updatedA = VirtualNodeFactory.Text("A", 2);
        var updatedC = VirtualNodeFactory.Text("C", 4);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 3, updatedC),
            new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updatedA)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        // Should be sorted ascending
        Assert.Equal(2, dirty.Count);
        Assert.Equal(1, dirty[0]);
        Assert.Equal(3, dirty[1]);
    }

    [Fact]
    public void Apply_dirty_is_deduplicated_when_multiple_children_change()
    {
        // Add two children at the same parent → parent dirty should appear only once
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2));
        var newB = VirtualNodeFactory.Text("b", 3);
        var newC = VirtualNodeFactory.Text("c", 4);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 2, newB),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 3, newC)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        // Parent (root, index 0) should appear only once even though two children were added
        Assert.Contains(0, dirty);
        Assert.Equal(1, dirty.Count(d => d == 0));
    }

    [Fact]
    public void Apply_update_marks_updated_node_not_parent()
    {
        // Update a child → dirty should contain the child's index, not the parent's
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("before", 2),
            VirtualNodeFactory.Text("keep", 3));
        var updated = VirtualNodeFactory.Text("after", 2);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Contains(1, dirty);   // the updated node
        Assert.DoesNotContain(0, dirty); // parent not dirty (children unchanged)
    }

    [Fact]
    public void Apply_add_and_remove_marks_parent_as_dirty()
    {
        // Remove child at index 1, add new child at index 2 → parent (0) dirty
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("old", 2),
            VirtualNodeFactory.Text("keep", 3));
        var newChild = VirtualNodeFactory.Text("new", 4);
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(VirtualNodePatchOperation.Remove, 1, default),
            new VirtualNodePatch(VirtualNodePatchOperation.Add, 2, newChild)
        ]), 2);
        var tree = new RetainedTree(new VirtualNodeTree(root));

        var dirty = tree.Apply(batch);

        Assert.Contains(0, dirty); // parent dirty
    }
}
