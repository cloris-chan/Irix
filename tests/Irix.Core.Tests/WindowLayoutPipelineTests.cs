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
        Assert.Equal(LayoutElementKind.Text, rootNode.Children[0].Kind);
        Assert.Equal(0, rootNode.Children[0].ElementStart);
        Assert.Equal(1, rootNode.Children[0].ElementCount);

        Assert.Equal(2, rootNode.Children[1].DfsIndex);
        Assert.Equal(LayoutElementKind.Button, rootNode.Children[1].Kind);
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
}
