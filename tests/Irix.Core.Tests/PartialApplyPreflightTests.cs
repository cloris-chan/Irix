using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class PartialApplyPreflightTests
{
    private readonly VirtualTextArena _arena = new();

    private static DrawingBackendCall BeginFrameCall => DrawingBackendCall.BeginFrame;

    private static DrawingBackendCall ExecuteCall(int commandCount) => DrawingBackendCall.Execute(commandCount);

    private static DrawingBackendCall EndFrameCall => DrawingBackendCall.EndFrame;

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
        Assert.False(table.TryAcceptPartial([(0, 2), (1, 1)], replacementSnapshot));
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
    public void SegmentedRetainedFrameOwner_full_apply_replaces_snapshot_and_root_metadata()
    {
        var oldTracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        using var frame = new SegmentedRetainedFrameOwner();
        using var oldBatch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var replacementBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        var oldRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1))));
        var replacementRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(4))));
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
    public void SegmentedRetainedFrameOwner_invalidate_releases_snapshots_and_clears_metadata()
    {
        var tracker = new SnapshotTracker();
        using var frame = new SegmentedRetainedFrameOwner();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1))));

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
    public void SegmentedRetainedFrameOwner_full_apply_empty_releases_segments_without_retaining_empty_snapshot()
    {
        var tracker = new SnapshotTracker();
        var emptyTracker = new SnapshotTracker();
        using var frame = new SegmentedRetainedFrameOwner();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var emptyBatch = CreateCommandBatch();
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1));
        var emptyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(2));

        frame.ApplyFull(batch, tracker.CreateSnapshot(), retainedRoot);
        frame.ApplyFull(emptyBatch, emptyTracker.CreateSnapshot(), emptyRoot);

        Assert.Equal(1, tracker.RetainCount);
        Assert.Equal(1, tracker.ReleaseCount);
        Assert.Equal(0, emptyTracker.RetainCount);
        Assert.Equal(0, emptyTracker.ReleaseCount);
        Assert.Equal(0, frame.CommandCount);
        Assert.Empty(frame.ResourceSegments);
        Assert.Equal(emptyRoot, frame.RetainedRoot);
        Assert.Empty(frame.ReadSegments());
    }

    [Fact]
    public void SegmentedRetainedFrameOwner_dispose_releases_snapshots_exactly_once()
    {
        var tracker = new SnapshotTracker();
        var replacementTracker = new SnapshotTracker();
        var frame = new SegmentedRetainedFrameOwner();
        using var batch = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));

        frame.ApplyFull(batch, tracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(new NodeKey(1)));
        frame.Dispose();
        frame.Dispose();

        Assert.Equal(1, tracker.RetainCount);
        Assert.Equal(1, tracker.ReleaseCount);
        Assert.Throws<ObjectDisposedException>(() => frame.ApplyFull(batch, replacementTracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(new NodeKey(2))));
        Assert.Throws<ObjectDisposedException>(() => frame.Invalidate());
        Assert.Throws<ObjectDisposedException>(() => frame.ReadSegments());
        Assert.Equal(0, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
    }

    [Fact]
    public void SegmentedRetainedFrameOwner_does_not_change_existing_retained_frame_contract()
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
        using var owner = new SegmentedRetainedFrameOwner();
        using var ownerBatch = CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun));

        owner.ApplyFull(ownerBatch, tracker.CreateSnapshot(), VirtualNodeFactory.ScrollContainer(new NodeKey(1)));
        owner.Invalidate();

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
    public void SegmentedRetainedFrameOwner_applies_real_pipeline_batch_without_mutating_retained_frame()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var retainedCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedResources = pipeline.RetainedFrame.Resources;
        var retainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var retainedHitTargets = pipeline.RetainedFrame.HitTargets.ToArray();
        using var frame = new SegmentedRetainedFrameOwner();
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
    public void SegmentedRetainedFrameOwner_rehearses_accepted_cross_frame_partial_with_synchronized_segments_resources_and_root()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 120, 140, 40);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var retainedHitTargets = new[] { new HitTestTarget(buttonBounds, new ActionId(1)) };
        var snapshot = CreateStyleOnlySnapshot(viewport, buttonBounds, retainedRoot, retainedHitTargets, _arena.GetOrCreateSnapshot());
        var oldTracker = new SnapshotTracker("old");
        var replacementTracker = new SnapshotTracker("replacement");
        var replacementResolver = replacementTracker.CreateResolverOnly();
        using var frame = new SegmentedRetainedFrameOwner();
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
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, snapshot.DirtyClassifications, snapshot.PreviousTextSnapshot, snapshot.TextSnapshot);
        var accepted = frame.TryAcceptPartial(replacementBatch, replacementTracker.CreateSnapshot(replacementResolver), rootPatch);
        var reads = frame.ReadSegments();

        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, plan.Kind);
        Assert.True(hitTargetProjection.Succeeded);
        Assert.Equal(new ActionId(4), hitTargetProjection.HitTargets[0].ActionId);
        Assert.True(rootPatch.Succeeded);
        Assert.True(accepted);
        Assert.Equal(1, oldTracker.RetainCount);
        Assert.Equal(0, oldTracker.ReleaseCount);
        Assert.Equal(1, replacementTracker.RetainCount);
        Assert.Equal(0, replacementTracker.ReleaseCount);
        Assert.Equal(new ActionId(4), GetSingleProperty(frame.RetainedRoot.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(frame.RetainedRoot.Children[0], VirtualPropertyKey.IsHovered, true);
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
    public void SegmentedRetainedFrameOwner_rehearsal_failed_partial_falls_back_without_pre_fallback_mutation()
    {
        var oldTracker = new SnapshotTracker("old");
        var replacementTracker = new SnapshotTracker("replacement");
        var replacementResolver = replacementTracker.CreateResolverOnly();
        using var frame = new SegmentedRetainedFrameOwner();
        using var oldBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, [], replacementResolver, [(1, 1)]);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment v2", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        frame.ApplyFull(oldBatch, oldTracker.CreateSnapshot(), retainedRoot);
        var beforeSegments = frame.ResourceSegments.ToArray();
        var beforeRoot = frame.RetainedRoot;
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());
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
    public void SegmentedRetainedFrameShadowHarness_accepts_real_pipeline_partial_without_polluting_pipeline_retained_frame()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var shadow = new SegmentedRetainedFrameShadowHarness();
        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        shadow.ApplyFull(frame1, retainedRoot);
        using var frame2 = pipeline.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var retainedFrameCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedFrameResources = pipeline.RetainedFrame.Resources;
        var retainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var lastDirtyRanges = pipeline.LastDirtyCommandRanges.ToArray();
        var layoutRebuildCount = pipeline.LayoutRebuildCount;

        var result = shadow.TryAcceptPartial(snapshot, viewport, frame2, nextRoot);

        Assert.True(result.Accepted);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, result.Kind);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, result.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, result.Reason);
        Assert.Equal(3, shadow.Owner.CommandCount);
        Assert.Equal(new ActionId(4), GetSingleProperty(shadow.Owner.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(shadow.Owner.RetainedRoot.Children[1], VirtualPropertyKey.IsHovered, true);
        Assert.Equal(2, result.Reads.Count);
        var replacementRead = Assert.Single(result.Reads, segment => ReferenceEquals(frame2.Resources, segment.Resolver));
        var retainedRead = Assert.Single(result.Reads, segment => ReferenceEquals(frame1.Resources, segment.Resolver));
        var dirtyRange = Assert.Single(lastDirtyRanges);
        Assert.Equal(dirtyRange.Start, replacementRead.CommandStart);
        Assert.Equal(dirtyRange.Count, replacementRead.Commands.Length);
        Assert.Equal(shadow.Owner.CommandCount - dirtyRange.Count, retainedRead.Commands.Length);
        Assert.Equal(retainedFrameCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(lastDirtyRanges, pipeline.LastDirtyCommandRanges);
        Assert.Equal(layoutRebuildCount, pipeline.LayoutRebuildCount);
    }

    [Fact]
    public void SegmentedRetainedFrameShadowHarness_failed_partial_requires_explicit_full_fallback()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment v2", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var shadow = new SegmentedRetainedFrameShadowHarness();
        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        shadow.ApplyFull(frame1, retainedRoot);
        var beforeRoot = shadow.Owner.RetainedRoot;
        var beforeSegments = shadow.Owner.ResourceSegments.ToArray();
        using var frame2 = pipeline.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        var result = shadow.TryAcceptPartial(snapshot, viewport, frame2, nextRoot);

        Assert.False(result.Accepted);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, result.Kind);
        Assert.NotEqual(RetainedPartialApplyFallbackReason.None, result.Reason);
        Assert.Empty(result.Reads);
        Assert.Equal(beforeRoot, shadow.Owner.RetainedRoot);
        Assert.Equal(beforeSegments, shadow.Owner.ResourceSegments);

        shadow.ApplyFull(frame2, nextRoot);

        Assert.Equal(nextRoot, shadow.Owner.RetainedRoot);
        var segment = Assert.Single(shadow.Owner.ResourceSegments);
        Assert.Equal(0, segment.CommandStart);
        Assert.Equal(frame2.Commands.Count, segment.CommandCount);
        Assert.Same(frame2.Resources, segment.Snapshot.Resolver);
    }

    [Fact]
    public void SegmentedRetainedFrameShadowHarness_reports_rejected_without_mutation_when_owner_rejects_applied_plan()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 120, 140, 40);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var retainedHitTargets = new[] { new HitTestTarget(buttonBounds, new ActionId(1)) };
        var snapshot = CreateStyleOnlySnapshot(viewport, buttonBounds, retainedRoot, retainedHitTargets, _arena.GetOrCreateSnapshot());
        using var shadow = new SegmentedRetainedFrameShadowHarness();
        using var oldBatch = new RenderFrameBatch(
            CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun)),
            retainedHitTargets,
            new NamedResolver("old"));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, retainedHitTargets, new NamedResolver("replacement"), [(1, 1)]);

        shadow.ApplyFull(oldBatch, retainedRoot);
        var beforeRoot = shadow.Owner.RetainedRoot;
        var beforeSegments = shadow.Owner.ResourceSegments.ToArray();
        var result = shadow.TryAcceptPartial(snapshot, viewport, replacementBatch, nextRoot);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowRejected, result.Kind);
        Assert.False(result.Accepted);
        Assert.Equal(RetainedPartialApplyFallbackReason.UnstableCommandRange, result.Reason);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, result.PlanKind);
        Assert.Empty(result.Reads);
        Assert.Equal(beforeRoot, shadow.Owner.RetainedRoot);
        Assert.Equal(beforeSegments, shadow.Owner.ResourceSegments);
    }

    [Fact]
    public async Task SegmentedRetainedFrameDiagnosticHarness_disabled_matches_production_pipeline_compositor_backend_and_hit_test()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));

        var directPipeline = new RenderPipeline();
        using var directBatch = directPipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directBackend = new CapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directBatch, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(20, 130, out var directActionId);

        var diagnosticPipeline = new RenderPipeline();
        using var diagnosticHarness = new SegmentedRetainedFrameDiagnosticHarness(diagnosticPipeline, RenderPipelineShadowOptions.Disabled);
        using var diagnosticBatch = diagnosticHarness.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var diagnosticBackend = new CapturingBackend();
        using var diagnosticCompositor = new DrawingBackendCompositor(diagnosticBackend);
        await diagnosticCompositor.RenderAsync(diagnosticBatch, cancellationToken);
        var diagnosticHit = diagnosticCompositor.TryGetActionIdAtPhysicalPixel(20, 130, out var diagnosticActionId);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.Disabled, diagnosticHarness.LastShadowResult.Kind);
        Assert.False(diagnosticHarness.HasSegmentedRetainedFrameOwner);
        Assert.Null(diagnosticHarness.SegmentedRetainedFrameOwner);
        Assert.Equal(directPipeline.LayoutRebuildCount, diagnosticPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastViewport, diagnosticPipeline.LastViewport);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, diagnosticPipeline.LastDirtyCommandRanges);
        Assert.Equal(directPipeline.RetainedFrame.CommandCount, diagnosticPipeline.RetainedFrame.CommandCount);
        Assert.Equal(directPipeline.RetainedFrame.DirtyCommandRanges, diagnosticPipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(directCompositor.RenderCount, diagnosticCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, diagnosticCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, diagnosticCompositor.PartialApplyCount);
        Assert.Equal(directBackend.ExecuteCalls.Count, diagnosticBackend.ExecuteCalls.Count);
        Assert.Equal(directBackend.ExecuteCalls[0].CommandCount, diagnosticBackend.ExecuteCalls[0].CommandCount);
        Assert.Equal(directHit, diagnosticHit);
        Assert.Equal(directActionId, diagnosticActionId);
    }

    [Fact]
    public void SegmentedRetainedFrameDiagnosticHarness_opt_in_runs_real_batch_shadow_flow_without_rendering_shadow_result()
    {
        var pipeline = new RenderPipeline();
        using var diagnosticHarness = new SegmentedRetainedFrameDiagnosticHarness(pipeline, RenderPipelineShadowOptions.SegmentedRetainedFrameEnabled);
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = diagnosticHarness.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var fullResult = diagnosticHarness.LastShadowResult;
        using var frame2 = diagnosticHarness.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var partialResult = diagnosticHarness.LastShadowResult;
        var retainedFrameCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedFrameResources = pipeline.RetainedFrame.Resources;
        var retainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var compositorRenderCount = compositor.RenderCount;
        var compositorFullApplyCount = compositor.FullApplyCount;
        var compositorPartialApplyCount = compositor.PartialApplyCount;
        var adapterBackend = new CapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(adapterBackend);

        adapter.Execute(new FrameContext(960, 540), partialResult.Reads);

        Assert.True(diagnosticHarness.HasSegmentedRetainedFrameOwner);
        Assert.NotNull(diagnosticHarness.SegmentedRetainedFrameOwner);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fullResult.Kind);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, partialResult.Kind);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, partialResult.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, partialResult.Reason);
        Assert.Equal(2, partialResult.Reads.Count);
        Assert.Collection(adapterBackend.ExecuteCalls,
            call => Assert.Same(frame1.Resources, call.Resolver),
            call => Assert.Same(frame2.Resources, call.Resolver));
        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(2), EndFrameCall], adapterBackend.Calls);
        Assert.Equal(retainedFrameCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(compositorRenderCount, compositor.RenderCount);
        Assert.Equal(compositorFullApplyCount, compositor.FullApplyCount);
        Assert.Equal(compositorPartialApplyCount, compositor.PartialApplyCount);
    }

    [Fact]
    public async Task SegmentedRetainedFrameProductionOwnerFeed_default_off_matches_production_path_and_diagnostics_text()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directBatch = directPipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directHitTarget = Assert.Single(directBatch.HitTargets);
        var hitTestX = directHitTarget.Bounds.X + 1;
        var hitTestY = directHitTarget.Bounds.Y + 1;
        var directBackend = new CapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directBatch, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.Disabled);
        using var feedBatch = feed.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var feedBackend = new CapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        await feedCompositor.RenderAsync(feedBatch, cancellationToken);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var feedActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.Disabled, feed.LastResult.Kind);
        Assert.Null(feed.SegmentOwnership);
        Assert.False(feed.HasRuntimeOwner);
        Assert.Null(feed.RuntimeOwner);
        Assert.Equal(directPipeline.LayoutRebuildCount, feedPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastViewport, feedPipeline.LastViewport);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, feedPipeline.LastDirtyCommandRanges);
        Assert.Equal(directPipeline.RetainedFrame.CommandCount, feedPipeline.RetainedFrame.CommandCount);
        Assert.Equal(directPipeline.RetainedFrame.DirtyCommandRanges, feedPipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directBackend.ExecuteCalls.Count, feedBackend.ExecuteCalls.Count);
        Assert.Equal(directBackend.ExecuteCalls[0].CommandCount, feedBackend.ExecuteCalls[0].CommandCount);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SegmentedRetainedFrameProductionOwnerFeed_visible_outputs_match_production_in_default_off_and_enabled_secondary_modes(bool enabled)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directFrame1 = directPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var directFrame2 = directPipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var hitTarget = Assert.Single(directFrame2.HitTargets);
        var hitTestX = hitTarget.Bounds.X + 1;
        var hitTestY = hitTarget.Bounds.Y + 1;
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame1, cancellationToken);
        await directCompositor.RenderAsync(directFrame2, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        var options = enabled
            ? RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled
            : RenderPipelineProductionOwnerOptions.Disabled;
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, options);
        using var feedFrame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var feedFrame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        await feedCompositor.RenderAsync(feedFrame1, cancellationToken);
        await feedCompositor.RenderAsync(feedFrame2, cancellationToken);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var feedActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Equal(directPipeline.LayoutRebuildCount, feedPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastViewport, feedPipeline.LastViewport);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, feedPipeline.LastDirtyCommandRanges);
        Assert.Equal(directPipeline.RetainedFrame.CommandCount, feedPipeline.RetainedFrame.CommandCount);
        Assert.Equal(directPipeline.RetainedFrame.DirtyCommandRanges, feedPipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directCompositor.EmptyFrameCount, feedCompositor.EmptyFrameCount);
        Assert.Equal(directCompositor.LastDirtyCommandRanges, feedCompositor.LastDirtyCommandRanges);
        Assert.Equal(directBackend.SetDirtyCommandRangeCount, feedBackend.SetDirtyCommandRangeCount);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
        Assert.True(directHit);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(new ActionId(4), feedActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
        if (enabled)
        {
            Assert.True(feed.HasRuntimeOwner);
            Assert.NotNull(feed.SegmentOwnership);
            Assert.Same(feedPipeline.RetainedFrame, feed.SegmentOwnership.RetainedFrame);
            Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, feed.LastResult.Kind);
            Assert.NotSame(feed.RuntimeOwner, feedCompositor.RetainedFrame);
        }
        else
        {
            Assert.Equal(SegmentedRetainedFrameShadowResultKind.Disabled, feed.LastResult.Kind);
            Assert.Null(feed.SegmentOwnership);
            Assert.False(feed.HasRuntimeOwner);
            Assert.Null(feed.RuntimeOwner);
        }
    }

    [Fact]
    public void RetainedRenderFrameSegmentOwnership_disabled_preserves_try_read_frame_contract_and_has_no_segmented_owner()
    {
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var resources = new NamedResolver("frame");
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun));
        using var batch = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(16, 16, 140, 40), new ActionId(1))],
            resources,
            [(0, 1)]);
        using var retainedFrame = new RetainedRenderFrame();
        retainedFrame.ApplyFull(batch);
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Disabled);

        var result = ownership.Update(null, root, new PixelRectangle(0, 0, 960, 540), batch);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.Disabled, result.Kind);
        Assert.False(result.RuntimeOwnerEnabled);
        Assert.False(ownership.HasSegmentedOwner);
        Assert.Null(ownership.RuntimeOwner);
        Assert.Same(retainedFrame, ownership.RetainedFrame);
        Assert.True(retainedFrame.TryReadFrame(out var readCommands, out var readResources));
        Assert.Equal(1, readCommands.Length);
        Assert.Same(resources, readResources);
        Assert.Single(retainedFrame.HitTargets);

        ownership.Dispose();

        Assert.True(retainedFrame.TryReadFrame(out var afterDisposeCommands, out var afterDisposeResources));
        Assert.Equal(1, afterDisposeCommands.Length);
        Assert.Same(resources, afterDisposeResources);
    }

    [Fact]
    public void RetainedRenderFrameSegmentOwnership_enabled_syncs_secondary_owner_with_full_partial_fallback_and_dispose_without_becoming_render_source()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment fallback", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(5)),
                VirtualNodeProperty.Hovered(false)));
        using var ownership = new RetainedRenderFrameSegmentOwnership(pipeline.RetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);

        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var fullResult = ownership.Update(pipeline.LastRetainedInputSnapshot, retainedRoot, viewport, frame1);
        using var frame2 = pipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var partialResult = ownership.Update(pipeline.LastRetainedInputSnapshot, partialRoot, viewport, frame2);

        Assert.Same(pipeline.RetainedFrame, ownership.RetainedFrame);
        Assert.True(ownership.HasSegmentedOwner);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fullResult.Kind);
        Assert.False(fullResult.FallbackApplied);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, partialResult.Kind);
        Assert.False(partialResult.FallbackApplied);
        Assert.True(partialResult.OwnerStatePreservedBeforeFallback);
        Assert.Collection(ownership.RuntimeOwner!.ResourceSegments,
            segment => Assert.Same(frame1.Resources, segment.Snapshot.Resolver),
            segment => Assert.Same(frame2.Resources, segment.Snapshot.Resolver));
        Assert.Equal(new ActionId(4), GetSingleProperty(ownership.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        var partialHitTarget = Assert.Single(ownership.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(4), partialHitTarget.ActionId);
        Assert.True(pipeline.RetainedFrame.TryReadFrame(out var retainedCommands, out var retainedResources));
        Assert.Equal(frame2.Commands.Count, retainedCommands.Length);
        Assert.Same(frame2.Resources, retainedResources);

        using var frame3 = pipeline.Build(fallbackRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var fallbackResult = ownership.Update(pipeline.LastRetainedInputSnapshot, fallbackRoot, viewport, frame3);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.True(fallbackResult.FallbackApplied);
        Assert.True(fallbackResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, fallbackResult.ShadowResult.Reason);
        var fallbackSegment = Assert.Single(ownership.RuntimeOwner.ResourceSegments);
        Assert.Same(frame3.Resources, fallbackSegment.Snapshot.Resolver);
        var fallbackHitTarget = Assert.Single(ownership.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(5), fallbackHitTarget.ActionId);
        Assert.Equal(new ActionId(5), GetSingleProperty(ownership.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        Assert.True(pipeline.RetainedFrame.TryReadFrame(out var fallbackCommands, out var fallbackResources));
        Assert.Equal(frame3.Commands.Count, fallbackCommands.Length);
        Assert.Same(frame3.Resources, fallbackResources);

        ownership.Dispose();

        Assert.True(pipeline.RetainedFrame.TryReadFrame(out var afterDisposeCommands, out var afterDisposeResources));
        Assert.Equal(frame3.Commands.Count, afterDisposeCommands.Length);
        Assert.Same(frame3.Resources, afterDisposeResources);
    }

    [Fact]
    public void RetainedRenderFrameSegmentOwnership_retains_and_releases_frame_resource_snapshots_once_across_lifecycle_paths()
    {
        var tracker = new FrameResourceSnapshotCaptureTracker();
        using var retainedFrame = new RetainedRenderFrame();
        using var retainedCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var retainedBatch = new RenderFrameBatch(retainedCommands, [], new NamedResolver("retained"), [(0, 1)]);
        retainedFrame.ApplyFull(retainedBatch);
        var retainedCommandCount = retainedFrame.CommandCount;
        var retainedResources = retainedFrame.Resources;
        var retainedDirtyRanges = retainedFrame.DirtyCommandRanges.ToArray();
        var options = new RetainedRenderFrameSegmentOwnershipOptions(true, tracker.Capture);
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, options);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var changedViewport = new PixelRectangle(0, 0, 640, 480);
        var buttonBounds = new PixelRectangle(16, 16, 140, 40);
        var root1 = CreateActionButtonRoot(new ActionId(101));
        var root2 = CreateActionButtonRoot(new ActionId(102));
        var root3 = CreateActionButtonRoot(new ActionId(103));
        var root4 = CreateActionButtonRoot(new ActionId(104));
        var root6 = CreateActionButtonRoot(new ActionId(106));
        var root7 = CreateActionButtonRoot(new ActionId(107));
        var emptyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1));

        using var frame1 = CreateFrameResourceTextBatch("one", [new HitTestTarget(buttonBounds, new ActionId(101))], [], commandCount: 2);
        var fullResult = ownership.Update(null, root1, viewport, frame1);
        frame1.Dispose();

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fullResult.Kind);
        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 0);
        Assert.Equal("one", ResolveSegmentText(Assert.Single(ownership.RuntimeOwner!.ReadSegments())));

        using var frame2 = CreateFrameResourceTextBatch("two", [new HitTestTarget(buttonBounds, new ActionId(102))], [(1, 1)], commandCount: 2);
        var snapshot1 = CreateDirtySnapshot(viewport, buttonBounds, root1, ownership.RuntimeOwner.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot());
        var partialResult = ownership.Update(snapshot1, root2, viewport, frame2);
        frame2.Dispose();

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, partialResult.Kind);
        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 0);
        AssertFrameResourceCapture(tracker.Captures[1], frame2.Resources, retainCount: 1, releaseCount: 0);
        Assert.Collection(ownership.RuntimeOwner.ReadSegments(),
            segment => Assert.Equal("one", ResolveSegmentText(segment)),
            segment => Assert.Equal("two", ResolveSegmentText(segment)));

        using var frame3 = CreateFrameResourceTextBatch("three", [new HitTestTarget(buttonBounds, new ActionId(103))], [(2, 1)], commandCount: 2);
        var snapshot2 = CreateDirtySnapshot(viewport, buttonBounds, root2, ownership.RuntimeOwner.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot());
        var rejectedFallbackResult = ownership.Update(snapshot2, root3, viewport, frame3);
        frame3.Dispose();

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, rejectedFallbackResult.Kind);
        Assert.True(rejectedFallbackResult.FallbackApplied);
        Assert.True(rejectedFallbackResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(RetainedPartialApplyFallbackReason.UnstableCommandRange, rejectedFallbackResult.ShadowResult.Reason);
        Assert.Equal(4, tracker.Captures.Count);
        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[1], frame2.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[2], frame3.Resources, retainCount: 0, releaseCount: 0);
        AssertFrameResourceCapture(tracker.Captures[3], frame3.Resources, retainCount: 1, releaseCount: 0);
        Assert.Equal("three", ResolveSegmentText(Assert.Single(ownership.RuntimeOwner.ReadSegments())));

        using var frame4 = CreateFrameResourceTextBatch("four", [new HitTestTarget(buttonBounds, new ActionId(104))], [(1, 1)], commandCount: 2);
        var snapshot3 = CreateDirtySnapshot(viewport, buttonBounds, root3, ownership.RuntimeOwner.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot());
        var fallbackResult = ownership.Update(snapshot3, root4, changedViewport, frame4);
        frame4.Dispose();

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.True(fallbackResult.FallbackApplied);
        Assert.Equal(RetainedPartialApplyFallbackReason.ViewportChanged, fallbackResult.ShadowResult.Reason);
        Assert.Equal(5, tracker.Captures.Count);
        AssertFrameResourceCapture(tracker.Captures[3], frame3.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[4], frame4.Resources, retainCount: 1, releaseCount: 0);
        Assert.Equal("four", ResolveSegmentText(Assert.Single(ownership.RuntimeOwner.ReadSegments())));

        using var emptyFrame = CreateFrameResourceTextBatch("", [], [], commandCount: 0);
        var emptyResult = ownership.Update(null, emptyRoot, viewport, emptyFrame);
        emptyFrame.Dispose();

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, emptyResult.Kind);
        Assert.False(emptyResult.FallbackApplied);
        Assert.Empty(emptyResult.ShadowResult.Reads);
        Assert.Equal(0, ownership.RuntimeOwner.CommandCount);
        Assert.Empty(ownership.RuntimeOwner.ResourceSegments);
        AssertFrameResourceCapture(tracker.Captures[4], frame4.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[5], emptyFrame.Resources, retainCount: 0, releaseCount: 0);

        using var frame6 = CreateFrameResourceTextBatch("six", [new HitTestTarget(buttonBounds, new ActionId(106))], [], commandCount: 1);
        ownership.Update(null, root6, viewport, frame6);
        frame6.Dispose();
        AssertFrameResourceCapture(tracker.Captures[6], frame6.Resources, retainCount: 1, releaseCount: 0);
        ownership.InvalidateSegmentedOwner();
        ownership.InvalidateSegmentedOwner();

        Assert.Equal(0, ownership.RuntimeOwner.CommandCount);
        Assert.Empty(ownership.RuntimeOwner.ResourceSegments);
        AssertFrameResourceCapture(tracker.Captures[6], frame6.Resources, retainCount: 1, releaseCount: 1);

        using var frame7 = CreateFrameResourceTextBatch("seven", [new HitTestTarget(buttonBounds, new ActionId(107))], [], commandCount: 1);
        ownership.Update(null, root7, viewport, frame7);
        frame7.Dispose();
        ownership.Dispose();
        ownership.Dispose();

        AssertFrameResourceCapture(tracker.Captures[7], frame7.Resources, retainCount: 1, releaseCount: 1);
        Assert.All(tracker.Captures, capture => Assert.Equal(capture.RetainCount, capture.ReleaseCount));
        Assert.Equal(retainedCommandCount, retainedFrame.CommandCount);
        Assert.Same(retainedResources, retainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, retainedFrame.DirtyCommandRanges);
    }

    [Fact]
    public void RetainedRenderFrameSegmentOwnership_repeated_replacement_releases_replaced_snapshots_once_without_touching_retained_frame()
    {
        var tracker = new FrameResourceSnapshotCaptureTracker();
        using var retainedFrame = new RetainedRenderFrame();
        using var retainedCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var retainedBatch = new RenderFrameBatch(retainedCommands, [], new NamedResolver("retained"), [(0, 1)]);
        retainedFrame.ApplyFull(retainedBatch);
        var retainedCommandCount = retainedFrame.CommandCount;
        var retainedResources = retainedFrame.Resources;
        var options = new RetainedRenderFrameSegmentOwnershipOptions(true, tracker.Capture);
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, options);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 16, 140, 40);
        var root1 = CreateActionButtonRoot(new ActionId(101));
        var root2 = CreateActionButtonRoot(new ActionId(102));
        var root3 = CreateActionButtonRoot(new ActionId(103));
        var root4 = CreateActionButtonRoot(new ActionId(104));

        using var frame1 = CreateFrameResourceTextBatch("one", [new HitTestTarget(buttonBounds, new ActionId(101))], [], commandCount: 2);
        ownership.Update(null, root1, viewport, frame1);
        frame1.Dispose();
        using var frame2 = CreateFrameResourceTextBatch("two", [new HitTestTarget(buttonBounds, new ActionId(102))], [(1, 1)], commandCount: 2);
        ownership.Update(CreateDirtySnapshot(viewport, buttonBounds, root1, ownership.RuntimeOwner!.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot()), root2, viewport, frame2);
        frame2.Dispose();
        using var frame3 = CreateFrameResourceTextBatch("three", [new HitTestTarget(buttonBounds, new ActionId(103))], [(1, 1)], commandCount: 2);
        ownership.Update(CreateDirtySnapshot(viewport, buttonBounds, root2, ownership.RuntimeOwner.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot()), root3, viewport, frame3);
        frame3.Dispose();
        using var frame4 = CreateFrameResourceTextBatch("four", [new HitTestTarget(buttonBounds, new ActionId(104))], [(0, 1)], commandCount: 2);
        ownership.Update(CreateDirtySnapshot(viewport, buttonBounds, root3, ownership.RuntimeOwner.HitTargets, LayoutRebuildReason.StyleOnly, _arena.GetOrCreateSnapshot()), root4, viewport, frame4);
        frame4.Dispose();

        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[1], frame2.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[2], frame3.Resources, retainCount: 1, releaseCount: 0);
        AssertFrameResourceCapture(tracker.Captures[3], frame4.Resources, retainCount: 1, releaseCount: 0);
        Assert.Collection(ownership.RuntimeOwner.ReadSegments(),
            segment => Assert.Equal("four", ResolveSegmentText(segment)),
            segment => Assert.Equal("three", ResolveSegmentText(segment)));
        Assert.Equal(retainedCommandCount, retainedFrame.CommandCount);
        Assert.Same(retainedResources, retainedFrame.Resources);

        ownership.Dispose();

        AssertFrameResourceCapture(tracker.Captures[2], frame3.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[3], frame4.Resources, retainCount: 1, releaseCount: 1);
        Assert.All(tracker.Captures, capture => Assert.Equal(capture.RetainCount, capture.ReleaseCount));
    }

    [Fact]
    public async Task RetainedRenderFrameSegmentOwnership_owner_side_hit_test_matches_production_for_hit_clip_and_no_hit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        var hitTarget = new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(109), new PixelRectangle(10, 10, 20, 20));
        using var batch = new RenderFrameBatch(commands, [hitTarget], new NamedResolver("frame"), [(0, 1)]);
        var root = CreateActionButtonRoot(new ActionId(109));
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());

        ownership.Update(null, root, new PixelRectangle(0, 0, 100, 100), batch);
        await compositor.RenderAsync(batch, cancellationToken);

        AssertOwnerAndProductionHit(ownership, compositor, 11, 11, expectedHit: true, new ActionId(109));
        AssertOwnerAndProductionHit(ownership, compositor, 5, 5, expectedHit: false, ActionId.None);
        AssertOwnerAndProductionHit(ownership, compositor, 120, 120, expectedHit: false, ActionId.None);
    }

    [Fact]
    public async Task RetainedRenderFrameSegmentOwnership_owner_side_hit_test_tracks_dirty_action_id_projection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeline = new RenderPipeline();
        using var ownership = new RetainedRenderFrameSegmentOwnership(pipeline.RetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var nextRoot = CreateActionButtonRoot(new ActionId(4));

        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        ownership.Update(pipeline.LastRetainedInputSnapshot, retainedRoot, viewport, frame1);
        await compositor.RenderAsync(frame1, cancellationToken);
        var hitTarget = Assert.Single(frame1.HitTargets);
        using var frame2 = pipeline.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var result = ownership.Update(pipeline.LastRetainedInputSnapshot, nextRoot, viewport, frame2);
        await compositor.RenderAsync(frame2, cancellationToken);
        var hitX = hitTarget.Bounds.X + 1;
        var hitY = hitTarget.Bounds.Y + 1;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, result.Kind);
        AssertOwnerAndProductionHit(ownership, compositor, hitX, hitY, expectedHit: true, new ActionId(4));
    }

    [Fact]
    public async Task RetainedRenderFrameSegmentOwnership_owner_side_hit_test_syncs_fallback_targets_after_projection_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeline = new RenderPipeline();
        using var ownership = new RetainedRenderFrameSegmentOwnership(pipeline.RetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Primary", new NodeKey(10),
                VirtualNodeProperty.Action(new ActionId(200)),
                VirtualNodeProperty.Hovered(false)),
            VirtualNodeBuilder.Button(_arena, "Secondary", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(201)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Primary", new NodeKey(10),
                VirtualNodeProperty.Action(new ActionId(202)),
                VirtualNodeProperty.Hovered(false)),
            VirtualNodeBuilder.Button(_arena, "Secondary", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(201)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        ownership.Update(pipeline.LastRetainedInputSnapshot, retainedRoot, viewport, frame1);
        await compositor.RenderAsync(frame1, cancellationToken);
        var primaryHitTarget = frame1.HitTargets[0];
        using var frame2 = pipeline.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [3]);
        var result = ownership.Update(pipeline.LastRetainedInputSnapshot, nextRoot, viewport, frame2);
        await compositor.RenderAsync(frame2, cancellationToken);
        var hitX = primaryHitTarget.Bounds.X + 1;
        var hitY = primaryHitTarget.Bounds.Y + 1;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, result.Kind);
        Assert.True(result.FallbackApplied);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, result.ShadowResult.Reason);
        Assert.True(ownership.TryGetSegmentedOwnerActionIdAt(hitX, hitY, out var ownerActionId));
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var productionActionId));
        Assert.Equal(new ActionId(202), ownerActionId);
        Assert.Equal(new ActionId(202), productionActionId);
    }

    [Fact]
    public void RetainedRenderFrameHandoffCounterSemantics_describe_selected_render_source_counters()
    {
        var semantics = RetainedRenderFrameHandoffCounterSemantics.All;
        var expectedIds = Enum.GetValues<RetainedRenderFrameHandoffCounterId>();

        Assert.Equal(expectedIds.Length, semantics.Count);
        Assert.All(semantics, semantic => Assert.True(semantic.ExistingCounterBehaviorUnchanged));
        Assert.Equal(expectedIds, semantics.Select(semantic => semantic.CounterId).ToArray());
    }

    [Fact]
    public void SegmentedBackendDirtyRangeHandoffPlanner_maps_retained_dirty_ranges_to_segment_local_ranges_without_backend_contract_change()
    {
        var resolver = new NamedResolver("segment");
        var reads = new[]
        {
            new SegmentedFrameRead(0, [new DrawCommand(DrawCommandKind.FillRect), new DrawCommand(DrawCommandKind.FillRect)], resolver),
            new SegmentedFrameRead(2, [new DrawCommand(DrawCommandKind.FillRect), new DrawCommand(DrawCommandKind.FillRect), new DrawCommand(DrawCommandKind.FillRect)], resolver),
            new SegmentedFrameRead(5, [new DrawCommand(DrawCommandKind.FillRect)], resolver)
        };
        var backend = new DirtyRangeAwareCapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        var plan = SegmentedBackendDirtyRangeHandoffPlanner.Plan(reads, [(1, 3), (5, 1)]);
        adapter.Execute(new FrameContext(100, 100), reads);

        Assert.Collection(plan,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Equal(2, segment.CommandCount);
                Assert.Equal([(1, 1)], segment.SegmentDirtyRanges);
            },
            segment =>
            {
                Assert.Equal(2, segment.CommandStart);
                Assert.Equal(3, segment.CommandCount);
                Assert.Equal([(0, 2)], segment.SegmentDirtyRanges);
            },
            segment =>
            {
                Assert.Equal(5, segment.CommandStart);
                Assert.Equal(1, segment.CommandCount);
                Assert.Equal([(0, 1)], segment.SegmentDirtyRanges);
            });
        Assert.Equal(0, backend.SetDirtyCommandRangeCount);
        Assert.Empty(backend.DirtyRanges);
        Assert.Equal([BeginFrameCall, ExecuteCall(2), ExecuteCall(3), ExecuteCall(1), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_default_off_does_not_execute_or_change_production_outputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = CreateActionButtonRoot(new ActionId(1));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directFrame = directPipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directHitTarget = Assert.Single(directFrame.HitTargets);
        var hitX = directHitTarget.Bounds.X + 1;
        var hitY = directHitTarget.Bounds.Y + 1;
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var feedFrame = feed.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        await feedCompositor.RenderAsync(feedFrame, cancellationToken);
        var renderCountBeforeHarness = feedCompositor.RenderCount;
        var fullApplyCountBeforeHarness = feedCompositor.FullApplyCount;
        var partialApplyCountBeforeHarness = feedCompositor.PartialApplyCount;
        var harnessBackend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(harnessBackend, RetainedRenderFrameHandoffHarnessOptions.Disabled);

        var result = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), feedPipeline.LastDirtyCommandRanges);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var feedActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Disabled, result.Kind);
        Assert.Empty(result.Reads);
        Assert.Empty(result.DirtyRangePlan);
        Assert.Equal(0, result.Counters.RenderCount);
        Assert.Equal(0, result.Counters.FullApplyCount);
        Assert.Equal(0, result.Counters.PartialApplyCount);
        Assert.Equal(0, result.Counters.EmptyFrameCount);
        Assert.Empty(harnessBackend.Calls);
        Assert.Empty(harnessBackend.ExecuteCalls);
        Assert.Equal(0, harnessBackend.SetDirtyCommandRangeCount);
        Assert.Equal(renderCountBeforeHarness, feedCompositor.RenderCount);
        Assert.Equal(fullApplyCountBeforeHarness, feedCompositor.FullApplyCount);
        Assert.Equal(partialApplyCountBeforeHarness, feedCompositor.PartialApplyCount);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directCompositor.LastDirtyCommandRanges, feedCompositor.LastDirtyCommandRanges);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
    }

    [Fact]
    public void RetainedRenderFrameHandoffHarness_enabled_missing_owner_does_not_execute_or_change_counters()
    {
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Disabled);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(backend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var result = harness.ExecuteCandidateFrame(ownership, new FrameContext(100, 100), [(0, 1)]);
        var hit = harness.TryGetActionIdAtLogicalPixel(1, 1, out var actionId);

        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.MissingSegmentedOwner, result.Kind);
        Assert.Empty(result.Reads);
        Assert.Empty(result.DirtyRangePlan);
        Assert.Equal(0, result.Counters.RenderCount);
        Assert.Equal(0, result.Counters.FullApplyCount);
        Assert.Equal(0, result.Counters.PartialApplyCount);
        Assert.Equal(0, result.Counters.EmptyFrameCount);
        Assert.False(result.Counters.LastPartialApplySucceeded);
        Assert.Empty(backend.Calls);
        Assert.Empty(backend.ExecuteCalls);
        Assert.Equal(0, backend.SetDirtyCommandRangeCount);
        Assert.False(hit);
        Assert.True(actionId.IsNone);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_enabled_executes_candidate_source_without_changing_production_outputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directFrame1 = directPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var directFrame2 = directPipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame1, cancellationToken);
        await directCompositor.RenderAsync(directFrame2, cancellationToken);
        var directHitTarget = Assert.Single(directFrame2.HitTargets);
        var hitX = directHitTarget.Bounds.X + 1;
        var hitY = directHitTarget.Bounds.Y + 1;
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        var harnessBackend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(harnessBackend, RetainedRenderFrameHandoffHarnessOptions.Enabled);
        using var feedFrame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await feedCompositor.RenderAsync(feedFrame1, cancellationToken);
        var productionRenderCountAfterFrame1 = feedCompositor.RenderCount;
        var fullResult = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), feedPipeline.LastDirtyCommandRanges);
        using var feedFrame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        await feedCompositor.RenderAsync(feedFrame2, cancellationToken);
        var productionRenderCountAfterFrame2 = feedCompositor.RenderCount;
        var partialResult = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), feedPipeline.LastDirtyCommandRanges);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var feedActionId);
        var harnessHit = harness.TryGetActionIdAtLogicalPixel(hitX, hitY, out var harnessActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Equal(productionRenderCountAfterFrame1, feedCompositor.RenderCount - 1);
        Assert.Equal(productionRenderCountAfterFrame2, feedCompositor.RenderCount);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, fullResult.Kind);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, partialResult.Kind);
        Assert.Single(fullResult.Reads);
        Assert.Equal(2, partialResult.Reads.Count);
        Assert.Equal(2, partialResult.Counters.RenderCount);
        Assert.Equal(1, partialResult.Counters.FullApplyCount);
        Assert.Equal(1, partialResult.Counters.PartialApplyCount);
        Assert.Equal(0, partialResult.Counters.EmptyFrameCount);
        Assert.Equal(feedPipeline.LastDirtyCommandRanges, partialResult.Counters.LastDirtyCommandRanges);
        Assert.True(partialResult.Counters.LastPartialApplySucceeded);
        Assert.All(partialResult.CounterSemantics, semantic => Assert.True(semantic.ExistingCounterBehaviorUnchanged));
        Assert.Equal(
            Enum.GetValues<RetainedRenderFrameHandoffCounterId>(),
            partialResult.CounterSemantics.Select(semantic => semantic.CounterId).ToArray());
        AssertHandoffBackendCallsMatchReads([fullResult, partialResult], harnessBackend);
        AssertDirtyRangePlansMatchBackend([fullResult, partialResult], harnessBackend);
        Assert.True(harnessHit);
        Assert.Equal(new ActionId(4), harnessActionId);
        Assert.Equal(directPipeline.LayoutRebuildCount, feedPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, feedPipeline.LastDirtyCommandRanges);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directCompositor.LastDirtyCommandRanges, feedCompositor.LastDirtyCommandRanges);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(directActionId, harnessActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
    }

    [Fact]
    public void RetainedRenderFrameHandoffHarness_enabled_uses_owner_side_clip_and_no_hit_metadata()
    {
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var batch = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(203), new PixelRectangle(10, 10, 20, 20))],
            new NamedResolver("clipped"),
            [(0, 1)]);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Clipped", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(203))));
        ownership.Update(null, root, new PixelRectangle(0, 0, 100, 100), batch);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(backend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var result = harness.ExecuteCandidateFrame(ownership, new FrameContext(100, 100), [(0, 1)]);
        var inClipHit = harness.TryGetActionIdAtLogicalPixel(15, 15, out var inClipActionId);
        var outsideClipHit = harness.TryGetActionIdAtLogicalPixel(5, 5, out var outsideClipActionId);
        var outsideBoundsHit = harness.TryGetActionIdAtLogicalPixel(101, 101, out var outsideBoundsActionId);

        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, result.Kind);
        Assert.Single(result.Reads);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), EndFrameCall], backend.Calls);
        Assert.Equal(1, backend.SetDirtyCommandRangeCount);
        Assert.Single(backend.DirtyRanges);
        Assert.Equal([(0, 1)], backend.DirtyRanges[0]);
        Assert.True(inClipHit);
        Assert.Equal(new ActionId(203), inClipActionId);
        Assert.False(outsideClipHit);
        Assert.True(outsideClipActionId.IsNone);
        Assert.False(outsideBoundsHit);
        Assert.True(outsideBoundsActionId.IsNone);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_enabled_routes_dirty_range_mismatch_as_empty_segment_local_ranges_without_production_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var productionBackend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(productionBackend);
        await compositor.RenderAsync(frame1, cancellationToken);
        await compositor.RenderAsync(frame2, cancellationToken);
        var renderCount = compositor.RenderCount;
        var fullApplyCount = compositor.FullApplyCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var productionDirtyRanges = compositor.LastDirtyCommandRanges.ToArray();
        var productionBackendDirtyRanges = productionBackend.DirtyRanges.ToArray();
        var mismatchedDirtyRanges = new[] { (-10, 2), (100, 4), (1, 0) };
        var harnessBackend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(harnessBackend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var result = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), mismatchedDirtyRanges);

        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, result.Kind);
        Assert.Equal(2, result.Reads.Count);
        Assert.Equal(result.Reads.Count, result.DirtyRangePlan.Count);
        Assert.Equal(mismatchedDirtyRanges, result.Counters.LastDirtyCommandRanges);
        Assert.Equal(1, result.Counters.RenderCount);
        Assert.Equal(0, result.Counters.FullApplyCount);
        Assert.Equal(1, result.Counters.PartialApplyCount);
        Assert.True(result.Counters.LastPartialApplySucceeded);
        AssertHandoffBackendCallsMatchReads([result], harnessBackend);
        Assert.Equal(result.Reads.Count, harnessBackend.SetDirtyCommandRangeCount);
        Assert.Equal(result.Reads.Count, harnessBackend.DirtyRanges.Count);
        Assert.All(result.DirtyRangePlan, segment => Assert.Empty(segment.SegmentDirtyRanges));
        Assert.All(harnessBackend.DirtyRanges, Assert.Empty);
        Assert.Equal(renderCount, compositor.RenderCount);
        Assert.Equal(fullApplyCount, compositor.FullApplyCount);
        Assert.Equal(partialApplyCount, compositor.PartialApplyCount);
        Assert.Equal(productionDirtyRanges, compositor.LastDirtyCommandRanges);
        Assert.Equal(productionBackendDirtyRanges, productionBackend.DirtyRanges);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_enabled_handles_empty_frame_without_backend_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var emptyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1));

        var directPipeline = new RenderPipeline();
        using var directFrame1 = directPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var directEmptyFrame = directPipeline.Build(emptyRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame1, cancellationToken);
        await directCompositor.RenderAsync(directEmptyFrame, cancellationToken);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var feedFrame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var feedEmptyFrame = feed.Build(emptyRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        await feedCompositor.RenderAsync(feedFrame1, cancellationToken);
        await feedCompositor.RenderAsync(feedEmptyFrame, cancellationToken);
        var harnessBackend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(harnessBackend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var emptyResult = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), feedPipeline.LastDirtyCommandRanges);
        var harnessHit = harness.TryGetActionIdAtLogicalPixel(20, 20, out var harnessActionId);

        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.EmptyFrame, emptyResult.Kind);
        Assert.Empty(emptyResult.Reads);
        Assert.Empty(emptyResult.DirtyRangePlan);
        Assert.Equal(0, emptyResult.Counters.RenderCount);
        Assert.Equal(0, emptyResult.Counters.FullApplyCount);
        Assert.Equal(0, emptyResult.Counters.PartialApplyCount);
        Assert.Equal(1, emptyResult.Counters.EmptyFrameCount);
        Assert.False(emptyResult.Counters.LastPartialApplySucceeded);
        Assert.Empty(harnessBackend.Calls);
        Assert.Empty(harnessBackend.ExecuteCalls);
        Assert.Equal(0, harnessBackend.SetDirtyCommandRangeCount);
        Assert.False(harnessHit);
        Assert.True(harnessActionId.IsNone);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.EmptyFrameCount, feedCompositor.EmptyFrameCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_enabled_ends_frame_when_candidate_backend_throws_without_production_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var backend = new CapturingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        await compositor.RenderAsync(frame1, cancellationToken);
        await compositor.RenderAsync(frame2, cancellationToken);
        var hitTarget = Assert.Single(frame2.HitTargets);
        var hitX = hitTarget.Bounds.X + 1;
        var hitY = hitTarget.Bounds.Y + 1;
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionBeforeHarness));
        var renderCount = compositor.RenderCount;
        var fullApplyCount = compositor.FullApplyCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var throwingBackend = new ThrowingBackend(throwOnExecuteCall: 2);
        using var harness = new RetainedRenderFrameHandoffHarness(throwingBackend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var exception = Assert.Throws<InvalidOperationException>(() => harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), pipeline.LastDirtyCommandRanges));

        Assert.Equal("execute failed", exception.Message);
        var expectedCalls = new List<DrawingBackendCall> { BeginFrameCall };
        foreach (var read in harness.LastResult.Reads)
        {
            expectedCalls.Add(ExecuteCall(read.Commands.Length));
        }

        expectedCalls.Add(EndFrameCall);
        Assert.Equal(expectedCalls, throwingBackend.Calls);
        Assert.Equal(1, throwingBackend.BeginFrameCount);
        Assert.Equal(1, throwingBackend.EndFrameCount);
        Assert.Equal(1, harness.Counters.RenderCount);
        Assert.Equal(0, harness.Counters.FullApplyCount);
        Assert.Equal(1, harness.Counters.PartialApplyCount);
        Assert.True(harness.Counters.LastPartialApplySucceeded);
        Assert.Equal(renderCount, compositor.RenderCount);
        Assert.Equal(fullApplyCount, compositor.FullApplyCount);
        Assert.Equal(partialApplyCount, compositor.PartialApplyCount);
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionAfterHarness));
        Assert.Equal(actionBeforeHarness, actionAfterHarness);
    }

    [Fact]
    public async Task RetainedRenderFrameHandoffHarness_enabled_counts_reported_fallback_as_candidate_full_apply_and_preserves_owner_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment fallback", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(5)),
                VirtualNodeProperty.Hovered(false)));
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = feed.Build(fallbackRoot, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var productionBackend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(productionBackend);
        await compositor.RenderAsync(frame1, cancellationToken);
        await compositor.RenderAsync(frame2, cancellationToken);
        var hitTarget = Assert.Single(frame2.HitTargets);
        var hitX = hitTarget.Bounds.X + 1;
        var hitY = hitTarget.Bounds.Y + 1;
        var harnessBackend = new DirtyRangeAwareCapturingBackend();
        using var harness = new RetainedRenderFrameHandoffHarness(harnessBackend, RetainedRenderFrameHandoffHarnessOptions.Enabled);

        var result = harness.ExecuteCandidateFrame(feed.SegmentOwnership!, new FrameContext(960, 540), pipeline.LastDirtyCommandRanges);
        var harnessHit = harness.TryGetActionIdAtLogicalPixel(hitX, hitY, out var harnessActionId);
        var productionHit = compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var productionActionId);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, feed.LastResult.Kind);
        Assert.True(feed.LastResult.FallbackApplied);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, result.Kind);
        Assert.Single(result.Reads);
        Assert.Equal(1, result.Counters.RenderCount);
        Assert.Equal(1, result.Counters.FullApplyCount);
        Assert.Equal(0, result.Counters.PartialApplyCount);
        Assert.False(result.Counters.LastPartialApplySucceeded);
        Assert.Equal([BeginFrameCall, ExecuteCall(result.Reads[0].Commands.Length), EndFrameCall], harnessBackend.Calls);
        Assert.Equal(1, harnessBackend.SetDirtyCommandRangeCount);
        Assert.True(harnessHit);
        Assert.True(productionHit);
        Assert.Equal(new ActionId(5), harnessActionId);
        Assert.Equal(new ActionId(5), productionActionId);
    }

    [Fact]
    public void RetainedRenderFrameHandoffHarness_dispose_releases_backend_and_blocks_future_execution()
    {
        var backend = new DisposeTrackingBackend();
        var harness = new RetainedRenderFrameHandoffHarness(backend, RetainedRenderFrameHandoffHarnessOptions.Enabled);
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);

        harness.Dispose();
        harness.Dispose();

        Assert.Equal(1, backend.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => harness.ExecuteCandidateFrame(ownership, new FrameContext(1, 1), []));
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_default_off_does_not_allocate_candidate_or_change_production_outputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = CreateActionButtonRoot(new ActionId(1));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directFrame = directPipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directHitTarget = Assert.Single(directFrame.HitTargets);
        var hitX = directHitTarget.Bounds.X + 1;
        var hitY = directHitTarget.Bounds.Y + 1;
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var feedFrame = feed.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(
            feedBackend,
            DrawingBackendCompositorHandoffOptions.Disabled);

        await feedCompositor.RenderAsync(feedFrame, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var feedActionId);
        var candidateHit = feedCompositor.TryGetCandidateActionIdAtPhysicalPixel(hitX, hitY, out var candidateActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, feedCompositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.Disabled, feedCompositor.LastHandoffResult.Reason);
        Assert.False(feedCompositor.HasHandoffCandidateHarness);
        Assert.False(candidateHit);
        Assert.True(candidateActionId.IsNone);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directCompositor.EmptyFrameCount, feedCompositor.EmptyFrameCount);
        Assert.Equal(directCompositor.LastDirtyCommandRanges, feedCompositor.LastDirtyCommandRanges);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_selects_fresh_partial_segments_without_changing_visible_outputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var diagnosticsBefore = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        var directPipeline = new RenderPipeline();
        using var directFrame1 = directPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var directFrame2 = directPipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame1, cancellationToken);
        await directCompositor.RenderAsync(directFrame2, cancellationToken);
        var directHitTarget = Assert.Single(directFrame2.HitTargets);
        var hitX = directHitTarget.Bounds.X + 1;
        var hitY = directHitTarget.Bounds.Y + 1;
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(
            feedBackend,
            DrawingBackendCompositorHandoffOptions.Enabled);
        using var feedFrame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await feedCompositor.RenderAsync(feedFrame1, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var fullResult = feedCompositor.LastHandoffResult;
        using var feedFrame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        await feedCompositor.RenderAsync(feedFrame2, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var partialResult = feedCompositor.LastHandoffResult;
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var feedActionId);
        var candidateHit = feedCompositor.TryGetCandidateActionIdAtPhysicalPixel(hitX, hitY, out var candidateActionId);
        var diagnosticsAfter = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePresetId.Default, RenderStylePreset.Default));

        Assert.True(feedCompositor.HasHandoffCandidateHarness);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, fullResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Executed, partialResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, fullResult.Reason);
        Assert.Equal(DrawingBackendCompositorHandoffReason.None, partialResult.Reason);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Disabled, fullResult.CandidateResult.Kind);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, partialResult.CandidateResult.Kind);
        Assert.Empty(fullResult.CandidateResult.Reads);
        Assert.Equal(2, partialResult.CandidateResult.Reads.Count);
        Assert.Equal(1, partialResult.CandidateResult.Counters.RenderCount);
        Assert.Equal(0, partialResult.CandidateResult.Counters.FullApplyCount);
        Assert.Equal(1, partialResult.CandidateResult.Counters.PartialApplyCount);
        Assert.True(partialResult.CandidateResult.Counters.LastPartialApplySucceeded);
        Assert.Equal(2, feedCompositor.RenderCount);
        Assert.Equal(1, feedCompositor.FullApplyCount);
        Assert.Equal(1, feedCompositor.PartialApplyCount);
        Assert.True(feedCompositor.LastPartialApplySucceeded);
        var expectedCalls = new List<DrawingBackendCall> { BeginFrameCall, ExecuteCall(feedFrame1.Commands.Count), EndFrameCall, BeginFrameCall };
        foreach (var read in partialResult.CandidateResult.Reads)
        {
            expectedCalls.Add(ExecuteCall(read.Commands.Length));
        }

        expectedCalls.Add(EndFrameCall);
        Assert.Equal(expectedCalls, feedBackend.Calls);
        Assert.Equal(1 + partialResult.CandidateResult.Reads.Count, feedBackend.ExecuteCalls.Count);
        var selectedDirtyRanges = feedBackend.DirtyRanges.Skip(1).ToArray();
        Assert.Equal(partialResult.CandidateResult.DirtyRangePlan.Count, selectedDirtyRanges.Length);
        for (var i = 0; i < selectedDirtyRanges.Length; i++)
        {
            Assert.Equal(partialResult.CandidateResult.DirtyRangePlan[i].SegmentDirtyRanges, selectedDirtyRanges[i]);
        }

        Assert.True(candidateHit);
        Assert.Equal(new ActionId(4), candidateActionId);
        Assert.Equal(directPipeline.LayoutRebuildCount, feedPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, feedPipeline.LastDirtyCommandRanges);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.EmptyFrameCount, feedCompositor.EmptyFrameCount);
        Assert.Equal(directCompositor.LastDirtyCommandRanges, feedCompositor.LastDirtyCommandRanges);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
        Assert.Equal(directActionId, candidateActionId);
        Assert.Equal(diagnosticsBefore, diagnosticsAfter);
    }

    [Fact]
    public async Task PartialApplyHandoffDiagnosticSnapshot_reports_disabled_executed_fallback_and_rejected_states()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var disabledFeed = new SegmentedRetainedFrameProductionOwnerFeed(new RenderPipeline(), RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var disabledFrame = disabledFeed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var disabledBackend = new DirtyRangeAwareCapturingBackend();
        using var disabledCompositor = new DrawingBackendCompositor(disabledBackend, DrawingBackendCompositorHandoffOptions.Disabled);
        await disabledCompositor.RenderAsync(disabledFrame, disabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);

        var enabledPipeline = new RenderPipeline();
        using var enabledFeed = new SegmentedRetainedFrameProductionOwnerFeed(enabledPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var enabledBackend = new DirtyRangeAwareCapturingBackend();
        using var enabledCompositor = new DrawingBackendCompositor(enabledBackend, DrawingBackendCompositorHandoffOptions.Enabled);
        using var enabledFrame1 = enabledFeed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await enabledCompositor.RenderAsync(enabledFrame1, enabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var fallbackSnapshot = PartialApplyHandoffDiagnosticSnapshot.FromCompositor(enabledCompositor);
        using var enabledFrame2 = enabledFeed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        await enabledCompositor.RenderAsync(enabledFrame2, enabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);

        var rejectedBackend = new DirtyRangeAwareCapturingBackend();
        using var rejectedCompositor = new DrawingBackendCompositor(rejectedBackend, DrawingBackendCompositorHandoffOptions.Enabled);
        using var rejectedRetainedFrame = new RetainedRenderFrame();
        using var rejectedOwnership = new RetainedRenderFrameSegmentOwnership(rejectedRetainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var rejectedCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var rejectedFrame = new RenderFrameBatch(
            rejectedCommands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(204))],
            new NamedResolver("selected"),
            [(0, 1), (0, 1)]);
        var rejectedRoot = CreateActionButtonRoot(new ActionId(204));
        rejectedOwnership.Update(null, rejectedRoot, new PixelRectangle(0, 0, 100, 100), rejectedFrame);
        SetOwnershipLastResult(rejectedOwnership, new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial,
                RetainedPartialApplyFallbackReason.None,
                RetainedPartialApplyResultKind.AppliedPartial,
                rejectedOwnership.RuntimeOwner!.ReadSegments()),
            RuntimeOwnerEnabled: true,
            FallbackApplied: false,
            OwnerStatePreservedBeforeFallback: true,
            BatchFrameId: 0,
            BatchCommandCount: rejectedFrame.Commands.Count,
            BatchResources: rejectedFrame.Resources,
            BatchCommandOwner: rejectedFrame.Commands.Owner));
        await rejectedCompositor.RenderAsync(rejectedFrame, rejectedOwnership, new FrameContext(100, 100), cancellationToken);

        var disabledSnapshot = PartialApplyHandoffDiagnosticSnapshot.FromCompositor(disabledCompositor);
        var executedSnapshot = PartialApplyHandoffDiagnosticSnapshot.FromCompositor(enabledCompositor);
        var rejectedSnapshot = PartialApplyHandoffDiagnosticSnapshot.FromCompositor(rejectedCompositor);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, disabledSnapshot.HandoffKind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.Disabled, disabledSnapshot.Reason);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.Disabled, disabledSnapshot.OwnerKind);
        Assert.False(disabledSnapshot.RuntimeOwnerEnabled);
        Assert.Equal(0UL, disabledSnapshot.BatchFrameId);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, fallbackSnapshot.HandoffKind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, fallbackSnapshot.Reason);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackSnapshot.OwnerKind);
        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, fallbackSnapshot.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, fallbackSnapshot.FallbackReason);
        Assert.True(fallbackSnapshot.RuntimeOwnerEnabled);
        Assert.False(fallbackSnapshot.FallbackApplied);
        Assert.True(fallbackSnapshot.OwnerStatePreserved);
        Assert.Equal((ulong)((FrameDrawingResources)enabledFrame1.Resources).FrameId, fallbackSnapshot.BatchFrameId);
        Assert.Equal(enabledFrame1.Commands.Count, fallbackSnapshot.BatchCommandCount);
        Assert.Empty(fallbackSnapshot.DirtyRanges);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Executed, executedSnapshot.HandoffKind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.None, executedSnapshot.Reason);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, executedSnapshot.OwnerKind);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, executedSnapshot.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, executedSnapshot.FallbackReason);
        Assert.True(executedSnapshot.RuntimeOwnerEnabled);
        Assert.False(executedSnapshot.FallbackApplied);
        Assert.True(executedSnapshot.OwnerStatePreserved);
        Assert.Equal((ulong)((FrameDrawingResources)enabledFrame2.Resources).FrameId, executedSnapshot.BatchFrameId);
        Assert.Equal(enabledFrame2.Commands.Count, executedSnapshot.BatchCommandCount);
        Assert.Equal(enabledCompositor.LastDirtyCommandRanges, executedSnapshot.DirtyRanges);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Rejected, rejectedSnapshot.HandoffKind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.DirtyRangeMismatch, rejectedSnapshot.Reason);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, rejectedSnapshot.OwnerKind);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, rejectedSnapshot.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, rejectedSnapshot.FallbackReason);
        Assert.True(rejectedSnapshot.RuntimeOwnerEnabled);
        Assert.False(rejectedSnapshot.FallbackApplied);
        Assert.Equal(rejectedFrame.Commands.Count, rejectedSnapshot.BatchCommandCount);
        Assert.Equal(rejectedCompositor.LastDirtyCommandRanges, rejectedSnapshot.DirtyRanges);
    }

    [Fact]
    public async Task StyleOnlyFastPathOptions_default_off_pre_switch_controls_selected_segmented_render_source()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var disabledFeed = new SegmentedRetainedFrameProductionOwnerFeed(new RenderPipeline(), StyleOnlyFastPathOptions.Disabled.ProductionOwnerOptions);
        using var disabledFrame = disabledFeed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var disabledBackend = new DirtyRangeAwareCapturingBackend();
        using var disabledCompositor = new DrawingBackendCompositor(disabledBackend, StyleOnlyFastPathOptions.Disabled.HandoffOptions);
        await disabledCompositor.RenderAsync(disabledFrame, disabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);

        var enabledPipeline = new RenderPipeline();
        using var enabledFeed = new SegmentedRetainedFrameProductionOwnerFeed(enabledPipeline, StyleOnlyFastPathOptions.Enabled.ProductionOwnerOptions);
        var enabledBackend = new DirtyRangeAwareCapturingBackend();
        using var enabledCompositor = new DrawingBackendCompositor(enabledBackend, StyleOnlyFastPathOptions.Enabled.HandoffOptions);
        using var enabledFrame1 = enabledFeed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await enabledCompositor.RenderAsync(enabledFrame1, enabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        using var enabledFrame2 = enabledFeed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        await enabledCompositor.RenderAsync(enabledFrame2, enabledFeed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, disabledCompositor.LastHandoffResult.Kind);
        Assert.False(disabledCompositor.HasHandoffCandidateHarness);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Executed, enabledCompositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.None, enabledCompositor.LastHandoffResult.Reason);
        Assert.True(enabledCompositor.HasHandoffCandidateHarness);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_missing_owner_reports_without_allocating_candidate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = CreateActionButtonRoot(new ActionId(1));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(
            backend,
            DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(frame, null, new FrameContext(960, 540), cancellationToken);
        var result = compositor.LastHandoffResult;
        var hit = compositor.TryGetCandidateActionIdAtPhysicalPixel(1, 1, out var actionId);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.MissingOwner, result.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.MissingOwner, result.Reason);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.False(hit);
        Assert.True(actionId.IsNone);
        Assert.Equal([BeginFrameCall, ExecuteCall(frame.Commands.Count), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_selected_path_uses_owner_side_hit_test_and_fallback_restores_retained_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 100, 100);
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var selectedCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var selectedFrame = new RenderFrameBatch(
            selectedCommands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(204), new PixelRectangle(10, 10, 20, 20))],
            new NamedResolver("selected"),
            [(0, 1)]);
        var selectedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Selected", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(204))));
        ownership.Update(null, selectedRoot, viewport, selectedFrame);
        SetOwnershipLastResult(ownership, new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial,
                RetainedPartialApplyFallbackReason.None,
                RetainedPartialApplyResultKind.AppliedPartial,
                ownership.RuntimeOwner!.ReadSegments()),
            true,
            false,
            true,
            0,
            selectedFrame.Commands.Count,
            selectedFrame.Resources,
            selectedFrame.Commands.Owner));
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(backend, DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(selectedFrame, ownership, new FrameContext(100, 100), cancellationToken);
        var inClipHit = compositor.TryGetActionIdAtPhysicalPixel(15, 15, out var inClipActionId);
        var outsideClipHit = compositor.TryGetActionIdAtPhysicalPixel(5, 5, out var outsideClipActionId);

        using var fallbackCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var fallbackFrame = new RenderFrameBatch(
            fallbackCommands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(205))],
            new NamedResolver("fallback"),
            [(0, 1)]);
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Fallback", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(205))));
        ownership.Update(null, fallbackRoot, viewport, fallbackFrame);
        await compositor.RenderAsync(fallbackFrame, ownership, new FrameContext(100, 100), cancellationToken);
        var fallbackHit = compositor.TryGetActionIdAtPhysicalPixel(5, 5, out var fallbackActionId);

        Assert.True(inClipHit);
        Assert.Equal(new ActionId(204), inClipActionId);
        Assert.False(outsideClipHit);
        Assert.True(outsideClipActionId.IsNone);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, compositor.LastHandoffResult.Reason);
        Assert.True(fallbackHit);
        Assert.Equal(new ActionId(205), fallbackActionId);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_rejects_malformed_segment_coverage_before_candidate_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 100, 100);
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var commands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.FillRect));
        using var frame = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(204))],
            new NamedResolver("selected"),
            [(0, 1)]);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Selected", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(204))));
        ownership.Update(null, root, viewport, frame);
        var freshAcceptedResult = new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial,
                RetainedPartialApplyFallbackReason.None,
                RetainedPartialApplyResultKind.AppliedPartial,
                ownership.RuntimeOwner!.ReadSegments()),
            true,
            false,
            true,
            0,
            frame.Commands.Count,
            frame.Resources,
            frame.Commands.Owner);
        SetOwnershipLastResult(ownership, freshAcceptedResult);
        SetRuntimeOwnerSegmentsUnchecked(
            ownership.RuntimeOwner,
            [new RetainedResourceSegment(0, 1, RetainedResourceSnapshot.Capture(frame.Resources))]);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(backend, DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(frame, ownership, new FrameContext(100, 100), cancellationToken);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Rejected, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.MalformedSegmentCoverage, compositor.LastHandoffResult.Reason);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.Equal([BeginFrameCall, ExecuteCall(2), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_rejects_malformed_dirty_ranges_before_candidate_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 100, 100);
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var frame = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(204))],
            new NamedResolver("selected"),
            [(0, 1), (0, 1)]);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Selected", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(204))));
        ownership.Update(null, root, viewport, frame);
        SetOwnershipLastResult(ownership, new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial,
                RetainedPartialApplyFallbackReason.None,
                RetainedPartialApplyResultKind.AppliedPartial,
                ownership.RuntimeOwner!.ReadSegments()),
            true,
            false,
            true,
            0,
            frame.Commands.Count,
            frame.Resources,
            frame.Commands.Owner));
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(backend, DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(frame, ownership, new FrameContext(100, 100), cancellationToken);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Rejected, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.DirtyRangeMismatch, compositor.LastHandoffResult.Reason);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_rejects_stale_owner_without_allocating_candidate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var ownerFrame = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var staleFrame = feedPipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(
            backend,
            DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(staleFrame, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Rejected, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.StaleOwner, compositor.LastHandoffResult.Reason);
        Assert.Same(ownerFrame.Resources, compositor.LastHandoffResult.OwnerResult.BatchResources);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.Equal([BeginFrameCall, ExecuteCall(staleFrame.Commands.Count), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_reports_fallback_and_empty_reads_without_candidate_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment fallback", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(5)),
                VirtualNodeProperty.Hovered(false)));
        var emptyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1));
        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(
            backend,
            DrawingBackendCompositorHandoffOptions.Enabled);
        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame1, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var initialFullResult = compositor.LastHandoffResult;
        using var fallbackFrame = feed.Build(fallbackRoot, viewport, _arena.GetOrCreateSnapshot(), [1]);
        await compositor.RenderAsync(fallbackFrame, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var fallbackResult = compositor.LastHandoffResult;
        using var emptyFrame = feed.Build(emptyRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(emptyFrame, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var emptyResult = compositor.LastHandoffResult;

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, initialFullResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, fallbackResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, emptyResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, initialFullResult.Reason);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, fallbackResult.Reason);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerFallbackFull, emptyResult.Reason);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, fallbackResult.OwnerResult.ShadowResult.Reason);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Disabled, fallbackResult.CandidateResult.Kind);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Disabled, emptyResult.CandidateResult.Kind);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.Equal(1, compositor.EmptyFrameCount);
        Assert.Equal(2, backend.ExecuteCalls.Count);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_reports_rejected_owner_without_candidate_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 100, 100);
        var root = CreateActionButtonRoot(new ActionId(300));
        using var retainedFrame = new RetainedRenderFrame();
        using var ownership = new RetainedRenderFrameSegmentOwnership(retainedFrame, RetainedRenderFrameSegmentOwnershipOptions.Enabled);
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var frame = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(300))],
            new NamedResolver("rejected"),
            [(0, 1)]);
        ownership.Update(null, root, viewport, frame);
        var rejectedResult = new SegmentedRetainedFrameProductionOwnerFeedResult(
            new SegmentedRetainedFrameShadowResult(
                SegmentedRetainedFrameShadowResultKind.ShadowRejected,
                RetainedPartialApplyFallbackReason.UnstableCommandRange,
                RetainedPartialApplyResultKind.AppliedPartial,
                ownership.RuntimeOwner!.ReadSegments()),
            true,
            false,
            true,
            0,
            frame.Commands.Count,
            frame.Resources,
            frame.Commands.Owner);
        SetOwnershipLastResult(ownership, rejectedResult);
        var backend = new DirtyRangeAwareCapturingBackend();
        using var compositor = new DrawingBackendCompositor(
            backend,
            DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(frame, ownership, new FrameContext(100, 100), cancellationToken);

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Rejected, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.OwnerRejected, compositor.LastHandoffResult.Reason);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowRejected, compositor.LastHandoffResult.OwnerResult.Kind);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Disabled, compositor.LastHandoffResult.CandidateResult.Kind);
        Assert.False(compositor.HasHandoffCandidateHarness);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), EndFrameCall], backend.Calls);
    }

    [Fact]
    public async Task DrawingBackendCompositor_handoff_selector_enabled_rethrows_candidate_backend_failure_after_production_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var throwingBackend = new ThrowingBackend(throwOnExecuteCall: 3);
        using var compositor = new DrawingBackendCompositor(
            throwingBackend,
            DrawingBackendCompositorHandoffOptions.Enabled);

        await compositor.RenderAsync(frame1, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        var firstHitTarget = Assert.Single(frame1.HitTargets);
        var hitX = firstHitTarget.Bounds.X + 1;
        var hitY = firstHitTarget.Bounds.Y + 1;
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionBeforeThrow));

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, compositor.LastHandoffResult.Kind);
        Assert.False(compositor.HasHandoffCandidateHarness);

        using var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await compositor.RenderAsync(frame2, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken));

        Assert.Equal("execute failed", exception.Message);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Executed, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.BackendThrewBeforeCommit, compositor.LastHandoffResult.Reason);
        Assert.Equal(RetainedRenderFrameHandoffHarnessResultKind.Executed, compositor.LastHandoffResult.CandidateResult.Kind);
        Assert.True(compositor.HasHandoffCandidateHarness);
        var expectedCalls = new List<DrawingBackendCall> { BeginFrameCall, ExecuteCall(frame1.Commands.Count), EndFrameCall, BeginFrameCall };
        foreach (var read in compositor.LastHandoffResult.CandidateResult.Reads)
        {
            expectedCalls.Add(ExecuteCall(read.Commands.Length));
        }

        expectedCalls.Add(EndFrameCall);
        Assert.Equal(expectedCalls, throwingBackend.Calls);
        Assert.Equal(2, throwingBackend.BeginFrameCount);
        Assert.Equal(2, throwingBackend.EndFrameCount);
        Assert.Equal(1, compositor.RenderCount);
        Assert.Equal(1, compositor.FullApplyCount);
        Assert.Equal(0, compositor.PartialApplyCount);
        Assert.False(compositor.LastPartialApplySucceeded);
        Assert.Equal(feedPipeline.LastDirtyCommandRanges, compositor.LastDirtyCommandRanges);
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionAfterThrow));
        Assert.Equal(actionBeforeThrow, actionAfterThrow);
    }

    [Fact]
    public async Task DrawingBackendCompositor_selected_path_backend_throw_preserves_hit_targets_and_releases_owner_snapshots_on_dispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tracker = new FrameResourceSnapshotCaptureTracker();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var pipeline = new RenderPipeline();
        using var ownership = new RetainedRenderFrameSegmentOwnership(
            pipeline.RetainedFrame,
            new RetainedRenderFrameSegmentOwnershipOptions(true, tracker.Capture));
        var throwingBackend = new ThrowingBackend(throwOnExecuteCall: 3);
        using var compositor = new DrawingBackendCompositor(
            throwingBackend,
            DrawingBackendCompositorHandoffOptions.Enabled);

        using var frame1 = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        ownership.Update(pipeline.LastRetainedInputSnapshot, retainedRoot, viewport, frame1);
        await compositor.RenderAsync(frame1, ownership, new FrameContext(960, 540), cancellationToken);
        var firstHitTarget = Assert.Single(frame1.HitTargets);
        var hitX = firstHitTarget.Bounds.X + 1;
        var hitY = firstHitTarget.Bounds.Y + 1;
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionBeforeThrow));

        using var frame2 = pipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var partialResult = ownership.Update(pipeline.LastRetainedInputSnapshot, partialRoot, viewport, frame2);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await compositor.RenderAsync(frame2, ownership, new FrameContext(960, 540), cancellationToken));

        Assert.Equal("execute failed", exception.Message);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, partialResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Executed, compositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.BackendThrewBeforeCommit, compositor.LastHandoffResult.Reason);
        Assert.Equal(2, tracker.Captures.Count);
        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 0);
        AssertFrameResourceCapture(tracker.Captures[1], frame2.Resources, retainCount: 1, releaseCount: 0);
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitX, hitY, out var actionAfterThrow));
        Assert.Equal(actionBeforeThrow, actionAfterThrow);

        ownership.Dispose();

        AssertFrameResourceCapture(tracker.Captures[0], frame1.Resources, retainCount: 1, releaseCount: 1);
        AssertFrameResourceCapture(tracker.Captures[1], frame2.Resources, retainCount: 1, releaseCount: 1);
    }

    [Fact]
    public async Task SegmentedRetainedFrameProductionOwnerFeed_enabled_follows_build_for_partial_fallback_and_rebuild_without_rendering_from_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment fallback", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(5)),
                VirtualNodeProperty.Hovered(false)));

        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var fullResult = feed.LastResult;
        using var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var partialResult = feed.LastResult;
        var partialOwner = feed.RuntimeOwner!;
        var partialSegments = partialOwner.ResourceSegments.ToArray();
        var partialRootSnapshot = partialOwner.RetainedRoot;
        var partialHitTargets = partialOwner.HitTargets.ToArray();
        var retainedFrameCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedFrameResources = pipeline.RetainedFrame.Resources;
        var retainedDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var backend = new CapturingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        await compositor.RenderAsync(frame2, cancellationToken);

        Assert.True(feed.HasRuntimeOwner);
        Assert.NotNull(feed.RuntimeOwner);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fullResult.Kind);
        Assert.False(fullResult.FallbackApplied);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, partialResult.Kind);
        Assert.False(partialResult.FallbackApplied);
        Assert.True(partialResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(new ActionId(4), GetSingleProperty(partialRootSnapshot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(partialRootSnapshot.Children[1], VirtualPropertyKey.IsHovered, true);
        var partialHitTarget = Assert.Single(partialHitTargets);
        Assert.Equal(new ActionId(4), partialHitTarget.ActionId);
        Assert.Equal(frame1.HitTargets[0].Bounds, partialHitTarget.Bounds);
        Assert.Equal(frame1.HitTargets[0].ClipBounds, partialHitTarget.ClipBounds);
        Assert.Collection(partialSegments,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Same(frame1.Resources, segment.Snapshot.Resolver);
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Same(frame2.Resources, segment.Snapshot.Resolver);
            });
        var normalRenderCall = Assert.Single(backend.ExecuteCalls);
        Assert.Equal(frame2.Commands.Count, normalRenderCall.CommandCount);
        Assert.Same(compositor.RetainedFrame.Resources, normalRenderCall.Resolver);
        Assert.Equal(retainedFrameCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);

        using var frame3 = feed.Build(fallbackRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var fallbackResult = feed.LastResult;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.True(fallbackResult.FallbackApplied);
        Assert.True(fallbackResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, fallbackResult.ShadowResult.Reason);
        Assert.Equal(new ActionId(5), GetSingleProperty(feed.RuntimeOwner!.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(feed.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.IsHovered, false);
        var fallbackHitTarget = Assert.Single(feed.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(5), fallbackHitTarget.ActionId);
        var fallbackSegment = Assert.Single(feed.RuntimeOwner.ResourceSegments);
        Assert.Same(frame3.Resources, fallbackSegment.Snapshot.Resolver);

        using var frame4 = feed.Build(fallbackRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var rebuildResult = feed.LastResult;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, rebuildResult.Kind);
        Assert.False(rebuildResult.FallbackApplied);
        var rebuildHitTarget = Assert.Single(feed.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(5), rebuildHitTarget.ActionId);
        var rebuildSegment = Assert.Single(feed.RuntimeOwner.ResourceSegments);
        Assert.Same(frame4.Resources, rebuildSegment.Snapshot.Resolver);
    }

    [Fact]
    public void SegmentedRetainedFrameProductionOwnerFeed_accepted_partials_advance_retained_root_for_next_rehearsal()
    {
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var root3 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(6)),
                VirtualNodeProperty.Hovered(false)));

        using var frame1 = feed.Build(root1, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = feed.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var secondResult = feed.LastResult;
        using var frame3 = feed.Build(root3, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var thirdResult = feed.LastResult;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, secondResult.Kind);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, thirdResult.Kind);
        Assert.Equal(new ActionId(6), GetSingleProperty(feed.RuntimeOwner!.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(feed.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.IsHovered, false);
        var hitTarget = Assert.Single(feed.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(6), hitTarget.ActionId);
    }

    [Fact]
    public void SegmentedRetainedFrameRuntimeOwner_rejects_partial_without_mutating_commands_resources_root_or_hit_targets()
    {
        var oldResolver = new NamedResolver("old");
        var replacementResolver = new NamedResolver("replacement");
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        using var owner = new SegmentedRetainedFrameRuntimeOwner();
        using var oldCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.FillRect),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var oldBatch = new RenderFrameBatch(
            oldCommands,
            [new HitTestTarget(new PixelRectangle(16, 16, 140, 40), new ActionId(1))],
            oldResolver);
        using var malformedBatch = new RenderFrameBatch(
            replacementCommands,
            [new HitTestTarget(new PixelRectangle(16, 16, 140, 40), new ActionId(4))],
            replacementResolver,
            [(2, 1)]);
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(
            retainedRoot,
            partialRoot,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            _arena.GetOrCreateSnapshot());

        owner.ApplyFull(oldBatch, retainedRoot);
        var beforeReads = owner.ReadSegments();
        var beforeSegments = owner.ResourceSegments.ToArray();
        var beforeRoot = owner.RetainedRoot;
        var beforeHitTargets = owner.HitTargets.ToArray();
        var accepted = owner.TryAcceptPartial(malformedBatch, rootPatch, malformedBatch.HitTargets);

        Assert.False(accepted);
        AssertSegmentedReadsEqual(beforeReads, owner.ReadSegments());
        Assert.Equal(beforeSegments, owner.ResourceSegments);
        Assert.Equal(beforeRoot, owner.RetainedRoot);
        Assert.Equal(beforeHitTargets, owner.HitTargets);

        owner.ApplyFallbackFull(malformedBatch, partialRoot, RetainedPartialApplyFallbackReason.UnstableCommandRange);

        var fallbackRead = Assert.Single(owner.ReadSegments());
        Assert.Equal(0, fallbackRead.CommandStart);
        Assert.Same(replacementResolver, fallbackRead.Resolver);
        Assert.Equal(partialRoot, owner.RetainedRoot);
        var fallbackHitTarget = Assert.Single(owner.HitTargets);
        Assert.Equal(new ActionId(4), fallbackHitTarget.ActionId);
    }

    [Fact]
    public void SegmentedRetainedFrameProductionOwnerFeed_falls_back_full_when_hit_target_projection_fails_after_preserving_owner_state()
    {
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Primary", new NodeKey(10),
                VirtualNodeProperty.Action(new ActionId(200)),
                VirtualNodeProperty.Hovered(false)),
            VirtualNodeBuilder.Button(_arena, "Secondary", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(201)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Primary", new NodeKey(10),
                VirtualNodeProperty.Action(new ActionId(202)),
                VirtualNodeProperty.Hovered(false)),
            VirtualNodeBuilder.Button(_arena, "Secondary", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(201)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var beforeReads = feed.RuntimeOwner!.ReadSegments();
        var beforeSegments = feed.RuntimeOwner.ResourceSegments.ToArray();
        var beforeRoot = feed.RuntimeOwner.RetainedRoot;
        var beforeHitTargets = feed.RuntimeOwner.HitTargets.ToArray();
        using var frame2 = feed.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [3]);
        var fallbackResult = feed.LastResult;

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.True(fallbackResult.FallbackApplied);
        Assert.True(fallbackResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, fallbackResult.ShadowResult.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, fallbackResult.ShadowResult.Reason);
        AssertSegmentedReadsEqual(beforeReads, [new SegmentedFrameRead(0, frame1.Commands.Memory[..frame1.Commands.Count].ToArray(), frame1.Resources)]);
        Assert.Single(beforeSegments);
        Assert.Equal(retainedRoot, beforeRoot);
        Assert.Equal([new ActionId(200), new ActionId(201)], beforeHitTargets.Select(target => target.ActionId).ToArray());
        Assert.Equal([new ActionId(202), new ActionId(201)], feed.RuntimeOwner.HitTargets.Select(target => target.ActionId).ToArray());
        Assert.Equal(new ActionId(202), GetSingleProperty(feed.RuntimeOwner.RetainedRoot.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(feed.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.IsHovered, true);
        var fallbackSegment = Assert.Single(feed.RuntimeOwner.ResourceSegments);
        Assert.Same(frame2.Resources, fallbackSegment.Snapshot.Resolver);
    }

    [Fact]
    public async Task SegmentedRetainedFrameProductionOwnerFeed_enabled_handles_empty_batch_as_secondary_rebuild_without_backend_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var emptyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1));

        var directPipeline = new RenderPipeline();
        using var directFrame1 = directPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var directEmptyFrame = directPipeline.Build(emptyRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var directBackend = new DirtyRangeAwareCapturingBackend();
        using var directCompositor = new DrawingBackendCompositor(directBackend);
        await directCompositor.RenderAsync(directFrame1, cancellationToken);
        await directCompositor.RenderAsync(directEmptyFrame, cancellationToken);
        var directHit = directCompositor.TryGetActionIdAtPhysicalPixel(16, 16, out var directActionId);

        var feedPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(feedPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        using var feedFrame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var feedEmptyFrame = feed.Build(emptyRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var emptyResult = feed.LastResult;
        var feedBackend = new DirtyRangeAwareCapturingBackend();
        using var feedCompositor = new DrawingBackendCompositor(feedBackend);
        await feedCompositor.RenderAsync(feedFrame1, cancellationToken);
        await feedCompositor.RenderAsync(feedEmptyFrame, cancellationToken);
        var feedHit = feedCompositor.TryGetActionIdAtPhysicalPixel(16, 16, out var feedActionId);

        Assert.Equal(0, directEmptyFrame.Commands.Count);
        Assert.Equal(0, feedEmptyFrame.Commands.Count);
        Assert.True(feed.HasRuntimeOwner);
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, emptyResult.Kind);
        Assert.False(emptyResult.FallbackApplied);
        Assert.Empty(emptyResult.ShadowResult.Reads);
        Assert.Equal(0, feed.RuntimeOwner!.CommandCount);
        Assert.Empty(feed.RuntimeOwner.ResourceSegments);
        Assert.Empty(feed.RuntimeOwner.HitTargets);
        Assert.Equal(directPipeline.LayoutRebuildCount, feedPipeline.LayoutRebuildCount);
        Assert.Equal(directPipeline.LastDirtyCommandRanges, feedPipeline.LastDirtyCommandRanges);
        Assert.Equal(directPipeline.RetainedFrame.CommandCount, feedPipeline.RetainedFrame.CommandCount);
        Assert.Equal(directCompositor.RenderCount, feedCompositor.RenderCount);
        Assert.Equal(directCompositor.EmptyFrameCount, feedCompositor.EmptyFrameCount);
        Assert.Equal(directCompositor.FullApplyCount, feedCompositor.FullApplyCount);
        Assert.Equal(directCompositor.PartialApplyCount, feedCompositor.PartialApplyCount);
        Assert.Equal(directBackend.SetDirtyCommandRangeCount, feedBackend.SetDirtyCommandRangeCount);
        Assert.Equal(directBackend.DirtyRanges, feedBackend.DirtyRanges);
        AssertExecuteCommandCountsEqual(directBackend.ExecuteCalls, feedBackend.ExecuteCalls);
        Assert.False(directHit);
        Assert.Equal(directHit, feedHit);
        Assert.Equal(directActionId, feedActionId);
    }

    [Fact]
    public void SegmentedRetainedFrameProductionOwnerFeed_enabled_falls_back_full_for_malformed_dirty_ranges_after_preserving_owner_state()
    {
        var pipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(pipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var acceptedResult = feed.LastResult;
        var beforeRoot = feed.RuntimeOwner!.RetainedRoot;
        var beforeSegments = feed.RuntimeOwner.ResourceSegments.ToArray();
        var beforeHitTargets = feed.RuntimeOwner.HitTargets.ToArray();
        var beforeReads = feed.RuntimeOwner.ReadSegments();
        var malformedCommandArray = new DrawCommand[frame2.Commands.Count];
        Array.Fill(malformedCommandArray, new DrawCommand(DrawCommandKind.FillRect));
        using var malformedCommands = CreateCommandBatch(malformedCommandArray);
        using var malformedBatch = new RenderFrameBatch(
            malformedCommands,
            frame2.HitTargets,
            new NamedResolver("malformed"),
            [(1, 1), (frame2.Commands.Count, 1)]);

        var fallbackResult = InvokeProductionOwnerFeedUpdate(feed, partialRoot, viewport, malformedBatch);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, acceptedResult.Kind);
        Assert.Equal(VirtualNodeKind.ScrollContainer, beforeRoot.Kind);
        Assert.Equal(2, beforeRoot.Children.Length);
        Assert.Equal(new ActionId(4), GetSingleProperty(beforeRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(beforeRoot.Children[1], VirtualPropertyKey.IsHovered, true);
        var beforeHitTarget = Assert.Single(beforeHitTargets);
        Assert.Equal(new ActionId(4), beforeHitTarget.ActionId);
        Assert.Collection(beforeReads,
            segment => Assert.Same(frame1.Resources, segment.Resolver),
            segment => Assert.Same(frame2.Resources, segment.Resolver));
        Assert.Collection(beforeSegments,
            segment => Assert.Same(frame1.Resources, segment.Snapshot.Resolver),
            segment => Assert.Same(frame2.Resources, segment.Snapshot.Resolver));
        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.True(fallbackResult.FallbackApplied);
        Assert.True(fallbackResult.OwnerStatePreservedBeforeFallback);
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, fallbackResult.ShadowResult.PlanKind);
        Assert.Equal(RetainedPartialApplyFallbackReason.UnstableCommandRange, fallbackResult.ShadowResult.Reason);
        Assert.Equal(new ActionId(4), GetSingleProperty(feed.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(feed.RuntimeOwner.RetainedRoot.Children[1], VirtualPropertyKey.IsHovered, true);
        var fallbackHitTarget = Assert.Single(feed.RuntimeOwner.HitTargets);
        Assert.Equal(new ActionId(4), fallbackHitTarget.ActionId);
        var fallbackSegment = Assert.Single(feed.RuntimeOwner.ResourceSegments);
        Assert.Same(malformedBatch.Resources, fallbackSegment.Snapshot.Resolver);
        var fallbackRead = Assert.Single(feed.RuntimeOwner.ReadSegments());
        Assert.Same(malformedBatch.Resources, fallbackRead.Resolver);
    }

    [Fact]
    public async Task DrawingBackendCompositorShadowProbe_executes_segmented_reads_outside_compositor_and_preserves_hit_test()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeline = new RenderPipeline();
        using var diagnosticHarness = new SegmentedRetainedFrameDiagnosticHarness(pipeline, RenderPipelineShadowOptions.SegmentedRetainedFrameEnabled);
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(10)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(20),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = diagnosticHarness.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        using var frame2 = diagnosticHarness.Build(nextRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var hitTarget = Assert.Single(frame2.HitTargets);
        var hitTestX = hitTarget.Bounds.X + 1;
        var hitTestY = hitTarget.Bounds.Y + 1;
        var shadowResult = diagnosticHarness.LastShadowResult;
        var productionBackend = new CapturingBackend();
        using var compositor = new DrawingBackendCompositor(productionBackend);
        await compositor.RenderAsync(frame2, cancellationToken);
        var renderCount = compositor.RenderCount;
        var fullApplyCount = compositor.FullApplyCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var productionExecuteCount = productionBackend.ExecuteCalls.Count;
        var hitBeforeProbe = compositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var actionBeforeProbe);
        var probeBackend = new CapturingBackend();
        var probe = new DrawingBackendCompositorShadowProbe(probeBackend);

        var probeResult = probe.Execute(new FrameContext(960, 540), shadowResult.Reads, compositor, hitTestX, hitTestY);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowAppliedPartial, shadowResult.Kind);
        Assert.True(probeResult.HitTestUnchanged);
        Assert.True(probeResult.HitTest.BeforeHit);
        Assert.Equal(new ActionId(4), probeResult.HitTest.BeforeActionId);
        Assert.Equal(probeResult.HitTest.BeforeHit, hitBeforeProbe);
        Assert.Equal(probeResult.HitTest.BeforeActionId, actionBeforeProbe);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(2), EndFrameCall], probeResult.Calls);
        Assert.Collection(probeResult.Executions,
            execution =>
            {
                Assert.Equal(0, execution.CommandStart);
                Assert.Equal(1, execution.CommandCount);
                Assert.Same(frame1.Resources, execution.Resolver);
            },
            execution =>
            {
                Assert.Equal(1, execution.CommandStart);
                Assert.Equal(2, execution.CommandCount);
                Assert.Same(frame2.Resources, execution.Resolver);
            });
        Assert.Equal(probeResult.Calls, probeBackend.Calls);
        Assert.Equal(renderCount, compositor.RenderCount);
        Assert.Equal(fullApplyCount, compositor.FullApplyCount);
        Assert.Equal(partialApplyCount, compositor.PartialApplyCount);
        Assert.Equal(productionExecuteCount, productionBackend.ExecuteCalls.Count);
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(hitTestX, hitTestY, out var actionAfterProbe));
        Assert.Equal(actionBeforeProbe, actionAfterProbe);
    }

    [Fact]
    public async Task DrawingBackendCompositorShadowProbe_handles_empty_segments_clipped_hit_test_and_dirty_range_isolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var frame = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(203), new PixelRectangle(10, 10, 20, 20))],
            new NamedResolver("frame"),
            [(0, 1)]);
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());
        await compositor.RenderAsync(frame, cancellationToken);
        var probeBackend = new DirtyRangeAwareCapturingBackend();
        var probe = new DrawingBackendCompositorShadowProbe(probeBackend);

        var result = probe.Execute(new FrameContext(100, 100), [], compositor, 5, 5);

        Assert.True(result.HitTestUnchanged);
        Assert.False(result.HitTest.BeforeHit);
        Assert.False(result.HitTest.AfterHit);
        Assert.Equal([BeginFrameCall, EndFrameCall], result.Calls);
        Assert.Empty(result.Executions);
        Assert.Equal(0, probeBackend.SetDirtyCommandRangeCount);
        Assert.Empty(probeBackend.ExecuteCalls);
    }

    [Fact]
    public async Task DrawingBackendCompositorShadowProbe_ends_frame_when_probe_backend_throws_without_compositor_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var commands = CreateCommandBatch(new DrawCommand(DrawCommandKind.FillRect));
        using var frame = new RenderFrameBatch(
            commands,
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 100), new ActionId(1))],
            new NamedResolver("frame"));
        using var compositor = new DrawingBackendCompositor(new CapturingBackend());
        await compositor.RenderAsync(frame, cancellationToken);
        var renderCount = compositor.RenderCount;
        var fullApplyCount = compositor.FullApplyCount;
        var partialApplyCount = compositor.PartialApplyCount;
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(1, 1, out var actionBeforeProbe));
        var throwingBackend = new ThrowingBackend(throwOnExecuteCall: 2);
        var probe = new DrawingBackendCompositorShadowProbe(throwingBackend);
        var resolver = new NamedResolver("segment");
        var segments = new[]
        {
            new SegmentedFrameRead(0, [new DrawCommand(DrawCommandKind.FillRect)], resolver),
            new SegmentedFrameRead(1, [new DrawCommand(DrawCommandKind.DrawTextRun)], resolver)
        };

        var exception = Assert.Throws<InvalidOperationException>(() => probe.Execute(new FrameContext(100, 100), segments, compositor, 1, 1));

        Assert.Equal("execute failed", exception.Message);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(1), EndFrameCall], throwingBackend.Calls);
        Assert.Equal(1, throwingBackend.BeginFrameCount);
        Assert.Equal(1, throwingBackend.EndFrameCount);
        Assert.Equal(renderCount, compositor.RenderCount);
        Assert.Equal(fullApplyCount, compositor.FullApplyCount);
        Assert.Equal(partialApplyCount, compositor.PartialApplyCount);
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(1, 1, out var actionAfterProbe));
        Assert.Equal(actionBeforeProbe, actionAfterProbe);
    }

    [Fact]
    public void SegmentedRetainedFrameRuntimeOwner_can_be_long_lived_for_partial_fallback_rebuild_and_dispose()
    {
        var oldResolver = new NamedResolver("old");
        var replacementResolver = new NamedResolver("replacement");
        var fallbackResolver = new NamedResolver("fallback");
        var rebuildResolver = new NamedResolver("rebuild");
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var partialRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var fallbackRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment fallback", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(5))));
        var rebuildRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment rebuild", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(7))));
        using var runtimeOwner = new SegmentedRetainedFrameRuntimeOwner();
        using var oldCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var oldBatch = new RenderFrameBatch(oldCommands, [], oldResolver);
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, [], replacementResolver, [(1, 1)]);
        using var fallbackCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun));
        using var fallbackBatch = new RenderFrameBatch(fallbackCommands, [], fallbackResolver);
        using var rebuildCommands = CreateCommandBatch(new DrawCommand(DrawCommandKind.DrawTextRun));
        using var rebuildBatch = new RenderFrameBatch(rebuildCommands, [], rebuildResolver);

        var fullResult = runtimeOwner.ApplyFull(oldBatch, retainedRoot);
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, partialRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());
        var accepted = runtimeOwner.TryAcceptPartial(replacementBatch, rootPatch);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fullResult.Kind);
        Assert.True(accepted);
        Assert.Equal(new ActionId(4), GetSingleProperty(runtimeOwner.RetainedRoot.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(runtimeOwner.RetainedRoot.Children[0], VirtualPropertyKey.IsHovered, true);
        Assert.Collection(runtimeOwner.ResourceSegments,
            segment =>
            {
                Assert.Equal(0, segment.CommandStart);
                Assert.Same(oldResolver, segment.Snapshot.Resolver);
            },
            segment =>
            {
                Assert.Equal(1, segment.CommandStart);
                Assert.Same(replacementResolver, segment.Snapshot.Resolver);
            });

        var fallbackResult = runtimeOwner.ApplyFallbackFull(fallbackBatch, fallbackRoot, RetainedPartialApplyFallbackReason.NotStyleOnly);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, fallbackResult.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, fallbackResult.Reason);
        Assert.Equal(fallbackRoot, runtimeOwner.RetainedRoot);
        var fallbackSegment = Assert.Single(runtimeOwner.ResourceSegments);
        Assert.Same(fallbackResolver, fallbackSegment.Snapshot.Resolver);

        var rebuildResult = runtimeOwner.Rebuild(rebuildBatch, rebuildRoot);

        Assert.Equal(SegmentedRetainedFrameShadowResultKind.ShadowFallbackFull, rebuildResult.Kind);
        Assert.Equal(rebuildRoot, runtimeOwner.RetainedRoot);
        var rebuildRead = Assert.Single(runtimeOwner.ReadSegments());
        Assert.Same(rebuildResolver, rebuildRead.Resolver);

        runtimeOwner.Dispose();
        runtimeOwner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runtimeOwner.ReadSegments());
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_executes_shadow_owner_reads_in_segment_order()
    {
        using var owner = CreateTwoSegmentShadowOwner(out var oldResolver, out var replacementResolver);
        var backend = new CapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        adapter.Execute(new FrameContext(960, 540), owner.ReadSegments());

        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(1), EndFrameCall], backend.Calls);
        Assert.Collection(backend.ExecuteCalls,
            call => Assert.Same(oldResolver, call.Resolver),
            call => Assert.Same(replacementResolver, call.Resolver));
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_ends_frame_when_shadow_owner_read_execute_throws()
    {
        using var owner = CreateTwoSegmentShadowOwner(out _, out _);
        var backend = new ThrowingBackend(throwOnExecuteCall: 2);
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Execute(new FrameContext(960, 540), owner.ReadSegments()));

        Assert.Equal("execute failed", exception.Message);
        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(1), EndFrameCall], backend.Calls);
        Assert.Equal(1, backend.BeginFrameCount);
        Assert.Equal(1, backend.EndFrameCount);
    }

    [Fact]
    public void SegmentedBackendExecutionAdapter_pairs_begin_end_for_empty_shadow_owner_reads()
    {
        using var owner = new SegmentedRetainedFrameOwner();
        var backend = new CapturingBackend();
        var adapter = new SegmentedBackendExecutionAdapter(backend);

        adapter.Execute(new FrameContext(960, 540), owner.ReadSegments());

        Assert.Equal([BeginFrameCall, EndFrameCall], backend.Calls);
        Assert.Empty(backend.ExecuteCalls);
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
        Assert.Equal(
            SegmentedBackendExecutionRationale.SmallestShapeThatPreservesCurrentLocalResourceHandles,
            decision.Rationale);
        Assert.Equal(
            SegmentedBackendExecutionContractImpact.ExistingIDrawingBackendExecuteSignatureRemainsUnchanged,
            decision.BackendContractImpact);
        Assert.Equal(
            SegmentedBackendExecutionBlockedAlternative.CompositeResolverNeedsSegmentMetadata
                | SegmentedBackendExecutionBlockedAlternative.ResourceRebaseNeedsTextStyleCopyingAndCommandRewriting
                | SegmentedBackendExecutionBlockedAlternative.StableGlobalHandlesRemainDeferred,
            decision.BlockedAlternatives);
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

        Assert.Equal([BeginFrameCall, EndFrameCall], backend.Calls);
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
        Assert.Equal([BeginFrameCall, ExecuteCall(1), ExecuteCall(1), EndFrameCall], backend.Calls);
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
        Assert.Equal([BeginFrameCall, ExecuteCall(1), EndFrameCall], backend.Calls);
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_action_id_without_next_layout()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1), new PixelRectangle(0, 0, 960, 540))
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1], retainedHitTargets);

        Assert.True(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, projection.FallbackReason);
        var target = Assert.Single(projection.HitTargets);
        Assert.Equal(retainedHitTargets[0].Bounds, target.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, target.ClipBounds);
        Assert.Equal(new ActionId(4), target.ActionId);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_next_tree_shape_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_action_id_is_missing()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2)));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_dirty_dfs_is_not_action_node()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [0],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_when_non_dirty_action_id_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(206))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [
                new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1)),
                new HitTestTarget(new PixelRectangle(16, 172, 140, 40), new ActionId(2))
            ]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_falls_back_on_key_path_mismatch()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(99),
                VirtualNodeProperty.Action(new ActionId(4))));

        var projection = HitTargetMetadataProjector.ProjectActionIds(
            retainedRoot,
            nextRoot,
            [1],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))]);

        Assert.False(projection.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, projection.FallbackReason);
        Assert.Empty(projection.HitTargets);
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_multiple_buttons()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(206))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1)),
            new HitTestTarget(new PixelRectangle(16, 172, 140, 40), new ActionId(2))
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [1, 3], retainedHitTargets);

        Assert.True(projection.Succeeded);
        Assert.Equal(new ActionId(4), projection.HitTargets[0].ActionId);
        Assert.Equal(new ActionId(206), projection.HitTargets[1].ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, projection.HitTargets[0].Bounds);
        Assert.Equal(retainedHitTargets[1].Bounds, projection.HitTargets[1].Bounds);
    }

    [Fact]
    public void HitTargetMetadataProjector_reprojects_nested_button()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(10),
                VirtualNodeBuilder.Button(_arena, "Inner", new NodeKey(2),
                    VirtualNodeProperty.Action(new ActionId(207)))));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(10),
                VirtualNodeBuilder.Button(_arena, "Inner", new NodeKey(2),
                    VirtualNodeProperty.Action(new ActionId(208)))));
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(32, 136, 140, 40), new ActionId(207), new PixelRectangle(16, 16, 928, 508))
        };

        var projection = HitTargetMetadataProjector.ProjectActionIds(retainedRoot, nextRoot, [2], retainedHitTargets);

        Assert.True(projection.Succeeded);
        var target = Assert.Single(projection.HitTargets);
        Assert.Equal(new ActionId(208), target.ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, target.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, target.ClipBounds);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_projects_dirty_control_metadata_from_next_root()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false),
                VirtualNodeProperty.Pressed(false),
                VirtualNodeProperty.Focused(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true),
                VirtualNodeProperty.Pressed(true),
                VirtualNodeProperty.Focused(true)));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.True(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, patch.FallbackReason);
        var patchedButton = patch.Root.Children[0];
        Assert.Equal(new ActionId(4), GetSingleProperty(patchedButton, VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(patchedButton, VirtualPropertyKey.IsHovered, true);
        AssertBooleanProperty(patchedButton, VirtualPropertyKey.IsPressed, true);
        AssertBooleanProperty(patchedButton, VirtualPropertyKey.IsFocused, true);
        Assert.Equal(retainedRoot.Children[0].Children[0], patchedButton.Children[0]);
        Assert.Equal(new ActionId(1), GetSingleProperty(retainedRoot.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_non_dirty_control_metadata_drifts()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2)),
                VirtualNodeProperty.Hovered(true)));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_on_key_path_mismatch()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(99),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_text_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment v2", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_uses_distinct_text_snapshots_for_cross_frame_content_compare()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var retainedSnapshot = _arena.GetOrCreateSnapshot();
        _arena.BeginFrame();
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var nextSnapshot = _arena.GetOrCreateSnapshot();

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(
            retainedRoot,
            nextRoot,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            retainedSnapshot,
            nextSnapshot);

        Assert.True(patch.Succeeded);
        Assert.NotEqual(retainedSnapshot.BufferId, nextSnapshot.BufferId);
        Assert.Equal(new ActionId(4), GetSingleProperty(patch.Root.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(patch.Root.Children[0], VirtualPropertyKey.IsHovered, true);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_layout_property_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true),
                VirtualNodeProperty.Height(52)));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, patch.FallbackReason);
    }

    [Fact]
    public void RetainedRootMetadataPatcher_falls_back_when_tree_shape_changes()
    {
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)),
            VirtualNodeBuilder.Button(_arena, "Decrement", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(2))));

        var patch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], _arena.GetOrCreateSnapshot());

        Assert.False(patch.Succeeded);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, patch.FallbackReason);
    }

    [Fact]
    public void Planner_projector_segment_table_dry_run_chains_preflight_without_runtime_mutation()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var buttonBounds = new PixelRectangle(16, 120, 140, 40);
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        var retainedHitTargets = new[] { new HitTestTarget(buttonBounds, new ActionId(1)) };
        var layoutResult = new LayoutTreeResult(
            [
                new LayoutElement(LayoutElementKind.Text, new PixelRectangle(16, 80, 120, 24)),
                new LayoutElement(LayoutElementKind.Button, buttonBounds, ActionId: new ActionId(4), ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: false))
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
            LayoutRebuildReason: LayoutRebuildReason.StyleOnly,
            PreviousTextSnapshot: _arena.GetOrCreateSnapshot(),
            TextSnapshot: _arena.GetOrCreateSnapshot());
        var planningResolver = new NamedResolver("planning");
        var pipeline = new RenderPipeline();
        using var pipelineFrame = pipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
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
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, snapshot.DirtyClassifications, snapshot.PreviousTextSnapshot, snapshot.TextSnapshot);
        AssertDryRunSentinels(pipeline, pipelineLayoutRebuildCount, pipelineLastViewport, pipelineRetainedFrameCommandCount, pipelineRetainedFrameResources, pipelineRetainedDirtyRanges, pipelineLastDirtyCommandRanges, retainedFrame, retainedFrameCommandCount, retainedFrameResources, retainedFrameDirtyRanges, compositor, compositorRenderCount, compositorFullApplyCount, compositorPartialApplyCount, compositorLastDirtyRanges);

        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, plan.Kind);
        Assert.Equal([(1, 1)], plan.DirtyCommandRanges);
        Assert.True(hitTargetProjection.Succeeded);
        Assert.Equal(new ActionId(4), hitTargetProjection.HitTargets[0].ActionId);
        Assert.True(rootPatch.Succeeded);
        Assert.Equal(new ActionId(4), GetSingleProperty(rootPatch.Root.Children[0], VirtualPropertyKey.ActionId).Value.GetRequiredActionId());
        AssertBooleanProperty(rootPatch.Root.Children[0], VirtualPropertyKey.IsHovered, true);

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
    public void PartialApplyIntegrationGateChecklist_all_v1_core_gates_are_satisfied_before_hookup()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;
        var expectedGates = Enum.GetValues<PartialApplyIntegrationGate>();

        Assert.True(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
        Assert.Equal(expectedGates.Length, gates.Count);
        foreach (var expectedGate in expectedGates)
        {
            var status = Assert.Single(gates, gate => gate.Gate == expectedGate);
            Assert.True(status.Satisfied);
            Assert.False(string.IsNullOrWhiteSpace(status.BlockingCondition));
            Assert.False(string.IsNullOrWhiteSpace(status.PreflightEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.ShadowRuntimeEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.ProductionOffRuntimeEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.ProductionRuntimeEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.NoChangeRegressionEvidence));
            Assert.False(string.IsNullOrWhiteSpace(status.RuntimePromotionCondition));
            Assert.DoesNotContain("None; no", status.ProductionRuntimeEvidence);
            Assert.Contains("Satisfied for current baseline", status.BlockingCondition);
            Assert.Contains("Internal selected render-source path is promoted", status.RuntimePromotionCondition);
        }
    }

    [Fact]
    public void PartialApplyIntegrationGateChecklist_keeps_runtime_promotion_separate_from_public_rollout()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;

        Assert.True(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceResolverOwnership && gate.RuntimePromotionCondition.Contains("external API shape") && gate.RuntimePromotionCondition.Contains("separate target work"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceDisposePolicy && gate.RuntimePromotionCondition.Contains("D3D12-specific") && gate.RuntimePromotionCondition.Contains("separate target work"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.RetainedRootUpdate && gate.RuntimePromotionCondition.Contains("layout-skip") && gate.RuntimePromotionCondition.Contains("separate target work"));
        foreach (var gate in gates)
        {
            Assert.True(gate.Satisfied);
        }
    }

    [Fact]
    public void PartialApplyIntegrationGateChecklist_distinguishes_shadow_runtime_evidence_from_production_runtime_evidence()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;

        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceResolverOwnership && gate.ShadowRuntimeEvidence.Contains("DrawingBackendCompositorShadowProbe") && gate.ProductionRuntimeEvidence.Contains("selected render-source path"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceDisposePolicy && gate.ShadowRuntimeEvidence.Contains("SegmentedRetainedFrameRuntimeOwner") && gate.ShadowRuntimeEvidence.Contains("rebuild") && gate.ProductionRuntimeEvidence.Contains("backend"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.CommandRangeStability && gate.ShadowRuntimeEvidence.Contains("ShadowRejected") && gate.ProductionRuntimeEvidence.Contains("contiguous segment coverage"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.FallbackReporting && gate.ShadowRuntimeEvidence.Contains("Disabled") && gate.ShadowRuntimeEvidence.Contains("ShadowAppliedPartial") && gate.ShadowRuntimeEvidence.Contains("ShadowFallbackFull") && gate.ShadowRuntimeEvidence.Contains("ShadowRejected") && gate.ProductionRuntimeEvidence.Contains("BackendThrewBeforeCommit"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.CompositorOwnership && gate.ShadowRuntimeEvidence.Contains("hit-test no-change") && gate.ProductionRuntimeEvidence.Contains("selected render source"));
        Assert.True(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
    }

    [Fact]
    public void PartialApplyIntegrationGateChecklist_tracks_production_off_and_runtime_evidence_for_satisfied_gates()
    {
        var gates = PartialApplyIntegrationGateChecklist.RequiredGates;

        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceResolverOwnership && gate.ProductionOffRuntimeEvidence.Contains("DrawingBackendCompositor's internal handoff selector") && gate.ProductionOffRuntimeEvidence.Contains("fresh accepted partial owner") && gate.ProductionRuntimeEvidence.Contains("real backend"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.ResourceDisposePolicy && gate.ProductionOffRuntimeEvidence.Contains("multiple FrameDrawingResources") && gate.ProductionOffRuntimeEvidence.Contains("throw path") && gate.ProductionOffRuntimeEvidence.Contains("repeated replacement") && gate.ProductionRuntimeEvidence.Contains("releases accepted partial snapshots"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.CommandRangeStability && gate.ProductionOffRuntimeEvidence.Contains("commands, resources, retained root, and hit targets") && gate.ProductionRuntimeEvidence.Contains("strict dirty ranges"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.HitTargetMetadataProjection && gate.ProductionOffRuntimeEvidence.Contains("handoff harness uses owner-side hit targets") && gate.ProductionOffRuntimeEvidence.Contains("projection-failure fallback") && gate.ProductionRuntimeEvidence.Contains("owner-side hit targets"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.FallbackReporting && gate.ProductionOffRuntimeEvidence.Contains("DrawingBackendCompositor.LastHandoffResult") && gate.ProductionOffRuntimeEvidence.Contains("MissingOwner") && gate.ProductionOffRuntimeEvidence.Contains("Rejected") && gate.ProductionRuntimeEvidence.Contains("internal reason vocabulary"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.CompositorOwnership && gate.ProductionOffRuntimeEvidence.Contains("Production-adjacent no-change") && gate.ProductionOffRuntimeEvidence.Contains("default-off selector") && gate.ProductionOffRuntimeEvidence.Contains("segment-local dirty ranges") && gate.ProductionRuntimeEvidence.Contains("owner-side hit targets"));
        Assert.Contains(gates, gate => gate.Gate == PartialApplyIntegrationGate.RegressionCoverage && gate.ProductionOffRuntimeEvidence.Contains("main-compositor selector default-off lazy allocation") && gate.ProductionOffRuntimeEvidence.Contains("same-frame freshness guard") && gate.ProductionOffRuntimeEvidence.Contains("internal result reporting") && gate.ProductionOffRuntimeEvidence.Contains("dirty-range mismatch routing") && gate.ProductionOffRuntimeEvidence.Contains("clipped/no-hit hit-test") && gate.ProductionRuntimeEvidence.Contains("style-only pre-switch"));
        foreach (var gate in gates)
        {
            Assert.True(gate.Satisfied);
        }

        Assert.True(PartialApplyIntegrationGateChecklist.CanHookUpPartialApply);
    }

    [Fact]
    public async Task Rollback_enabled_to_disabled_compositor_falls_back_to_full_path()
    {
        var cancellationToken = CancellationToken.None;
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var partialRoot = CreateActionButtonRoot(new ActionId(2));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Phase 1: Enabled compositor with ownership (handoff path)
        var enabledPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(
            enabledPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var enabledBackend = new DirtyRangeAwareCapturingBackend();
        using var enabledCompositor = new DrawingBackendCompositor(
            enabledBackend, DrawingBackendCompositorHandoffOptions.Enabled);

        using (var frame1 = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot()))
        {
            await enabledCompositor.RenderAsync(frame1, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        }

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.FallbackFull, enabledCompositor.LastHandoffResult.Kind);
        Assert.Equal(1, enabledCompositor.RenderCount);
        Assert.Equal(1, enabledCompositor.FullApplyCount);

        using (var frame2 = feed.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]))
        {
            await enabledCompositor.RenderAsync(frame2, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        }

        Assert.Equal(2, enabledCompositor.RenderCount);
        enabledCompositor.Dispose();

        // Phase 2: Disabled compositor (production path, no ownership)
        var disabledPipeline = new RenderPipeline();
        var disabledBackend = new DirtyRangeAwareCapturingBackend();
        using var disabledCompositor = new DrawingBackendCompositor(
            disabledBackend, DrawingBackendCompositorHandoffOptions.Disabled);

        using (var frame1 = disabledPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot()))
        {
            await disabledCompositor.RenderAsync(frame1, cancellationToken);
        }

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, disabledCompositor.LastHandoffResult.Kind);
        Assert.Equal(DrawingBackendCompositorHandoffReason.Disabled, disabledCompositor.LastHandoffResult.Reason);
        Assert.Equal(1, disabledCompositor.RenderCount);
        Assert.Equal(1, disabledCompositor.FullApplyCount);
        Assert.Equal(0, disabledCompositor.PartialApplyCount);
        Assert.False(disabledCompositor.LastPartialApplySucceeded);

        using (var frame2 = disabledPipeline.Build(partialRoot, viewport, _arena.GetOrCreateSnapshot(), [2]))
        {
            await disabledCompositor.RenderAsync(frame2, cancellationToken);
        }

        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, disabledCompositor.LastHandoffResult.Kind);
        Assert.Equal(2, disabledCompositor.RenderCount);
        Assert.Equal(2, disabledCompositor.FullApplyCount);
        Assert.Equal(0, disabledCompositor.PartialApplyCount);

        // Verify backend received commands in both phases
        Assert.NotEmpty(enabledBackend.ExecuteCalls);
        Assert.NotEmpty(disabledBackend.ExecuteCalls);
    }

    [Fact]
    public async Task Rollback_default_on_to_forced_off_preserves_retained_frame_state()
    {
        var cancellationToken = CancellationToken.None;
        var retainedRoot = CreateActionButtonRoot(new ActionId(1));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Phase 1: Default-on (enabled handoff) renders a frame
        var enabledPipeline = new RenderPipeline();
        using var feed = new SegmentedRetainedFrameProductionOwnerFeed(
            enabledPipeline, RenderPipelineProductionOwnerOptions.SegmentedRetainedFrameRuntimeOwnerEnabled);
        var enabledBackend = new DirtyRangeAwareCapturingBackend();
        using var enabledCompositor = new DrawingBackendCompositor(
            enabledBackend, DrawingBackendCompositorHandoffOptions.Enabled);

        using (var frame = feed.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot()))
        {
            await enabledCompositor.RenderAsync(frame, feed.SegmentOwnership, new FrameContext(960, 540), cancellationToken);
        }

        Assert.Equal(1, enabledCompositor.RenderCount);
        enabledCompositor.Dispose();

        // Phase 2: Forced-off (disabled handoff) renders the same root
        var disabledPipeline = new RenderPipeline();
        var disabledBackend = new DirtyRangeAwareCapturingBackend();
        using var disabledCompositor = new DrawingBackendCompositor(
            disabledBackend, DrawingBackendCompositorHandoffOptions.Disabled);

        using (var frame = disabledPipeline.Build(retainedRoot, viewport, textSnapshot: _arena.GetOrCreateSnapshot()))
        {
            await disabledCompositor.RenderAsync(frame, cancellationToken);
        }

        // Verify: disabled compositor has clean state (no leftover from enabled path)
        Assert.Equal(DrawingBackendCompositorHandoffResultKind.Disabled, disabledCompositor.LastHandoffResult.Kind);
        Assert.False(disabledCompositor.HasHandoffCandidateHarness);
        Assert.Equal(1, disabledCompositor.RenderCount);
        Assert.Equal(1, disabledCompositor.FullApplyCount);
        Assert.Equal(0, disabledCompositor.PartialApplyCount);
        Assert.False(disabledCompositor.LastPartialApplySucceeded);

        // Verify: backend received commands (retained frame works)
        Assert.NotEmpty(disabledBackend.ExecuteCalls);
    }

    private static void AssertTextProperty(VirtualNode node, VirtualPropertyKey key, string expected)
    {
        var property = GetSingleProperty(node, key);
        Assert.True(property.Value.TryGetNumber(out _));
    }

    private static void AssertBooleanProperty(VirtualNode node, VirtualPropertyKey key, bool expected)
    {
        var property = GetSingleProperty(node, key);
        Assert.Equal(PropertyValueKind.Boolean, property.Value.Kind);
        Assert.Equal(expected, property.Value.GetRequiredBoolean());
    }

    private static VirtualNodeProperty GetSingleProperty(VirtualNode node, VirtualPropertyKey key)
    {
        var matchCount = 0;
        var match = default(VirtualNodeProperty);
        foreach (var property in node.Properties)
        {
            if (property.Key != key)
            {
                continue;
            }

            matchCount++;
            match = property;
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
        IReadOnlyList<HitTestTarget> retainedHitTargets,
        TextBufferSnapshot? textSnapshot = null)
    {
        return CreateDirtySnapshot(viewport, buttonBounds, retainedRoot, retainedHitTargets, LayoutRebuildReason.StyleOnly, textSnapshot);
    }

    private static RenderPipelineRetainedInputSnapshot CreateDirtySnapshot(
        PixelRectangle viewport,
        PixelRectangle buttonBounds,
        VirtualNode retainedRoot,
        IReadOnlyList<HitTestTarget> retainedHitTargets,
        LayoutRebuildReason reason,
        TextBufferSnapshot? textSnapshot = null)
    {
        var layoutResult = new LayoutTreeResult(
            [
                new LayoutElement(LayoutElementKind.Text, new PixelRectangle(16, 80, 120, 24)),
                new LayoutElement(LayoutElementKind.Button, buttonBounds, ActionId: new ActionId(4), ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: false))
            ],
            [],
            [(1, 1)]);
        return new RenderPipelineRetainedInputSnapshot(
            layoutResult,
            [new ElementCommandRange(0, 1), new ElementCommandRange(1, 1)],
            retainedHitTargets,
            retainedRoot,
            viewport,
            [new LayoutDirtyClassification(1, reason)],
            [(1, 1)],
            [(1, 1)],
            LayoutRebuildReason: reason,
            PreviousTextSnapshot: textSnapshot,
            TextSnapshot: textSnapshot);
    }

    private VirtualNode CreateActionButtonRoot(ActionId actionId)
    {
        return VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(actionId),
                VirtualNodeProperty.Hovered(false)));
    }

    private static RenderFrameBatch CreateFrameResourceTextBatch(
        string text,
        IReadOnlyList<HitTestTarget> hitTargets,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges,
        int commandCount)
    {
        var resources = FrameDrawingResources.Rent();
        var textSlice = string.IsNullOrEmpty(text) ? default : resources.AddText(text);
        resources.Seal();
        var commands = new DrawCommand[commandCount];
        Array.Fill(commands, new DrawCommand(DrawCommandKind.DrawTextRun, Text: textSlice));
        return new RenderFrameBatch(CreateCommandBatch(commands), hitTargets, resources, dirtyCommandRanges);
    }

    private static string ResolveSegmentText(SegmentedFrameRead segment)
    {
        Assert.NotEmpty(segment.Commands);
        return segment.Resolver.Resolve(segment.Commands[0].Text).ToString();
    }

    private static void AssertOwnerAndProductionHit(
        RetainedRenderFrameSegmentOwnership ownership,
        DrawingBackendCompositor compositor,
        int x,
        int y,
        bool expectedHit,
        ActionId expectedActionId)
    {
        var ownerHit = ownership.TryGetSegmentedOwnerActionIdAt(x, y, out var ownerActionId);
        var productionHit = compositor.TryGetActionIdAtPhysicalPixel(x, y, out var productionActionId);

        Assert.Equal(expectedHit, ownerHit);
        Assert.Equal(expectedHit, productionHit);
        Assert.Equal(expectedActionId, ownerActionId);
        Assert.Equal(expectedActionId, productionActionId);
    }

    private static void AssertFrameResourceCapture(
        FrameResourceSnapshotCapture capture,
        IFrameResourceResolver resolver,
        int retainCount,
        int releaseCount)
    {
        Assert.Same(resolver, capture.Resolver);
        Assert.Equal(retainCount, capture.RetainCount);
        Assert.Equal(releaseCount, capture.ReleaseCount);
    }

    private static SegmentedRetainedFrameOwner CreateTwoSegmentShadowOwner(out IFrameResourceResolver oldResolver, out IFrameResourceResolver replacementResolver)
    {
        var arena = new VirtualTextArena();
        var owner = new SegmentedRetainedFrameOwner();
        oldResolver = new NamedResolver("old");
        replacementResolver = new NamedResolver("replacement");
        var retainedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var nextRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4)),
                VirtualNodeProperty.Hovered(true)));
        using var oldBatch = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementCommands = CreateCommandBatch(
            new DrawCommand(DrawCommandKind.DrawTextRun),
            new DrawCommand(DrawCommandKind.DrawTextRun));
        using var replacementBatch = new RenderFrameBatch(replacementCommands, [], replacementResolver, [(1, 1)]);
        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(retainedRoot, nextRoot, [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], arena.GetOrCreateSnapshot());

        owner.ApplyFull(oldBatch, RetainedResourceSnapshot.Capture(oldResolver), retainedRoot);
        Assert.True(owner.TryAcceptPartial(replacementBatch, RetainedResourceSnapshot.Capture(replacementResolver), rootPatch));
        return owner;
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

    private static void AssertExecuteCommandCountsEqual(
        IReadOnlyList<(int CommandCount, IFrameResourceResolver Resolver)> expected,
        IReadOnlyList<(int CommandCount, IFrameResourceResolver Resolver)> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var callIndex = 0; callIndex < expected.Count; callIndex++)
        {
            Assert.Equal(expected[callIndex].CommandCount, actual[callIndex].CommandCount);
        }
    }

    private static void AssertSegmentedReadsEqual(IReadOnlyList<SegmentedFrameRead> expected, IReadOnlyList<SegmentedFrameRead> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var segmentIndex = 0; segmentIndex < expected.Count; segmentIndex++)
        {
            Assert.Equal(expected[segmentIndex].CommandStart, actual[segmentIndex].CommandStart);
            Assert.Same(expected[segmentIndex].Resolver, actual[segmentIndex].Resolver);
            Assert.Equal(expected[segmentIndex].Commands, actual[segmentIndex].Commands);
        }
    }

    private static void AssertHandoffBackendCallsMatchReads(
        IReadOnlyList<RetainedRenderFrameHandoffHarnessResult> results,
        CapturingBackend backend)
    {
        var expectedCalls = new List<DrawingBackendCall>();
        var expectedExecuteCallCount = 0;
        foreach (var result in results)
        {
            if (result.Reads.Count == 0)
            {
                continue;
            }

            expectedCalls.Add(BeginFrameCall);
            foreach (var read in result.Reads)
            {
                expectedCalls.Add(ExecuteCall(read.Commands.Length));
                expectedExecuteCallCount++;
            }

            expectedCalls.Add(EndFrameCall);
        }

        Assert.Equal(expectedCalls, backend.Calls);
        Assert.Equal(expectedExecuteCallCount, backend.ExecuteCalls.Count);
    }

    private static void AssertDirtyRangePlansMatchBackend(
        IReadOnlyList<RetainedRenderFrameHandoffHarnessResult> results,
        DirtyRangeAwareCapturingBackend backend)
    {
        var expectedDirtyRanges = new List<(int Start, int Count)[]>();
        foreach (var result in results)
        {
            foreach (var segment in result.DirtyRangePlan)
            {
                expectedDirtyRanges.Add(segment.SegmentDirtyRanges.ToArray());
            }
        }

        Assert.Equal(expectedDirtyRanges.Count, backend.SetDirtyCommandRangeCount);
        Assert.Equal(expectedDirtyRanges.Count, backend.DirtyRanges.Count);
        for (var i = 0; i < expectedDirtyRanges.Count; i++)
        {
            Assert.Equal(expectedDirtyRanges[i], backend.DirtyRanges[i]);
        }
    }

    private static SegmentedRetainedFrameProductionOwnerFeedResult InvokeProductionOwnerFeedUpdate(
        SegmentedRetainedFrameProductionOwnerFeed feed,
        VirtualNode root,
        PixelRectangle viewport,
        RenderFrameBatch batch)
    {
        var method = typeof(SegmentedRetainedFrameProductionOwnerFeed).GetMethod("UpdateRuntimeOwner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (SegmentedRetainedFrameProductionOwnerFeedResult)method.Invoke(feed, [root, viewport, batch])!;
    }

    private static void SetOwnershipLastResult(RetainedRenderFrameSegmentOwnership ownership, SegmentedRetainedFrameProductionOwnerFeedResult result)
    {
        var field = typeof(RetainedRenderFrameSegmentOwnership).GetField("<LastResult>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(ownership, result);
    }

    private static void SetRuntimeOwnerSegmentsUnchecked(
        SegmentedRetainedFrameRuntimeOwner runtimeOwner,
        IReadOnlyList<RetainedResourceSegment> segments)
    {
        var ownerField = typeof(SegmentedRetainedFrameRuntimeOwner).GetField("_owner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var owner = (SegmentedRetainedFrameOwner)ownerField.GetValue(runtimeOwner)!;
        var resourceSegmentsField = typeof(SegmentedRetainedFrameOwner).GetField("_resourceSegments", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var resourceSegments = (RetainedResourceSegmentTable)resourceSegmentsField.GetValue(owner)!;
        resourceSegments.ApplyUncheckedForPreflight(segments);
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

    private sealed class SnapshotTracker(string resolverText = "")
    {
        private readonly string _resolverText = resolverText;

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

    private sealed class FrameResourceSnapshotCaptureTracker
    {
        public List<FrameResourceSnapshotCapture> Captures { get; } = [];

        public RetainedResourceSnapshot Capture(IFrameResourceResolver resolver)
        {
            var capture = new FrameResourceSnapshotCapture(resolver);
            Captures.Add(capture);
            return RetainedResourceSnapshot.Capture(
                resolver,
                retain: () => capture.RetainCount++,
                release: () => capture.ReleaseCount++);
        }
    }

    private sealed class FrameResourceSnapshotCapture(IFrameResourceResolver resolver)
    {
        public IFrameResourceResolver Resolver { get; } = resolver;

        public int RetainCount { get; set; }

        public int ReleaseCount { get; set; }
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
        public List<DrawingBackendCall> Calls { get; } = [];

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
            Calls.Add(DrawingBackendCall.BeginFrame);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCalls.Add((commands.Length, resources));
            Calls.Add(DrawingBackendCall.Execute(commands.Length));
        }

        public void EndFrame()
        {
            EndFrameCount++;
            Calls.Add(DrawingBackendCall.EndFrame);
        }

        public void Dispose()
        {
        }
    }

    private sealed class DirtyRangeAwareCapturingBackend : CapturingBackend, IDirtyRangeAware
    {
        public int SetDirtyCommandRangeCount { get; private set; }

        public List<(int Start, int Count)[]> DirtyRanges { get; } = [];

        public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
        {
            SetDirtyCommandRangeCount++;
            DirtyRanges.Add(ranges.ToArray());
        }
    }

    private sealed class ThrowingBackend(int throwOnExecuteCall) : IDrawingBackend
    {
        public int BeginFrameCount { get; private set; }
        public int EndFrameCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public List<DrawingBackendCall> Calls { get; } = [];

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
            Calls.Add(DrawingBackendCall.BeginFrame);
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
            Calls.Add(DrawingBackendCall.Execute(commands.Length));
            if (ExecuteCount == throwOnExecuteCall)
            {
                throw new InvalidOperationException("execute failed");
            }
        }

        public void EndFrame()
        {
            EndFrameCount++;
            Calls.Add(DrawingBackendCall.EndFrame);
        }

        public void Dispose()
        {
        }
    }

    private sealed class DisposeTrackingBackend : IDrawingBackend
    {
        public int DisposeCount { get; private set; }

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
            DisposeCount++;
        }
    }
}
