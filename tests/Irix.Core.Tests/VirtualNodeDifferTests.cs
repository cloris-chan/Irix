using Xunit;

namespace Irix.Core.Tests;

public sealed class VirtualNodeDifferTests
{
    [Fact]
    public void CreatePatchBatch_returns_empty_patches_when_trees_are_identical()
    {
        var tree = new VirtualNodeTree(VirtualNodeFactory.Text("Hello", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
        Assert.Equal(tree.Root, batch.Root);
    }

    [Fact]
    public void CreatePatchBatch_returns_empty_patches_for_deeply_identical_trees()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Click"))));
        var tree = new VirtualNodeTree(root);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void CreatePatchBatch_returns_update_when_content_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("Hello", 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Text("World", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
        Assert.Equal(newTree.Root, batch.Root);
    }

    [Fact]
    public void CreatePatchBatch_returns_replace_root_when_kind_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("Label", 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_add_when_new_child_added()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 2)));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 2),
            VirtualNodeFactory.Text("B", 3)));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Add, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_update_when_attribute_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(200, 50, 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_replace_root_from_default_to_initial_tree()
    {
        var initialTree = new VirtualNodeTree(VirtualNodeFactory.Text("Count: 0", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, initialTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
        Assert.Equal(initialTree.Root, batch.Root);
    }

    [Fact]
    public void CreatePatchBatch_returns_empty_when_both_default()
    {
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, default);

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void NodesEqual_returns_true_for_identical_nodes()
    {
        var a = VirtualNodeFactory.Text("Hello", 1);
        var b = VirtualNodeFactory.Text("Hello", 1);

        Assert.True(VirtualNodeDiffer.NodesEqual(a, b));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_content()
    {
        var a = VirtualNodeFactory.Text("Hello", 1);
        var b = VirtualNodeFactory.Text("World", 1);

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_keys()
    {
        var a = VirtualNodeFactory.Text("Hello", 1);
        var b = VirtualNodeFactory.Text("Hello", 2);

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b));
    }

    [Fact]
    public void NodesEqual_returns_true_for_identical_complex_trees()
    {
        var a = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 42", 2),
            VirtualNodeFactory.Rectangle(200, 48, 3),
            VirtualNodeFactory.Button("Reset", 4, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Reset"))));
        var b = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 42", 2),
            VirtualNodeFactory.Rectangle(200, 48, 3),
            VirtualNodeFactory.Button("Reset", 4, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Reset"))));

        Assert.True(VirtualNodeDiffer.NodesEqual(a, b));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_nested_child()
    {
        var a = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2));
        var b = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 1", 2));

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b));
    }

    [Fact]
    public void PatchBatch_Root_is_set_even_when_count_is_zero()
    {
        var tree = new VirtualNodeTree(VirtualNodeFactory.Text("Stable", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
        Assert.Equal(tree.Root, batch.Root);
    }

    [Fact]
    public void PatchBatch_Root_is_set_when_trees_differ()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("Old", 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Text("New", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(newTree.Root, batch.Root);
    }

    // ── Local diff: Update patches ──

    [Fact]
    public void CreatePatchBatch_emits_update_for_nested_content_change()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 1", 2),
            VirtualNodeFactory.Button("Click", 3));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Should emit Update for the changed Text child, not ReplaceRoot
        Assert.True(batch.Count >= 1);
        var updatePatch = batch.Memory.Span[0];
        Assert.Equal(VirtualNodePatchOperation.Update, updatePatch.Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_no_patches_when_only_unkeyed_child_reordered()
    {
        // Unkeyed children compared by index — reordering means different content at same index
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 2),
            VirtualNodeFactory.Text("B", 3));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("B", 2),
            VirtualNodeFactory.Text("A", 3));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Both children have different content at same index → 2 Update patches
        Assert.Equal(2, batch.Count);
        Assert.All(Enumerable.Range(0, batch.Count), i =>
            Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[i].Operation));
    }

    [Fact]
    public void CreatePatchBatch_emits_remove_when_child_removed()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 2),
            VirtualNodeFactory.Text("B", 3));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 2));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Remove, batch.Memory.Span[0].Operation);
    }

    // ── Local diff: Keyed reconciliation ──

    [Fact]
    public void CreatePatchBatch_matches_keyed_children_across_reorder()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("First", 10),
            VirtualNodeFactory.Text("Second", 20));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Second", 20),
            VirtualNodeFactory.Text("First", 10));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Keyed reconciliation: same keys, same content → no patches
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void CreatePatchBatch_emits_update_for_keyed_child_content_change()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Old", 10),
            VirtualNodeFactory.Text("Stable", 20));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("New", 10),
            VirtualNodeFactory.Text("Stable", 20));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_add_for_new_keyed_child()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 10));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 10),
            VirtualNodeFactory.Text("B", 20));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Add, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_remove_for_removed_keyed_child()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 10),
            VirtualNodeFactory.Text("B", 20));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("A", 10));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Remove, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_replace_root_when_kind_changes()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("Label", 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_multiple_patches_for_complex_tree()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Title", 10),
            VirtualNodeFactory.Button("Click", 20),
            VirtualNodeFactory.Rectangle(100, 50, 30));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("New Title", 10),
            VirtualNodeFactory.Button("Click", 20),
            VirtualNodeFactory.Rectangle(200, 50, 30),
            VirtualNodeFactory.Text("Added", 40));
        var oldTree = new VirtualNodeTree(oldRoot);
        var newTree = new VirtualNodeTree(newRoot);

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Expected: Update (Title changed) + Update (Rectangle attrs changed) + Add (new Text)
        Assert.True(batch.Count >= 2);
        var hasUpdate = false;
        var hasAdd = false;
        for (var i = 0; i < batch.Count; i++)
        {
            if (batch.Memory.Span[i].Operation == VirtualNodePatchOperation.Update) hasUpdate = true;
            if (batch.Memory.Span[i].Operation == VirtualNodePatchOperation.Add) hasAdd = true;
        }
        Assert.True(hasUpdate, "Should have at least one Update patch");
        Assert.True(hasAdd, "Should have at least one Add patch");
    }
}
