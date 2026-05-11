using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class PartialApplyPreflightTests
{
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
        }
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