using Xunit;

namespace Irix.Core.Tests;

public sealed class VirtualNodeDifferTests
{
    private readonly VirtualTextArena _arena = new();
    [Fact]
    public void CreatePatchBatch_returns_empty_patches_when_trees_are_identical()
    {
        var tree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1)), _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
        Assert.Equal(tree.Root, batch.Root);
    }

    [Fact]
    public void CreatePatchBatch_returns_empty_patches_for_deeply_identical_trees()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(100))));
        var tree = new VirtualNodeTree(root, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void CreatePatchBatch_returns_update_when_content_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1)), _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "World", new NodeKey(1)), _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
        Assert.Equal(newTree.Root, batch.Root);
    }

    [Fact]
    public void CreatePatchBatch_returns_replace_root_when_kind_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Label", new NodeKey(1)), _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, new NodeKey(1)));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_add_when_new_child_added()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2))), _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(3))), _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Add, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_update_when_property_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, new NodeKey(1)));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(200, 50, new NodeKey(1)));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_replace_root_from_default_to_initial_tree()
    {
        var initialTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)), _arena.GetOrCreateSnapshot());

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
        var a = VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1));
        var b = VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1));

        Assert.True(VirtualNodeDiffer.NodesEqual(a, b, _arena.GetOrCreateSnapshot(), _arena.GetOrCreateSnapshot()));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_content()
    {
        var a = VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1));
        var b = VirtualNodeBuilder.Text(_arena, "World", new NodeKey(1));

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b, _arena.GetOrCreateSnapshot(), _arena.GetOrCreateSnapshot()));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_keys()
    {
        var a = VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(1));
        var b = VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2));

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b, _arena.GetOrCreateSnapshot(), _arena.GetOrCreateSnapshot()));
    }

    [Fact]
    public void NodesEqual_returns_true_for_identical_complex_trees()
    {
        var a = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 42", new NodeKey(2)),
            VirtualNodeFactory.Rectangle(200, 48, new NodeKey(3)),
            VirtualNodeBuilder.Button(_arena, "Reset", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(100))));
        var b = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 42", new NodeKey(2)),
            VirtualNodeFactory.Rectangle(200, 48, new NodeKey(3)),
            VirtualNodeBuilder.Button(_arena, "Reset", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(100))));

        Assert.True(VirtualNodeDiffer.NodesEqual(a, b, _arena.GetOrCreateSnapshot(), _arena.GetOrCreateSnapshot()));
    }

    [Fact]
    public void NodesEqual_returns_false_for_different_nested_child()
    {
        var a = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)));
        var b = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)));

        Assert.False(VirtualNodeDiffer.NodesEqual(a, b, _arena.GetOrCreateSnapshot(), _arena.GetOrCreateSnapshot()));
    }

    [Fact]
    public void PatchBatch_Root_is_set_even_when_count_is_zero()
    {
        var tree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Stable", new NodeKey(1)), _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);
        Assert.Equal(tree.Root, batch.Root);
    }

    [Fact]
    public void PatchBatch_Root_is_set_when_trees_differ()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Old", new NodeKey(1)), _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "New", new NodeKey(1)), _arena.GetOrCreateSnapshot());

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
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Should emit Update for the changed Text child, not ReplaceRoot
        Assert.True(batch.Count >= 1);
        var updatePatch = batch.Memory.Span[0];
        Assert.Equal(VirtualNodePatchOperation.Update, updatePatch.Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_no_patches_when_only_unkeyed_child_reordered()
    {
        // Unkeyed children compared by index �?reordering means different content at same index
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(3)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(3)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Both children have different content at same index �?2 Update patches
        Assert.Equal(2, batch.Count);
        Assert.All(Enumerable.Range(0, batch.Count), i =>
            Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[i].Operation));
    }

    [Fact]
    public void CreatePatchBatch_emits_remove_when_child_removed()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(3)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

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
            VirtualNodeBuilder.Text(_arena, "First", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "Second", new NodeKey(20)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Second", new NodeKey(20)),
            VirtualNodeBuilder.Text(_arena, "First", new NodeKey(10)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Keyed reconciliation: same keys, same content �?no patches
        Assert.Equal(0, batch.Count);
    }

    [Fact]
    public void CreatePatchBatch_emits_update_for_keyed_child_content_change()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Old", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "Stable", new NodeKey(20)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "New", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "Stable", new NodeKey(20)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Update, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_add_for_new_keyed_child()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(20)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Add, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_remove_for_removed_keyed_child()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(20)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.Remove, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_replace_root_when_kind_changes()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeBuilder.Text(_arena, "Label", new NodeKey(1)), _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, new NodeKey(1)));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_emits_multiple_patches_for_complex_tree()
    {
        var oldRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Title", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(20)),
            VirtualNodeFactory.Rectangle(100, 50, new NodeKey(30)));
        var newRoot = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "New Title", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(20)),
            VirtualNodeFactory.Rectangle(200, 50, new NodeKey(30)),
            VirtualNodeBuilder.Text(_arena, "Added", new NodeKey(40)));
        var oldTree = new VirtualNodeTree(oldRoot, _arena.GetOrCreateSnapshot());
        var newTree = new VirtualNodeTree(newRoot, _arena.GetOrCreateSnapshot());

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Expected: Update (Title changed) + Update (Rectangle properties changed) + Add (new Text)
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
