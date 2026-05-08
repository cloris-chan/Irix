using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowLayoutPipelineTests
{
    [Fact]
    public void LayoutTreeBuilder_builds_expected_layout_elements()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Rectangle(220, 48, 3),
            VirtualNodeFactory.Button(
                "Increment",
                4,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));
        var builder = new LayoutTreeBuilder();

        var elements = builder.Build(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Equal(3, elements.Count);

        Assert.Equal(LayoutElementKind.Text, elements[0].Kind);
        Assert.Equal(new PixelRectangle(16, 16, 928, 32), elements[0].Bounds);
        Assert.Equal("Count: 0", elements[0].Text);

        Assert.Equal(LayoutElementKind.Rectangle, elements[1].Kind);
        Assert.Equal(new PixelRectangle(16, 60, 220, 48), elements[1].Bounds);

        Assert.Equal(LayoutElementKind.Button, elements[2].Kind);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), elements[2].Bounds);
        Assert.Equal("Increment", elements[2].Text);
        Assert.Equal("Increment", elements[2].ActionId);
    }

    [Fact]
    public void DrawCommandRecorder_records_button_as_fill_and_text_commands()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: "Increment",
                ActionId: "Increment")
        };

        var result = recorder.Record(elements);

        Assert.Equal(2, result.Commands.Count);

        var fillCommand = result.Commands.Memory.Span[0];
        Assert.Equal(DrawCommandKind.FillRect, fillCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), fillCommand.Rect);
        var textCommand = result.Commands.Memory.Span[1];
        Assert.Equal(DrawCommandKind.DrawTextRun, textCommand.Kind);
        Assert.Equal(new DrawRect(16, 120, 140, 40), textCommand.Rect);
        Assert.Equal(DrawingResourceKind.TextStyle, textCommand.Resource.Kind);
        Assert.Equal(TextStyle.Default, result.Resources.ResolveTextStyle(textCommand.Resource));

        Assert.Equal("Increment", result.Resources.Resolve(textCommand.Text).ToString());

        result.Commands.Dispose();
    }

    [Fact]
    public void LayoutTreeBuilder_uses_supplied_layout_style()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Title", 2),
            VirtualNodeFactory.Button(
                "Go",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Go"))));
        var builder = new LayoutTreeBuilder(new LayoutStyle(
            HorizontalPadding: 24,
            VerticalPadding: 20,
            ItemSpacing: 8,
            TextHeight: 28,
            ButtonHeight: 36,
            MinimumButtonWidth: 120,
            ButtonTextWidthFactor: 10,
            ButtonHorizontalPadding: 20));

        var elements = builder.Build(root, new PixelRectangle(0, 0, 400, 300));

        Assert.Equal(new PixelRectangle(24, 20, 352, 28), elements[0].Bounds);
        Assert.Equal(new PixelRectangle(24, 56, 120, 36), elements[1].Bounds);
    }

    [Fact]
    public void DrawCommandRecorder_uses_supplied_drawing_style()
    {
        var recorder = new DrawCommandRecorder(new DrawingStyle(
            TextColor: DrawColor.Opaque(230, 230, 230),
            RectangleFillColor: DrawColor.Opaque(20, 30, 40),
            ButtonFillColor: DrawColor.Opaque(10, 20, 30),
            ButtonTextColor: DrawColor.Opaque(200, 210, 220),
            TextStyle: TextStyle.Default,
            ButtonTextStyle: TextStyle.Default));
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Rectangle,
                new PixelRectangle(16, 60, 220, 48)),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: "Increment",
                ActionId: "Increment")
        };

        var result = recorder.Record(elements);

        Assert.Equal(DrawColor.Opaque(20, 30, 40), result.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawColor.Opaque(10, 20, 30), result.Commands.Memory.Span[1].Color);
        Assert.Equal(DrawColor.Opaque(200, 210, 220), result.Commands.Memory.Span[2].Color);

        result.Commands.Dispose();
    }

    [Fact]
    public void RenderPipeline_builds_draw_commands_from_virtual_node()
    {
        var pipeline = new RenderPipeline(
            new LayoutStyle(
                HorizontalPadding: 16,
                VerticalPadding: 16,
                ItemSpacing: 12,
                TextHeight: 32,
                ButtonHeight: 40,
                MinimumButtonWidth: 140,
                ButtonTextWidthFactor: 12,
                ButtonHorizontalPadding: 32),
            new DrawingStyle(
                TextColor: DrawColor.Opaque(255, 255, 255),
                RectangleFillColor: DrawColor.Opaque(72, 72, 72),
                ButtonFillColor: DrawColor.Opaque(52, 120, 246),
                ButtonTextColor: DrawColor.Opaque(255, 255, 255),
                TextStyle: TextStyle.Default,
                ButtonTextStyle: TextStyle.Default));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button(
                "Increment",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[0].Kind);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(DrawCommandKind.FillRect, frame.Commands.Memory.Span[1].Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[2].Kind);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment"), frame.HitTargets[0]);

        Assert.Equal("Count: 0", frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString());
        Assert.Equal("Increment", frame.Resources.Resolve(frame.Commands.Memory.Span[2].Text).ToString());
        Assert.Equal(TextStyle.Default, frame.Resources.ResolveTextStyle(frame.Commands.Memory.Span[0].Resource));
    }

    [Fact]
    public void WindowDrawCommandTranslator_uses_non_zero_default_layout_style()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button(
                "Increment",
                3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        using var patchBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root));
        using var frame = translator.Translate(patchBatch);

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[1].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[2].Rect);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment"), frame.HitTargets[0]);
    }

    [Fact]
    public void RenderRequest_before_first_diff_does_not_crash()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));

        // Simulate a RenderRequest before any diff has set _lastTree
        var renderRequest = PatchBatch.CreateRenderRequest();
        using var frame = translator.Translate(renderRequest);

        // Should produce an empty frame (no commands) since _lastTree is default
        Assert.Equal(0, frame.Commands.Count);
    }

    [Fact]
    public void RenderPipeline_reuses_retained_layout_when_tree_and_viewport_unchanged()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame1 = pipeline.Build(root, viewport);
        var frame1Text = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();
        using var frame2 = pipeline.Build(root, viewport);
        var frame2Text = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        // Both frames should have identical layout
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
        Assert.Equal(frame1.HitTargets.Count, frame2.HitTargets.Count);
        Assert.Equal(frame1Text, frame2Text);
        for (var i = 0; i < frame1.Commands.Count; i++)
        {
            Assert.Equal(frame1.Commands.Memory.Span[i].Rect, frame2.Commands.Memory.Span[i].Rect);
            Assert.Equal(frame1.Commands.Memory.Span[i].Kind, frame2.Commands.Memory.Span[i].Kind);
        }
    }

    [Fact]
    public void RenderPipeline_rebuilds_layout_when_viewport_changes()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2));

        using var frame1 = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));
        using var frame2 = pipeline.Build(root, new PixelRectangle(0, 0, 1920, 1080));

        // Layout should differ because viewport width changed
        var text1 = frame1.Commands.Memory.Span[0];
        var text2 = frame2.Commands.Memory.Span[0];
        Assert.NotEqual(text1.Rect.Width, text2.Rect.Width);
    }

    [Fact]
    public void RenderPipeline_rebuilds_layout_when_tree_changes()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(1, VirtualNodeFactory.Text("Hello", 2));
        var root2 = VirtualNodeFactory.ScrollContainer(1, VirtualNodeFactory.Text("World", 2));

        using var frame1 = pipeline.Build(root1, viewport);
        var text1 = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();
        using var frame2 = pipeline.Build(root2, viewport);
        var text2 = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        Assert.Equal("Hello", text1);
        Assert.Equal("World", text2);
    }

    private sealed class FakeWindow(ScreenRegion region) : INativeWindow
    {
        public string Title => "Test";

        public ScreenRegion Region { get; set; } = region;

        public bool ExternalRenderingEnabled { get; set; }

        public nint Handle => nint.Zero;

        public void Dispose()
        {
        }

        public void RunMessageLoop()
        {
        }

        public void SetContentElements(IReadOnlyList<WindowContentElement> elements)
        {
        }

        public void Show()
        {
        }

        public event Action<int, int>? SizeChanged { add { } remove { } }
    }

    [Fact]
    public void RenderPipeline_rebuilds_layout_when_dirty_nodes_provided()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // First build with no dirty
        using var frame1 = pipeline.Build(root, viewport);

        // Second build with same root/viewport but dirty nodes → forces rebuild
        using var frame2 = pipeline.Build(root, viewport, [0]);

        // Both frames should have identical layout (v0: full rebuild regardless)
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
        for (var i = 0; i < frame1.Commands.Count; i++)
        {
            Assert.Equal(frame1.Commands.Memory.Span[i].Rect, frame2.Commands.Memory.Span[i].Rect);
        }
    }

    [Fact]
    public void RenderPipeline_reuses_layout_when_dirty_nodes_empty()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // First build
        using var frame1 = pipeline.Build(root, viewport);
        var text1 = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();

        // Second build with empty dirty set → should reuse retained layout
        using var frame2 = pipeline.Build(root, viewport, []);
        var text2 = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        Assert.Equal(text1, text2);
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
    }

    [Fact]
    public void WindowDrawCommandTranslator_diff_batch_produces_correct_layout()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));

        var root1 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Increment", 3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        // Initial frame via diff from default → root1
        using var batch1 = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root1));
        using var frame1 = translator.Translate(batch1);
        Assert.Equal(3, frame1.Commands.Count);

        var root2 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 1", 2),
            VirtualNodeFactory.Button("Increment", 3,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Increment"))));

        // Update frame via diff from root1 → root2
        using var batch2 = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(root1), new VirtualNodeTree(root2));
        using var frame2 = translator.Translate(batch2);

        // Layout should reflect updated content
        var textContent = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("Count: 1", textContent);
    }

    [Fact]
    public void WindowDrawCommandTranslator_render_request_reuses_retained_tree()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));

        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));

        // Set up retained tree via initial diff
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root));
        using var frame1 = translator.Translate(batch);
        Assert.Equal(3, frame1.Commands.Count);

        // Render request should reuse retained tree
        var renderRequest = PatchBatch.CreateRenderRequest();
        using var frame2 = translator.Translate(renderRequest);

        // Should have the same layout as before (retained tree is reused)
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
        for (var i = 0; i < frame1.Commands.Count; i++)
        {
            Assert.Equal(frame1.Commands.Memory.Span[i].Rect, frame2.Commands.Memory.Span[i].Rect);
        }
    }

    [Fact]
    public void LayoutTree_text_update_produces_correct_dirty_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("before", 2),
            VirtualNodeFactory.Button("Click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Build with dirty node 1 (the Text node)
        var result = builder.BuildLayoutTree(root, viewport, [1]);

        // Text is element 0, Button is element 1
        Assert.Equal(2, result.Elements.Count);
        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((0, 1), result.DirtyElementRanges[0]); // Text element at index 0
    }

    [Fact]
    public void LayoutTree_button_update_produces_correct_dirty_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Build with dirty node 2 (the Button node)
        var result = builder.BuildLayoutTree(root, viewport, [2]);

        // Text is element 0, Button is element 1
        Assert.Equal(2, result.Elements.Count);
        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((1, 1), result.DirtyElementRanges[0]); // Button element at index 1
    }

    [Fact]
    public void LayoutTree_add_remove_child_produces_parent_dirty_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty node 0 (root/parent) → its element range spans all children
        var result = builder.BuildLayoutTree(root, viewport, [0]);

        Assert.Equal(2, result.Elements.Count);
        Assert.Single(result.DirtyElementRanges);
        // Root's element range covers all children: elements 0..1
        Assert.Equal((0, 2), result.DirtyElementRanges[0]);
    }

    [Fact]
    public void LayoutTree_multiple_dirty_nodes_produce_multiple_ranges()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3),
            VirtualNodeFactory.Text("c", 4));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty nodes 1 and 3 (first and third text nodes)
        var result = builder.BuildLayoutTree(root, viewport, [1, 3]);

        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(2, result.DirtyElementRanges.Count);
        Assert.Equal((0, 1), result.DirtyElementRanges[0]); // Text "a" at element 0
        Assert.Equal((2, 1), result.DirtyElementRanges[1]); // Text "c" at element 2
    }

    [Fact]
    public void LayoutTree_viewport_change_no_dirty_ranges()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // No dirty nodes → empty dirty ranges
        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.Elements);
        Assert.Empty(result.DirtyElementRanges);
    }

    [Fact]
    public void LayoutTree_tree_structure_maps_dfs_indices()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("first", 2),
            VirtualNodeFactory.Button("btn", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        // Should have 1 top-level tree node (root ScrollContainer)
        Assert.Single(result.TreeNodes);
        var rootNode = result.TreeNodes[0];
        Assert.Equal(0, rootNode.DfsIndex);
        Assert.Equal(2, rootNode.ElementCount); // 2 children → 2 elements
        Assert.Equal(2, rootNode.Children.Length);

        // Children should be the Text and Button
        Assert.Equal(1, rootNode.Children[0].DfsIndex);
        Assert.Equal(VirtualNodeKind.Text, rootNode.Children[0].Kind);
        Assert.Equal(0, rootNode.Children[0].ElementStart);
        Assert.Equal(1, rootNode.Children[0].ElementCount);

        Assert.Equal(2, rootNode.Children[1].DfsIndex);
        Assert.Equal(VirtualNodeKind.Button, rootNode.Children[1].Kind);
        Assert.Equal(1, rootNode.Children[1].ElementStart);
        Assert.Equal(1, rootNode.Children[1].ElementCount);
    }

    [Fact]
    public void LayoutTree_incremental_text_update_only_affects_one_element()
    {
        var builder = new LayoutTreeBuilder();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var root1 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var result1 = builder.BuildLayoutTree(root1, viewport);
        Assert.Equal(2, result1.Elements.Count);

        // Simulate text update: dirty node 1 (the Text)
        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 1", 2),
            VirtualNodeFactory.Button("Click", 3));
        var result2 = builder.BuildLayoutTree(root2, viewport, [1]);

        // Only element 0 (Text) should be in dirty range
        Assert.Single(result2.DirtyElementRanges);
        Assert.Equal((0, 1), result2.DirtyElementRanges[0]);

        // Button element (index 1) bounds should be identical
        Assert.Equal(result1.Elements[1].Bounds, result2.Elements[1].Bounds);
    }

    [Fact]
    public void LayoutTree_parent_and_child_dirty_ranges_are_merged()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty node 0 (parent) and node 1 (child "a")
        // Parent's range covers both children → child's range is subsumed
        var result = builder.BuildLayoutTree(root, viewport, [0, 1]);

        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((0, 2), result.DirtyElementRanges[0]); // merged into one range
    }

    [Fact]
    public void LayoutTree_adjacent_dirty_ranges_are_merged()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3),
            VirtualNodeFactory.Text("c", 4));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty nodes 1 and 2 (adjacent elements "a" and "b")
        var result = builder.BuildLayoutTree(root, viewport, [1, 2]);

        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((0, 2), result.DirtyElementRanges[0]); // adjacent → merged
    }

    [Fact]
    public void LayoutTree_non_adjacent_dirty_ranges_stay_separate()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("a", 2),
            VirtualNodeFactory.Text("b", 3),
            VirtualNodeFactory.Text("c", 4));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty nodes 1 and 3 (non-adjacent elements "a" and "c")
        var result = builder.BuildLayoutTree(root, viewport, [1, 3]);

        Assert.Equal(2, result.DirtyElementRanges.Count);
        Assert.Equal((0, 1), result.DirtyElementRanges[0]); // "a"
        Assert.Equal((2, 1), result.DirtyElementRanges[1]); // "c"
    }

    [Fact]
    public void DrawCommandRecorder_maps_elements_to_command_ranges()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: "hello"),
            new(LayoutElementKind.Rectangle, new PixelRectangle(0, 40, 100, 48)),
            new(LayoutElementKind.Button, new PixelRectangle(0, 100, 100, 40), Text: "click"),
        };

        var result = recorder.Record(elements);

        // Text → 1 command (DrawTextRun)
        Assert.Equal(new ElementCommandRange(0, 1), result.ElementCommandRanges[0]);
        // Rectangle → 1 command (FillRect)
        Assert.Equal(new ElementCommandRange(1, 1), result.ElementCommandRanges[1]);
        // Button → 2 commands (FillRect + DrawTextRun)
        Assert.Equal(new ElementCommandRange(2, 2), result.ElementCommandRanges[2]);
    }

    [Fact]
    public void DrawCommandRecorder_computes_dirty_command_ranges()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: "hello"),
            new(LayoutElementKind.Button, new PixelRectangle(0, 40, 100, 40), Text: "click"),
            new(LayoutElementKind.Text, new PixelRectangle(0, 100, 100, 32), Text: "world"),
        };

        // Dirty element range: element 1 (the Button, which produces 2 commands)
        var result = recorder.Record(elements, [(1, 1)]);

        // Button maps to commands 1..2 (index 1, count 2)
        Assert.Single(result.DirtyCommandRanges);
        Assert.Equal((1, 2), result.DirtyCommandRanges[0]);
    }

    [Fact]
    public void DrawCommandRecorder_merges_adjacent_dirty_command_ranges()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: "a"),
            new(LayoutElementKind.Button, new PixelRectangle(0, 40, 100, 40), Text: "b"),
            new(LayoutElementKind.Text, new PixelRectangle(0, 100, 100, 32), Text: "c"),
        };

        // Dirty elements 0 and 1 → commands 0..2 (adjacent, should merge)
        var result = recorder.Record(elements, [(0, 2)]);

        Assert.Single(result.DirtyCommandRanges);
        Assert.Equal((0, 3), result.DirtyCommandRanges[0]); // commands 0,1,2
    }

    [Fact]
    public void DrawCommandRecorder_empty_dirty_ranges_returns_empty()
    {
        var recorder = new DrawCommandRecorder();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: "hello"),
        };

        var result = recorder.Record(elements, []);

        Assert.Empty(result.DirtyCommandRanges);
        Assert.Single(result.ElementCommandRanges);
    }

    [Fact]
    public void RenderPipeline_exposes_dirty_command_ranges_on_dirty_build()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 0", 2),
            VirtualNodeFactory.Button("Click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport);
        Assert.Empty(pipeline.LastDirtyElementRanges);
        Assert.Empty(pipeline.LastDirtyCommandRanges);

        // Build with dirty node 1 (Text) → should produce dirty ranges
        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Count: 1", 2),
            VirtualNodeFactory.Button("Click", 3));
        using var frame2 = pipeline.Build(root2, viewport, [1]);

        Assert.Single(pipeline.LastDirtyElementRanges);
        Assert.Equal((0, 1), pipeline.LastDirtyElementRanges[0]);

        // Text → 1 draw command at index 0
        Assert.Single(pipeline.LastDirtyCommandRanges);
        Assert.Equal((0, 1), pipeline.LastDirtyCommandRanges[0]);
    }

    [Fact]
    public void RenderPipeline_element_command_mapping_reflects_button_two_commands()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2),
            VirtualNodeFactory.Button("click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame = pipeline.Build(root, viewport);

        Assert.Equal(2, pipeline.LastElementCommandRanges.Length);
        // Text → 1 command
        Assert.Equal(new ElementCommandRange(0, 1), pipeline.LastElementCommandRanges[0]);
        // Button → 2 commands
        Assert.Equal(new ElementCommandRange(1, 2), pipeline.LastElementCommandRanges[1]);
    }

    [Fact]
    public void RangeUtils_merge_combines_adjacent_ranges()
    {
        var ranges = new List<(int, int)> { (0, 1), (1, 2), (4, 2) };
        var merged = RangeUtils.Merge(ranges);

        Assert.Equal(2, merged.Count);
        Assert.Equal((0, 3), merged[0]); // (0,1) + (1,2) → (0,3)
        Assert.Equal((4, 2), merged[1]);
    }

    [Fact]
    public void RangeUtils_merge_combines_overlapping_ranges()
    {
        var ranges = new List<(int, int)> { (0, 3), (2, 4) };
        var merged = RangeUtils.Merge(ranges);

        Assert.Single(merged);
        Assert.Equal((0, 6), merged[0]); // overlap → merged
    }

    [Fact]
    public void RangeUtils_merge_single_range_unchanged()
    {
        var ranges = new List<(int, int)> { (5, 3) };
        var merged = RangeUtils.Merge(ranges);

        Assert.Single(merged);
        Assert.Equal((5, 3), merged[0]);
    }

    [Fact]
    public void RangeUtils_merge_empty_returns_empty()
    {
        var merged = RangeUtils.Merge([]);
        Assert.Empty(merged);
    }

    [Fact]
    public void RangeUtils_contains_finds_index_in_range()
    {
        var ranges = new List<(int, int)> { (0, 2), (5, 3) };
        Assert.True(RangeUtils.Contains(ranges, 0));
        Assert.True(RangeUtils.Contains(ranges, 1));
        Assert.False(RangeUtils.Contains(ranges, 2));
        Assert.True(RangeUtils.Contains(ranges, 5));
        Assert.True(RangeUtils.Contains(ranges, 7));
        Assert.False(RangeUtils.Contains(ranges, 8));
    }

    [Fact]
    public void RangeUtils_map_and_merge_converts_element_to_command_ranges()
    {
        var elementRanges = new ElementCommandRange[]
        {
            new(0, 1),  // element 0 → command 0
            new(1, 2),  // element 1 → commands 1-2
            new(3, 1),  // element 2 → command 3
        };
        var dirtyElements = new List<(int, int)> { (0, 2) }; // elements 0-1

        var result = RangeUtils.MapAndMerge(elementRanges, dirtyElements);

        Assert.Single(result);
        Assert.Equal((0, 3), result[0]); // commands 0-2
    }

    [Fact]
    public void RetainedCommandBuffer_full_apply_stores_commands()
    {
        var buffer = new RetainedCommandBuffer();
        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 100)),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(10, 10, 80, 32)),
        ]);
        var batch = new DrawCommandBatch(owner, 2);

        buffer.ApplyFull(batch);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(DrawCommandKind.FillRect, buffer.Commands[0].Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, buffer.Commands[1].Kind);
    }

    [Fact]
    public void RetainedCommandBuffer_partial_apply_replaces_dirty_range()
    {
        var buffer = new RetainedCommandBuffer();
        var initialOwner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 100)),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(10, 10, 80, 32), Text: default),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 50, 100, 40)),
        ]);
        buffer.ApplyFull(new DrawCommandBatch(initialOwner, 3));

        // New batch with different text at index 1
        var newOwner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 100)),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(10, 10, 80, 32), Text: default),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 50, 100, 40)),
        ]);
        var dirtyRanges = new List<(int, int)> { (1, 1) }; // only command 1 is dirty

        buffer.ApplyPartial(new DrawCommandBatch(newOwner, 3), dirtyRanges);

        Assert.Equal(3, buffer.Count);
        // Commands 0 and 2 should be from the initial batch (unchanged)
        // Command 1 should be from the new batch
        Assert.Equal(DrawCommandKind.FillRect, buffer.Commands[0].Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, buffer.Commands[1].Kind);
        Assert.Equal(DrawCommandKind.FillRect, buffer.Commands[2].Kind);
    }

    [Fact]
    public void RetainedCommandBuffer_partial_apply_falls_back_to_full_when_count_differs()
    {
        var buffer = new RetainedCommandBuffer();
        var initialOwner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 100)),
        ]);
        buffer.ApplyFull(new DrawCommandBatch(initialOwner, 1));

        // New batch has different count → falls back to full
        var newOwner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(10, 10, 80, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 50, 100, 40)),
        ]);
        buffer.ApplyPartial(new DrawCommandBatch(newOwner, 2), [(0, 1)]);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(DrawCommandKind.DrawTextRun, buffer.Commands[0].Kind);
    }

    [Fact]
    public void RetainedCommandBuffer_reset_preserves_capacity()
    {
        var buffer = new RetainedCommandBuffer();
        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 100)),
        ]);
        buffer.ApplyFull(new DrawCommandBatch(owner, 1));
        Assert.Equal(1, buffer.Count);

        buffer.Reset();
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void RenderFrameBatch_carries_dirty_command_ranges()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2),
            VirtualNodeFactory.Button("click", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport);
        Assert.Empty(frame1.DirtyCommandRanges);

        // Dirty build
        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("world", 2),
            VirtualNodeFactory.Button("click", 3));
        using var frame2 = pipeline.Build(root2, viewport, [1]);

        Assert.Single(frame2.DirtyCommandRanges);
        Assert.Equal((0, 1), frame2.DirtyCommandRanges[0]); // Text → 1 command at index 0
    }

    [Fact]
    public void RenderFrameBatch_button_dirty_produces_two_dirty_commands()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2),
            VirtualNodeFactory.Button("old", 3));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame1 = pipeline.Build(root, viewport);

        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2),
            VirtualNodeFactory.Button("new", 3));
        using var frame2 = pipeline.Build(root2, viewport, [2]); // dirty button at DFS index 2

        // Button → 2 commands (FillRect + DrawTextRun) at index 1-2
        Assert.Single(frame2.DirtyCommandRanges);
        Assert.Equal((1, 2), frame2.DirtyCommandRanges[0]);
    }

    [Fact]
    public void RetainedCommandBuffer_reset_after_resource_return_does_not_resolve_stale_text()
    {
        // Simulate the lifecycle: record commands with resources, return resources, reset buffer
        var resources = FrameDrawingResources.Rent();
        var textSlice = resources.AddText("hello");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: textSlice),
        ]);
        var batch = new DrawCommandBatch(owner, 1);

        var buffer = new RetainedCommandBuffer();
        buffer.ApplyFull(batch);

        // Verify the command is there
        Assert.Equal(1, buffer.Count);
        Assert.Equal(textSlice, buffer.Commands[0].Text);

        // Return resources to pool (simulates frame end)
        FrameDrawingResources.Return(resources);

        // The buffer still holds the old TextSlice, but it's now invalid
        // because the arena was reset. Reset the buffer to prevent stale access.
        buffer.Reset();
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void RetainedCommandBuffer_partial_apply_within_same_resource_scope_is_safe()
    {
        // Simulate a single frame where we record two batches with the same resources.
        // In practice, DrawCommandRecorder.Record() creates resources once per frame.
        var resources = FrameDrawingResources.Rent();
        var slice1 = resources.AddText("hello");
        var slice2 = resources.AddText("world");
        var slice3 = resources.AddText("updated");
        resources.Seal();

        // Initial batch
        var owner1 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 40, 100, 32), Text: slice2),
        ]);
        var buffer = new RetainedCommandBuffer();
        buffer.ApplyFull(new DrawCommandBatch(owner1, 2));

        // Partial replace: update command 0 with new text (same resources)
        var owner2 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice3),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 40, 100, 32), Text: slice2),
        ]);
        buffer.ApplyPartial(new DrawCommandBatch(owner2, 2), [(0, 1)]);

        Assert.Equal(2, buffer.Count);
        // Command 0 should have the new text
        Assert.Equal(slice3, buffer.Commands[0].Text);
        // Command 1 should be unchanged
        Assert.Equal(slice2, buffer.Commands[1].Text);

        // Both slices should resolve correctly since resources are still alive
        Assert.Equal("updated", resources.Resolve(buffer.Commands[0].Text).ToString());
        Assert.Equal("world", resources.Resolve(buffer.Commands[1].Text).ToString());

        FrameDrawingResources.Return(resources);
    }

    [Fact]
    public void RetainedRenderFrame_apply_full_stores_commands_and_resources()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("test");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch);

        Assert.Equal(1, frame.CommandCount);
        Assert.Equal(slice, frame.Commands[0].Text);
        Assert.Same(resources, frame.Resources);
        Assert.Empty(frame.DirtyCommandRanges);

        // Retained frame owns resources — batch.Dispose() returns commands only.
        // Resources are released back to pool when frame is disposed.
        batch.Dispose();
        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_try_apply_partial_replaces_dirty_commands()
    {
        var resources = FrameDrawingResources.Rent();
        var slice1 = resources.AddText("old");
        var slice2 = resources.AddText("keep");
        var slice3 = resources.AddText("new");
        resources.Seal();

        var owner1 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 40, 100, 32), Text: slice2),
        ]);
        var batch1 = new RenderFrameBatch(
            new DrawCommandBatch(owner1, 2),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch1);

        // Partial update: replace command 0 (same resources instance, same generation)
        var owner2 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice3),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 40, 100, 32), Text: slice2),
        ]);
        var batch2 = new RenderFrameBatch(
            new DrawCommandBatch(owner2, 2),
            [],
            resources,
            [(0, 1)]);

        var result = frame.TryApplyPartial(batch2);

        Assert.True(result);
        Assert.Equal(2, frame.CommandCount);
        Assert.Equal(slice3, frame.Commands[0].Text);
        Assert.Equal(slice2, frame.Commands[1].Text);
        Assert.Equal("new", resources.Resolve(frame.Commands[0].Text).ToString());
        Assert.Equal("keep", resources.Resolve(frame.Commands[1].Text).ToString());

        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_invalidate_resets_all_state()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("test");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 32), "btn")],
            resources,
            [(0, 1)]);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch);

        Assert.Equal(1, frame.CommandCount);
        Assert.Single(frame.HitTargets);
        Assert.Single(frame.DirtyCommandRanges);

        frame.Invalidate();

        Assert.Equal(0, frame.CommandCount);
        Assert.Empty(frame.HitTargets);
        Assert.Empty(frame.DirtyCommandRanges);

        // Invalidate released resources back to pool; frame.Dispose() is safe.
        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_to_batch_creates_independent_snapshot()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("snapshot");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 32), "btn")],
            resources,
            [(0, 1)]);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch);

        using var snapshot = frame.ToBatch();

        Assert.Equal(1, snapshot.Commands.Count);
        Assert.Equal(slice, snapshot.Commands.Memory.Span[0].Text);
        Assert.Single(snapshot.HitTargets);
        Assert.Single(snapshot.DirtyCommandRanges);
        Assert.Same(resources, snapshot.Resources);

        // snapshot.Dispose() calls Return() but resources are retained — no-op.
        // frame.Dispose() releases resources back to pool.
        snapshot.Dispose();
        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_try_apply_partial_returns_false_on_different_resources()
    {
        var resources1 = FrameDrawingResources.Rent();
        var slice1 = resources1.AddText("old");
        resources1.Seal();

        var owner1 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
        ]);
        var batch1 = new RenderFrameBatch(
            new DrawCommandBatch(owner1, 1),
            [],
            resources1,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch1);
        Assert.Same(resources1, frame.Resources);

        // Different resources instance — partial must refuse without side effects
        var resources2 = FrameDrawingResources.Rent();
        var slice2 = resources2.AddText("new");
        resources2.Seal();

        var owner2 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice2),
        ]);
        var batch2 = new RenderFrameBatch(
            new DrawCommandBatch(owner2, 1),
            [],
            resources2,
            [(0, 1)]);

        var result = frame.TryApplyPartial(batch2);

        Assert.False(result);
        // Frame state is UNCHANGED — TryApplyPartial is pure on failure
        Assert.Same(resources1, frame.Resources);
        Assert.Equal(1, frame.CommandCount);
        Assert.Equal(slice1, frame.Commands[0].Text);

        frame.Dispose();
        batch2.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_try_read_frame_returns_commands_and_resources()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("hello");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();

        // Empty frame: TryReadFrame returns false
        Assert.False(frame.TryReadFrame(out _, out _));

        frame.ApplyFull(batch);

        // Non-empty frame: TryReadFrame returns true with valid data
        Assert.True(frame.TryReadFrame(out var commands, out var resolvedResources));
        Assert.Equal(1, commands.Length);
        Assert.Equal(slice, commands[0].Text);
        Assert.Same(resources, resolvedResources);

        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_try_apply_partial_returns_false_on_command_count_mismatch()
    {
        var resources = FrameDrawingResources.Rent();
        var slice1 = resources.AddText("one");
        var slice2 = resources.AddText("two");
        resources.Seal();

        // Retained frame has 2 commands
        var owner1 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 40, 100, 32), Text: slice2),
        ]);
        var batch1 = new RenderFrameBatch(
            new DrawCommandBatch(owner1, 2),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch1);

        // New batch has 1 command (count mismatch) + same resources + dirty ranges
        var owner2 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
        ]);
        var batch2 = new RenderFrameBatch(
            new DrawCommandBatch(owner2, 1),
            [],
            resources,
            [(0, 1)]);

        var result = frame.TryApplyPartial(batch2);

        Assert.False(result);
        // Frame state is UNCHANGED — no pollution from count mismatch
        Assert.Equal(2, frame.CommandCount);
        Assert.Equal(slice1, frame.Commands[0].Text);
        Assert.Equal(slice2, frame.Commands[1].Text);
        Assert.Same(resources, frame.Resources);
        Assert.Empty(frame.DirtyCommandRanges);

        frame.Dispose();
    }

    [Fact]
    public void RetainedRenderFrame_dispose_releases_retained_resources()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("dispose-test");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch);
        frame.RetainResources();

        // batch.Dispose() is a no-op for resources (retained)
        batch.Dispose();

        // frame.Dispose() should release retained resources back to pool
        frame.Dispose();

        // After dispose, resources should be back in pool.
        // A fresh Rent() may recycle the same object.
        var recycled = FrameDrawingResources.Rent();
        // Either same object (recycled) or different (pool was full).
        // Either way, no leak.
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void RetainedRenderFrame_explicit_retain_prevents_batch_return()
    {
        var resources = FrameDrawingResources.Rent();
        var slice = resources.AddText("owned");
        resources.Seal();

        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice),
        ]);
        var batch = new RenderFrameBatch(
            new DrawCommandBatch(owner, 1),
            [],
            resources,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch);
        // Without RetainResources, batch.Dispose() returns resources to pool
        // With RetainResources, batch.Dispose() is a no-op for resources
        frame.RetainResources();

        // Dispose batch — should NOT return resources to pool (retained by frame)
        batch.Dispose();

        // TextSlice is still valid because resources are retained
        Assert.True(frame.TryReadFrame(out var commands, out var res));
        Assert.Equal("owned", ((IFrameResourceResolver)res).Resolve(commands[0].Text).ToString());

        // Release + Dispose returns resources to pool
        frame.ReleaseResources();
        frame.Dispose();

        // Now resources are returned — a fresh Rent() would get a recycled instance
        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void RetainedRenderFrame_generation_mismatch_falls_back_to_full()
    {
        // Use fresh resources from pool — add ALL text before sealing
        var resources1 = FrameDrawingResources.Rent();
        var slice1 = resources1.AddText("frame1");
        var slice1b = resources1.AddText("frame1b");
        resources1.Seal();

        var owner1 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1),
        ]);
        var batch1 = new RenderFrameBatch(
            new DrawCommandBatch(owner1, 1),
            [],
            resources1,
            []);

        var frame = new RetainedRenderFrame();
        frame.ApplyFull(batch1);

        // Same resources, same generation → partial should succeed
        var owner1b = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice1b),
        ]);
        var batch1b = new RenderFrameBatch(
            new DrawCommandBatch(owner1b, 1),
            [],
            resources1,
            [(0, 1)]);

        Assert.True(frame.TryApplyPartial(batch1b));

        // Different resources instance → partial must refuse
        var resources2 = FrameDrawingResources.Rent();
        var slice2 = resources2.AddText("frame2");
        resources2.Seal();

        var owner2 = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: slice2),
        ]);
        var batch2 = new RenderFrameBatch(
            new DrawCommandBatch(owner2, 1),
            [],
            resources2,
            [(0, 1)]);

        Assert.False(frame.TryApplyPartial(batch2));

        // Frame state unchanged after refused partial
        Assert.Same(resources1, frame.Resources);
        Assert.Equal(1, frame.CommandCount);

        frame.Dispose();
    }

    [Fact]
    public void RenderPipeline_retained_frame_updates_on_each_build()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("hello", 2));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport);
        Assert.Equal(1, pipeline.RetainedFrame.CommandCount);
        Assert.Empty(pipeline.RetainedFrame.DirtyCommandRanges);

        // Dirty build
        var root2 = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("world", 2));
        using var frame2 = pipeline.Build(root2, viewport, [1]);

        Assert.Equal(1, pipeline.RetainedFrame.CommandCount);
        Assert.Single(pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal((0, 1), pipeline.RetainedFrame.DirtyCommandRanges[0]);
    }
}
