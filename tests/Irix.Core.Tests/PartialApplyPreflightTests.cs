using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class PartialApplyPreflightTests
{
    [Fact]
    public void SegmentedRetainedFrameReader_reads_old_and_replacement_segments_with_matching_resolvers()
    {
        var oldResources = FrameDrawingResources.Rent();
        var oldText = oldResources.AddText("old");
        oldResources.Seal();
        var replacementResources = FrameDrawingResources.Rent();
        var replacementText = replacementResources.AddText("new");
        replacementResources.Seal();
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        var oldSnapshot = RetainedResourceSnapshot.Capture(oldResources);
        var replacementSnapshot = RetainedResourceSnapshot.Capture(replacementResources);
        using var oldBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: oldText),
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: oldText));
        using var replacementBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: oldText),
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: replacementText));

        commandBuffer.ApplyFull(oldBatch);
        table.ApplyFull(2, oldSnapshot);
        commandBuffer.ApplyPartial(replacementBatch, [(1, 1)]);
        Assert.True(table.TryAcceptPartial([(1, 1)], replacementSnapshot));
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var reads = reader.ReadSegments();

        Assert.Collection(reads,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Single(segment.Commands);
                Assert.Same(oldResources, segment.Resolver);
                Assert.Equal("old", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Single(segment.Commands);
                Assert.Same(replacementResources, segment.Resolver);
                Assert.Equal("new", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            });
    }

    [Fact]
    public void RetainedResourceSegmentTable_resolves_old_and_replacement_resources_by_command_range()
    {
        var oldResources = FrameDrawingResources.Rent();
        var oldText = oldResources.AddText("old");
        oldResources.Seal();
        var replacementResources = FrameDrawingResources.Rent();
        var replacementText = replacementResources.AddText("new");
        replacementResources.Seal();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = RetainedResourceSnapshot.Capture(oldResources);
        var replacementSnapshot = RetainedResourceSnapshot.Capture(replacementResources);
        var commands = new[]
        {
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: oldText),
            new DrawCommand(DrawCommandKind.DrawTextRun, Text: replacementText)
        };

        table.ApplyFull(commands.Length, oldSnapshot);
        var accepted = table.TryAcceptPartial([(1, 1)], replacementSnapshot);

        Assert.True(accepted);
        Assert.True(table.TryGetSnapshotForCommand(0, out var command0Snapshot));
        Assert.True(table.TryGetSnapshotForCommand(1, out var command1Snapshot));
        Assert.Same(oldSnapshot, command0Snapshot);
        Assert.Same(replacementSnapshot, command1Snapshot);
        Assert.Equal("old", command0Snapshot.Resolver.Resolve(commands[0].Text).ToString());
        Assert.Equal("new", command1Snapshot.Resolver.Resolve(commands[1].Text).ToString());
    }

    [Fact]
    public void RetainedResourceSegmentTable_accepts_multiple_dirty_ranges_without_double_retaining_replacement_snapshot()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(5, oldSnapshot);
        var accepted = table.TryAcceptPartial([(1, 1), (3, 1)], replacementSnapshot);

        Assert.True(accepted);
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        Assert.Collection(table.Segments,
            segment => AssertSegment(segment, 0, 1, oldSnapshot),
            segment => AssertSegment(segment, 1, 1, replacementSnapshot),
            segment => AssertSegment(segment, 2, 1, oldSnapshot),
            segment => AssertSegment(segment, 3, 1, replacementSnapshot),
            segment => AssertSegment(segment, 4, 1, oldSnapshot));
    }

    [Fact]
    public void RetainedResourceSegmentTable_merges_adjacent_dirty_ranges_for_same_snapshot()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(4, oldSnapshot);
        var accepted = table.TryAcceptPartial([(2, 1), (1, 1)], replacementSnapshot);

        Assert.True(accepted);
        Assert.Collection(table.Segments,
            segment => AssertSegment(segment, 0, 1, oldSnapshot),
            segment => AssertSegment(segment, 1, 2, replacementSnapshot),
            segment => AssertSegment(segment, 3, 1, oldSnapshot));
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
    }

    [Fact]
    public void RetainedResourceSegmentTable_repeated_partial_accept_for_same_snapshot_is_idempotent()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(3, oldSnapshot);
        Assert.True(table.TryAcceptPartial([(1, 1)], replacementSnapshot));
        Assert.True(table.TryAcceptPartial([(1, 1)], replacementSnapshot));

        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        Assert.Collection(table.Segments,
            segment => AssertSegment(segment, 0, 1, oldSnapshot),
            segment => AssertSegment(segment, 1, 1, replacementSnapshot),
            segment => AssertSegment(segment, 2, 1, oldSnapshot));
    }

    [Fact]
    public void RetainedResourceSegmentTable_rejects_invalid_dirty_ranges_without_side_effects()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(2, oldSnapshot);

        Assert.False(table.TryAcceptPartial([(-1, 1)], replacementSnapshot));
        Assert.False(table.TryAcceptPartial([(0, 0)], replacementSnapshot));
        Assert.False(table.TryAcceptPartial([(2, 1)], replacementSnapshot));
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(0, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        var segment = Assert.Single(table.Segments);
        AssertSegment(segment, 0, 2, oldSnapshot);
    }

    [Fact]
    public void RetainedResourceSegmentTable_partial_accept_retains_old_and_replacement_snapshots()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(2, oldSnapshot);
        var accepted = table.TryAcceptPartial([(1, 1)], replacementSnapshot);

        Assert.True(accepted);
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        Assert.Collection(table.Segments,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Equal(1, segment.CommandCount);
                Assert.Same(oldSnapshot, segment.Snapshot);
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Equal(1, segment.CommandCount);
                Assert.Same(replacementSnapshot, segment.Snapshot);
            });

        table.Dispose();

        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.ReleaseCount);
    }

    [Fact]
    public void RetainedResourceSegmentTable_full_fallback_invalidate_and_dispose_release_exactly_once()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        var table = new RetainedResourceSegmentTable();

        table.ApplyFull(2, oldTracker.CreateSnapshot());
        table.ApplyFull(2, replacementTracker.CreateSnapshot());

        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);

        table.Invalidate();
        table.Dispose();
        table.Dispose();

        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.ReleaseCount);
        Assert.Empty(table.Segments);
    }

    [Fact]
    public void RetainedResourceSegmentTable_replaced_old_range_releases_old_snapshot_once()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        table.ApplyFull(1, oldSnapshot);
        var accepted = table.TryAcceptPartial([(0, 1)], replacementSnapshot);

        Assert.True(accepted);
        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Single(table.Segments);
        Assert.Same(replacementSnapshot, table.Segments[0].Snapshot);
    }

    [Fact]
    public void RetainedResourceSegmentTable_and_snapshot_disposed_behavior_is_explicit()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        var snapshot = oldTracker.CreateSnapshot();
        var table = new RetainedResourceSegmentTable();

        table.ApplyFull(1, snapshot);
        table.Dispose();
        snapshot.Dispose();

        Assert.Throws<ObjectDisposedException>(() => snapshot.Retain());
        Assert.Throws<ObjectDisposedException>(() => table.ApplyFull(1, replacementTracker.CreateSnapshot()));
        Assert.Throws<ObjectDisposedException>(() => table.TryAcceptPartial([(0, 1)], replacementTracker.CreateSnapshot()));
        Assert.Throws<ObjectDisposedException>(() => table.Invalidate());
        Assert.Throws<ObjectDisposedException>(() => table.TryGetSnapshotForCommand(0, out _));
        table.Dispose();
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Empty(table.Segments);
    }

    [Fact]
    public void RetainedResourceSnapshot_rejects_rerented_frame_resources_with_new_frame_id()
    {
        var resources1 = FrameDrawingResources.Rent();
        var snapshot = RetainedResourceSnapshot.Capture(resources1);
        var frameId1 = snapshot.FrameId;
        FrameDrawingResources.Return(resources1);

        var resources2 = FrameDrawingResources.Rent();
        try
        {
            Assert.False(snapshot.MatchesResolverScope(resources2));
            if (ReferenceEquals(resources1, resources2))
            {
                Assert.NotEqual(frameId1, resources2.FrameId);
            }
        }
        finally
        {
            FrameDrawingResources.Return(resources2);
        }
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_action_id_without_next_layout()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary"))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment", new PixelRectangle(0, 0, 960, 540))
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1], retainedHitTargets);

        Assert.True(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, projection.FallbackReason);
        var target = Assert.Single(projection.HitTargets);
        Assert.Equal(retainedHitTargets[0].Bounds, target.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, target.ClipBounds);
        Assert.Equal("Increment.Secondary", target.ActionId);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_next_tree_shape_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement"))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_action_id_is_missing()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_dirty_dfs_is_not_action_node()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [0],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_non_dirty_action_id_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary"))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement.Secondary"))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [
                new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment"),
                new HitTestTarget(new PixelRectangle(16, 172, 140, 40), "Decrement")
            ]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_on_key_path_mismatch()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 99,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary"))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_multiple_buttons()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement"))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary"))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement.Secondary"))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment"),
            new HitTestTarget(new PixelRectangle(16, 172, 140, 40), "Decrement")
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1, 3], retainedHitTargets);

        Assert.True(projection.Succeeded);
        Assert.Equal("Increment.Secondary", projection.HitTargets[0].ActionId);
        Assert.Equal("Decrement.Secondary", projection.HitTargets[1].ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, projection.HitTargets[0].Bounds);
        Assert.Equal(retainedHitTargets[1].Bounds, projection.HitTargets[1].Bounds);
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_nested_button()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.ScrollContainer(10,
                VirtualNodeFactory.Button("Inner", 2,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Inner")))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.ScrollContainer(10,
                VirtualNodeFactory.Button("Inner", 2,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Inner.Secondary")))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(32, 136, 140, 40), "Inner", new PixelRectangle(16, 16, 928, 508))
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [2], retainedHitTargets);

        Assert.True(projection.Succeeded);
        var target = Assert.Single(projection.HitTargets);
        Assert.Equal("Inner.Secondary", target.ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, target.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, target.ClipBounds);
    }

    [Fact]
    public void PartialApplyIntegrationGateChecklist_blocks_hookup_until_every_gate_is_satisfied()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;
        var expectedGates = Enum.GetValues<PartialApplyIntegrationGate>();

        Assert.False(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
        Assert.Equal(expectedGates.Length, gates.Count);
        foreach (var expectedGate in expectedGates)
        {
            var status = Assert.Single(gates, gate => gate.Gate == expectedGate);
            Assert.False(status.Satisfied);
            Assert.False(string.IsNullOrWhiteSpace(status.BlockingCondition));
            Assert.False(string.IsNullOrWhiteSpace(status.Evidence));
        }
    }

    private static DrawCommandBatch CreateCommandBatch(params DrawCommand[] commands)
    {
        return new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(commands), commands.Length);
    }

    private static void AssertSegment(RetainedResourceSegment segment, int commandStart, int commandCount, RetainedResourceSnapshot snapshot)
    {
        Assert.Equal(commandStart, segment.CommandStart);
        Assert.Equal(commandCount, segment.CommandCount);
        Assert.Same(snapshot, segment.Snapshot);
    }

    private sealed class SnapshotTracker
    {
        public int RetainCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public RetainedResourceSnapshot CreateSnapshot()
        {
            return RetainedResourceSnapshot.Capture(
                new TrackingResolver(),
                retain: () => RetainCount++,
                release: () => ReleaseCount++);
        }
    }

    private sealed class TrackingResolver : IFrameResourceResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => default;

        public TextStyle ResolveTextStyle(ResourceHandle handle) => TextStyle.Default;
    }
}