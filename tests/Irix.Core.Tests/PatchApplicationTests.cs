using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Tests that verify patches produced by VirtualNodeDiffer are semantically correct:
/// PatchBatch.Root must produce the same layout as if we built directly from the new tree.
/// NOTE: These test the diff-outputs-layout equivalence, not a retained-tree Apply(patches) path.
/// When a real RetainedTree.Apply(PatchBatch) is implemented, add dedicated tests for that.
/// </summary>
public sealed class PatchDiffEquivalenceTests
{
    private readonly VirtualTextArena _arena = new();
    [Fact]
    public void Update_patch_content_change_produces_correct_layout()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2))), _arena.Snapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "World", new NodeKey(2))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // PatchBatch.Root IS the new tree root — that's the retained tree
        Assert.Equal(1, batch.Count);
        var patch = batch.Memory.Span[0];
        Assert.Equal(VirtualNodePatchOperation.Update, patch.Operation);
        Assert.Equal("World", ResolveNodeText(_arena, patch.Node.Content));

        // Layout from new tree root should produce "World"
        var layout = new LayoutTreeBuilder().Build(batch.Root, new PixelRectangle(0, 0, 960, 540));
        Assert.Single(layout);
        Assert.Equal("World", batch.TextSnapshot.Resolve(layout[0].Text).ToString());

        batch.Dispose();
    }

    [Fact]
    public void Add_child_patch_produces_correct_layout()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2))), _arena.Snapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "World", new NodeKey(3))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.True(batch.Count > 0);
        var hasAdd = false;
        foreach (var p in batch.Memory.Span[..batch.Count])
        {
            if (p.Operation == VirtualNodePatchOperation.Add) { hasAdd = true; break; }
        }
        Assert.True(hasAdd, "Expected an Add patch");

        var layout = new LayoutTreeBuilder().Build(batch.Root, new PixelRectangle(0, 0, 960, 540));
        Assert.Equal(2, layout.Count);
        Assert.Equal("Hello", batch.TextSnapshot.Resolve(layout[0].Text).ToString());
        Assert.Equal("World", batch.TextSnapshot.Resolve(layout[1].Text).ToString());

        batch.Dispose();
    }

    [Fact]
    public void Remove_child_patch_produces_correct_layout()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "World", new NodeKey(3))), _arena.Snapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        Assert.True(batch.Count > 0);
        var hasRemove = false;
        foreach (var p in batch.Memory.Span[..batch.Count])
        {
            if (p.Operation == VirtualNodePatchOperation.Remove) { hasRemove = true; break; }
        }
        Assert.True(hasRemove, "Expected a Remove patch");

        var layout = new LayoutTreeBuilder().Build(batch.Root, new PixelRectangle(0, 0, 960, 540));
        Assert.Single(layout);
        Assert.Equal("Hello", batch.TextSnapshot.Resolve(layout[0].Text).ToString());

        batch.Dispose();
    }

    [Fact]
    public void Keyed_reconciliation_update_preserves_correct_child()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(20)),
            VirtualNodeBuilder.Text(_arena, "C", new NodeKey(30))), _arena.Snapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(10)),
            VirtualNodeBuilder.Text(_arena, "B-modified", new NodeKey(20)),
            VirtualNodeBuilder.Text(_arena, "C", new NodeKey(30))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Should produce exactly one Update for key=20
        VirtualNodePatch[] updatePatches = [];
        var span = batch.Memory.Span[..batch.Count];
        var updateList = new List<VirtualNodePatch>();
        foreach (var p in span)
        {
            if (p.Operation == VirtualNodePatchOperation.Update) updateList.Add(p);
        }
        updatePatches = updateList.ToArray();
        Assert.Single(updatePatches);
        Assert.Equal("B-modified", ResolveNodeText(_arena, updatePatches[0].Node.Content));

        // Layout from PatchBatch.Root should have all 3 children with "B-modified"
        var layout = new LayoutTreeBuilder().Build(batch.Root, new PixelRectangle(0, 0, 960, 540));
        Assert.Equal(3, layout.Count);
        Assert.Equal("A", batch.TextSnapshot.Resolve(layout[0].Text).ToString());
        Assert.Equal("B-modified", batch.TextSnapshot.Resolve(layout[1].Text).ToString());
        Assert.Equal("C", batch.TextSnapshot.Resolve(layout[2].Text).ToString());

        batch.Dispose();
    }

    [Fact]
    public void Kind_change_produces_ReplaceRoot_with_correct_layout()
    {
        var oldTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2))), _arena.Snapshot());
        var newTree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(oldTree, newTree);

        // Kind changed Text→Button, should be ReplaceRoot
        Assert.True(batch.Count > 0);
        var patch = batch.Memory.Span[0];
        Assert.Equal(VirtualNodePatchOperation.ReplaceRoot, patch.Operation);

        var layout = new LayoutTreeBuilder().Build(batch.Root, new PixelRectangle(0, 0, 960, 540));
        // Button produces at least one layout element with text
        Assert.True(layout.Count >= 1, $"Expected at least 1 layout element, got {layout.Count}");
        var hasClickText = false;
        foreach (var el in layout)
        {
            if (batch.TextSnapshot.Resolve(el.Text).ToString() == "Click") { hasClickText = true; break; }
        }
        Assert.True(hasClickText, "Expected 'Click' text in layout elements");

        batch.Dispose();
    }

    [Fact]
    public void No_change_produces_empty_patch_batch()
    {
        var tree = new VirtualNodeTree(VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2))), _arena.Snapshot());

        var batch = VirtualNodeDiffer.CreatePatchBatch(tree, tree);

        Assert.Equal(0, batch.Count);

        batch.Dispose();
    }

    private static string ResolveNodeText(VirtualTextArena arena, NodeContent content) =>
        content.TryGetText(out var tc) ? arena.ResolveString(tc) : "";
}
