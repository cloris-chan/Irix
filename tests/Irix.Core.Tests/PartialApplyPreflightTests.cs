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
    public void SegmentedRetainedFrameReader_fails_for_empty_segment_table()
    {
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        commandBuffer.ApplyFull(batch);
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadSegments());

        Assert.Contains("empty", exception.Message);
    }

    [Fact]
    public void SegmentedRetainedFrameReader_fails_when_segment_exceeds_command_buffer()
    {
        var tracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        commandBuffer.ApplyFull(batch);
        table.ApplyUncheckedForPreflight([new RetainedResourceSegment(0, 2, tracker.CreateSnapshot())]);
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadSegments());

        Assert.Contains("outside", exception.Message);
    }

    [Fact]
    public void SegmentedRetainedFrameReader_fails_for_non_contiguous_segments()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        using var batch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect));
        commandBuffer.ApplyFull(batch);
        table.ApplyUncheckedForPreflight(
        [
            new RetainedResourceSegment(0, 1, oldTracker.CreateSnapshot()),
            new RetainedResourceSegment(2, 1, replacementTracker.CreateSnapshot())
        ]);
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadSegments());

        Assert.Contains("contiguously", exception.Message);
    }

    [Fact]
    public void SegmentedRetainedFrameReader_fails_for_overlapping_segments()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        using var batch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect));
        commandBuffer.ApplyFull(batch);
        table.ApplyUncheckedForPreflight(
        [
            new RetainedResourceSegment(0, 2, oldTracker.CreateSnapshot()),
            new RetainedResourceSegment(1, 1, replacementTracker.CreateSnapshot())
        ]);
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadSegments());

        Assert.Contains("contiguously", exception.Message);
    }

    [Fact]
    public void SegmentedRetainedFrameReader_fails_when_segment_coverage_does_not_match_command_count()
    {
        var tracker = new SnapshotTracker();
        using var table = new RetainedResourceSegmentTable();
        using var commandBuffer = new RetainedCommandBuffer();
        using var batch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect));
        commandBuffer.ApplyFull(batch);
        table.ApplyUncheckedForPreflight([new RetainedResourceSegment(0, 1, tracker.CreateSnapshot())]);
        var reader = new SegmentedRetainedFrameReader(commandBuffer, table);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ReadSegments());

        Assert.Contains("command count", exception.Message);
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
    public void SegmentedRetainedFramePrototype_full_apply_replaces_snapshot_and_root_metadata()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var frame = new SegmentedRetainedFramePrototype();
        using var oldBatch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var replacementBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        var oldRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var replacementRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary"))));
        var oldSnapshot = oldTracker.CreateSnapshot();
        var replacementSnapshot = replacementTracker.CreateSnapshot();

        frame.ApplyFull(oldBatch, oldSnapshot, oldRoot);
        frame.ApplyFull(replacementBatch, replacementSnapshot, replacementRoot);

        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        Assert.Equal(2, frame.CommandCount);
        Assert.Equal(replacementRoot, frame.RetainedRoot);
        var segment = Assert.Single(frame.ResourceSegments);
        AssertSegment(segment, 0, 2, replacementSnapshot);
        var read = Assert.Single(frame.ReadSegments());
        Assert.Same(replacementSnapshot.Resolver, read.Resolver);
        Assert.Equal(2, read.Commands.Length);
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_invalidate_releases_snapshots_and_clears_metadata()
    {
        var tracker = new SnapshotTracker();
        using var frame = new SegmentedRetainedFramePrototype();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2, new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        frame.ApplyFull(batch, tracker.CreateSnapshot(), root);
        frame.Invalidate();
        frame.Invalidate();

        Assert.Equal(1, tracker.RetainCount);
        Assert.Equal(1, tracker.ReleaseCount);
        Assert.Equal(0, frame.CommandCount);
        Assert.Empty(frame.ResourceSegments);
        Assert.Equal(default, frame.RetainedRoot);
        Assert.Empty(frame.ReadSegments());
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_dispose_releases_snapshots_exactly_once()
    {
        var tracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        var frame = new SegmentedRetainedFramePrototype();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));

        frame.ApplyFull(batch, tracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(1));
        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, tracker.RetainCount);
        Assert.Equal(1, tracker.ReleaseCount);
        Assert.Throws<ObjectDisposedException>(() => frame.ApplyFull(batch, replacementTracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(2)));
        Assert.Throws<ObjectDisposedException>(() => frame.Invalidate());
        Assert.Throws<ObjectDisposedException>(() => frame.ReadSegments());
        Assert.Equal(0, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_does_not_change_existing_retained_frame_contract()
    {
        using var retainedFrame = new RetainedRenderFrame();
        var retainedResolver = new NamedResolver("retained");
        using var retainedBatch = new RenderFrameBatch(
            CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect)),
            [],
            retainedResolver,
            [(0, 1)]);
        retainedFrame.ApplyFull(retainedBatch);
        var retainedCommandCount = retainedFrame.CommandCount;
        var retainedDirtyRanges = retainedFrame.DirtyCommandRanges.ToArray();
        var retainedResources = retainedFrame.Resources;
        var tracker = new SnapshotTracker();
        using var prototype = new SegmentedRetainedFramePrototype();
        using var prototypeBatch = CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun));

        prototype.ApplyFull(prototypeBatch, tracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(1));
        prototype.Invalidate();

        Assert.Equal(retainedCommandCount, retainedFrame.CommandCount);
        Assert.Equal(retainedDirtyRanges, retainedFrame.DirtyCommandRanges);
        Assert.Same(retainedResources, retainedFrame.Resources);
        Assert.True(retainedFrame.TryReadFrame(out var commands, out var resources));
        Assert.Equal(1, commands.Length);
        Assert.Same(retainedResolver, resources);
        Assert.Equal(1, tracker.RetainCount);
        Assert.Equal(1, tracker.ReleaseCount);
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_applies_real_pipeline_batch_without_mutating_retained_frame()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        using var batch = pipeline.Build(root, viewport);
        var retainedCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedResources = pipeline.RetainedFrame.Resources;
        var retainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var retainedHitTargets = pipeline.RetainedFrame.HitTargets.ToArray();
        using var frame = new SegmentedRetainedFramePrototype();
        var snapshot = RetainedResourceSnapshot.Capture(batch.Resources);

        frame.ApplyFull(batch, snapshot, root);
        var reads = frame.ReadSegments();

        var read = Assert.Single(reads);
        Assert.Equal(0, read.CommandStart);
        Assert.Equal(batch.Commands.Count, read.Commands.Length);
        Assert.Same(batch.Resources, read.Resolver);
        Assert.Equal(root, frame.RetainedRoot);
        Assert.Same(snapshot, Assert.Single(frame.ResourceSegments).Snapshot);
        var textCommand = FindSingleTextCommand(read.Commands);
        Assert.Equal("Increment", read.Resolver.Resolve(textCommand.Text).ToString());
        Assert.Equal(retainedCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(retainedResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(retainedHitTargets, pipeline.RetainedFrame.HitTargets);
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_rehearses_accepted_cross_frame_partial_with_synchronized_segments_resources_and_root()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 120, 140, 40);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));
        var retainedHitTargets = new[] { new HitTestTarget(buttonBounds, "Increment") };
        var snapshot = CreateStyleOnlySnapshot(viewport, buttonBounds, retainedRoot, retainedHitTargets);
        var oldTracker = new SnapshotTracker("old");
        var replacementTracker = new SnapshotTracker("replacement");
        var replacementResolver = replacementTracker.CreateResolverOnly();
        using var frame = new SegmentedRetainedFramePrototype();
        using var oldBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, retainedHitTargets, replacementResolver, [(1, 1)]);

        frame.ApplyFull(oldBatch, oldTracker.CreateSnapshot(), retainedRoot);
        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, replacementBatch.Resources, replacementBatch.Resources);
        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1], retainedHitTargets);
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, snapshot.DirtyClassifications);
        var accepted = frame.TryAcceptPartial(replacementBatch, replacementTracker.CreateSnapshot(replacementResolver), rootPatch);
        var reads = frame.ReadSegments();

        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, plan.Kind);
        Assert.True(hitTargetProjection.Succeeded);
        Assert.Equal("Increment.Secondary", hitTargetProjection.HitTargets[0].ActionId);
        Assert.True(rootPatch.Succeeded);
        Assert.True(accepted);
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        AssertTextAttribute(frame.RetainedRoot.Children[0], "ActionId", "Increment.Secondary");
        AssertBooleanAttribute(frame.RetainedRoot.Children[0], "IsHovered", true);
        Assert.Collection(frame.ResourceSegments,
            segment => AssertSegment(segment, 0, 1, oldTracker.Snapshot),
            segment => AssertSegment(segment, 1, 1, replacementTracker.Snapshot));
        Assert.Collection(reads,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Same(oldTracker.Snapshot.Resolver, segment.Resolver);
                Assert.Equal("old", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Same(replacementTracker.Snapshot.Resolver, segment.Resolver);
                Assert.Equal("replacement", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            });
    }

    [Fact]
    public void SegmentedRetainedFramePrototype_rehearsal_failed_partial_falls_back_without_pre_fallback_mutation()
    {
        var oldTracker = new SnapshotTracker("old");
        var replacementTracker = new SnapshotTracker("replacement");
        var replacementResolver = replacementTracker.CreateResolverOnly();
        using var frame = new SegmentedRetainedFramePrototype();
        using var oldBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, [], replacementResolver, [(1, 1)]);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment v2", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));

        frame.ApplyFull(oldBatch, oldTracker.CreateSnapshot(), retainedRoot);
        var beforeSegments = frame.ResourceSegments.ToArray();
        var beforeRoot = frame.RetainedRoot;
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);
        var accepted = frame.TryAcceptPartial(replacementBatch, replacementTracker.CreateSnapshot(replacementResolver), rootPatch);

        Assert.False(rootPatch.Succeeded);
        Assert.False(accepted);
        Assert.Equal(beforeRoot, frame.RetainedRoot);
        Assert.Equal(beforeSegments, frame.ResourceSegments);
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(0, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);

        frame.ApplyFull(replacementBatch, replacementTracker.Snapshot, nextRoot);

        Assert.Equal(1, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(nextRoot, frame.RetainedRoot);
        var segment = Assert.Single(frame.ResourceSegments);
        AssertSegment(segment, 0, 2, replacementTracker.Snapshot);
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_prefers_per_segment_execute_without_contract_change()
    {
        var decision = SegmentedBackendExecutionAdapterDesign.Preferred;
        var backend = new CapturingBackend();
        var oldResolver = new NamedResolver("old");
        var replacementResolver = new NamedResolver("replacement");
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        adapter.Execute(
            new FrameContext(960, 540),
            [
                new SegmentedFrameRead(0, [new DrawCommand(DrawCommandKind.FillRect)], oldResolver),
                new SegmentedFrameRead(1, [new DrawCommand(DrawCommandKind.DrawTextRun), new DrawCommand(DrawCommandKind.FillRect)], replacementResolver)
            ]);

        Assert.Equal(SegmentedBackendExecutionStrategy.PerSegmentExecute, decision.PreferredStrategy);
        Assert.Contains("No IDrawingBackend.Execute signature change", decision.BackendContractImpact);
        Assert.Contains("stable global handles remain postponed", decision.BlockedAlternatives);
        Assert.Equal(1, backend.BeginFrameCount);
        Assert.Equal(1, backend.EndFrameCount);
        Assert.Collection(backend.ExecuteCalls,
            call =>
            {
                Assert.Equal(1, call.CommandCount);
                Assert.Same(oldResolver, call.Resolver);
            },
            call =>
            {
                Assert.Equal(2, call.CommandCount);
                Assert.Same(replacementResolver, call.Resolver);
            });
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_pairs_begin_and_end_for_empty_segments()
    {
        var backend = new CapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        adapter.Execute(new FrameContext(320, 240), []);

        Assert.Equal(["BeginFrame", "EndFrame"], backend.Calls);
        Assert.Empty(backend.ExecuteCalls);
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_ends_frame_when_backend_execute_throws()
    {
        var backend = new ThrowingBackend(throwOnExecuteCall: 2);
        var adapter = new SegmentedBackendExecutionAdapter(backend);
        var resolver = new NamedResolver("segment");

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Execute(
            new FrameContext(320, 240),
            [
                new SegmentedFrameRead(0, [new DrawCommand(DrawCommandKind.FillRect)], resolver),
                new SegmentedFrameRead(1, [new DrawCommand(DrawCommandKind.DrawTextRun)], resolver)
            ]));

        Assert.Equal("execute failed", exception.Message);
        Assert.Equal(1, backend.BeginFrameCount);
        Assert.Equal(1, backend.EndFrameCount);
        Assert.Equal(2, backend.ExecuteCount);
        Assert.Equal(["BeginFrame", "Execute:1", "Execute:1", "EndFrame"], backend.Calls);
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_does_not_leak_dirty_ranges_to_backend()
    {
        var backend = new DirtyRangeAwareCapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        adapter.Execute(
            new FrameContext(640, 480),
            [new SegmentedFrameRead(4, [new DrawCommand(DrawCommandKind.FillRect)], new NamedResolver("segment"))]);

        Assert.Equal(0, backend.SetDirtyCommandRangeCount);
        Assert.Single(backend.ExecuteCalls);
        Assert.Equal(["BeginFrame", "Execute:1", "EndFrame"], backend.Calls);
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
    public void RetainedRootMetadataPatcher_projects_dirty_control_metadata_from_next_root()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false)),
                new VirtualNodeAttribute("IsPressed", AttributeValue.FromBoolean(false)),
                new VirtualNodeAttribute("IsFocused", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true)),
                new VirtualNodeAttribute("IsPressed", AttributeValue.FromBoolean(true)),
                new VirtualNodeAttribute("IsFocused", AttributeValue.FromBoolean(true))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.True(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, patch.FallbackReason);
        var patchedButton = patch.Root.Children[0];
        AssertTextAttribute(patchedButton, "ActionId", "Increment.Secondary");
        AssertBooleanAttribute(patchedButton, "IsHovered", true);
        AssertBooleanAttribute(patchedButton, "IsPressed", true);
        AssertBooleanAttribute(patchedButton, "IsFocused", true);
        Assert.Equal(retainedRoot.Children[0].Children[0], patchedButton.Children[0]);
        AssertTextAttribute(retainedRoot.Children[0], "ActionId", "Increment");
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_non_dirty_control_metadata_drifts()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_on_key_path_mismatch()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 99,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_text_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment v2", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_layout_attribute_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true)),
                new VirtualNodeAttribute("ButtonHeight", AttributeValue.FromNumber(52))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_tree_shape_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))),
            VirtualNodeFactory.Button("Decrement", 4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Decrement"))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)]);

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void Planner_projector_segment_table_dry_run_chains_preflight_without_runtime_mutation()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 120, 140, 40);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(false))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Increment", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment.Secondary")),
                new VirtualNodeAttribute("IsHovered", AttributeValue.FromBoolean(true))));
        var retainedHitTargets = new[] { new HitTestTarget(buttonBounds, "Increment") };
        var layoutResult = new LayoutTreeResult(
            [
                new LayoutElement(LayoutElementKind.Text, new PixelRectangle(16, 80, 120, 24), Text: "Static"),
                new LayoutElement(LayoutElementKind.Button, buttonBounds, Text: "Increment", ActionId: "Increment.Secondary", ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: false))
            ],
            [],
            [(1, 1)]);
        var snapshot = new RenderPipelineRetainedInputSnapshot(
            layoutResult,
            [new ElementCommandRange(0, 1), new ElementCommandRange(1, 1)],
            retainedHitTargets,
            retainedRoot,
            viewport,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            [(1, 1)],
            [(1, 1)],
            LayoutRebuildReason.StyleOnly);
        var planningResolver = new NamedResolver("planning");
        var pipeline = new RenderPipeline();
        using var pipelineFrame = pipeline.Build(retainedRoot, viewport);
        var pipelineLayoutRebuildCount = pipeline.LayoutRebuildCount;
        var pipelineLastViewport = pipeline.LastViewport;
        var pipelineRetainedFrameCommandCount = pipeline.RetainedFrame.CommandCount;
        var pipelineRetainedFrameResources = pipeline.RetainedFrame.Resources;
        var pipelineRetainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var pipelineLastDirtyCommandRanges = pipeline.LastDirtyCommandRanges.ToArray();
        using var retainedFrame = new RetainedRenderFrame();
        using var compositor = new DrawingBackendCompositor(new NoOpBackend());
        var retainedFrameCommandCount = retainedFrame.CommandCount;
        var retainedFrameResources = retainedFrame.Resources;
        var retainedFrameDirtyRanges = retainedFrame.DirtyCommandRanges.ToArray();
        var compositorRenderCount = compositor.RenderCount;
        var compositorFullApplyCount = compositor.FullApplyCount;
        var compositorPartialApplyCount = compositor.PartialApplyCount;
        var compositorLastDirtyRanges = compositor.LastDirtyCommandRanges.ToArray();

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, planningResolver, planningResolver);
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);
        var hitTargetProjection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1], retainedHitTargets);
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, snapshot.DirtyClassifications);
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);

        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, plan.Kind);
        Assert.Equal([(1, 1)], plan.DirtyCommandRanges);
        Assert.True(hitTargetProjection.Succeeded);
        Assert.Equal("Increment.Secondary", hitTargetProjection.HitTargets[0].ActionId);
        Assert.True(rootPatch.Succeeded);
        AssertTextAttribute(rootPatch.Root.Children[0], "ActionId", "Increment.Secondary");
        AssertBooleanAttribute(rootPatch.Root.Children[0], "IsHovered", true);

        using var commandBuffer = new RetainedCommandBuffer();
        using var table = new RetainedResourceSegmentTable();
        using var mergedCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        var oldSnapshot = RetainedResourceSnapshot.Capture(new NamedResolver("old"));
        var replacementSnapshot = RetainedResourceSnapshot.Capture(new NamedResolver("replacement"));

        commandBuffer.ApplyFull(mergedCommands);
        table.ApplyFull(2, oldSnapshot);
        Assert.True(table.TryAcceptPartial(plan.DirtyCommandRanges, replacementSnapshot));
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);
        var reads = new SegmentedRetainedFrameReader(commandBuffer, table).ReadSegments();
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);

        Assert.Collection(reads,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Same(oldSnapshot.Resolver, segment.Resolver);
                Assert.Equal("old", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Same(replacementSnapshot.Resolver, segment.Resolver);
                Assert.Equal("replacement", segment.Resolver.Resolve(segment.Commands[0].Text).ToString());
            });
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);
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
            Assert.False(string.IsNullOrWhiteSpace(status.PreflightEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.RuntimeEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.NoChangeRegressionEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.RuntimePromotionCondition));
            Assert.Contains("None", status.RuntimeEvidence);
            Assert.Contains("Promote only after", status.RuntimePromotionCondition);
        }
    }

    [Fact]
    public void PartialApplyIntegrationGateChecklist_keeps_runtime_promotion_conditions_separate_from_satisfaction()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;

        Assert.False(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceResolverOwnership && gate.RuntimePromotionCondition.Contains("segment reads"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceDisposePolicy && gate.RuntimePromotionCondition.Contains("multiple FrameDrawingResources snapshots"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.RetainedRootUpdate && gate.RuntimePromotionCondition.Contains("atomically advance retained root metadata"));
        foreach (var gate in gates)
        {
            Assert.False(gate.Satisfied);
        }
    }

    private static void AssertTextAttribute(VirtualNode node, string name, string expected)
    {
        var attribute = GetSingleAttribute(node, name);
        Assert.Equal(AttributeValueKind.Text, attribute.Value.Kind);
        Assert.Equal(expected, attribute.Value.Text);
    }

    private static void AssertBooleanAttribute(VirtualNode node, string name, bool expected)
    {
        var attribute = GetSingleAttribute(node, name);
        Assert.Equal(AttributeValueKind.Boolean, attribute.Value.Kind);
        Assert.Equal(expected, attribute.Value.Boolean);
    }

    private static VirtualNodeAttribute GetSingleAttribute(VirtualNode node, string name)
    {
        var matchCount = 0;
        var match = default(VirtualNodeAttribute);
        foreach (var attribute in node.Attributes)
        {
            if (attribute.Name != name)
            {
                continue;
            }

            matchCount++;
            match = attribute;
        }

        Assert.Equal(1, matchCount);
        return match;
    }

    private static DrawCommand FindSingleTextCommand(IReadOnlyList<DrawCommand> commands)
    {
        var count = 0;
        var result = default(DrawCommand);
        foreach (var command in commands)
        {
            if (command.Kind != DrawCommandKind.DrawTextRun)
            {
                continue;
            }

            count++;
            result = command;
        }

        Assert.Equal(1, count);
        return result;
    }

    private static RenderPipelineRetainedInputSnapshot CreateStyleOnlySnapshot(
        PixelRectangle viewport,
        PixelRectangle buttonBounds,
        VirtualNode retainedRoot,
        IReadOnlyList<HitTestTarget> retainedHitTargets)
    {
        var layoutResult = new LayoutTreeResult(
            [
                new LayoutElement(LayoutElementKind.Text, new PixelRectangle(16, 80, 120, 24), Text: "Static"),
                new LayoutElement(LayoutElementKind.Button, buttonBounds, Text: "Increment", ActionId: "Increment.Secondary", ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: false))
            ],
            [],
            [(1, 1)]);
        return new RenderPipelineRetainedInputSnapshot(
            layoutResult,
            [new ElementCommandRange(0, 1), new ElementCommandRange(1, 1)],
            retainedHitTargets,
            retainedRoot,
            viewport,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            [(1, 1)],
            [(1, 1)],
            LayoutRebuildReason.StyleOnly);
    }

    private static void AssertDryRunSentinels(
        RenderPipeline pipeline,
        long pipelineLayoutRebuildCount,
        PixelRectangle pipelineLastViewport,
        int pipelineRetainedFrameCommandCount,
        IFrameResourceResolver pipelineRetainedFrameResources,
        IReadOnlyList<(int Start, int Count)> pipelineRetainedDirtyRanges,
        IReadOnlyList<(int Start, int Count)> pipelineLastDirtyCommandRanges,
        RetainedRenderFrame retainedFrame,
        int retainedFrameCommandCount,
        IFrameResourceResolver retainedFrameResources,
        IReadOnlyList<(int Start, int Count)> retainedFrameDirtyRanges,
        DrawingBackendCompositor compositor,
        long compositorRenderCount,
        long compositorFullApplyCount,
        long compositorPartialApplyCount,
        IReadOnlyList<(int Start, int Count)> compositorLastDirtyRanges)
    {
        Assert.Equal(pipelineLayoutRebuildCount, pipeline.LayoutRebuildCount);
        Assert.Equal(pipelineLastViewport, pipeline.LastViewport);
        Assert.Equal(pipelineRetainedFrameCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(pipelineRetainedFrameResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(pipelineRetainedDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(pipelineLastDirtyCommandRanges, pipeline.LastDirtyCommandRanges);
        Assert.Equal(retainedFrameCommandCount, retainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, retainedFrame.Resources);
        Assert.Equal(retainedFrameDirtyRanges, retainedFrame.DirtyCommandRanges);
        Assert.Equal(compositorRenderCount, compositor.RenderCount);
        Assert.Equal(compositorFullApplyCount, compositor.FullApplyCount);
        Assert.Equal(compositorPartialApplyCount, compositor.PartialApplyCount);
        Assert.Equal(compositorLastDirtyRanges, compositor.LastDirtyCommandRanges);
        Assert.Equal(0, compositor.RetainedFrame.CommandCount);
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
        private readonly string _resolverText;

        public SnapshotTracker(string resolverText = "")
        {
            _resolverText = resolverText;
        }

        public int RetainCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public RetainedResourceSnapshot Snapshot { get; private set; } = null!;

        public IFrameResourceResolver CreateResolverOnly()
        {
            return new TrackingResolver(_resolverText);
        }

        public RetainedResourceSnapshot CreateSnapshot(IFrameResourceResolver? resolver = null)
        {
            Snapshot = RetainedResourceSnapshot.Capture(
                resolver ?? new TrackingResolver(_resolverText),
                retain: () => RetainCount++,
                release: () => ReleaseCount++);
            return Snapshot;
        }
    }

    private sealed class TrackingResolver(string text = "") : IFrameResourceResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => text.AsSpan();

        public TextStyle ResolveTextStyle(ResourceHandle handle) => TextStyle.Default;
    }

    private sealed class NamedResolver(string text) : IFrameResourceResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => text.AsSpan();

        public TextStyle ResolveTextStyle(ResourceHandle handle) => TextStyle.Default;
    }

    private sealed class NoOpBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext)
        {
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
        }

        public void EndFrame()
        {
        }

        public void Dispose()
        {
        }
    }

    private class CapturingBackend : IDrawingBackend
    {
        public int BeginFrameCount { get; private set; }
        public int EndFrameCount { get; private set; }
        public List<(int CommandCount, IFrameResourceResolver Resolver)> ExecuteCalls { get; } = [];
        public List<string> Calls { get; } = [];

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
            Calls.Add("BeginFrame");
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCalls.Add((commands.Length, resources));
            Calls.Add($"Execute:{commands.Length}");
        }

        public void EndFrame()
        {
            EndFrameCount++;
            Calls.Add("EndFrame");
        }

        public void Dispose()
        {
        }
    }

    private sealed class DirtyRangeAwareCapturingBackend : CapturingBackend, IDirtyRangeAware
    {
        public int SetDirtyCommandRangeCount { get; private set; }

        public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
        {
            SetDirtyCommandRangeCount++;
        }
    }

    private sealed class ThrowingBackend(int throwOnExecuteCall) : IDrawingBackend
    {
        public int BeginFrameCount { get; private set; }
        public int EndFrameCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public List<string> Calls { get; } = [];

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
            Calls.Add("BeginFrame");
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
            Calls.Add($"Execute:{commands.Length}");
            if (ExecuteCount == throwOnExecuteCall)
            {
                throw new InvalidOperationException("execute failed");
            }
        }

        public void EndFrame()
        {
            EndFrameCount++;
            Calls.Add("EndFrame");
        }

        public void Dispose()
        {
        }
    }
}