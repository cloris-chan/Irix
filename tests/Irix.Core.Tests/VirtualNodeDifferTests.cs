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
    public void CreatePatchBatch_returns_replace_root_when_content_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Text("Hello", 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Text("World", 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
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
    public void CreatePatchBatch_returns_replace_root_when_children_differ()
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
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, batch.Memory.Span[0].Operation);
    }

    [Fact]
    public void CreatePatchBatch_returns_replace_root_when_attribute_differs()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(100, 50, 1));
        var newTree = new VirtualNodeTree(VirtualNodeFactory.Rectangle(200, 50, 1));

        using var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.Equal(1, batch.Count);
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
}
