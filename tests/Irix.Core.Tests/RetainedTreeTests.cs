using Xunit;

namespace Irix.Core.Tests;

public sealed class RetainedTreeTests
{
    private readonly VirtualTextArena _arena = new();

    [Fact]
    public void Apply_rejects_non_canonical_non_empty_batch()
    {
        var root = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "before", new NodeKey(2)));
        var updated = VirtualNodeBuilder.Text(_arena, "after", new NodeKey(2));
        var batch = new PatchBatch(updated, new PatchMemoryOwner<VirtualNodePatch>(
            [new VirtualNodePatch(VirtualNodePatchOperation.Update, 1, updated)]), 1);
        var tree = new RetainedTree(new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));

        var ex = Assert.Throws<InvalidOperationException>(() => tree.Apply(batch));

        Assert.Contains("canonical diff batches", ex.Message);
        Assert.False(batch.HasCanonicalRoot);
        Assert.Equal(new NodeKey(1), tree.Tree.Root.Key);
    }

    [Fact]
    public void Apply_empty_non_canonical_batch_preserves_tree()
    {
        var root = VirtualNodeBuilder.Text(_arena, "stable", new NodeKey(1));
        var snapshot = _arena.GetOrCreateSnapshot();
        var batch = new PatchBatch(root, new PatchMemoryOwner<VirtualNodePatch>([]), 0, textSnapshot: snapshot);
        var tree = new RetainedTree(new VirtualNodeTree(root, snapshot));

        var result = tree.Apply(batch);

        Assert.False(batch.HasCanonicalRoot);
        Assert.Empty(result.Dirty);
        Assert.True(VirtualNodeStructuralComparer.Equals(root, tree.Tree.Root, snapshot, tree.Tree.TextSnapshot));
        Assert.Equal(snapshot, tree.Tree.TextSnapshot);
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
    public void Apply_diff_batch_then_retained_tree_matches_next_tree()
    {
        var prev = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeTestBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));
        var next = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeTestBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        var snapshot = _arena.GetOrCreateSnapshot();
        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev, snapshot), new VirtualNodeTree(next, snapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, snapshot));

        tree.Apply(batch);

        Assert.True(batch.HasCanonicalRoot);
        Assert.True(VirtualNodeStructuralComparer.Equals(next, tree.Tree.Root, snapshot, tree.Tree.TextSnapshot));
    }

    [Fact]
    public void Apply_diff_batch_uses_canonical_root_for_style_only_update()
    {
        var prev = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeTestBuilder.Button(
                _arena,
                "Increment",
                new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var prevSnapshot = _arena.GetOrCreateSnapshot();

        _arena.BeginFrame();
        var next = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeTestBuilder.Button(
                _arena,
                "Increment",
                new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));
        var nextSnapshot = _arena.GetOrCreateSnapshot();

        using var batch = VirtualNodeDiffer.CreatePatchBatch(
            new VirtualNodeTree(prev, prevSnapshot),
            new VirtualNodeTree(next, nextSnapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, prevSnapshot));

        var result = tree.Apply(batch);

        Assert.True(batch.HasCanonicalRoot);
        Assert.Equal([1], result.Dirty);
        Assert.True(VirtualNodeStructuralComparer.Equals(next, tree.Tree.Root, nextSnapshot, tree.Tree.TextSnapshot));
    }

    [Fact]
    public void Apply_keyed_add_and_remove_matches_next_tree_and_marks_parent_dirty()
    {
        var prev = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(20)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(30)));
        var next = VirtualNodeFactory.Container(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(30)),
            VirtualNodeBuilder.Text(_arena, "d", new NodeKey(40)));

        var snapshot = _arena.GetOrCreateSnapshot();
        using var batch = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(prev, snapshot), new VirtualNodeTree(next, snapshot));
        var tree = new RetainedTree(new VirtualNodeTree(prev, snapshot));

        var result = tree.Apply(batch);

        Assert.True(batch.HasCanonicalRoot);
        Assert.Equal([0], result.Dirty);
        Assert.True(VirtualNodeStructuralComparer.Equals(next, tree.Tree.Root, snapshot, tree.Tree.TextSnapshot));
        Assert.True(ContainsChildKey(tree.Tree.Root.Children, new NodeKey(10)));
        Assert.True(ContainsChildKey(tree.Tree.Root.Children, new NodeKey(30)));
        Assert.True(ContainsChildKey(tree.Tree.Root.Children, new NodeKey(40)));
        Assert.False(ContainsChildKey(tree.Tree.Root.Children, new NodeKey(20)));
    }

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
