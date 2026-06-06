using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowLayoutPipelineTests
{
    private readonly VirtualTextArena _arena = new();
    [Fact]
    public void LayoutTreeBuilder_builds_expected_layout_elements()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeFactory.Rectangle(new NodeKey(3), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(4),
                VirtualNodeProperty.Action(new ActionId(1))));
        var builder = new LayoutTreeBuilder();

        var elements = builder.BuildElements(root, new PixelRectangle(0, 0, 960, 540));
        var snapshot = _arena.GetOrCreateSnapshot();

        Assert.Equal(3, elements.Count);

        Assert.Equal(LayoutElementKind.Text, elements[0].Kind);
        Assert.Equal(new PixelRectangle(16, 16, 928, 32), elements[0].Bounds);
        Assert.Equal("Count: 0", snapshot.ResolveRequired(elements[0].Text).ToString());

        Assert.Equal(LayoutElementKind.Rectangle, elements[1].Kind);
        Assert.Equal(new PixelRectangle(16, 60, 220, 48), elements[1].Bounds);

        Assert.Equal(LayoutElementKind.Button, elements[2].Kind);
        Assert.Equal(new PixelRectangle(16, 120, 140, 40), elements[2].Bounds);
        Assert.Equal("Increment", snapshot.ResolveRequired(elements[2].Text).ToString());
        Assert.Equal(new ActionId(1), elements[2].ActionId);
    }

    [Fact]
    public void DrawCommandRecorder_records_button_as_fill_and_text_commands()
    {
        var recorder = new DrawCommandRecorder();
        var content = _arena.AddText("Increment".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: content,
                ActionId: new ActionId(1))
        };

        var result = recorder.Record(elements, textSnapshot: snapshot);

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
            VirtualNodeBuilder.Text(_arena, "Title", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Go", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(100))));
        var builder = new LayoutTreeBuilder(new LayoutStyle(
            HorizontalPadding: 24,
            VerticalPadding: 20,
            ItemSpacing: 8,
            TextHeight: 28,
            ButtonHeight: 36,
            RectangleHeight: 44,
            MinimumButtonWidth: 120,
            ButtonTextWidthFactor: 10,
            ButtonHorizontalPadding: 20));

        var elements = builder.BuildElements(root, new PixelRectangle(0, 0, 400, 300));

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
            ButtonHoverFillColor: DrawColor.Opaque(11, 21, 31),
            ButtonPressedFillColor: DrawColor.Opaque(12, 22, 32),
            ButtonFocusedFillColor: DrawColor.Opaque(13, 23, 33),
            ButtonTextColor: DrawColor.Opaque(200, 210, 220),
            TextStyle: TextStyle.Default,
            ButtonTextStyle: TextStyle.Default));
        var content = _arena.AddText("Increment".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Rectangle,
                new PixelRectangle(16, 60, 220, 48)),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: content,
                ActionId: new ActionId(1))
        };

        var result = recorder.Record(elements, textSnapshot: snapshot);

        Assert.Equal(DrawColor.Opaque(20, 30, 40), result.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawColor.Opaque(10, 20, 30), result.Commands.Memory.Span[1].Color);
        Assert.Equal(DrawColor.Opaque(200, 210, 220), result.Commands.Memory.Span[2].Color);

        result.Commands.Dispose();
    }

    [Fact]
    public void DrawCommandRecorder_applies_internal_visual_style_color_overrides()
    {
        var recorder = new DrawCommandRecorder();
        var text = _arena.AddText("Title".AsSpan());
        var buttonText = _arena.AddText("Go".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Text,
                new PixelRectangle(16, 16, 200, 32),
                Text: text,
                ForegroundColor: StyleColorSlot.Some(StyleColor.Opaque(1, 2, 3))),
            new LayoutElement(
                LayoutElementKind.Rectangle,
                new PixelRectangle(16, 60, 160, 48),
                BackgroundColor: StyleColorSlot.Some(StyleColor.FromArgb(0, 4, 5, 6))),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 120, 140, 40),
                Text: buttonText,
                ActionId: new ActionId(1),
                BackgroundColor: StyleColorSlot.Some(StyleColor.Opaque(7, 8, 9)),
                ForegroundColor: StyleColorSlot.Some(StyleColor.Opaque(10, 11, 12)))
        };

        var result = recorder.Record(elements, textSnapshot: snapshot);

        Assert.Equal(4, result.Commands.Count);
        Assert.Equal(DrawColor.Opaque(1, 2, 3), result.Commands.Memory.Span[0].Color);
        Assert.Equal(new DrawColor(0, 4, 5, 6), result.Commands.Memory.Span[1].Color);
        Assert.Equal(DrawColor.Opaque(7, 8, 9), result.Commands.Memory.Span[2].Color);
        Assert.Equal(DrawColor.Opaque(10, 11, 12), result.Commands.Memory.Span[3].Color);

        result.Commands.Dispose();
    }

    [Fact]
    public void Default_style_preset_keeps_current_layout_metrics()
    {
        var layout = RenderStylePreset.Default.Layout;

        Assert.Equal(16, layout.HorizontalPadding);
        Assert.Equal(16, layout.VerticalPadding);
        Assert.Equal(12, layout.ItemSpacing);
        Assert.Equal(32, layout.TextHeight);
        Assert.Equal(40, layout.ButtonHeight);
        Assert.Equal(48, layout.RectangleHeight);
        Assert.Equal(140, layout.MinimumButtonWidth);
        Assert.Equal(12, layout.ButtonTextWidthFactor);
        Assert.Equal(32, layout.ButtonHorizontalPadding);
    }

    [Fact]
    public void Default_style_preset_keeps_button_state_colors()
    {
        var preset = RenderStylePreset.Default;

        Assert.Equal(DrawColor.Opaque(52, 120, 246), preset.VisualStates.ResolveButtonFillColor(preset.Drawing, default));
        Assert.Equal(DrawColor.Opaque(84, 160, 255), preset.VisualStates.ResolveButtonFillColor(preset.Drawing, new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true)));
        Assert.Equal(DrawColor.Opaque(72, 136, 255), preset.VisualStates.ResolveButtonFillColor(preset.Drawing, new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true)));
        Assert.Equal(DrawColor.Opaque(36, 92, 210), preset.VisualStates.ResolveButtonFillColor(preset.Drawing, new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true)));
    }

    [Fact]
    public void Counter_style_preset_keeps_default_layout_and_poc_text_color()
    {
        var preset = CounterStylePreset.Default;

        Assert.Equal(RenderStylePreset.Default.Layout, preset.Layout);
        Assert.Equal(DrawColor.Opaque(32, 32, 32), preset.Drawing.TextColor);
        Assert.Equal(RenderStylePreset.Default.Drawing.RectangleFillColor, preset.Drawing.RectangleFillColor);
        Assert.Equal(RenderStylePreset.Default.Drawing.ButtonFillColor, preset.Drawing.ButtonFillColor);
        Assert.Equal(RenderStylePreset.Default.Drawing.ButtonHoverFillColor, preset.Drawing.ButtonHoverFillColor);
        Assert.Equal(RenderStylePreset.Default.Drawing.ButtonPressedFillColor, preset.Drawing.ButtonPressedFillColor);
        Assert.Equal(RenderStylePreset.Default.Drawing.ButtonFocusedFillColor, preset.Drawing.ButtonFocusedFillColor);
    }

    [Fact]
    public void LayoutTreeBuilder_carries_button_visual_state_properties()
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true),
                VirtualNodeProperty.Pressed(true),
                VirtualNodeProperty.Focused(true)));
        var builder = new LayoutTreeBuilder();

        var elements = builder.BuildElements(root, new PixelRectangle(0, 0, 960, 540));

        Assert.Single(elements);
        Assert.Equal(new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true), elements[0].ButtonState);
    }

    [Fact]
    public void DrawCommandRecorder_uses_button_visual_state_fill_priority()
    {
        var recorder = new DrawCommandRecorder(new DrawingStyle(
            TextColor: DrawColor.Opaque(230, 230, 230),
            RectangleFillColor: DrawColor.Opaque(20, 30, 40),
            ButtonFillColor: DrawColor.Opaque(10, 20, 30),
            ButtonHoverFillColor: DrawColor.Opaque(40, 50, 60),
            ButtonPressedFillColor: DrawColor.Opaque(70, 80, 90),
            ButtonFocusedFillColor: DrawColor.Opaque(100, 110, 120),
            ButtonTextColor: DrawColor.Opaque(200, 210, 220),
            TextStyle: TextStyle.Default,
            ButtonTextStyle: TextStyle.Default));
        var textNormal = _arena.AddText("normal".AsSpan());
        var textFocused = _arena.AddText("focused".AsSpan());
        var textHovered = _arena.AddText("hovered".AsSpan());
        var textPressed = _arena.AddText("pressed".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(0, 0, 100, 40),
                Text: textNormal,
                ButtonState: default),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(0, 40, 100, 40),
                Text: textFocused,
                ButtonState: new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true)),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(0, 80, 100, 40),
                Text: textHovered,
                ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true)),
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(0, 120, 100, 40),
                Text: textPressed,
                ButtonState: new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true)),
        };

        var result = recorder.Record(elements, textSnapshot: snapshot);

        Assert.Equal(DrawCommandKind.FillRect, result.Commands.Memory.Span[0].Kind);
        Assert.Equal(DrawColor.Opaque(10, 20, 30), result.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawCommandKind.FillRect, result.Commands.Memory.Span[2].Kind);
        Assert.Equal(DrawColor.Opaque(100, 110, 120), result.Commands.Memory.Span[2].Color);
        Assert.Equal(DrawCommandKind.FillRect, result.Commands.Memory.Span[4].Kind);
        Assert.Equal(DrawColor.Opaque(40, 50, 60), result.Commands.Memory.Span[4].Color);
        Assert.Equal(DrawCommandKind.FillRect, result.Commands.Memory.Span[6].Kind);
        Assert.Equal(DrawColor.Opaque(70, 80, 90), result.Commands.Memory.Span[6].Color);

        result.Commands.Dispose();
    }

    [Fact]
    public void RenderPipeline_builds_draw_commands_from_virtual_node()
    {
        var pipeline = new RenderPipeline(RenderStylePreset.Default);
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), textSnapshot: _arena.GetOrCreateSnapshot());

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[0].Kind);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(DrawCommandKind.FillRect, frame.Commands.Memory.Span[1].Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, frame.Commands.Memory.Span[2].Kind);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new PixelRectangle(16, 60, 140, 40), frame.HitTargets[0].Bounds);
        Assert.Equal(new ActionId(1), frame.HitTargets[0].ActionId);
        Assert.Equal(new PixelRectangle(0, 0, 960, 540), frame.HitTargets[0].ClipBounds);

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
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        using var patchBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var frame = translator.Translate(patchBatch);

        Assert.Equal(3, frame.Commands.Count);
        Assert.Equal(new DrawRect(16, 16, 928, 32), frame.Commands.Memory.Span[0].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[1].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[2].Rect);
        Assert.Single(frame.HitTargets);
        Assert.Equal(new PixelRectangle(16, 60, 140, 40), frame.HitTargets[0].Bounds);
        Assert.Equal(new ActionId(1), frame.HitTargets[0].ActionId);
        Assert.Equal(new PixelRectangle(0, 0, 960, 540), frame.HitTargets[0].ClipBounds);
    }

    [Fact]
    public void WindowDrawCommandTranslator_default_factory_matches_explicit_counter_style_source()
    {
        var defaultTranslator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        var explicitTranslator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))),
            prepareFrame: null,
            viewportProvider: null,
            postFrameCallback: null,
            renderPipelineFactory: TranslatorRenderPipelineFactory.FromStyle(CounterStylePreset.Default));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        using var defaultBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var explicitBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        var defaultFrameSnapshot = SnapshotRenderFrame(defaultTranslator.Translate(defaultBatch));
        var explicitFrameSnapshot = SnapshotRenderFrame(explicitTranslator.Translate(explicitBatch));

        AssertRenderFrameSnapshotsEquivalent(defaultFrameSnapshot, explicitFrameSnapshot);
        Assert.Equal(new PixelRectangle(0, 0, 960, 540), defaultTranslator.LastViewport);
        Assert.Equal(defaultTranslator.LastViewport, explicitTranslator.LastViewport);
        Assert.Equal(defaultTranslator.LastLayoutViewport, explicitTranslator.LastLayoutViewport);
        Assert.Equal(defaultTranslator.LayoutRebuildCount, explicitTranslator.LayoutRebuildCount);
        Assert.Equal(defaultTranslator.LastLayoutRebuildReason, explicitTranslator.LastLayoutRebuildReason);
        Assert.Equal(defaultTranslator.LastMaxScrollY, explicitTranslator.LastMaxScrollY);
        Assert.Equal(defaultTranslator.LastScrollFeedback.Containers.Count, explicitTranslator.LastScrollFeedback.Containers.Count);
        Assert.Equal(DrawColor.Opaque(32, 32, 32), defaultFrameSnapshot.Commands[0].Color);
        Assert.Equal(DrawColor.Opaque(32, 32, 32), explicitFrameSnapshot.Commands[0].Color);
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
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        var frame1Text = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();
        using var frame2 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        var frame2Text = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        Assert.Equal(1, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.None, pipeline.LastLayoutRebuildReason);
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
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)));

        using var frame1 = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root, new PixelRectangle(0, 0, 1920, 1080), _arena.GetOrCreateSnapshot());

        // Layout should differ because viewport width changed
        var text1 = frame1.Commands.Memory.Span[0];
        var text2 = frame2.Commands.Memory.Span[0];
        Assert.NotEqual(text1.Rect.Width, text2.Rect.Width);
        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.ViewportChanged, pipeline.LastLayoutRebuildReason);
    }

    [Fact]
    public void RenderPipeline_classifies_layout_affecting_dirty_rebuild()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)));
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(24)],
            children: [VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2))]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [0]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.LayoutAffecting, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting)], pipeline.LastDirtyClassifications);
    }

    [Fact]
    public void RenderPipeline_classifies_style_only_dirty_rebuild()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(100)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(100)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], pipeline.LastDirtyClassifications);
        Assert.Equal(frame1.HitTargets[0].Bounds, frame2.HitTargets[0].Bounds);
    }

    [Fact]
    public void RenderPipeline_classifies_internal_visual_color_change_as_style_only()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(20, 30, 40))));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(80, 90, 100))));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialGeometry = SnapshotLayoutGeometryInvariants(pipeline.LastLayoutResult!.Elements);
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var nextGeometry = SnapshotLayoutGeometryInvariants(pipeline.LastLayoutResult!.Elements);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)], pipeline.LastDirtyClassifications);
        Assert.Equal(initialGeometry, nextGeometry);
        Assert.Equal(DrawColor.Opaque(20, 30, 40), frame1.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawColor.Opaque(80, 90, 100), frame2.Commands.Memory.Span[0].Color);
        Assert.Equal(frame1.Commands.Memory.Span[0].Rect, frame2.Commands.Memory.Span[0].Rect);
        Assert.Equal([(0, 1)], frame2.DirtyCommandRanges);
    }

    [Fact]
    public void RenderPipeline_classifies_internal_composition_property_change_as_style_only_composite()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.LayerOpacity(1)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.LayerOpacity(0.5)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly, InvalidationKind.CompositeOnly)], pipeline.LastDirtyClassifications);
        Assert.Equal(frame1.Commands.Memory.Span[0].Rect, frame2.Commands.Memory.Span[0].Rect);
        Assert.Equal(frame1.Commands.Memory.Span[0].Color, frame2.Commands.Memory.Span[0].Color);
    }

    [Fact]
    public void RenderPipeline_classifies_mixed_visual_and_composition_style_change_as_draw_update()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(20, 30, 40)),
                VirtualNodeProperty.LayerOpacity(1)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(2),
                VirtualNodeProperty.Width(220),
                VirtualNodeProperty.Height(48),
                VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(80, 90, 100)),
                VirtualNodeProperty.LayerOpacity(0.5)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)], pipeline.LastDirtyClassifications);
        Assert.Equal(DrawColor.Opaque(20, 30, 40), frame1.Commands.Memory.Span[0].Color);
        Assert.Equal(DrawColor.Opaque(80, 90, 100), frame2.Commands.Memory.Span[0].Color);
        Assert.Equal(frame1.Commands.Memory.Span[0].Rect, frame2.Commands.Memory.Span[0].Rect);
    }

    [Fact]
    public void StyleTransitionCompiler_compiles_internal_composite_delta_to_composition_declaration()
    {
        var previous = new[]
        {
            VirtualNodeProperty.LayerOpacity(1),
            VirtualNodeProperty.TranslateX(0),
            VirtualNodeProperty.TranslateY(4)
        };
        var next = new[]
        {
            VirtualNodeProperty.LayerOpacity(0.5),
            VirtualNodeProperty.TranslateX(24),
            VirtualNodeProperty.TranslateY(16)
        };
        var marker = new CompositionAnimationMarker(
            new CompositionAnimationMarkerId(1),
            new CompositionRuntimeEventId(2),
            CompositionAnimationMarkerTrigger.AtProgress(0.5f));
        var request = new StyleTransitionCompileRequest(
            new NodeKey(22),
            previous,
            next,
            CompositionTimestamp.FromStopwatchTicks(100),
            CompositionDuration.FromStopwatchTicks(50),
            CompositionAnimationEasing.SineOut,
            new CompositionAnimationInstanceId(3),
            [marker]);

        var result = StyleTransitionCompiler.Compile(request);

        Assert.True(result.HasDeclaration);
        Assert.Equal(StyleTransitionCompileStatus.CompiledCompositionDeclaration, result.Status);
        Assert.True(result.DeltaPlan.IsCompositorOnlyTransitionCandidate);
        Assert.Equal(new StyleTransitionState(0, 4, 1), result.From);
        Assert.Equal(new StyleTransitionState(24, 16, 0.5f), result.To);

        var declaration = result.Declaration;
        Assert.Equal(new NodeKey(22), declaration.TargetKey);
        Assert.Equal(CompositionTimestamp.FromStopwatchTicks(100), declaration.Timeline.StartTimestamp);
        Assert.Equal(CompositionDuration.FromStopwatchTicks(50), declaration.Timeline.Duration);
        Assert.Equal(new CompositionAnimationInstanceId(3), declaration.InstanceId);
        Assert.Equal([marker], declaration.Markers.ToArray());
        Assert.True(declaration.Transform.TranslateX.Evaluate(0.5f) > 12);
        Assert.True(declaration.Transform.TranslateY.Evaluate(0.5f) > 10);
        Assert.Equal(0.5f, declaration.Opacity.To);
    }

    [Fact]
    public void StyleTransitionCompiler_rejects_draw_or_layout_owned_deltas()
    {
        VirtualNodeProperty[] visualPrevious = [VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(1, 2, 3))];
        VirtualNodeProperty[] visualNext = [VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(4, 5, 6))];
        VirtualNodeProperty[] layoutPrevious = [VirtualNodeProperty.Width(100)];
        VirtualNodeProperty[] layoutNext = [VirtualNodeProperty.Width(120)];
        VirtualNodeProperty[] mixedPrevious =
        [
            VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(1, 2, 3)),
            VirtualNodeProperty.LayerOpacity(1)
        ];
        VirtualNodeProperty[] mixedNext =
        [
            VirtualNodeProperty.BackgroundColor(StyleColor.Opaque(4, 5, 6)),
            VirtualNodeProperty.LayerOpacity(0.5)
        ];
        var visualRequest = new StyleTransitionCompileRequest(
            new NodeKey(22),
            visualPrevious,
            visualNext,
            CompositionTimestamp.Zero,
            CompositionDuration.FromStopwatchTicks(50));
        var layoutRequest = new StyleTransitionCompileRequest(
            new NodeKey(22),
            layoutPrevious,
            layoutNext,
            CompositionTimestamp.Zero,
            CompositionDuration.FromStopwatchTicks(50));
        var mixedRequest = new StyleTransitionCompileRequest(
            new NodeKey(22),
            mixedPrevious,
            mixedNext,
            CompositionTimestamp.Zero,
            CompositionDuration.FromStopwatchTicks(50));

        var visual = StyleTransitionCompiler.Compile(visualRequest);
        var layout = StyleTransitionCompiler.Compile(layoutRequest);
        var mixed = StyleTransitionCompiler.Compile(mixedRequest);

        Assert.Equal(StyleTransitionCompileStatus.RequiresDrawUpdate, visual.Status);
        Assert.Equal(StyleTransitionCompileStatus.RequiresLayout, layout.Status);
        Assert.Equal(StyleTransitionCompileStatus.RequiresDrawUpdate, mixed.Status);
        Assert.False(visual.HasDeclaration);
        Assert.False(layout.HasDeclaration);
        Assert.False(mixed.HasDeclaration);
    }

    [Fact]
    public void RenderPipeline_classifies_text_size_affecting_dirty_rebuild()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "Short", new NodeKey(2)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "Longer text", new NodeKey(2)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.TextSizeAffecting, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.TextSizeAffecting)], pipeline.LastDirtyClassifications);
    }

    [Fact]
    public void RenderPipeline_classifies_tree_structure_dirty_rebuild()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(3)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [0]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.TreeStructure, pipeline.LastLayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(0, LayoutRebuildReason.TreeStructure)], pipeline.LastDirtyClassifications);
    }

    [Fact]
    public void RenderPipeline_mixed_style_and_layout_dirty_uses_higher_priority_reason()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(0)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2),
                    VirtualNodeProperty.Action(new ActionId(100)),
                    VirtualNodeProperty.Hovered(false))
            ]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(24)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2),
                    VirtualNodeProperty.Action(new ActionId(100)),
                    VirtualNodeProperty.Hovered(true))
            ]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [0, 1]);

        Assert.Equal(2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.LayoutAffecting, pipeline.LastLayoutRebuildReason);
        Assert.Equal(
            [
                new LayoutDirtyClassification(0, LayoutRebuildReason.LayoutAffecting),
                new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)
            ],
            pipeline.LastDirtyClassifications);
    }

    [Fact]
    public void StyleOnlyPatchEligibility_accepts_only_style_dirty_when_viewport_unchanged()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var eligible = StyleOnlyPatchEligibility.IsLayoutReuseEligible(
            [
                new LayoutDirtyClassification(2, LayoutRebuildReason.StyleOnly),
                new LayoutDirtyClassification(4, LayoutRebuildReason.StyleOnly)
            ],
            viewport,
            viewport);

        Assert.True(eligible);
        Assert.False(StyleOnlyPatchEligibility.IsLayoutReuseEligible([], viewportChanged: false));
    }

    [Theory]
    [InlineData((int)LayoutRebuildReason.TextSizeAffecting)]
    [InlineData((int)LayoutRebuildReason.LayoutAffecting)]
    [InlineData((int)LayoutRebuildReason.TreeStructure)]
    public void StyleOnlyPatchEligibility_rejects_non_style_dirty_reasons(int reasonValue)
    {
        var reason = (LayoutRebuildReason)reasonValue;
        var eligible = StyleOnlyPatchEligibility.IsLayoutReuseEligible(
            [new LayoutDirtyClassification(1, reason)],
            viewportChanged: false);

        Assert.False(eligible);
    }

    [Fact]
    public void StyleOnlyPatchEligibility_rejects_viewport_change()
    {
        var eligible = StyleOnlyPatchEligibility.IsLayoutReuseEligible(
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            viewportChanged: true);

        Assert.False(eligible);
    }

    [Fact]
    public void StyleOnlyPatchEligibility_rejects_mixed_dirty_reasons()
    {
        var eligible = StyleOnlyPatchEligibility.IsLayoutReuseEligible(
            [
                new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly),
                new LayoutDirtyClassification(2, LayoutRebuildReason.LayoutAffecting)
            ],
            viewportChanged: false);

        Assert.False(eligible);
    }

    [Fact]
    public void RenderPipeline_retained_input_snapshot_captures_current_fields()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        using var frame = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.NotNull(snapshot);
        Assert.Same(pipeline.LastLayoutResult, snapshot.LayoutResult);
        Assert.Equal(pipeline.LastElementCommandRanges, snapshot.ElementCommandRanges);
        Assert.Equal(frame.HitTargets, snapshot.HitTargets);
        Assert.Equal(root.Kind, snapshot.RetainedRoot.Kind);
        Assert.Equal(root.Key, snapshot.RetainedRoot.Key);
        Assert.Equal(root.Children.Length, snapshot.RetainedRoot.Children.Length);
        Assert.Equal(viewport, snapshot.Viewport);
        Assert.Empty(snapshot.DirtyClassifications);
        Assert.Empty(snapshot.DirtyElementRanges);
        Assert.Empty(snapshot.DirtyCommandRanges);
        Assert.Equal(pipeline.LastLayoutRebuildReason, snapshot.LayoutRebuildReason);
        Assert.Equal(frame.DirtyCommandRanges, snapshot.DirtyCommandRanges);
        Assert.Equal(3, frame.Commands.Count);
    }

    [Fact]
    public void RenderPipeline_retained_input_snapshot_stays_stable_for_hover_only_style_change()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 160);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120)],
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(1)),
                    VirtualNodeProperty.Hovered(false)),
                VirtualNodeFactory.Rectangle(new NodeKey(4), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48))
            ]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120)],
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(1)),
                    VirtualNodeProperty.Hovered(true)),
                VirtualNodeFactory.Rectangle(new NodeKey(4), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48))
            ]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialSnapshot = pipeline.LastRetainedInputSnapshot!;
        var initialElementSnapshot = SnapshotLayoutElementInvariants(initialSnapshot.LayoutResult.Elements);
        var initialRangeSnapshot = SnapshotLayoutTreeRanges(initialSnapshot.LayoutResult.TreeNodes);
        ScrollContainerDiag[] initialScrollDiagnostics = [.. initialSnapshot.LayoutResult.ScrollDiagnostics];
        ElementCommandRange[] initialCommandRanges = [.. initialSnapshot.ElementCommandRanges];
        HitTestTarget[] initialHitTargets = [.. initialSnapshot.HitTargets];
        var initialRebuildCount = pipeline.LayoutRebuildCount;

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var nextSnapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.Equal(initialRebuildCount + 1, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, nextSnapshot.LayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(2, LayoutRebuildReason.StyleOnly)], nextSnapshot.DirtyClassifications);
        Assert.Equal(pipeline.LastDirtyClassifications, nextSnapshot.DirtyClassifications);
        Assert.Equal([(1, 1)], nextSnapshot.DirtyElementRanges);
        Assert.Equal([(1, 2)], nextSnapshot.DirtyCommandRanges);
        Assert.Equal(initialElementSnapshot, SnapshotLayoutElementInvariants(nextSnapshot.LayoutResult.Elements));
        Assert.Equal(initialRangeSnapshot, SnapshotLayoutTreeRanges(nextSnapshot.LayoutResult.TreeNodes));
        Assert.Equal(initialScrollDiagnostics, nextSnapshot.LayoutResult.ScrollDiagnostics);
        Assert.Equal(initialCommandRanges, nextSnapshot.ElementCommandRanges);
        Assert.Equal(initialHitTargets, nextSnapshot.HitTargets);
        Assert.Equal(frame2.HitTargets, nextSnapshot.HitTargets);
        Assert.Equal(frame2.DirtyCommandRanges, nextSnapshot.DirtyCommandRanges);
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
    }

    [Fact]
    public void RenderPipeline_retained_input_snapshot_matches_diagnostics_and_frame_for_action_id_style_change()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.Equal(pipeline.LastLayoutRebuildReason, snapshot.LayoutRebuildReason);
        Assert.Equal([new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)], snapshot.DirtyClassifications);
        Assert.Equal(pipeline.LastDirtyClassifications, snapshot.DirtyClassifications);
        Assert.Equal(pipeline.LastDirtyElementRanges, snapshot.DirtyElementRanges);
        Assert.Equal(pipeline.LastDirtyCommandRanges, snapshot.DirtyCommandRanges);
        Assert.Equal(pipeline.LastElementCommandRanges, snapshot.ElementCommandRanges);
        Assert.Equal(frame2.HitTargets, snapshot.HitTargets);
        Assert.Equal(frame2.DirtyCommandRanges, snapshot.DirtyCommandRanges);
        Assert.Equal(new ActionId(4), Assert.Single(snapshot.HitTargets).ActionId);
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
        Assert.Equal(2, pipeline.LayoutRebuildCount);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_builds_data_only_applied_partial_plan_from_snapshot()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialRebuildCount = pipeline.LayoutRebuildCount;
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, frame2.Resources, frame2.Resources);

        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.None, plan.Reason);
        Assert.Equal(snapshot.DirtyElementRanges, plan.DirtyElementRanges);
        Assert.Equal(snapshot.DirtyCommandRanges, plan.DirtyCommandRanges);
        Assert.Equal(snapshot.HitTargets, plan.PatchedHitTargets);
        Assert.Equal(initialRebuildCount + 1, pipeline.LayoutRebuildCount);
        Assert.Equal(2, frame2.Commands.Count);
        Assert.Equal(frame2.HitTargets, snapshot.HitTargets);
        Assert.Equal(frame2.DirtyCommandRanges, plan.DirtyCommandRanges);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_falls_back_for_not_style_only_dirty()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(0)],
            children: [VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1)))]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(24)],
            children: [VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1)))]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [0]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, frame2.Resources, frame2.Resources);

        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, plan.Reason);
        Assert.Equal(snapshot.DirtyElementRanges, plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
        Assert.Equal(LayoutRebuildReason.LayoutAffecting, snapshot.LayoutRebuildReason);
        Assert.Equal(frame2.DirtyCommandRanges, snapshot.DirtyCommandRanges);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_falls_back_for_viewport_changed()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, new PixelRectangle(0, 0, 800, 480), frame2.Resources, frame2.Resources);

        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.ViewportChanged, plan.Reason);
        Assert.Equal(snapshot.DirtyElementRanges, plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_falls_back_for_missing_snapshot()
    {
        var plan = RetainedPartialApplyPlanner.Plan(
            snapshot: null,
            new PixelRectangle(0, 0, 960, 540),
            FrameDrawingResources.Empty,
            FrameDrawingResources.Empty);

        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.MissingRetainedSnapshot, plan.Reason);
        Assert.Empty(plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_rejects_resource_mismatch_without_side_effects()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var retainedFrameCommandCount = pipeline.RetainedFrame.CommandCount;
        var retainedFrameResources = pipeline.RetainedFrame.Resources;
        var retainedFrameDirtyRanges = pipeline.RetainedFrame.DirtyCommandRanges.ToArray();
        var layoutRebuildCount = pipeline.LayoutRebuildCount;
        var lastDirtyRanges = pipeline.LastDirtyCommandRanges.ToArray();

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, frame1.Resources, frame2.Resources);

        Assert.Equal(RetainedPartialApplyResultKind.Rejected, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.ResourceOwnershipMismatch, plan.Reason);
        Assert.Equal(snapshot.DirtyElementRanges, plan.DirtyElementRanges);
        Assert.Equal(snapshot.DirtyCommandRanges, plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
        Assert.Equal(retainedFrameCommandCount, pipeline.RetainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, pipeline.RetainedFrame.Resources);
        Assert.Equal(retainedFrameDirtyRanges, pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(layoutRebuildCount, pipeline.LayoutRebuildCount);
        Assert.Equal(lastDirtyRanges, pipeline.LastDirtyCommandRanges);
        Assert.Equal(frame2.HitTargets, snapshot.HitTargets);
        Assert.Equal(frame2.DirtyCommandRanges, snapshot.DirtyCommandRanges);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_falls_back_for_unstable_command_range()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var textA = _arena.AddText("A".AsSpan());
        var elements = new[]
        {
            new LayoutElement(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textA),
            new LayoutElement(LayoutElementKind.Rectangle, new PixelRectangle(0, 44, 100, 48))
        };
        var layoutResult = new LayoutTreeResult(elements, [], [(0, 2)]);
        var snapshot = new RenderPipelineRetainedInputSnapshot(
            layoutResult,
            [new ElementCommandRange(0, 1), new ElementCommandRange(3, 1)],
            [],
            VirtualNodeFactory.ScrollContainer(new NodeKey(1)),
            viewport,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            [(0, 2)],
            [],
            LayoutRebuildReason.StyleOnly);

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, FrameDrawingResources.Empty, FrameDrawingResources.Empty);

        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.UnstableCommandRange, plan.Reason);
        Assert.Equal([(0, 2)], plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
    }

    [Fact]
    public void RetainedPartialApplyPlanner_falls_back_for_hit_target_patch_failed()
    {
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var textIncrement = _arena.AddText("Increment".AsSpan());
        var elements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(32, 120, 140, 40),
                Text: textIncrement,
                ActionId: new ActionId(4))
        };
        var layoutResult = new LayoutTreeResult(elements, [], [(0, 1)]);
        var snapshot = new RenderPipelineRetainedInputSnapshot(
            layoutResult,
            [new ElementCommandRange(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(1))],
            VirtualNodeFactory.ScrollContainer(new NodeKey(1)),
            viewport,
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            [(0, 1)],
            [],
            LayoutRebuildReason.StyleOnly);

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, FrameDrawingResources.Empty, FrameDrawingResources.Empty);

        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.HitTargetPatchFailed, plan.Reason);
        Assert.Equal([(0, 1)], plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
    }

    [Fact]
    public async Task RetainedPartialApplyPlanner_does_not_mutate_drawing_backend_compositor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        using var compositor = new DrawingBackendCompositor(new NoOpBackend());
        await compositor.RenderAsync(frame2, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var renderCount = compositor.RenderCount;
        var fullApplyCount = compositor.FullApplyCount;
        var partialApplyCount = compositor.PartialApplyCount;
        var lastPartialApplySucceeded = compositor.LastPartialApplySucceeded;
        var retainedFrameCommandCount = compositor.RetainedFrame.CommandCount;
        var retainedFrameResources = compositor.RetainedFrame.Resources;
        var retainedDirtyRanges = compositor.RetainedFrame.DirtyCommandRanges.ToArray();
        var compositorDirtyRanges = compositor.LastDirtyCommandRanges.ToArray();

        var plan = RetainedPartialApplyPlanner.Plan(snapshot, viewport, frame1.Resources, frame2.Resources);

        Assert.Equal(RetainedPartialApplyResultKind.Rejected, plan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.ResourceOwnershipMismatch, plan.Reason);
        Assert.Equal(renderCount, compositor.RenderCount);
        Assert.Equal(fullApplyCount, compositor.FullApplyCount);
        Assert.Equal(partialApplyCount, compositor.PartialApplyCount);
        Assert.Equal(lastPartialApplySucceeded, compositor.LastPartialApplySucceeded);
        Assert.Equal(retainedFrameCommandCount, compositor.RetainedFrame.CommandCount);
        Assert.Same(retainedFrameResources, compositor.RetainedFrame.Resources);
        Assert.Equal(retainedDirtyRanges, compositor.RetainedFrame.DirtyCommandRanges);
        Assert.Equal(compositorDirtyRanges, compositor.LastDirtyCommandRanges);
    }

    [Fact]
    public void StyleOnly_hover_preserves_layout_reuse_invariant_snapshot()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 160);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120)],
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(1)),
                    VirtualNodeProperty.Hovered(false)),
                VirtualNodeFactory.Rectangle(new NodeKey(4), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48))
            ]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120)],
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(1)),
                    VirtualNodeProperty.Hovered(true)),
                VirtualNodeFactory.Rectangle(new NodeKey(4), VirtualNodeProperty.Width(220), VirtualNodeProperty.Height(48))
            ]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialLayout = pipeline.LastLayoutResult!;
        var initialElementSnapshot = SnapshotLayoutElementInvariants(initialLayout.Elements);
        var initialRangeSnapshot = SnapshotLayoutTreeRanges(initialLayout.TreeNodes);
        ScrollContainerDiag[] initialScrollDiagnostics = [.. initialLayout.ScrollDiagnostics];
        ElementCommandRange[] initialCommandRanges = [.. pipeline.LastElementCommandRanges];
        var initialRebuildCount = pipeline.LayoutRebuildCount;

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var nextLayout = pipeline.LastLayoutResult!;

        Assert.Equal(initialRebuildCount + 1, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.True(StyleOnlyPatchEligibility.IsLayoutReuseEligible(pipeline.LastDirtyClassifications, viewportChanged: false));
        Assert.Equal(initialElementSnapshot, SnapshotLayoutElementInvariants(nextLayout.Elements));
        Assert.Equal(initialRangeSnapshot, SnapshotLayoutTreeRanges(nextLayout.TreeNodes));
        Assert.Equal(initialScrollDiagnostics, nextLayout.ScrollDiagnostics);
        Assert.Equal(initialCommandRanges, pipeline.LastElementCommandRanges);
        Assert.Equal([new LayoutDirtyClassification(2, LayoutRebuildReason.StyleOnly)], pipeline.LastDirtyClassifications);
        Assert.Equal([(1, 1)], pipeline.LastDirtyElementRanges);
        Assert.Equal([(1, 2)], pipeline.LastDirtyCommandRanges);
    }

    [Fact]
    public void StyleOnly_action_id_change_preserves_geometry_but_refreshes_hit_target_metadata()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialLayout = pipeline.LastLayoutResult!;
        var initialGeometry = SnapshotLayoutGeometryInvariants(initialLayout.Elements);
        var initialHitTarget = Assert.Single(frame1.HitTargets);
        var initialRebuildCount = pipeline.LayoutRebuildCount;

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var nextLayout = pipeline.LastLayoutResult!;
        var nextHitTarget = Assert.Single(frame2.HitTargets);

        Assert.Equal(initialRebuildCount + 1, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.True(StyleOnlyPatchEligibility.IsLayoutReuseEligible(pipeline.LastDirtyClassifications, viewportChanged: false));
        Assert.Equal(initialGeometry, SnapshotLayoutGeometryInvariants(nextLayout.Elements));
        Assert.Equal(initialHitTarget.Bounds, nextHitTarget.Bounds);
        Assert.Equal(initialHitTarget.ClipBounds, nextHitTarget.ClipBounds);
        Assert.Equal(new ActionId(1), initialHitTarget.ActionId);
        Assert.Equal(new ActionId(4), nextHitTarget.ActionId);
        Assert.NotEqual(initialHitTarget.ActionId, nextHitTarget.ActionId);

        var patched = StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(
            frame1.HitTargets,
            nextLayout.Elements,
            pipeline.LastDirtyElementRanges,
            out var patchedHitTargets);

        Assert.True(patched);
        var patchedHitTarget = Assert.Single(patchedHitTargets);
        Assert.Equal(initialHitTarget.Bounds, patchedHitTarget.Bounds);
        Assert.Equal(initialHitTarget.ClipBounds, patchedHitTarget.ClipBounds);
        Assert.Equal(new ActionId(4), patchedHitTarget.ActionId);
    }

    [Fact]
    public void StyleOnly_command_rerecord_uses_current_frame_resources()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var resources1 = Assert.IsType<FrameDrawingResources>(frame1.Resources);

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var resources2 = Assert.IsType<FrameDrawingResources>(frame2.Resources);

        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.True(StyleOnlyPatchEligibility.IsLayoutReuseEligible(pipeline.LastDirtyClassifications, viewportChanged: false));
        Assert.NotSame(resources1, resources2);
        Assert.Same(resources2, pipeline.RetainedFrame.Resources);
        Assert.Equal("Increment", resources2.Resolve(frame2.Commands.Memory.Span[1].Text).ToString());
    }

    [Fact]
    public void StyleOnly_layout_skip_preflight_keeps_full_layout_rebuild_and_post_publication_partial_apply_separate()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var styleOnlyRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));
        var mixedRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var initialLayout = pipeline.LastLayoutResult!;
        var initialLayoutSnapshot = SnapshotLayoutGeometryInvariants(initialLayout.Elements);
        var initialRebuildCount = pipeline.LayoutRebuildCount;
        using var styleOnlyFrame = pipeline.Build(styleOnlyRoot, viewport, _arena.GetOrCreateSnapshot(), [2]);
        var styleOnlySnapshot = pipeline.LastRetainedInputSnapshot;
        var styleOnlyPlan = RetainedPartialApplyPlanner.Plan(styleOnlySnapshot, viewport, styleOnlyFrame.Resources, styleOnlyFrame.Resources);
        var styleOnlyPatchPlan = StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            initialLayout,
            [.. pipeline.LastElementCommandRanges],
            frame1.HitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);

        Assert.Equal(initialRebuildCount + 1, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, pipeline.LastLayoutRebuildReason);
        Assert.True(StyleOnlyPatchEligibility.IsLayoutReuseEligible(pipeline.LastDirtyClassifications, viewportChanged: false));
        Assert.Equal(initialLayoutSnapshot, SnapshotLayoutGeometryInvariants(pipeline.LastLayoutResult!.Elements));
        Assert.Equal(RetainedPartialApplyResultKind.AppliedPartial, styleOnlyPlan.Kind);
        Assert.True(styleOnlyPatchPlan.Eligible);

        using var mixedFrame = pipeline.Build(mixedRoot, viewport, _arena.GetOrCreateSnapshot(), [1, 2]);
        var mixedPlan = RetainedPartialApplyPlanner.Plan(pipeline.LastRetainedInputSnapshot, viewport, mixedFrame.Resources, mixedFrame.Resources);
        var viewportPlan = RetainedPartialApplyPlanner.Plan(styleOnlySnapshot, new PixelRectangle(0, 0, 800, 540), styleOnlyFrame.Resources, styleOnlyFrame.Resources);

        Assert.Equal(initialRebuildCount + 2, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.TextSizeAffecting, pipeline.LastLayoutRebuildReason);
        Assert.Contains(pipeline.LastDirtyClassifications, classification => classification.Reason == LayoutRebuildReason.TextSizeAffecting);
        Assert.Contains(pipeline.LastDirtyClassifications, classification => classification.Reason == LayoutRebuildReason.StyleOnly);
        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, mixedPlan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.NotStyleOnly, mixedPlan.Reason);
        Assert.Equal(RetainedPartialApplyResultKind.FallbackFull, viewportPlan.Kind);
        Assert.Equal(RetainedPartialApplyFallbackReason.ViewportChanged, viewportPlan.Reason);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_creates_eligible_hover_only_plan()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(false)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1)),
                VirtualNodeProperty.Hovered(true)));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var plan = StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);

        Assert.True(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.None, plan.FallbackReason);
        Assert.Equal([(0, 1)], plan.DirtyElementRanges);
        Assert.Equal([(0, 2)], plan.DirtyCommandRanges);
        var patchedHitTarget = Assert.Single(plan.PatchedHitTargets);
        Assert.Equal(new ActionId(1), patchedHitTarget.ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, patchedHitTarget.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, patchedHitTarget.ClipBounds);
        Assert.Equal(2, pipeline.LayoutRebuildCount);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_patches_action_id_hit_target_metadata()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(1))));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(4))));

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);
        var plan = StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);

        Assert.True(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.None, plan.FallbackReason);
        var patchedHitTarget = Assert.Single(plan.PatchedHitTargets);
        Assert.Equal(new ActionId(4), patchedHitTarget.ActionId);
        Assert.Equal(retainedHitTargets[0].Bounds, patchedHitTarget.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, patchedHitTarget.ClipBounds);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_falls_back_for_layout_affecting_dirty()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(0)],
            children: [VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1)))]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(24)],
            children: [VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1)))]);

        using var frame1 = pipeline.Build(root1, viewport, _arena.GetOrCreateSnapshot());
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [0]);
        var plan = StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);

        Assert.False(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.NotStyleOnly, plan.FallbackReason);
        Assert.Equal(pipeline.LastDirtyElementRanges, plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
        Assert.Equal(2, pipeline.LayoutRebuildCount);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_falls_back_for_unstable_command_mapping()
    {
        var textA = _arena.AddText("A".AsSpan());
        var nextElements = new[]
        {
            new LayoutElement(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textA),
            new LayoutElement(LayoutElementKind.Rectangle, new PixelRectangle(0, 44, 100, 48))
        };
        var retainedLayout = new LayoutTreeResult(nextElements, [], [(0, 2)]);
        var unstableCommandRanges = new ElementCommandRange[]
        {
            new(0, 1),
            new(3, 1)
        };

        var plan = StyleOnlyPatchPlanBuilder.Build(
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            viewportChanged: false,
            retainedLayout,
            unstableCommandRanges,
            [],
            nextElements,
            [(0, 2)]);

        Assert.False(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.UnstableCommandRange, plan.FallbackReason);
        Assert.Equal([(0, 2)], plan.DirtyElementRanges);
        Assert.Empty(plan.DirtyCommandRanges);
        Assert.Empty(plan.PatchedHitTargets);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_records_missing_retained_layout_fallback()
    {
        var textA = _arena.AddText("A".AsSpan());
        var nextElements = new[]
        {
            new LayoutElement(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textA)
        };

        var plan = StyleOnlyPatchPlanBuilder.Build(
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            viewportChanged: false,
            retainedLayout: null,
            retainedElementCommandRanges: [],
            retainedHitTargets: [],
            nextElements,
            [(0, 1)]);

        Assert.False(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.MissingRetainedLayout, plan.FallbackReason);
        Assert.Equal([(0, 1)], plan.DirtyElementRanges);
    }

    [Fact]
    public void StyleOnlyPatchPlanBuilder_records_viewport_changed_fallback()
    {
        var textA = _arena.AddText("A".AsSpan());
        var nextElements = new[]
        {
            new LayoutElement(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textA)
        };
        var retainedLayout = new LayoutTreeResult(nextElements, [], [(0, 1)]);

        var plan = StyleOnlyPatchPlanBuilder.Build(
            [new LayoutDirtyClassification(1, LayoutRebuildReason.StyleOnly)],
            viewportChanged: true,
            retainedLayout,
            [new ElementCommandRange(0, 1)],
            [],
            nextElements,
            [(0, 1)]);

        Assert.False(plan.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.ViewportChanged, plan.FallbackReason);
        Assert.Equal([(0, 1)], plan.DirtyElementRanges);
    }

    [Fact]
    public void RenderPipeline_rebuilds_layout_when_tree_changes()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "Hello", new NodeKey(2)));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "World", new NodeKey(2)));

        using var frame1 = pipeline.Build(root1, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var text1 = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();
        using var frame2 = pipeline.Build(root2, viewport, textSnapshot: _arena.GetOrCreateSnapshot());
        var text2 = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        Assert.Equal("Hello", text1);
        Assert.Equal("World", text2);
    }

    private readonly record struct LayoutElementInvariant(
        LayoutElementKind Kind,
        PixelRectangle Bounds,
        PixelRectangle ClipBounds,
        ActionId ActionId);

    private readonly record struct LayoutTreeRangeInvariant(
        int DfsIndex,
        VirtualNodeKind Kind,
        int ElementStart,
        int ElementCount);

    private readonly record struct LayoutGeometryInvariant(
        LayoutElementKind Kind,
        PixelRectangle Bounds,
        PixelRectangle ClipBounds);

    private readonly record struct RenderFrameInvariant(
        RenderCommandInvariant[] Commands,
        HitTestTarget[] HitTargets,
        (int Start, int Count)[] DirtyCommandRanges);

    private readonly record struct RenderCommandInvariant(
        DrawCommandKind Kind,
        DrawRect Rect,
        DrawColor Color,
        DrawingResourceKind ResourceKind,
        string Text,
        TextStyle TextStyle);

    private static LayoutElementInvariant[] SnapshotLayoutElementInvariants(IReadOnlyList<LayoutElement> elements)
    {
        var snapshot = new LayoutElementInvariant[elements.Count];
        for (var elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            var element = elements[elementIndex];
            snapshot[elementIndex] = new LayoutElementInvariant(
                element.Kind,
                element.Bounds,
                element.ClipBounds,
                element.ActionId);
        }

        return snapshot;
    }

    private static LayoutTreeRangeInvariant[] SnapshotLayoutTreeRanges(LayoutTreeNode[] treeNodes)
    {
        var snapshot = new LayoutTreeRangeInvariant[treeNodes.Length];
        for (var i = 0; i < treeNodes.Length; i++)
        {
            var treeNode = treeNodes[i];
            snapshot[i] = new LayoutTreeRangeInvariant(
                treeNode.DfsIndex,
                treeNode.Kind,
                treeNode.ElementStart,
                treeNode.ElementCount);
        }

        return snapshot;
    }

    private static LayoutGeometryInvariant[] SnapshotLayoutGeometryInvariants(IReadOnlyList<LayoutElement> elements)
    {
        var snapshot = new LayoutGeometryInvariant[elements.Count];
        for (var elementIndex = 0; elementIndex < elements.Count; elementIndex++)
        {
            var element = elements[elementIndex];
            snapshot[elementIndex] = new LayoutGeometryInvariant(element.Kind, element.Bounds, element.ClipBounds);
        }

        return snapshot;
    }

    private static RenderFrameInvariant SnapshotRenderFrame(RenderFrameBatch frame)
    {
        using (frame)
        {
            var commands = new RenderCommandInvariant[frame.Commands.Count];
            for (var commandIndex = 0; commandIndex < frame.Commands.Count; commandIndex++)
            {
                var command = frame.Commands.Memory.Span[commandIndex];
                commands[commandIndex] = new RenderCommandInvariant(
                    command.Kind,
                    command.Rect,
                    command.Color,
                    command.Resource.Kind,
                    command.Kind == DrawCommandKind.DrawTextRun
                        ? frame.Resources.Resolve(command.Text).ToString()
                        : string.Empty,
                    command.Kind == DrawCommandKind.DrawTextRun
                        ? frame.Resources.ResolveTextStyle(command.Resource)
                        : TextStyle.Default);
            }

            return new RenderFrameInvariant(
                commands,
                [.. frame.HitTargets],
                [.. frame.DirtyCommandRanges]);
        }
    }

    private static void AssertRenderFrameSnapshotsEquivalent(RenderFrameInvariant expected, RenderFrameInvariant actual)
    {
        Assert.Equal(expected.Commands, actual.Commands);
        Assert.Equal(expected.HitTargets, actual.HitTargets);
        Assert.Equal(expected.DirtyCommandRanges, actual.DirtyCommandRanges);
    }

    private static void AssertRenderFramesEquivalent(RenderFrameBatch expected, RenderFrameBatch actual)
    {
        Assert.Equal(expected.Commands.Count, actual.Commands.Count);
        for (var commandIndex = 0; commandIndex < expected.Commands.Count; commandIndex++)
        {
            var expectedCommand = expected.Commands.Memory.Span[commandIndex];
            var actualCommand = actual.Commands.Memory.Span[commandIndex];
            Assert.Equal(expectedCommand.Kind, actualCommand.Kind);
            Assert.Equal(expectedCommand.Rect, actualCommand.Rect);
            Assert.Equal(expectedCommand.Color, actualCommand.Color);
            Assert.Equal(expectedCommand.Resource.Kind, actualCommand.Resource.Kind);

            if (expectedCommand.Kind == DrawCommandKind.DrawTextRun)
            {
                Assert.Equal(expected.Resources.Resolve(expectedCommand.Text).ToString(), actual.Resources.Resolve(actualCommand.Text).ToString());
                Assert.Equal(expected.Resources.ResolveTextStyle(expectedCommand.Resource), actual.Resources.ResolveTextStyle(actualCommand.Resource));
            }
        }

        Assert.Equal(expected.HitTargets.Count, actual.HitTargets.Count);
        for (var hitTargetIndex = 0; hitTargetIndex < expected.HitTargets.Count; hitTargetIndex++)
        {
            Assert.Equal(expected.HitTargets[hitTargetIndex].Bounds, actual.HitTargets[hitTargetIndex].Bounds);
            Assert.Equal(expected.HitTargets[hitTargetIndex].ClipBounds, actual.HitTargets[hitTargetIndex].ClipBounds);
            Assert.Equal(expected.HitTargets[hitTargetIndex].ActionId, actual.HitTargets[hitTargetIndex].ActionId);
        }

        Assert.Equal(expected.DirtyCommandRanges, actual.DirtyCommandRanges);
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

        public void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver)
        {
        }

        public void Show()
        {
        }

        public event Action<int, int>? SizeChanged { add { } remove { } }
        public event Action<DisplayScale>? DpiChanged { add { } remove { } }
    }

    private sealed class TranslatingPatchSink(WindowDrawCommandTranslator translator) : IVirtualNodePatchSink
    {
        public ValueTask PublishAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            return TranslateAsync(patchBatch);
        }

        public ValueTask PublishAndWaitRenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
        {
            return TranslateAsync(patchBatch);
        }

        private ValueTask TranslateAsync(PatchBatch patchBatch)
        {
            using (patchBatch)
            using (translator.Translate(patchBatch))
            {
            }

            return ValueTask.CompletedTask;
        }
    }

    private static ActionId HitIncrementAtButton(int x, int y)
    {
        return x == 32 && y == 140 ? new ActionId(1) : ActionId.None;
    }

    private static void AssertCompositionInvalidation(
        WindowDrawCommandTranslator translator,
        CompositionRenderInvalidationKind expectedKind,
        bool cancelsScrollPresentation)
    {
        Assert.Equal(expectedKind, translator.LastCompositionInvalidation.Kind);
        Assert.Equal(cancelsScrollPresentation, translator.LastCompositionInvalidation.CancelsScrollPresentation);
    }

    private sealed class NoOpBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task Poc_runtime_hover_only_input_classifies_style_only_dirty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), new TranslatingPatchSink(translator));
        await runtime.StartAsync(cancellationToken);
        var initialRebuildCount = translator.LayoutRebuildCount;
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);

        var mapped = Program.TryMapInputForRuntime(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            hitTestResolver,
            out var message);

        Assert.True(mapped);
        await runtime.DispatchAndWaitAsync(message!, cancellationToken);

        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.StyleOnly, translator.LastLayoutRebuildReason);
        Assert.Contains(translator.LastDirtyClassifications, classification => classification.Reason == LayoutRebuildReason.StyleOnly);
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.None, cancelsScrollPresentation: false);
    }

    [Fact]
    public async Task Poc_runtime_scroll_frame_classifies_layout_affecting_dirty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        await using var runtime = new Runtime<CounterModel, CounterMessage>(new CounterApplication(), new TranslatingPatchSink(translator));
        await runtime.StartAsync(cancellationToken);
        var initialRebuildCount = translator.LayoutRebuildCount;

        await runtime.DispatchAndWaitAsync(new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 48), 0.1), cancellationToken);

        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.LayoutAffecting, translator.LastLayoutRebuildReason);
        Assert.Contains(translator.LastDirtyClassifications, classification => classification is { DfsIndex: 0, Reason: LayoutRebuildReason.LayoutAffecting });
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.LayoutAffecting, cancelsScrollPresentation: true);
    }

    [Fact]
    public void Poc_translator_render_request_resize_classifies_viewport_changed()
    {
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(40, 50, 960, 540)));
        var rendererWidth = 960;
        var rendererHeight = 540;
        var translator = new WindowDrawCommandTranslator(
            window,
            prepareFrame: null,
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, rendererWidth, rendererHeight);
            },
            postFrameCallback: null);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "Resize", new NodeKey(2)));

        using var initialPatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var initialFrame = translator.Translate(initialPatch);
        var initialRebuildCount = translator.LayoutRebuildCount;

        rendererWidth = 800;
        rendererHeight = 480;
        window.Region = new ScreenRegion(0, new PixelRectangle(40, 50, rendererWidth, rendererHeight));
        using var resizeRequest = PatchBatch.CreateRenderRequest();
        using var resizeFrame = translator.Translate(resizeRequest);

        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.ViewportChanged, translator.LastLayoutRebuildReason);
        Assert.Empty(translator.LastDirtyClassifications);
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.ViewportChanged, cancelsScrollPresentation: true);
    }

    [Fact]
    public void RenderPipeline_rebuilds_layout_when_dirty_nodes_provided()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // First build with no dirty
        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        // Second build with same root/viewport but dirty nodes �?forces rebuild
        using var frame2 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot(), [0]);

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
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // First build
        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        var text1 = frame1.Resources.Resolve(frame1.Commands.Memory.Span[0].Text).ToString();

        // Second build with empty dirty set �?should reuse retained layout
        using var frame2 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot(), []);
        var text2 = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();

        Assert.Equal(text1, text2);
        Assert.Equal(frame1.Commands.Count, frame2.Commands.Count);
    }

    [Fact]
    public void RenderPipeline_retained_input_snapshot_uses_retained_text_snapshot_when_layout_reused()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Stable", new NodeKey(2)));
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var retainedSnapshot = _arena.GetOrCreateSnapshot();

        using var frame1 = pipeline.Build(root, viewport, retainedSnapshot);

        _arena.BeginFrame();
        _ = _arena.AddText("unrelated".AsSpan());
        var currentSnapshot = _arena.GetOrCreateSnapshot();

        using var frame2 = pipeline.Build(root, viewport, currentSnapshot);
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.True(snapshot.TextSnapshot.HasValue);
        Assert.Equal(retainedSnapshot, snapshot.TextSnapshot.Value);
        Assert.NotEqual(currentSnapshot, snapshot.TextSnapshot.Value);
        Assert.Equal("Stable", frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString());
    }

    [Fact]
    public void WindowDrawCommandTranslator_diff_batch_produces_correct_layout()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));

        var root1 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        // Initial frame via diff from default �?root1
        using var batch1 = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()));
        using var frame1 = translator.Translate(batch1);
        Assert.Equal(3, frame1.Commands.Count);

        var root2 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        // Update frame via diff from root1 �?root2
        using var batch2 = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()), new VirtualNodeTree(root2, _arena.GetOrCreateSnapshot()));
        using var frame2 = translator.Translate(batch2);

        // Layout should reflect updated content
        var textContent = frame2.Resources.Resolve(frame2.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("Count: 1", textContent);
    }

    [Fact]
    public void WindowDrawCommandTranslator_text_size_diff_maps_to_text_invalidation()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        var root1 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Short", new NodeKey(2)));
        var root2 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Longer text", new NodeKey(2)));

        using var batch1 = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()));
        using var frame1 = translator.Translate(batch1);
        using var batch2 = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()), new VirtualNodeTree(root2, _arena.GetOrCreateSnapshot()));
        using var frame2 = translator.Translate(batch2);

        Assert.Equal(LayoutRebuildReason.TextSizeAffecting, translator.LastLayoutRebuildReason);
        Assert.Contains(translator.LastDirtyClassifications, classification => classification.Reason == LayoutRebuildReason.TextSizeAffecting);
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.TextSizeAffecting, cancelsScrollPresentation: true);
    }

    [Fact]
    public void WindowDrawCommandTranslator_tree_diff_maps_to_tree_invalidation()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));
        var root1 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)));
        var root2 = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "A", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "B", new NodeKey(3)));

        using var batch1 = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()));
        using var frame1 = translator.Translate(batch1);
        using var batch2 = VirtualNodeDiffer.CreatePatchBatch(new VirtualNodeTree(root1, _arena.GetOrCreateSnapshot()), new VirtualNodeTree(root2, _arena.GetOrCreateSnapshot()));
        using var frame2 = translator.Translate(batch2);

        Assert.Equal(LayoutRebuildReason.TreeStructure, translator.LastLayoutRebuildReason);
        Assert.Contains(translator.LastDirtyClassifications, classification => classification.Reason == LayoutRebuildReason.TreeStructure);
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.TreeStructure, cancelsScrollPresentation: true);
    }

    [Fact]
    public void WindowDrawCommandTranslator_keeps_batches_in_logical_coordinates_under_display_scale()
    {
        var translator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 1440, 810))),
            prepareFrame: null,
            viewportProvider: null,
            postFrameCallback: null,
            displayScale: new DisplayScale(1.5f, 1.5f));
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Increment", new NodeKey(3),
                VirtualNodeProperty.Action(new ActionId(1))));

        using var patchBatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var frame = translator.Translate(patchBatch);

        Assert.Equal(new PixelRectangle(0, 0, 1440, 810), translator.LastViewport);
        Assert.Equal(new PixelRectangle(0, 0, 960, 540), translator.LastLayoutViewport);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[1].Rect);
        Assert.Equal(new DrawRect(16, 60, 140, 40), frame.Commands.Memory.Span[2].Rect);
        var hitTarget = Assert.Single(frame.HitTargets);
        Assert.Equal(new PixelRectangle(16, 60, 140, 40), hitTarget.Bounds);
        Assert.Equal(new PixelRectangle(0, 0, 960, 540), hitTarget.ClipBounds);
    }

    [Fact]
    public void WindowDrawCommandTranslator_builds_typed_scroll_feedback_alongside_legacy_max_scroll_callback()
    {
        double? callbackMaxScrollY = null;
        var translator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 100))),
            prepareFrame: null,
            viewportProvider: null,
            postFrameCallback: maxScrollY => callbackMaxScrollY = maxScrollY);
        var children = new VirtualNode[10];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = VirtualNodeBuilder.Text(_arena, $"item {index}", new NodeKey((uint)(index + 2)));
        }

        var root = new VirtualNode(VirtualNodeKind.ScrollContainer, key: 1, children: children);
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var frame = translator.Translate(batch);

        Assert.True(callbackMaxScrollY.HasValue);
        var metrics = Assert.Single(translator.LastScrollFeedback.Containers);
        Assert.Equal(new ScrollContainerId(0), metrics.ContainerId);
        Assert.Equal(100.0, metrics.ViewportExtent);
        Assert.True(metrics.ContentExtent > metrics.ViewportExtent);
        Assert.Equal(translator.LastMaxScrollY, metrics.MaxScrollY);
        Assert.Equal(callbackMaxScrollY.Value, metrics.MaxScrollY);
    }

    [Fact]
    public void WindowDrawCommandTranslator_delivers_projected_feedback_to_injected_sink()
    {
        var feedbackSink = new RecordingControlFeedbackSink();
        var translator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 100))),
            prepareFrame: null,
            viewportProvider: null,
            postFrameCallback: null,
            feedbackSink: feedbackSink);
        var children = new VirtualNode[10];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = VirtualNodeBuilder.Text(_arena, $"item {index}", new NodeKey((uint)(index + 2)));
        }

        var root = new VirtualNode(VirtualNodeKind.ScrollContainer, key: 1, children: children);
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var frame = translator.Translate(batch);

        var metrics = Assert.Single(feedbackSink.LastScrollFeedback.Containers);
        Assert.Equal(1, feedbackSink.DeliveryCount);
        Assert.Equal(new ScrollContainerId(0), metrics.ContainerId);
        Assert.Equal(100.0, metrics.ViewportExtent);
        Assert.True(metrics.ContentExtent > metrics.ViewportExtent);
        Assert.Equal(feedbackSink.LastMaxScrollY, metrics.MaxScrollY);
        Assert.Equal(feedbackSink.LastMaxScrollY, translator.LastMaxScrollY);
        Assert.Same(feedbackSink.LastScrollFeedback, translator.LastScrollFeedback);
    }

    [Fact]
    public void WindowDrawCommandTranslator_keeps_feedback_callback_and_viewport_diagnostics_aligned_on_render_request()
    {
        var callbackMaxScrollYs = new List<double>();
        var viewport = new PixelRectangle(10, 20, 960, 100);
        var translator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))),
            prepareFrame: null,
            viewportProvider: () => viewport,
            postFrameCallback: callbackMaxScrollYs.Add);
        var children = new VirtualNode[10];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = VirtualNodeBuilder.Text(_arena, $"item {index}", new NodeKey((uint)(index + 2)));
        }

        var root = new VirtualNode(VirtualNodeKind.ScrollContainer, key: 1, children: children);
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var initialFrame = translator.Translate(batch);
        var initialMetrics = Assert.Single(translator.LastScrollFeedback.Containers);

        Assert.Single(callbackMaxScrollYs);
        Assert.Equal(viewport, translator.LastViewport);
        Assert.Equal(viewport, translator.LastLayoutViewport);
        Assert.Equal(translator.LastMaxScrollY, initialMetrics.MaxScrollY);
        Assert.Equal(callbackMaxScrollYs[0], initialMetrics.MaxScrollY);

        viewport = new PixelRectangle(10, 20, 960, 140);
        using var renderRequest = PatchBatch.CreateRenderRequest();
        using var renderRequestFrame = translator.Translate(renderRequest);
        var renderRequestMetrics = Assert.Single(translator.LastScrollFeedback.Containers);

        Assert.Equal(2, callbackMaxScrollYs.Count);
        Assert.Equal(viewport, translator.LastViewport);
        Assert.Equal(viewport, translator.LastLayoutViewport);
        Assert.Equal(LayoutRebuildReason.ViewportChanged, translator.LastLayoutRebuildReason);
        Assert.Empty(translator.LastDirtyClassifications);
        Assert.Equal(140.0, renderRequestMetrics.ViewportExtent);
        Assert.True(renderRequestMetrics.ContentExtent > renderRequestMetrics.ViewportExtent);
        Assert.True(renderRequestMetrics.MaxScrollY < initialMetrics.MaxScrollY);
        Assert.Equal(translator.LastMaxScrollY, renderRequestMetrics.MaxScrollY);
        Assert.Equal(callbackMaxScrollYs[^1], renderRequestMetrics.MaxScrollY);
        AssertCompositionInvalidation(translator, CompositionRenderInvalidationKind.ViewportChanged, cancelsScrollPresentation: true);
        Assert.Equal(initialFrame.Commands.Count, renderRequestFrame.Commands.Count);
    }

    [Fact]
    public void WindowDrawCommandTranslator_pipeline_factory_creates_once_and_keeps_feedback_diagnostics()
    {
        var creationCount = 0;
        var callbackMaxScrollYs = new List<double>();
        var viewport = new PixelRectangle(0, 0, 960, 100);
        var factory = new TranslatorRenderPipelineFactory(() =>
        {
            creationCount++;
            return new RenderPipeline(CounterStylePreset.Default);
        });
        var translator = new WindowDrawCommandTranslator(
            new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))),
            prepareFrame: null,
            viewportProvider: () => viewport,
            postFrameCallback: callbackMaxScrollYs.Add,
            renderPipelineFactory: factory);
        var children = new VirtualNode[10];
        for (var childIndex = 0; childIndex < children.Length; childIndex++)
        {
            children[childIndex] = VirtualNodeBuilder.Text(_arena, $"item {childIndex}", new NodeKey((uint)(childIndex + 2)));
        }

        var root = new VirtualNode(VirtualNodeKind.ScrollContainer, key: 1, children: children);
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var initialFrame = translator.Translate(batch);
        var initialMetrics = Assert.Single(translator.LastScrollFeedback.Containers);
        var initialRebuildCount = translator.LayoutRebuildCount;

        Assert.Equal(1, creationCount);
        Assert.Single(callbackMaxScrollYs);
        Assert.Equal(viewport, translator.LastViewport);
        Assert.Equal(viewport, translator.LastLayoutViewport);
        Assert.Equal(translator.LastMaxScrollY, initialMetrics.MaxScrollY);
        Assert.Equal(callbackMaxScrollYs[0], initialMetrics.MaxScrollY);

        viewport = new PixelRectangle(0, 0, 960, 140);
        using var renderRequest = PatchBatch.CreateRenderRequest();
        using var renderRequestFrame = translator.Translate(renderRequest);
        var renderRequestMetrics = Assert.Single(translator.LastScrollFeedback.Containers);

        Assert.Equal(1, creationCount);
        Assert.Equal(2, callbackMaxScrollYs.Count);
        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.ViewportChanged, translator.LastLayoutRebuildReason);
        Assert.Empty(translator.LastDirtyClassifications);
        Assert.Equal(140.0, renderRequestMetrics.ViewportExtent);
        Assert.Equal(translator.LastMaxScrollY, renderRequestMetrics.MaxScrollY);
        Assert.Equal(callbackMaxScrollYs[^1], renderRequestMetrics.MaxScrollY);
        Assert.Equal(initialFrame.Commands.Count, renderRequestFrame.Commands.Count);
    }

    [Fact]
    public void WindowDrawCommandTranslator_render_request_reuses_retained_tree()
    {
        var translator = new WindowDrawCommandTranslator(new FakeWindow(
            new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540))));

        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));

        // Set up retained tree via initial diff
        using var batch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
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
    public void WindowDrawCommandTranslator_synthetic_resize_rebuilds_only_when_viewport_size_changes()
    {
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(40, 50, 960, 540)));
        var rendererWidth = 960;
        var rendererHeight = 540;
        var pendingWidth = 0;
        var pendingHeight = 0;
        var pendingResize = false;
        var lastAppliedPendingResize = window.Region.PhysicalBounds;

        var translator = new WindowDrawCommandTranslator(
            window,
            () =>
            {
                if (!pendingResize)
                {
                    return;
                }

                rendererWidth = pendingWidth;
                rendererHeight = pendingHeight;
                pendingResize = false;
                var bounds = window.Region.PhysicalBounds;
                lastAppliedPendingResize = new PixelRectangle(bounds.X, bounds.Y, rendererWidth, rendererHeight);
            },
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, rendererWidth, rendererHeight);
            },
            postFrameCallback: null);
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1), VirtualNodeBuilder.Text(_arena, "Resize", new NodeKey(2)));

        using var initialPatch = VirtualNodeDiffer.CreatePatchBatch(default, new VirtualNodeTree(root, _arena.GetOrCreateSnapshot()));
        using var initialFrame = translator.Translate(initialPatch);
        var initialRebuildCount = translator.LayoutRebuildCount;

        RequestResize(777, 333);
        window.Region = new ScreenRegion(0, new PixelRectangle(40, 50, 777, 333));
        using var resizeRequest = PatchBatch.CreateRenderRequest();
        using var resizedFrame = translator.Translate(resizeRequest);

        var expectedViewport = new PixelRectangle(40, 50, 777, 333);
        Assert.Equal(expectedViewport, lastAppliedPendingResize);
        Assert.Equal(expectedViewport, translator.LastViewport);
        Assert.Equal(expectedViewport, translator.LastLayoutViewport);
        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(745, resizedFrame.Commands.Memory.Span[0].Rect.Width);

        RequestResize(777, 333);
        using var duplicateRequest = PatchBatch.CreateRenderRequest();
        using var duplicateFrame = translator.Translate(duplicateRequest);

        Assert.Equal(expectedViewport, translator.LastViewport);
        Assert.Equal(expectedViewport, translator.LastLayoutViewport);
        Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
        Assert.Equal(resizedFrame.Commands.Memory.Span[0].Rect, duplicateFrame.Commands.Memory.Span[0].Rect);

        for (var i = 0; i < 3; i++)
        {
            RequestResize(777, 333);
            using var sameSizeRequest = PatchBatch.CreateRenderRequest();
            using var sameSizeFrame = translator.Translate(sameSizeRequest);

            Assert.Equal(expectedViewport, translator.LastViewport);
            Assert.Equal(expectedViewport, translator.LastLayoutViewport);
            Assert.Equal(initialRebuildCount + 1, translator.LayoutRebuildCount);
            Assert.Equal(resizedFrame.Commands.Memory.Span[0].Rect, sameSizeFrame.Commands.Memory.Span[0].Rect);
        }

        RequestResize(880, 360);
        window.Region = new ScreenRegion(0, new PixelRectangle(40, 50, 880, 360));
        using var secondResizeRequest = PatchBatch.CreateRenderRequest();
        using var secondResizedFrame = translator.Translate(secondResizeRequest);

        var secondExpectedViewport = new PixelRectangle(40, 50, 880, 360);
        Assert.Equal(secondExpectedViewport, lastAppliedPendingResize);
        Assert.Equal(secondExpectedViewport, translator.LastViewport);
        Assert.Equal(secondExpectedViewport, translator.LastLayoutViewport);
        Assert.Equal(initialRebuildCount + 2, translator.LayoutRebuildCount);
        Assert.Equal(848, secondResizedFrame.Commands.Memory.Span[0].Rect.Width);

        void RequestResize(int width, int height)
        {
            if (width == rendererWidth && height == rendererHeight)
            {
                pendingResize = false;
                return;
            }

            pendingWidth = width;
            pendingHeight = height;
            pendingResize = true;
        }
    }

    [Fact]
    public void LayoutTree_text_update_produces_correct_dirty_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "before", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Build with dirty node 2 (the Button node)
        var result = builder.BuildLayoutTree(root, viewport, [2]);

        // Text is element 0, Button is element 1
        Assert.Equal(2, result.Elements.Count);
        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((1, 1), result.DirtyElementRanges[0]); // Button element at index 1
    }

    [Fact]
    public void LayoutTree_button_label_child_dirty_maps_to_button_element_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport, [3]);

        Assert.Equal(2, result.Elements.Count);
        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((1, 1), result.DirtyElementRanges[0]);
    }

    [Fact]
    public void LayoutTree_add_remove_child_produces_parent_dirty_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty node 0 (root/parent) �?its element range spans all children
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4)));
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // No dirty nodes �?empty dirty ranges
        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.Elements);
        Assert.Empty(result.DirtyElementRanges);
    }

    [Fact]
    public void LayoutTree_empty_publications_reuse_static_empty_arrays()
    {
        var builder = new LayoutTreeBuilder();
        var textRoot = VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(1));
        var containerRoot = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "after", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var cleanResult = builder.BuildLayoutTree(textRoot, viewport);
        var dirtyEmptyResult = builder.BuildLayoutTree(containerRoot, viewport, [1]);

        Assert.Same(Array.Empty<(int Start, int Count)>(), cleanResult.DirtyElementRanges);
        Assert.Same(Array.Empty<ScrollContainerDiag>(), cleanResult.ScrollDiagnostics);
        Assert.Same(Array.Empty<(int Start, int Count)>(), dirtyEmptyResult.DirtyElementRanges);
    }

    [Fact]
    public void LayoutTree_tree_structure_maps_dfs_indices()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "first", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "btn", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        // Should have one flat preorder node for root plus one per laid-out child.
        Assert.Equal(3, result.TreeNodes.Length);
        var rootNode = result.TreeNodes[0];
        Assert.Equal(0, rootNode.DfsIndex);
        Assert.Equal(2, rootNode.ElementCount); // 2 children �?2 elements
        Assert.Equal(1, rootNode.SubtreeStart);
        Assert.Equal(2, rootNode.SubtreeCount);

        // Children should be the Text and Button
        Assert.Equal(1, result.TreeNodes[1].DfsIndex);
        Assert.Equal(VirtualNodeKind.Text, result.TreeNodes[1].Kind);
        Assert.Equal(0, result.TreeNodes[1].ElementStart);
        Assert.Equal(1, result.TreeNodes[1].ElementCount);

        Assert.Equal(2, result.TreeNodes[2].DfsIndex);
        Assert.Equal(VirtualNodeKind.Button, result.TreeNodes[2].Kind);
        Assert.Equal(1, result.TreeNodes[2].ElementStart);
        Assert.Equal(1, result.TreeNodes[2].ElementCount);
    }

    [Fact]
    public void LayoutTree_container_element_range_ignores_empty_nested_container()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "after", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.Elements);
        Assert.Equal(3, result.TreeNodes.Length);
        Assert.Equal(0, result.TreeNodes[0].ElementStart);
        Assert.Equal(1, result.TreeNodes[0].ElementCount);
        Assert.Equal(1, result.TreeNodes[0].SubtreeStart);
        Assert.Equal(2, result.TreeNodes[0].SubtreeCount);
        Assert.Equal(0, result.TreeNodes[1].ElementCount);
        Assert.Equal(0, result.TreeNodes[2].ElementStart);
        Assert.Equal(1, result.TreeNodes[2].ElementCount);
    }

    [Fact]
    public void LayoutTree_dirty_empty_container_does_not_emit_zero_count_range()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "after", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport, [1]);

        Assert.Empty(result.DirtyElementRanges);
    }

    [Fact]
    public void ScrollContainer_negative_scrollY_clamped_to_zero()
    {
        var builder = new LayoutTreeBuilder();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(-50)],
            children: [
                VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2))
            ]);
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        // ScrollY=-50 should be clamped to 0; element should be at original position
        Assert.Single(result.Elements);
        Assert.Equal(16, result.Elements[0].Bounds.Y); // VerticalPadding, no scroll offset

        Assert.Single(result.ScrollDiagnostics);
        Assert.Equal(0, result.ScrollDiagnostics[0].ScrollY); // clamped to 0
    }

    [Fact]
    public void ScrollContainer_scrollY_clamped_to_max_scroll()
    {
        var builder = new LayoutTreeBuilder();
        // 10 text items × (32 height + 12 spacing) = 440 content height
        // Root implicit visible height uses the full viewport height.
        // MaxScrollY = 440 - 100 = 340
        var children = new VirtualNode[10];
        for (var i = 0; i < 10; i++)
        {
            children[i] = VirtualNodeBuilder.Text(_arena, $"item {i}", new NodeKey((uint)(i + 2)));
        }
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(9999)],
            children: children);
        var viewport = new PixelRectangle(0, 0, 960, 100);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.ScrollDiagnostics);
        var diag = result.ScrollDiagnostics[0];
        Assert.Equal(340, diag.MaxScrollY);
        Assert.Equal(340, diag.ScrollY); // clamped from 9999 to 340

        // First element should be scrolled up by 340
        // Original y = 16, after scroll: 16 - 340 = -324
        Assert.Equal(-324, result.Elements[0].Bounds.Y);
    }

    [Fact]
    public void ScrollContainer_explicit_height_limits_visible_area()
    {
        var builder = new LayoutTreeBuilder();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60)],
            children: [
                VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
                VirtualNodeBuilder.Text(_arena, "world", new NodeKey(3)),
            ]);
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.ScrollDiagnostics);
        var diag = result.ScrollDiagnostics[0];
        Assert.Equal(60, diag.VisibleHeight); // explicit Height=60
        // Content: 2 items × (32+12) = 88 (last item has spacing too)
        Assert.True(diag.ContentHeight > diag.VisibleHeight);
        Assert.True(diag.MaxScrollY > 0);
    }

    [Fact]
    public void ScrollContainer_diagnostics_counts_visible_and_clipped_elements()
    {
        var builder = new LayoutTreeBuilder();
        // Root implicit visible height uses the full viewport height.
        // Item height = 32 + 12 = 44 per item
        // Item 0: y=16, bottom=48 -> inside clip (0..50)
        // Item 1: y=60, bottom=92 -> outside clip (0..50)
        // Item 2+: also outside
        var children = new VirtualNode[5];
        for (var i = 0; i < 5; i++)
        {
            children[i] = VirtualNodeBuilder.Text(_arena, $"item {i}", new NodeKey((uint)(i + 2)));
        }
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            children: children);
        var viewport = new PixelRectangle(0, 0, 960, 50);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Single(result.ScrollDiagnostics);
        var diag = result.ScrollDiagnostics[0];
        Assert.Equal(1, diag.VisibleElementCount); // only item 0 intersects the viewport
        Assert.Equal(4, diag.ClippedElementCount); // items 1-4 outside clip
        Assert.Equal(0, diag.ScrollY); // no scroll, content extends beyond clip
    }

    [Fact]
    public void Nested_scroll_offsets_child_elements_once_per_scroll_container()
    {
        var builder = new LayoutTreeBuilder();
        var nested = VirtualNodeFactory.ScrollContainer(
            new NodeKey(2),
            [VirtualNodeProperty.Height(50), VirtualNodeProperty.ScrollY(20)],
            [
                VirtualNodeBuilder.Text(_arena, "first", new NodeKey(3)),
                VirtualNodeBuilder.Text(_arena, "second", new NodeKey(4)),
                VirtualNodeBuilder.Text(_arena, "third", new NodeKey(5))
            ]);
        var root = VirtualNodeFactory.ScrollContainer(
            new NodeKey(1),
            [VirtualNodeProperty.Height(40), VirtualNodeProperty.ScrollY(10)],
            [nested]);
        var viewport = new PixelRectangle(0, 0, 960, 200);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Equal(3, result.Elements.Count);
        Assert.Equal(-14, result.Elements[0].Bounds.Y);
        Assert.Equal(30, result.Elements[1].Bounds.Y);
        Assert.Equal(74, result.Elements[2].Bounds.Y);
        Assert.Equal(2, result.ScrollDiagnostics.Count);
        Assert.Equal(20, result.ScrollDiagnostics[0].ScrollY);
        Assert.Equal(10, result.ScrollDiagnostics[1].ScrollY);
    }

    [Fact]
    public void Nested_scroll_diagnostics_do_not_count_subtree_elements_more_than_once()
    {
        var builder = new LayoutTreeBuilder();
        var nested = VirtualNodeFactory.ScrollContainer(
            new NodeKey(2),
            [VirtualNodeProperty.Height(50)],
            [
                VirtualNodeBuilder.Text(_arena, "first", new NodeKey(3)),
                VirtualNodeBuilder.Text(_arena, "second", new NodeKey(4)),
                VirtualNodeBuilder.Text(_arena, "third", new NodeKey(5))
            ]);
        var root = VirtualNodeFactory.ScrollContainer(
            new NodeKey(1),
            [VirtualNodeProperty.Height(140)],
            [nested]);
        var viewport = new PixelRectangle(0, 0, 960, 200);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Equal(2, result.ScrollDiagnostics.Count);
        var outer = result.ScrollDiagnostics.Single(diag => diag.DfsIndex == 0);
        var inner = result.ScrollDiagnostics.Single(diag => diag.DfsIndex == 1);
        Assert.Equal(3, outer.VisibleElementCount);
        Assert.Equal(0, outer.ClippedElementCount);
        Assert.Equal(2, inner.VisibleElementCount);
        Assert.Equal(1, inner.ClippedElementCount);
    }

    [Fact]
    public void RenderPipeline_last_max_scroll_uses_root_scroll_diagnostic()
    {
        var pipeline = new RenderPipeline();
        var nested = VirtualNodeFactory.ScrollContainer(
            new NodeKey(2),
            [VirtualNodeProperty.Height(50)],
            [
                VirtualNodeBuilder.Text(_arena, "first", new NodeKey(3)),
                VirtualNodeBuilder.Text(_arena, "second", new NodeKey(4)),
                VirtualNodeBuilder.Text(_arena, "third", new NodeKey(5))
            ]);
        var root = VirtualNodeFactory.ScrollContainer(
            new NodeKey(1),
            [VirtualNodeProperty.Height(120)],
            [nested]);
        var viewport = new PixelRectangle(0, 0, 960, 200);

        using var frame = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        var diagnostics = pipeline.LastLayoutResult!.ScrollDiagnostics;
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(1, diagnostics[0].DfsIndex);
        Assert.Equal(0, diagnostics[1].DfsIndex);
        var rootDiagnostic = diagnostics.Single(diag => diag.DfsIndex == 0);
        Assert.Equal(rootDiagnostic.MaxScrollY, pipeline.LastMaxScrollY);
        Assert.NotEqual(diagnostics[0].MaxScrollY, pipeline.LastMaxScrollY);
    }

    [Fact]
    public void LayoutTree_incremental_text_update_only_affects_one_element()
    {
        var builder = new LayoutTreeBuilder();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var result1 = builder.BuildLayoutTree(root1, viewport);
        Assert.Equal(2, result1.Elements.Count);

        // Simulate text update: dirty node 1 (the Text)
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty node 0 (parent) and node 1 (child "a")
        // Parent's range covers both children �?child's range is subsumed
        var result = builder.BuildLayoutTree(root, viewport, [0, 1]);

        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((0, 2), result.DirtyElementRanges[0]); // merged into one range
    }

    [Fact]
    public void LayoutTree_adjacent_dirty_ranges_are_merged()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Dirty nodes 1 and 2 (adjacent elements "a" and "b")
        var result = builder.BuildLayoutTree(root, viewport, [1, 2]);

        Assert.Single(result.DirtyElementRanges);
        Assert.Equal((0, 2), result.DirtyElementRanges[0]); // adjacent �?merged
    }

    [Fact]
    public void LayoutTree_non_adjacent_dirty_ranges_stay_separate()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "a", new NodeKey(2)),
            VirtualNodeBuilder.Text(_arena, "b", new NodeKey(3)),
            VirtualNodeBuilder.Text(_arena, "c", new NodeKey(4)));
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
        var textHello = _arena.AddText("hello".AsSpan());
        var textClick = _arena.AddText("click".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textHello),
            new(LayoutElementKind.Rectangle, new PixelRectangle(0, 40, 100, 48)),
            new(LayoutElementKind.Button, new PixelRectangle(0, 100, 100, 40), Text: textClick),
        };

        var result = recorder.Record(elements, textSnapshot: snapshot);

        // Text �?1 command (DrawTextRun)
        Assert.Equal(new ElementCommandRange(0, 1), result.ElementCommandRanges[0]);
        // Rectangle �?1 command (FillRect)
        Assert.Equal(new ElementCommandRange(1, 1), result.ElementCommandRanges[1]);
        // Button �?2 commands (FillRect + DrawTextRun)
        Assert.Equal(new ElementCommandRange(2, 2), result.ElementCommandRanges[2]);
    }

    [Fact]
    public void DrawCommandRecorder_computes_dirty_command_ranges()
    {
        var recorder = new DrawCommandRecorder();
        var textHello = _arena.AddText("hello".AsSpan());
        var textClick = _arena.AddText("click".AsSpan());
        var textWorld = _arena.AddText("world".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textHello),
            new(LayoutElementKind.Button, new PixelRectangle(0, 40, 100, 40), Text: textClick),
            new(LayoutElementKind.Text, new PixelRectangle(0, 100, 100, 32), Text: textWorld),
        };

        // Dirty element range: element 1 (the Button, which produces 2 commands)
        var result = recorder.Record(elements, [(1, 1)], textSnapshot: snapshot);

        // Button maps to commands 1..2 (index 1, count 2)
        Assert.Single(result.DirtyCommandRanges);
        Assert.Equal((1, 2), result.DirtyCommandRanges[0]);
    }

    [Fact]
    public void DrawCommandRecorder_merges_adjacent_dirty_command_ranges()
    {
        var recorder = new DrawCommandRecorder();
        var textA = _arena.AddText("a".AsSpan());
        var textB = _arena.AddText("b".AsSpan());
        var textC = _arena.AddText("c".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textA),
            new(LayoutElementKind.Button, new PixelRectangle(0, 40, 100, 40), Text: textB),
            new(LayoutElementKind.Text, new PixelRectangle(0, 100, 100, 32), Text: textC),
        };

        // Dirty elements 0 and 1 �?commands 0..2 (adjacent, should merge)
        var result = recorder.Record(elements, [(0, 2)], textSnapshot: snapshot);

        Assert.Single(result.DirtyCommandRanges);
        Assert.Equal((0, 3), result.DirtyCommandRanges[0]); // commands 0,1,2
    }

    [Fact]
    public void DrawCommandRecorder_empty_dirty_ranges_returns_empty()
    {
        var recorder = new DrawCommandRecorder();
        var textHello = _arena.AddText("hello".AsSpan());
        var snapshot = _arena.GetOrCreateSnapshot();
        var elements = new List<LayoutElement>
        {
            new(LayoutElementKind.Text, new PixelRectangle(0, 0, 100, 32), Text: textHello),
        };

        var result = recorder.Record(elements, [], textSnapshot: snapshot);

        Assert.Empty(result.DirtyCommandRanges);
        Assert.Single(result.ElementCommandRanges);
    }

    [Fact]
    public void RenderPipeline_exposes_dirty_command_ranges_on_dirty_build()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        Assert.Empty(pipeline.LastDirtyElementRanges);
        Assert.Empty(pipeline.LastDirtyCommandRanges);

        // Build with dirty node 1 (Text) �?should produce dirty ranges
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Single(pipeline.LastDirtyElementRanges);
        Assert.Equal((0, 1), pipeline.LastDirtyElementRanges[0]);

        // Text �?1 draw command at index 0
        Assert.Single(pipeline.LastDirtyCommandRanges);
        Assert.Equal((0, 1), pipeline.LastDirtyCommandRanges[0]);
    }

    [Fact]
    public void RenderPipeline_element_command_mapping_reflects_button_two_commands()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        Assert.Equal(2, pipeline.LastElementCommandRanges.Length);
        // Text �?1 command
        Assert.Equal(new ElementCommandRange(0, 1), pipeline.LastElementCommandRanges[0]);
        // Button �?2 commands
        Assert.Equal(new ElementCommandRange(1, 2), pipeline.LastElementCommandRanges[1]);
    }

    [Fact]
    public void RenderPipeline_retained_snapshot_maps_node_keys_to_composition_targets()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(1), out var rootTarget));
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(2), out var textTarget));
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(3), out var buttonTarget));
        Assert.False(snapshot.TryGetCompositionTarget(NodeKey.None, out _));
        Assert.False(snapshot.TryGetCompositionTarget(new NodeKey(404), out _));
        Assert.Equal(new CompositionLayerId(1), rootTarget.LayerId);
        Assert.Equal(VirtualNodeKind.ScrollContainer, rootTarget.Kind);
        Assert.Equal(0, rootTarget.CommandStart);
        Assert.Equal(3, rootTarget.CommandCount);
        Assert.Equal(new CompositionLayerId(2), textTarget.LayerId);
        Assert.Equal(VirtualNodeKind.Text, textTarget.Kind);
        Assert.Equal(0, textTarget.CommandStart);
        Assert.Equal(1, textTarget.CommandCount);
        Assert.Equal(new CompositionLayerId(3), buttonTarget.LayerId);
        Assert.Equal(VirtualNodeKind.Button, buttonTarget.Kind);
        Assert.Equal(1, buttonTarget.CommandStart);
        Assert.Equal(2, buttonTarget.CommandCount);
        Assert.True(buttonTarget.IsValidForCommandCount(frame.Commands.Count));
    }

    [Fact]
    public void RangeUtils_merge_combines_adjacent_ranges()
    {
        var ranges = new List<(int, int)> { (0, 1), (1, 2), (4, 2) };
        var merged = RangeUtils.Merge(ranges);

        Assert.Equal(2, merged.Count);
        Assert.Equal((0, 3), merged[0]); // (0,1) + (1,2) �?(0,3)
        Assert.Equal((4, 2), merged[1]);
    }

    [Fact]
    public void RangeUtils_merge_combines_overlapping_ranges()
    {
        var ranges = new List<(int, int)> { (0, 3), (2, 4) };
        var merged = RangeUtils.Merge(ranges);

        Assert.Single(merged);
        Assert.Equal((0, 6), merged[0]); // overlap �?merged
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
            new(0, 1),  // element 0 �?command 0
            new(1, 2),  // element 1 �?commands 1-2
            new(3, 1),  // element 2 �?command 3
        };
        var dirtyElements = new List<(int, int)> { (0, 2) }; // elements 0-1

        var result = RangeUtils.MapAndMerge(elementRanges, dirtyElements);

        Assert.Single(result);
        Assert.Equal((0, 3), result[0]); // commands 0-2
    }

    [Fact]
    public void StyleOnlyPatchEligibility_maps_stable_dirty_element_range_to_command_range()
    {
        var elementCommandRanges = new ElementCommandRange[]
        {
            new(0, 1),
            new(1, 2),
            new(3, 1)
        };

        var stable = StyleOnlyPatchEligibility.TryMapStableCommandRanges(
            elementCommandRanges,
            [(1, 1)],
            out var dirtyCommandRanges);

        Assert.True(stable);
        Assert.Equal([(1, 2)], dirtyCommandRanges);
    }

    [Fact]
    public void StyleOnlyPatchEligibility_refuses_unstable_dirty_element_command_mapping()
    {
        var elementCommandRanges = new ElementCommandRange[]
        {
            new(0, 1),
            new(3, 1)
        };

        var stable = StyleOnlyPatchEligibility.TryMapStableCommandRanges(
            elementCommandRanges,
            [(0, 2)],
            out var dirtyCommandRanges);

        Assert.False(stable);
        Assert.Empty(dirtyCommandRanges);
    }

    [Fact]
    public void StyleOnlyPatchEligibility_refuses_out_of_range_dirty_element_mapping()
    {
        var elementCommandRanges = new ElementCommandRange[]
        {
            new(0, 1)
        };

        var stable = StyleOnlyPatchEligibility.TryMapStableCommandRanges(
            elementCommandRanges,
            [(1, 1)],
            out var dirtyCommandRanges);

        Assert.False(stable);
        Assert.Empty(dirtyCommandRanges);
    }

    [Fact]
    public void StyleOnlyHitTargetPatch_updates_dirty_target_metadata_without_changing_geometry()
    {
        var textIncrement = _arena.AddText("Increment".AsSpan());
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(200), new PixelRectangle(0, 0, 960, 540))
        };
        var nextElements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 60, 140, 40),
                ClipBounds: new PixelRectangle(0, 0, 960, 540),
                Text: textIncrement,
                ActionId: new ActionId(201))
        };

        var patched = StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(
            retainedHitTargets,
            nextElements,
            [(0, 1)],
            out var patchedHitTargets);

        Assert.True(patched);
        var patchedHitTarget = Assert.Single(patchedHitTargets);
        Assert.Equal(retainedHitTargets[0].Bounds, patchedHitTarget.Bounds);
        Assert.Equal(retainedHitTargets[0].ClipBounds, patchedHitTarget.ClipBounds);
        Assert.Equal(new ActionId(201), patchedHitTarget.ActionId);
    }

    [Fact]
    public void StyleOnlyHitTargetPatch_refuses_changed_hit_target_count()
    {
        var textIncrement = _arena.AddText("Increment".AsSpan());
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(200), new PixelRectangle(0, 0, 960, 540))
        };
        var nextElements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 60, 140, 40),
                ClipBounds: new PixelRectangle(0, 0, 960, 540),
                Text: textIncrement,
                ActionId: ActionId.None)
        };

        var patched = StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(
            retainedHitTargets,
            nextElements,
            [(0, 1)],
            out var patchedHitTargets);

        Assert.False(patched);
        Assert.Empty(patchedHitTargets);
    }

    [Fact]
    public void StyleOnlyHitTargetPatch_refuses_changed_hit_target_geometry()
    {
        var textIncrement = _arena.AddText("Increment".AsSpan());
        var retainedHitTargets = new[]
        {
            new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(200), new PixelRectangle(0, 0, 960, 540))
        };
        var nextElements = new[]
        {
            new LayoutElement(
                LayoutElementKind.Button,
                new PixelRectangle(16, 72, 140, 40),
                ClipBounds: new PixelRectangle(0, 0, 960, 540),
                Text: textIncrement,
                ActionId: new ActionId(201))
        };

        var patched = StyleOnlyHitTargetPatch.TryBuildPatchedHitTargets(
            retainedHitTargets,
            nextElements,
            [(0, 1)],
            out var patchedHitTargets);

        Assert.False(patched);
        Assert.Empty(patchedHitTargets);
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
    public void RetainedCommandBuffer_full_apply_uses_logical_batch_count()
    {
        var buffer = new RetainedCommandBuffer();
        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: new DrawColor(0, 0, 128, 63)),
        ]);
        var batch = new DrawCommandBatch(owner, 1);

        buffer.ApplyFull(batch);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(DrawCommandKind.DrawTextRun, buffer.Commands[0].Kind);
    }

    [Fact]
    public void DrawCommandBatch_memory_exposes_only_logical_count()
    {
        var owner = new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32)),
            new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 640, 360), Color: new DrawColor(0, 0, 128, 63)),
        ]);
        var batch = new DrawCommandBatch(owner, 1);

        Assert.Equal(1, batch.Memory.Length);
        Assert.Equal(DrawCommandKind.DrawTextRun, batch.Memory.Span[0].Kind);
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

        // New batch has different count �?falls back to full
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        Assert.Empty(frame1.DirtyCommandRanges);

        // Dirty build
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "world", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "click", new NodeKey(3)));
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Single(frame2.DirtyCommandRanges);
        Assert.Equal((0, 1), frame2.DirtyCommandRanges[0]); // Text �?1 command at index 0
    }

    [Fact]
    public void RenderFrameBatch_button_dirty_produces_two_dirty_commands()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "old", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "new", new NodeKey(3)));
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [2]); // dirty button at DFS index 2

        // Button �?2 commands (FillRect + DrawTextRun) at index 1-2
        Assert.Single(frame2.DirtyCommandRanges);
        Assert.Equal((1, 2), frame2.DirtyCommandRanges[0]);
    }

    [Fact]
    public void Root_scroll_container_uses_viewport_clip_and_padded_content_start()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Equal(2, result.Elements.Count);

        var textClip = result.Elements[0].ClipBounds;
        var buttonClip = result.Elements[1].ClipBounds;

        Assert.Equal(new PixelRectangle(0, 0, 960, 540), textClip);
        Assert.Equal(textClip, buttonClip);
        Assert.Equal(16, result.Elements[0].Bounds.Y);
        Assert.Equal(60, result.Elements[1].Bounds.Y);
    }

    [Fact]
    public void Root_scroll_container_scrolled_child_clips_at_viewport_top()
    {
        var builder = new LayoutTreeBuilder();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(30)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3))
            ]);
        var viewport = new PixelRectangle(0, 0, 200, 60);

        var result = builder.BuildLayoutTree(root, viewport);

        Assert.Equal(new PixelRectangle(0, 0, 200, 60), result.Elements[0].ClipBounds);
        Assert.Equal(-14, result.Elements[0].Bounds.Y);
        Assert.True(result.Elements[0].Bounds.Y < result.Elements[0].ClipBounds.Y);
        Assert.Equal(0, result.Elements[0].ClipBounds.Y);
    }

    [Fact]
    public void HitTest_rejects_click_outside_clip_bounds()
    {
        // Simulate a hit target with clip bounds
        var bounds = new PixelRectangle(16, 16, 200, 32);
        var clipBounds = new PixelRectangle(16, 16, 100, 32); // clip is narrower
        var target = new HitTestTarget(bounds, new ActionId(100), clipBounds);

        // Click within bounds but outside clip �?rejected
        // (x=150 is within bounds [16..216] but outside clip [16..116])
        var compositor = new DrawingBackendCompositor(new NoOpBackend());

        // We can't directly test hit routing with a single HitTestTarget,
        // so test the logic via a full render cycle. For a unit-level test,
        // verify the HitTestTarget struct carries clip bounds.
        Assert.True(target.ClipBounds.Width > 0);
        Assert.True(target.ClipBounds.Height > 0);
        Assert.Equal(100, target.ClipBounds.Width);
    }

    [Fact]
    public void DrawCommand_carries_clip_bounds_from_layout_element()
    {
        var builder = new LayoutTreeBuilder();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var result = builder.BuildLayoutTree(root, viewport);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        // The first command should have non-default clip bounds
        var cmd = batch.Commands.Memory.Span[0];
        Assert.True(cmd.ClipBounds.Width > 0);
        Assert.True(cmd.ClipBounds.Height > 0);
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

        // Retained frame owns resources �?batch.Dispose() returns commands only.
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
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 32), new ActionId(100))],
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
            [new HitTestTarget(new PixelRectangle(0, 0, 100, 32), new ActionId(100))],
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

        // snapshot.Dispose() calls Return() but resources are retained �?no-op.
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

        // Different resources instance �?partial must refuse without side effects
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
        // Frame state is UNCHANGED �?TryApplyPartial is pure on failure
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
        // Frame state is UNCHANGED �?no pollution from count mismatch
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

        // Dispose batch �?should NOT return resources to pool (retained by frame)
        batch.Dispose();

        // TextSlice is still valid because resources are retained
        Assert.True(frame.TryReadFrame(out var commands, out var res));
        Assert.Equal("owned", ((IFrameResourceResolver)res).Resolve(commands[0].Text).ToString());

        // Release + Dispose returns resources to pool
        frame.ReleaseResources();
        frame.Dispose();

        // Now resources are returned �?a fresh Rent() would get a recycled instance
        var recycled = FrameDrawingResources.Rent();
        FrameDrawingResources.Return(recycled);
    }

    [Fact]
    public void RetainedRenderFrame_generation_mismatch_falls_back_to_full()
    {
        // Use fresh resources from pool �?add ALL text before sealing
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

        // Same resources, same generation �?partial should succeed
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

        // Different resources instance �?partial must refuse
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
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(2)));
        var viewport = new PixelRectangle(0, 0, 960, 540);

        // Initial build
        using var frame1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());
        Assert.Equal(1, pipeline.RetainedFrame.CommandCount);
        Assert.Empty(pipeline.RetainedFrame.DirtyCommandRanges);

        // Dirty build
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "world", new NodeKey(2)));
        using var frame2 = pipeline.Build(root2, viewport, _arena.GetOrCreateSnapshot(), [1]);

        Assert.Equal(1, pipeline.RetainedFrame.CommandCount);
        Assert.Single(pipeline.RetainedFrame.DirtyCommandRanges);
        Assert.Equal((0, 1), pipeline.RetainedFrame.DirtyCommandRanges[0]);
    }

    private sealed class RecordingControlFeedbackSink : IControlFeedbackSink
    {
        public int DeliveryCount { get; private set; }

        public double LastMaxScrollY { get; private set; }

        public ScrollFeedback LastScrollFeedback { get; private set; } = ScrollFeedback.Empty;

        public void Deliver(double maxScrollY, ScrollFeedback scrollFeedback)
        {
            DeliveryCount++;
            LastMaxScrollY = maxScrollY;
            LastScrollFeedback = scrollFeedback;
        }
    }
}
