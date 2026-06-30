using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

[Trait("Category", "Guard")]
public class TypedIdAllocationGuardTests
{
    private readonly VirtualTextArena _arena = new();

    [Fact]
    public void Render_pipeline_hot_path_does_not_allocate_string_collections()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var root = new VirtualNode(
            VirtualNodeKind.Container,
            key: 1,
            children:
            [
                VirtualNodeTestBuilder.Button(_arena, "Click", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1))),
                VirtualNodeTestBuilder.Button(_arena, "Other", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(2))),
                .. Enumerable.Range(0, 20).Select(i => VirtualNodeBuilder.Text(_arena, $"Row {i}", new NodeKey((uint)(10 + i))))
            ]);

        pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        var snapshot1 = pipeline.Build(root, viewport, _arena.GetOrCreateSnapshot());

        Assert.NotEmpty(snapshot1.HitTargets);
        Assert.Equal(new ActionId(1), snapshot1.HitTargets[0].ActionId);
        Assert.Equal(new ActionId(2), snapshot1.HitTargets[1].ActionId);
    }

    [Fact]
    public void HitTestTarget_uses_typed_ActionId()
    {
        var target = new HitTestTarget(new PixelRectangle(0, 0, 100, 40), new ActionId(42));

        Assert.Equal(new ActionId(42), target.ActionId);
        Assert.False(target.ActionId.IsNone);
    }

    [Fact]
    public void LayoutElement_uses_typed_ActionId()
    {
        var element = new LayoutElement(LayoutElementKind.Button, new PixelRectangle(0, 0, 100, 40), ActionId: new ActionId(7));

        Assert.Equal(new ActionId(7), element.ActionId);
        Assert.False(element.ActionId.IsNone);
    }

    [Fact]
    public void Draw_command_record_result_is_value_publication_not_heap_identity()
    {
        var root = FindRepoRoot();
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));

        Assert.True(typeof(DrawCommandRecordResult).IsValueType);
        Assert.Contains("internal readonly struct DrawCommandRecordResult", layoutModelSource);
        Assert.DoesNotContain("internal sealed class DrawCommandRecordResult", layoutModelSource);
    }

    [Fact]
    public void IndexRangeList_is_value_publication_for_dirty_ranges()
    {
        var root = FindRepoRoot();
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));

        Assert.True(typeof(IndexRangeList).IsValueType);
        Assert.Contains("internal readonly struct IndexRangeList", layoutModelSource);
        Assert.Contains("public static IndexRangeList Empty => default", layoutModelSource);
        Assert.DoesNotContain("internal sealed class IndexRangeList", layoutModelSource);

        IndexRangeList empty = [];
        IndexRangeList single = [(4, 2)];

        Assert.Empty(empty);
        Assert.Single(single);
        Assert.Equal((4, 2), single[0]);
    }

    [Fact]
    public void ElementCommandRangeList_is_value_publication_for_element_command_ranges()
    {
        var root = FindRepoRoot();
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));
        var recorderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "DrawCommandRecorder.cs"));

        Assert.True(typeof(ElementCommandRangeList).IsValueType);
        Assert.Contains("internal readonly struct ElementCommandRangeList", layoutModelSource);
        Assert.Contains("private const int InlineCapacity = 4", layoutModelSource);
        Assert.DoesNotContain("ElementCommandRange[] ElementCommandRanges { get; }", layoutModelSource);
        Assert.Contains("ElementCommandRangeList.CopyFrom(stackRanges)", recorderSource);
        Assert.DoesNotContain("stackRanges.ToArray()", recorderSource);

        ElementCommandRangeList ranges =
        [
            new(0, 1),
            new(1, 2),
            new(3, 1),
            new(4, 1)
        ];

        Assert.Equal(4, ranges.Count);
        Assert.Equal(new ElementCommandRange(1, 2), ranges[1]);
    }

    [Fact]
    public void ScrollContainerDiagList_is_value_publication_for_scroll_diagnostics()
    {
        var root = FindRepoRoot();
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));
        var layoutBuilderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutTreeBuilder.cs"));

        Assert.True(typeof(ScrollContainerDiagList).IsValueType);
        Assert.Contains("internal readonly struct ScrollContainerDiagList", layoutModelSource);
        Assert.DoesNotContain("IReadOnlyList<ScrollContainerDiag>? _scrollDiagnostics", layoutModelSource);
        Assert.Contains("ScrollContainerDiagList.CopyFrom(_scrollDiags.Written)", layoutBuilderSource);
        Assert.DoesNotContain("ScrollDiagnosticsToArray()", layoutBuilderSource);

        ScrollContainerDiagList diagnostics =
        [
            new(0, 100, 160, 12, 60, 3, 1)
        ];

        Assert.Single(diagnostics);
        Assert.Equal(12, diagnostics[0].ScrollY);
    }

    [Fact]
    public void Render_pipeline_retained_input_snapshot_is_value_publication_not_heap_identity()
    {
        var root = FindRepoRoot();
        var renderPipelineSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RenderPipeline.cs"));
        var renderFrameBatchSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RenderFrameBatch.cs"));
        var retainedFrameSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RetainedRenderFrame.cs"));

        Assert.True(typeof(RenderPipelineRetainedInputSnapshot).IsValueType);
        Assert.True(typeof(HitTargetList).IsValueType);
        Assert.Contains("internal readonly struct RenderPipelineRetainedInputSnapshot", renderPipelineSource);
        Assert.Contains("public readonly struct HitTargetList", renderFrameBatchSource);
        Assert.Contains("HitTargetList HitTargets", renderPipelineSource);
        Assert.Contains("internal static HitTargetList FromOwnedArray", renderFrameBatchSource);
        Assert.Contains("public bool HasLastRetainedInputSnapshot", renderPipelineSource);
        Assert.DoesNotContain("internal sealed record RenderPipelineRetainedInputSnapshot", renderPipelineSource);
        Assert.DoesNotContain("HitTargetPublication", renderFrameBatchSource);
        Assert.DoesNotContain("public static HitTargetList FromOwnedArray", renderFrameBatchSource);
        Assert.DoesNotContain("private HitTestTarget[] _hitTargets", retainedFrameSource);
    }

    [Fact]
    public void ActionId_None_is_default_value()
    {
        var defaultId = default(ActionId);

        Assert.Equal(ActionId.None, defaultId);
        Assert.True(defaultId.IsNone);
        Assert.Equal(0u, defaultId.Value);
    }

    [Fact]
    public void VirtualNodeProperty_uses_typed_key()
    {
        var property = VirtualNodeProperty.Action(new ActionId(5));

        Assert.Equal(VirtualPropertyKey.ActionId, property.Key);
        Assert.Equal(PropertyValueKind.ActionId, property.Value.Kind);
        Assert.Equal(new ActionId(5), property.Value.GetRequiredActionId());
    }

    [Fact]
    public void VirtualNodeProperty_helpers_use_unified_typed_properties()
    {
        Assert.Equal(VirtualPropertyKey.Width, VirtualNodeProperty.Width(12).Key);
        Assert.Equal(VirtualPropertyKey.Height, VirtualNodeProperty.Height(12).Key);
        Assert.Equal(VirtualPropertyKey.ScrollY, VirtualNodeProperty.ScrollY(12).Key);
        Assert.Equal(VirtualPropertyKey.ActionId, VirtualNodeProperty.Action(new ActionId(1)).Key);
        Assert.Equal(VirtualPropertyKey.IsHovered, VirtualNodeProperty.Hovered(true).Key);
        Assert.Equal(VirtualPropertyKey.IsPressed, VirtualNodeProperty.Pressed(true).Key);
        Assert.Equal(VirtualPropertyKey.IsFocused, VirtualNodeProperty.Focused(true).Key);
        Assert.Equal(VirtualPropertyKey.Background, VirtualNodeProperty.Background(Color.FromSrgb(1, 2, 3)).Key);
        Assert.Equal(VirtualPropertyKey.Border, VirtualNodeProperty.Border(Color.FromSrgb(4, 5, 6), 2f).Key);
        Assert.Equal(VirtualPropertyKey.ForegroundColor, VirtualNodeProperty.ForegroundColor(StyleColor.Opaque(4, 5, 6)).Key);
        Assert.Equal(VirtualPropertyKey.LayerOpacity, VirtualNodeProperty.LayerOpacity(0.5).Key);
        Assert.Equal(VirtualPropertyKey.TranslateX, VirtualNodeProperty.TranslateX(12).Key);
        Assert.Equal(VirtualPropertyKey.TranslateY, VirtualNodeProperty.TranslateY(24).Key);
    }

    [Fact]
    public void VirtualNodePropertyListBuilder_writes_to_caller_span_and_rejects_duplicates()
    {
        Span<VirtualNodeProperty> storage = stackalloc VirtualNodeProperty[4];
        var builder = new VirtualNodePropertyListBuilder(storage);

        builder.AddWidth(120);
        builder.AddHeight(48);
        builder.AddAction(new ActionId(9));

        Assert.Equal(3, builder.Count);
        Assert.Equal(VirtualPropertyKey.Width, builder.Written[0].Key);
        Assert.Equal(VirtualPropertyKey.Height, builder.Written[1].Key);
        Assert.Equal(VirtualPropertyKey.ActionId, builder.Written[2].Key);
        try
        {
            builder.AddWidth(160);
            throw new Xunit.Sdk.XunitException("Expected duplicate width to throw.");
        }
        catch (ArgumentException)
        {
        }
    }

    [Fact]
    public void VirtualNodeFactory_span_overload_freezes_property_snapshot_once()
    {
        Span<VirtualNodeProperty> storage = stackalloc VirtualNodeProperty[2];
        var builder = new VirtualNodePropertyListBuilder(storage);
        builder.AddWidth(120);
        builder.AddHeight(48);

        var node = VirtualNodeFactory.Rectangle(new NodeKey(7), builder.Written);
        storage[0] = VirtualNodeProperty.Width(999);

        Assert.Equal(120, node.Properties[0].Value.GetRequiredNumber());
        Assert.Equal(48, node.Properties[1].Value.GetRequiredNumber());
    }

    [Fact]
    public void VirtualNodeFactory_control_metadata_property_publication_does_not_allocate()
    {
        _ = BuildActionPropertyNode(1);
        _ = BuildControlBundlePropertyNode(1);

        const int iterations = 1_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var actionNode = default(VirtualNode);
        var bundleNode = default(VirtualNode);
        for (var i = 0; i < iterations; i++)
        {
            actionNode = BuildActionPropertyNode(i + 1);
            bundleNode = BuildControlBundlePropertyNode(i + 1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(actionNode);
        GC.KeepAlive(bundleNode);
        Assert.Equal(0, allocated);
    }

    private static VirtualNode BuildActionPropertyNode(int index)
    {
        Span<VirtualNodeProperty> storage = stackalloc VirtualNodeProperty[1];
        var builder = new VirtualNodePropertyListBuilder(storage);
        builder.AddAction(new ActionId((uint)index));
        return VirtualNodeFactory.Container(new NodeKey((uint)index), builder.Written, ReadOnlySpan<VirtualNode>.Empty);
    }

    private static VirtualNode BuildControlBundlePropertyNode(int index)
    {
        Span<VirtualNodeProperty> storage = stackalloc VirtualNodeProperty[4];
        ButtonPropertyBundle.Write(
            new ActionId((uint)index),
            new ControlVisualState(IsHovered: (index & 1) == 0, IsPressed: (index & 2) == 0, IsFocused: (index & 4) == 0),
            storage);
        return VirtualNodeFactory.Container(new NodeKey((uint)(index + 1_000)), storage, ReadOnlySpan<VirtualNode>.Empty);
    }

    [Fact]
    public void VirtualNodeChildrenBuilder_handles_inline_and_overflow_children()
    {
        var children = new VirtualNodeChildrenBuilder();
        for (var i = 0; i < 6; i++)
        {
            children.Add(VirtualNodeBuilder.Text(_arena, $"child {i}", new NodeKey((uint)(10 + i))));
        }

        var root = VirtualNodeFactory.Container(new NodeKey(1), ReadOnlySpan<VirtualNodeProperty>.Empty, ref children);

        Assert.Equal(6, root.Children.Length);
        Assert.Equal(new NodeKey(10), root.Children[0].Key);
        Assert.Equal(new NodeKey(15), root.Children[5].Key);
    }

    [Fact]
    public void VirtualNodeChildrenBuilder_default_value_is_usable()
    {
        VirtualNodeChildrenBuilder children = default;
        children.Add(VirtualNodeBuilder.Text(_arena, "child", new NodeKey(10)));

        var root = VirtualNodeFactory.Container(new NodeKey(1), ReadOnlySpan<VirtualNodeProperty>.Empty, ref children);

        Assert.Equal(1, root.Children.Length);
        Assert.Equal(new NodeKey(10), root.Children[0].Key);
    }

    [Fact]
    public void VirtualNode_Key_is_NodeKey()
    {
        var node = VirtualNodeBuilder.Text(_arena, "hello", new NodeKey(42));

        Assert.Equal(new NodeKey(42), node.Key);
    }

    [Fact]
    public void DrawingBackendCompositor_TryGetActionIdAtPhysicalPixel_returns_typed_ActionId()
    {
        var backend = new NullBackend();
        var compositor = new DrawingBackendCompositor(backend);

        var result = compositor.TryGetActionIdAtPhysicalPixel(0, 0, out var actionId);

        Assert.False(result);
        Assert.Equal(ActionId.None, actionId);
    }

    // ── R13-24: No managed refs in core IR types ──────────────────

    [Fact]
    public void ActionId_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ActionId>());
        Assert.True(Unsafe.SizeOf<ActionId>() == 4);
    }

    [Fact]
    public void NodeKey_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<NodeKey>());
        Assert.True(Unsafe.SizeOf<NodeKey>() == 4);
    }

    [Fact]
    public void TextContentResource_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextContentResource>());
    }

    [Fact]
    public void TextBufferId_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextBufferId>());
        Assert.True(Unsafe.SizeOf<TextBufferId>() == 4);
    }

    [Fact]
    public void TextRange_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextRange>());
    }

    [Fact]
    public void ContentResource_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ContentResource>());
        Assert.Equal(24, Unsafe.SizeOf<ContentResource>());
    }

    [Fact]
    public void PropertyValue_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PropertyValue>());
        Assert.Equal(44, Unsafe.SizeOf<PropertyValue>());
    }

    [Fact]
    public void Color_has_no_managed_references_and_uses_16_byte_storage()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Color>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SrgbColor>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Paint>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<BorderStroke>());
        Assert.Equal(16, Unsafe.SizeOf<Color>());
        Assert.Equal(4, Unsafe.SizeOf<SrgbColor>());
        Assert.Equal(36, Unsafe.SizeOf<Paint>());
        Assert.Equal(40, Unsafe.SizeOf<BorderStroke>());
    }

    [Fact]
    public void DrawCommand_color_payload_has_no_managed_references_and_uses_canonical_storage()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DrawCommand>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DrawPayloadColor>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DrawMaterial>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<ColorOutputMapping>());

        var command = DrawCommand.FromCanonicalColor(
            DrawCommandKind.FillRect,
            Color: Color.FromSrgb(255, 0, 0));
        var materialCommand = DrawCommand.FromMaterial(
            DrawCommandKind.FillRect,
            Material: DrawMaterial.SolidColor(Color.FromSrgb(255, 0, 0)));
        var outputMapping = ColorOutputMapping.SdrSrgb;

        Assert.InRange(command.CanonicalColor.LinearBt2020R, 0.6273f, 0.6275f);
        Assert.NotEqual(1, command.CanonicalColor.LinearBt2020R);
        Assert.Equal(DrawColor.Opaque(255, 0, 0), command.Color);
        Assert.Equal(DrawColor.Opaque(255, 0, 0), command.ToSdrColor());
        Assert.Equal(DrawMaterialKind.SolidColor, command.Material.Kind);
        Assert.Equal(command.CanonicalColor, command.Material.Color);
        Assert.Equal(command, materialCommand);
        Assert.Equal(DrawColor.Opaque(255, 0, 0), outputMapping.MapToSdr(command));
        Assert.Equal(DrawColor.Opaque(255, 0, 0), outputMapping.MapToSdr(command.CanonicalColor));
        Assert.Equal(ColorOutputKind.SdrSrgb, outputMapping.Kind);
    }

    [Fact]
    public void DrawCommand_recorder_uses_typed_capacity_owner_not_generic_array_pooling()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DrawCommand>());

        var root = FindRepoRoot();
        var drawingDir = Path.Combine(root, "src", "Irix.Drawing");
        var renderingDir = Path.Combine(root, "src", "Irix.Rendering");
        var batchSource = File.ReadAllText(Path.Combine(drawingDir, "DrawCommandBatch.cs"));
        var ownerSource = File.ReadAllText(Path.Combine(drawingDir, "PooledDrawCommandMemoryOwner.cs"));
        var recorderSource = File.ReadAllText(Path.Combine(renderingDir, "DrawCommandRecorder.cs"));
        var drawingRenderingSource = string.Concat(
            Directory.GetFiles(drawingDir, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(renderingDir, "*.cs", SearchOption.AllDirectories))
                .Select(File.ReadAllText));

        Assert.Contains("internal sealed class PooledDrawCommandMemoryOwner", ownerSource);
        Assert.Contains("IMemoryOwner<DrawCommand>", ownerSource);
        Assert.Contains("Queue<PooledDrawCommandMemoryOwner>", ownerSource);
        Assert.Contains("ArrayPool<DrawCommand>.Shared.Rent(minimumLength)", ownerSource);
        Assert.Contains("ReturnOwner(this)", ownerSource);
        Assert.Contains("owner.ReleaseStorage()", ownerSource);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences<DrawCommand>()", ownerSource);
        Assert.Contains("PooledDrawCommandMemoryOwner pooledOwner", batchSource);
        Assert.Contains("internal static DrawCommandBatch FromPooled(PooledDrawCommandMemoryOwner owner, int count)", batchSource);
        Assert.Contains("PooledDrawCommandMemoryOwner.Rent(stackCommandCount)", recorderSource);
        Assert.Contains("PooledDrawCommandMemoryOwner.Rent(maximumCommandCount)", recorderSource);
        Assert.DoesNotContain("PooledArrayMemoryOwner", drawingRenderingSource);
        Assert.False(File.Exists(Path.Combine(drawingDir, "PooledArrayMemoryOwner.cs")));
    }

    [Fact]
    public void Internal_style_value_slots_have_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleColor>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleColorSlot>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PaintSlot>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<BorderStrokeSlot>());
    }

    [Fact]
    public void VirtualPropertyKey_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<VirtualPropertyKey>());
    }

    [Fact]
    public void TextStyle_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextStyle>());
    }

    [Fact]
    public void WindowContentElement_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowContentElement>());
    }

    [Fact]
    public void VirtualPropertyKey_does_not_define_primitive_ToString_formatter()
    {
        var toString = typeof(VirtualPropertyKey).GetMethod(nameof(ToString), Type.EmptyTypes);

        Assert.NotEqual(typeof(VirtualPropertyKey), toString?.DeclaringType);
    }

    [Fact]
    public void VirtualPropertyKey_has_no_public_constructor()
    {
        var publicConstructors = typeof(VirtualPropertyKey).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void VirtualNodeKind_default_is_None()
    {
        Assert.Equal(VirtualNodeKind.None, default);
        Assert.Equal(VirtualNodeKind.None, new VirtualNode().Kind);
    }

    [Fact]
    public void VirtualNodeKind_None_shape_must_be_empty()
    {
        var label = VirtualNodeBuilder.Text(_arena, "label", new NodeKey(11));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.None,
            key: new NodeKey(1)));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.None,
            content: label.Content));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.None,
            properties: [VirtualNodeProperty.Width(10)]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.None,
            children: [label]));
    }

    [Fact]
    public void PropertyChangeSet_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PropertyChangeSet>());
    }

    [Fact]
    public void Internal_style_delta_plan_is_value_typed()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StylePropertyId>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleValue>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleDeclaration>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleDeltaPlan>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleDeltaWork>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<StyleTransitionState>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<StyleTransitionCompileRequest>());
        Assert.True(RuntimeHelpers.IsReferenceOrContainsReferences<StyleTransitionCompileResult>());
    }

    [Fact]
    public void Every_public_virtual_property_key_has_metadata()
    {
        foreach (var field in GetPublicVirtualPropertyKeyFields())
        {
            var key = (VirtualPropertyKey)field.GetValue(null)!;

            Assert.True(VirtualPropertyMetadata.TryGet(key, out var metadata), $"Missing metadata for {field.Name}");
            Assert.Equal(key, metadata.Key);
            Assert.NotEqual(PropertyValueKind.None, metadata.ValueKind);
            Assert.NotEqual(StyleEffect.None, metadata.Effects);
            Assert.NotEqual(VirtualNodeKindFlags.None, metadata.SupportedNodeKinds);
        }
    }

    [Fact]
    public void Property_metadata_classifies_current_public_keys()
    {
        Assert.Equal(StyleEffect.Layout, VirtualPropertyMetadata.Get(VirtualPropertyKey.Width).Effects);
        Assert.Equal(StyleEffect.Layout, VirtualPropertyMetadata.Get(VirtualPropertyKey.Height).Effects);
        Assert.Equal(StyleEffect.Layout, VirtualPropertyMetadata.Get(VirtualPropertyKey.ScrollY).Effects);

        var action = VirtualPropertyMetadata.Get(VirtualPropertyKey.ActionId);
        Assert.Equal(PropertyValueKind.ActionId, action.ValueKind);
        Assert.Equal(StyleEffect.Interaction, action.Effects);
        Assert.Equal(AnimationChannel.None, action.AnimationChannel);

        var hovered = VirtualPropertyMetadata.Get(VirtualPropertyKey.IsHovered);
        Assert.Equal(PropertyValueKind.Boolean, hovered.ValueKind);
        Assert.True((hovered.Effects & StyleEffect.Interaction) != 0);
        Assert.True((hovered.Effects & StyleEffect.Visual) != 0);
        Assert.Equal(AnimationChannel.Discrete, hovered.AnimationChannel);

        var background = VirtualPropertyMetadata.Get(VirtualPropertyKey.Background);
        Assert.Equal(PropertyValueKind.Paint, background.ValueKind);
        Assert.Equal(StyleEffect.Visual, background.Effects);
        Assert.Equal(AnimationChannel.CpuStyle, background.AnimationChannel);

        var foreground = VirtualPropertyMetadata.Get(VirtualPropertyKey.ForegroundColor);
        Assert.Equal(PropertyValueKind.Color, foreground.ValueKind);
        Assert.Equal(StyleEffect.Visual, foreground.Effects);
        Assert.Equal(AnimationChannel.CpuStyle, foreground.AnimationChannel);

        var border = VirtualPropertyMetadata.Get(VirtualPropertyKey.Border);
        Assert.Equal(PropertyValueKind.BorderStroke, border.ValueKind);
        Assert.Equal(StyleEffect.Visual, border.Effects);
        Assert.Equal(AnimationChannel.CpuStyle, border.AnimationChannel);

        var opacity = VirtualPropertyMetadata.Get(VirtualPropertyKey.LayerOpacity);
        Assert.Equal(PropertyValueKind.Number, opacity.ValueKind);
        Assert.Equal(StyleEffect.Composite, opacity.Effects);
        Assert.Equal(AnimationChannel.Composite, opacity.AnimationChannel);

        var translateX = VirtualPropertyMetadata.Get(VirtualPropertyKey.TranslateX);
        Assert.Equal(PropertyValueKind.Number, translateX.ValueKind);
        Assert.Equal(StyleEffect.Composite, translateX.Effects);
        Assert.Equal(AnimationChannel.Composite, translateX.AnimationChannel);
    }

    [Fact]
    public void Property_change_classification_uses_metadata_effects()
    {
        Assert.Equal(InvalidationKind.Layout, PropertyChangeSet.AddKey(default, VirtualPropertyKey.Width).ClassifySet());
        Assert.Equal(InvalidationKind.Layout, PropertyChangeSet.AddKey(default, VirtualPropertyKey.ScrollY).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.ActionId).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.IsHovered).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.Background).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.Border).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.ForegroundColor).ClassifySet());
        Assert.Equal(InvalidationKind.CompositeOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.LayerOpacity).ClassifySet());
        Assert.Equal(InvalidationKind.CompositeOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.TranslateX).ClassifySet());

        var visualAndComposite = PropertyChangeSet.AddKey(default, VirtualPropertyKey.Background);
        visualAndComposite = PropertyChangeSet.AddKey(visualAndComposite, VirtualPropertyKey.LayerOpacity);
        Assert.Equal(InvalidationKind.VisualOnly, visualAndComposite.ClassifySet());
    }

    [Fact]
    public void Property_change_set_tracks_composition_separately_from_visual_draw_style()
    {
        var background = PropertyChangeSet.AddKey(default, VirtualPropertyKey.Background);
        Assert.Equal(1ul, background.VisualMask);
        Assert.Equal(0ul, background.CompositionMask);

        var translate = PropertyChangeSet.AddKey(default, VirtualPropertyKey.TranslateY);
        Assert.Equal(0ul, translate.VisualMask);
        Assert.Equal(4ul, translate.CompositionMask);

        var mixed = PropertyChangeSet.AddKey(background, VirtualPropertyKey.LayerOpacity);
        Assert.Equal(1ul, mixed.VisualMask);
        Assert.Equal(1ul, mixed.CompositionMask);
        Assert.Equal(StyleEffect.Visual | StyleEffect.Composite, mixed.Effects);
    }

    [Fact]
    public void Style_delta_planner_preserves_internal_execution_work_flags()
    {
        var visualPlan = StyleDeltaPlanner.Plan(PropertyChangeSet.AddKey(default, VirtualPropertyKey.ForegroundColor));
        Assert.True(visualPlan.CanReuseLayout);
        Assert.True(visualPlan.RequiresDrawUpdate);
        Assert.False(visualPlan.RequiresCompositionUpdate);
        Assert.Equal(InvalidationKind.VisualOnly, visualPlan.InvalidationKind);
        Assert.Equal(LayoutRebuildReason.StyleOnly, visualPlan.LayoutRebuildReason);

        var compositionPlan = StyleDeltaPlanner.Plan(PropertyChangeSet.AddKey(default, VirtualPropertyKey.TranslateX));
        Assert.True(compositionPlan.CanReuseLayout);
        Assert.False(compositionPlan.RequiresDrawUpdate);
        Assert.True(compositionPlan.RequiresCompositionUpdate);
        Assert.True(compositionPlan.IsCompositorOnlyTransitionCandidate);
        Assert.Equal(InvalidationKind.CompositeOnly, compositionPlan.InvalidationKind);
        Assert.Equal(LayoutRebuildReason.StyleOnly, compositionPlan.LayoutRebuildReason);

        var mixedPlan = StyleDeltaPlanner.Plan(
            StyleDeltaPlanner.BuildChangeSet(
                [VirtualNodeProperty.Background(Color.FromSrgb(1, 2, 3))],
                [
                    VirtualNodeProperty.Background(Color.FromSrgb(4, 5, 6)),
                    VirtualNodeProperty.LayerOpacity(0.75)
                ]));
        Assert.True(mixedPlan.CanReuseLayout);
        Assert.True(mixedPlan.RequiresDrawUpdate);
        Assert.True(mixedPlan.RequiresCompositionUpdate);
        Assert.False(mixedPlan.IsCompositorOnlyTransitionCandidate);
        Assert.Equal(InvalidationKind.VisualOnly, mixedPlan.InvalidationKind);
    }

    [Fact]
    public void Semantic_style_declarations_map_one_way_to_internal_properties()
    {
        var background = StyleDeclaration.Background(StyleColor.Opaque(1, 2, 3)).ToVirtualNodeProperty();
        var borderStroke = BorderStroke.Solid(Color.FromSrgb(7, 8, 9), 2);
        var border = StyleDeclaration.Border(borderStroke).ToVirtualNodeProperty();
        var foreground = StyleDeclaration.Foreground(StyleColor.Opaque(4, 5, 6)).ToVirtualNodeProperty();
        var opacity = StyleDeclaration.Opacity(0.75).ToVirtualNodeProperty();
        var translationX = StyleDeclaration.TranslationX(12).ToVirtualNodeProperty();
        var translationY = StyleDeclaration.TranslationY(24).ToVirtualNodeProperty();
        var hovered = StyleDeclaration.Hovered(true).ToVirtualNodeProperty();

        Assert.Equal(VirtualPropertyKey.Background, background.Key);
        Assert.True(background.Value.GetRequiredPaint().TryGetSolidColor(out var backgroundColor));
        Assert.Equal(StyleColor.Opaque(1, 2, 3).Value, backgroundColor);
        Assert.Equal(VirtualPropertyKey.Border, border.Key);
        Assert.Equal(borderStroke, border.Value.GetRequiredBorderStroke());
        Assert.Equal(VirtualPropertyKey.ForegroundColor, foreground.Key);
        Assert.Equal(StyleColor.Opaque(4, 5, 6), foreground.Value.GetRequiredColor());
        Assert.Equal(VirtualPropertyKey.LayerOpacity, opacity.Key);
        Assert.Equal(0.75, opacity.Value.GetRequiredNumber());
        Assert.Equal(VirtualPropertyKey.TranslateX, translationX.Key);
        Assert.Equal(12, translationX.Value.GetRequiredNumber());
        Assert.Equal(VirtualPropertyKey.TranslateY, translationY.Key);
        Assert.Equal(24, translationY.Value.GetRequiredNumber());
        Assert.Equal(VirtualPropertyKey.IsHovered, hovered.Key);
        Assert.True(hovered.Value.GetRequiredBoolean());
    }

    [Fact]
    public void Semantic_style_declaration_collection_maps_to_node_properties()
    {
        var properties = StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.Width(100),
            StyleDeclaration.Height(48),
            StyleDeclaration.Background(StyleColor.Opaque(20, 30, 40)),
            StyleDeclaration.Opacity(0.5)
        ]);

        Assert.Equal(4, properties.Length);
        Assert.Equal(VirtualPropertyKey.Width, properties[0].Key);
        Assert.Equal(VirtualPropertyKey.Height, properties[1].Key);
        Assert.Equal(VirtualPropertyKey.Background, properties[2].Key);
        Assert.Equal(VirtualPropertyKey.LayerOpacity, properties[3].Key);

        var node = VirtualNodeFactory.Rectangle(new NodeKey(7), properties);
        properties[0] = VirtualNodeProperty.Width(999);

        Assert.Equal(100, node.Properties[0].Value.GetRequiredNumber());
    }

    [Fact]
    public void Semantic_style_declaration_collection_rejects_duplicate_properties()
    {
        Assert.Throws<ArgumentException>(() => StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.Width(100),
            StyleDeclaration.Width(120)
        ]));
    }

    [Fact]
    public void Semantic_style_values_use_canonical_color_storage()
    {
        var declaration = StyleDeclaration.Background(StyleColor.Opaque(255, 0, 0));

        Assert.Equal(PropertyValueKind.Paint, declaration.Value.Kind);
        Assert.True(declaration.Value.GetRequiredPaint().TryGetSolidColor(out var color));
        Assert.InRange(color.LinearBt2020R, 0.6273f, 0.6275f);
        Assert.NotEqual(1, color.LinearBt2020R);
        Assert.Equal(SrgbColor.Opaque(255, 0, 0), color.ToSrgb());
    }

    [Fact]
    public void Semantic_style_declaration_rejects_mismatched_value_kind()
    {
        Assert.Throws<ArgumentException>(() => StyleDeclaration.Create(StylePropertyId.Background, StyleValue.FromNumber(1)));
        Assert.Throws<ArgumentException>(() => StyleDeclaration.Create(StylePropertyId.Border, StyleValue.FromPaint(Paint.Solid(Color.FromSrgb(1, 2, 3)))));
        Assert.Throws<ArgumentException>(() => StyleDeclaration.Create(StylePropertyId.Opacity, StyleValue.FromBoolean(true)));
        Assert.Throws<ArgumentException>(() => StyleDeclaration.Create(StylePropertyId.Hovered, StyleValue.FromColor(StyleColor.Opaque(1, 2, 3))));
        Assert.Throws<ArgumentOutOfRangeException>(() => StyleDeclaration.Create(StylePropertyId.None, StyleValue.None));
    }

    [Fact]
    public void VirtualNodeProperty_constructor_is_private()
    {
        var constructors = typeof(VirtualNodeProperty)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate));
    }

    [Fact]
    public void VirtualNode_rejects_duplicate_properties()
    {
        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Container,
            properties:
            [
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Width(120)
            ]));
    }

    [Fact]
    public void VirtualNode_rejects_unsupported_properties_for_node_kind()
    {
        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Content,
            properties: [VirtualNodeProperty.Action(new ActionId(1))]));

        Assert.Throws<ArgumentException>(() => VirtualNodeBuilder.Text(
            _arena,
            "unsupported",
            new NodeKey(88),
            VirtualNodeProperty.ScrollY(10)));
    }

    [Fact]
    public void VirtualNode_container_shape_allows_children_but_rejects_content()
    {
        var label = VirtualNodeBuilder.Text(_arena, "label", new NodeKey(11));
        var container = new VirtualNode(
            VirtualNodeKind.Container,
            children:
            [
                VirtualNodeFactory.Rectangle(),
                label
            ]);

        Assert.Equal(VirtualNodeKind.Container, container.Kind);
        Assert.Equal(2, container.Children.Length);
        Assert.All(container.Children.ToArray(), child => Assert.Equal(VirtualNodeKind.Content, child.Kind));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Container,
            content: label.Content));
    }

    [Fact]
    public void VirtualNode_leaf_shapes_reject_children()
    {
        var label = VirtualNodeBuilder.Text(_arena, "label", new NodeKey(11));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Content,
            content: label.Content,
            children: [VirtualNodeFactory.Rectangle()]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Content,
            children: [label]));
    }

    [Fact]
    public void VirtualNode_content_contract_matches_node_kind()
    {
        var textContent = _arena.AddText("text".AsSpan());
        var textNode = new VirtualNode(VirtualNodeKind.Content, content: ContentResource.FromText(textContent));
        Assert.Equal(ContentResourceKind.Text, textNode.Content.Kind);

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Content,
            content: ContentResource.None));

        var rectangleNode = new VirtualNode(VirtualNodeKind.Content, content: ContentResource.Rectangle);
        Assert.Equal(ContentResourceKind.Rectangle, rectangleNode.Content.Kind);

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Container,
            content: ContentResource.FromText(textContent),
            children: [VirtualNodeFactory.Text(textContent)]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Container,
            content: ContentResource.FromText(textContent)));
    }

    [Fact]
    public void VirtualNodePropertySupport_declares_control_support_sets()
    {
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.Width));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.Height));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.ScrollY));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.ActionId));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.IsHovered));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.IsPressed));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.IsFocused));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.Background));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.ForegroundColor));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Container, VirtualPropertyKey.Border));

        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.Width));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.Height));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.Background));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.ForegroundColor));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.Border));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.ActionId));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.ScrollY));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Content, VirtualPropertyKey.IsHovered));
    }

    [Fact]
    public void ActionId_value_type_semantics()
    {
        var a = new ActionId(42);
        var b = new ActionId(42);
        var c = new ActionId(99);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void ActionId_does_not_define_primitive_ToString_formatter()
    {
        var toString = typeof(ActionId).GetMethod(nameof(ToString), Type.EmptyTypes);

        Assert.NotEqual(typeof(ActionId), toString?.DeclaringType);
    }

    [Fact]
    public void ContentResource_Rectangle_roundtrip()
    {
        var nc = ContentResource.Rectangle;
        Assert.Equal(ContentResourceKind.Rectangle, nc.Kind);
        Assert.False(nc.TryGetText(out _));
    }

    [Fact]
    public void ContentResource_Text_roundtrip()
    {
        var arena = new VirtualTextArena();
        var textContent = arena.AddText("hello".AsSpan());
        var nc = ContentResource.FromText(textContent);
        Assert.Equal(ContentResourceKind.Text, nc.Kind);
        Assert.True(nc.TryGetText(out var resolved));
        Assert.Equal(textContent, resolved);
    }

    [Fact]
    public void PropertyValue_Number_roundtrip()
    {
        var av = PropertyValue.FromNumber(42.5);
        Assert.Equal(PropertyValueKind.Number, av.Kind);
        Assert.True(av.TryGetNumber(out var value));
        Assert.Equal(42.5, value);
    }

    [Fact]
    public void PropertyValue_Boolean_roundtrip()
    {
        var av = PropertyValue.FromBoolean(true);
        Assert.Equal(PropertyValueKind.Boolean, av.Kind);
        Assert.True(av.TryGetBoolean(out var value));
        Assert.True(value);
    }

    [Fact]
    public void PropertyValue_ActionId_roundtrip()
    {
        var av = PropertyValue.FromActionId(new ActionId(7));
        Assert.Equal(PropertyValueKind.ActionId, av.Kind);
        Assert.True(av.TryGetActionId(out var value));
        Assert.Equal(new ActionId(7), value);
    }

    [Fact]
    public void PropertyValue_Color_roundtrip_preserves_transparent_values()
    {
        var transparent = PropertyValue.FromColor(StyleColor.Transparent);
        var opaque = PropertyValue.FromColor(StyleColor.Opaque(10, 20, 30));

        Assert.Equal(PropertyValueKind.Color, transparent.Kind);
        Assert.True(transparent.TryGetColor(out var transparentValue));
        Assert.Equal(StyleColor.Transparent, transparentValue);
        Assert.Equal(StyleColor.Transparent, transparent.GetRequiredColor());

        Assert.Equal(PropertyValueKind.Color, opaque.Kind);
        Assert.True(opaque.TryGetColor(out var opaqueValue));
        Assert.Equal(StyleColor.Opaque(10, 20, 30), opaqueValue);
    }

    [Fact]
    public void Public_paint_roundtrip_preserves_canonical_colors_and_direction()
    {
        var start = Color.FromSrgb(255, 0, 0);
        var end = Color.FromSrgb(0, 255, 0);
        var paint = Paint.LinearGradient(start, end, LinearGradientDirection.TopRightToBottomLeft);
        var value = PropertyValue.FromPaint(paint);

        Assert.Equal(PaintKind.LinearGradient, paint.Kind);
        Assert.True(paint.TryGetLinearGradient(out var actualStart, out var actualEnd, out var direction));
        Assert.Equal(start, actualStart);
        Assert.Equal(end, actualEnd);
        Assert.Equal(LinearGradientDirection.TopRightToBottomLeft, direction);
        Assert.Equal(PropertyValueKind.Paint, value.Kind);
        Assert.Equal(paint, value.GetRequiredPaint());
        Assert.Throws<ArgumentOutOfRangeException>(() => Paint.LinearGradient(start, end, LinearGradientDirection.None));
        Assert.Throws<ArgumentException>(() => PropertyValue.FromPaint(Paint.None));
        Assert.Throws<ArgumentException>(() => VirtualNodeProperty.Background(Paint.None));
    }

    [Fact]
    public void Public_border_stroke_roundtrip_keeps_paint_and_positive_finite_thickness()
    {
        var paint = Paint.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            LinearGradientDirection.TopToBottom);
        var border = new BorderStroke(paint, 2.5f);
        var value = PropertyValue.FromBorderStroke(border);
        var property = VirtualNodeProperty.Border(border);

        Assert.Equal(paint, border.Paint);
        Assert.Equal(2.5f, border.Thickness);
        Assert.False(border.IsNone);
        Assert.Equal(PropertyValueKind.BorderStroke, value.Kind);
        Assert.Equal(border, value.GetRequiredBorderStroke());
        Assert.Equal(VirtualPropertyKey.Border, property.Key);
        Assert.Equal(border, property.Value.GetRequiredBorderStroke());
        Assert.Throws<ArgumentException>(() => new BorderStroke(Paint.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BorderStroke(paint, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BorderStroke(paint, float.NaN));
        Assert.Throws<ArgumentException>(() => PropertyValue.FromBorderStroke(BorderStroke.None));
        Assert.Throws<ArgumentException>(() => VirtualNodeProperty.Border(BorderStroke.None));
    }

    [Fact]
    public void Color_FromSrgb_canonicalizes_to_linear_bt2020()
    {
        var red = Color.FromSrgb(255, 0, 0);

        Assert.InRange(red.LinearBt2020R, 0.6273f, 0.6275f);
        Assert.InRange(red.LinearBt2020G, 0.0690f, 0.0692f);
        Assert.InRange(red.LinearBt2020B, 0.0163f, 0.0165f);
        Assert.Equal(1, red.A);

        var srgb = red.ToSrgb();
        Assert.Equal(SrgbColor.Opaque(255, 0, 0), srgb);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 255, 255, 255)]
    [InlineData(255, 10, 20, 30)]
    [InlineData(128, 52, 120, 246)]
    [InlineData(1, 1, 2, 3)]
    public void Color_srgb_roundtrip_preserves_current_sdr_payload(byte a, byte r, byte g, byte b)
    {
        var color = Color.FromSrgb(a, r, g, b);

        Assert.Equal(SrgbColor.FromArgb(a, r, g, b), color.ToSrgb());
    }

    [Fact]
    public void Color_WithOpacity_updates_canonical_alpha_before_sdr_output_mapping()
    {
        var color = Color.FromSrgb(255, 100, 120, 140);
        var half = color.WithOpacity(0.5f);

        Assert.Equal(color.LinearBt2020R, half.LinearBt2020R);
        Assert.Equal(color.LinearBt2020G, half.LinearBt2020G);
        Assert.Equal(color.LinearBt2020B, half.LinearBt2020B);
        Assert.InRange(half.A, 0.499f, 0.501f);
        Assert.Equal(SrgbColor.FromArgb(128, 100, 120, 140), half.ToSrgb());
    }

    [Fact]
    public void StyleColor_uses_canonical_color_while_preserving_srgb_output_bridge()
    {
        var styleColor = StyleColor.Opaque(255, 0, 0);

        Assert.InRange(styleColor.Value.LinearBt2020R, 0.6273f, 0.6275f);
        Assert.NotEqual(1, styleColor.Value.LinearBt2020R);
        Assert.Equal(255u << 24 | 255u << 16, styleColor.Argb);
        Assert.Equal(255, styleColor.A);
        Assert.Equal(255, styleColor.R);
        Assert.Equal(0, styleColor.G);
        Assert.Equal(0, styleColor.B);
    }

    [Fact]
    public void PropertyValue_Color_roundtrip_stores_canonical_color_not_argb_only()
    {
        var value = PropertyValue.FromColor(StyleColor.Opaque(255, 0, 0));

        var color = value.GetRequiredColor();

        Assert.InRange(color.Value.LinearBt2020R, 0.6273f, 0.6275f);
        Assert.InRange(color.Value.LinearBt2020G, 0.0690f, 0.0692f);
        Assert.InRange(color.Value.LinearBt2020B, 0.0163f, 0.0165f);
        Assert.Equal(SrgbColor.Opaque(255, 0, 0), color.ToSrgb());
    }

    [Fact]
    public void Color_value_source_guard_keeps_source_and_output_policy_metadata_out()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "Color.cs"));

        Assert.Contains("struct Color", source);
        Assert.Contains("FromSrgb", source);
        Assert.Contains("ToSrgb", source);
        Assert.DoesNotContain("SourceSpace", source);
        Assert.DoesNotContain("SourceTransfer", source);
        Assert.DoesNotContain("ToneMapPolicy", source);
        Assert.DoesNotContain("HdrOutput", source);
        Assert.DoesNotContain("Swapchain", source);
    }

    [Fact]
    public void DrawCommand_source_guard_keeps_canonical_payload_and_sdr_output_bridge_separate()
    {
        var root = FindRepoRoot();
        var drawingSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Drawing", "DrawingPrimitives.cs"));
        var resourceSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Drawing", "FrameDrawingResources.cs"));
        var recorderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "DrawCommandRecorder.cs"));
        var d3d12Source = File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12DrawingBackend.cs"));
        var normalizedD3D12Source = NormalizeLineEndings(d3d12Source);
        var windowBackendSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "WindowBackend.cs"));
        var d3d12RendererSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer.cs"));
        var d3d12Renderer2DSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12Renderer2D.cs"));
        var d3d12GlyphAtlasSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Platform.Windows", "D3D12GlyphAtlasTextRenderer.DrawPipeline.cs"));

        Assert.Contains("struct DrawPayloadColor(Color Value)", drawingSource);
        Assert.Contains("internal enum ColorOutputKind : byte", drawingSource);
        Assert.Contains("internal enum DrawMaterialBackendCapabilities : byte", drawingSource);
        Assert.Contains("internal enum DrawMaterialFallbackReason : byte", drawingSource);
        Assert.Contains("internal readonly struct DrawMaterialOutputMappingResult", drawingSource);
        Assert.Contains("internal readonly struct DrawMaterialOutputDiagnostics", drawingSource);
        Assert.Contains("internal readonly struct ColorOutputMapping", drawingSource);
        Assert.Contains("public static ColorOutputMapping SdrSrgb", drawingSource);
        Assert.Contains("public DrawColor MapToSdr(Color color)", drawingSource);
        Assert.Contains("public DrawColor MapToSdr(DrawMaterial material) => MapToSdr(material.FallbackColor)", drawingSource);
        Assert.Contains("DrawMaterialOutputMappingResult MapToSdr", drawingSource);
        Assert.Contains("public DrawColor MapToSdr(in DrawCommand command)", drawingSource);
        Assert.Contains("internal enum DrawMaterialKind : byte", drawingSource);
        Assert.Contains("internal readonly struct DrawMaterial", drawingSource);
        Assert.Contains("public static DrawMaterial SolidColor(Color color)", drawingSource);
        Assert.Contains("public static DrawMaterial LinearGradient(Color startColor, Color endColor, DrawPoint startPoint, DrawPoint endPoint)", drawingSource);
        Assert.Contains("public Color FallbackColor => Kind switch", drawingSource);
        Assert.Contains("private readonly DrawMaterial _material", drawingSource);
        Assert.Contains("internal DrawMaterial Material => _material", drawingSource);
        Assert.Contains("internal static DrawCommand FromMaterial", drawingSource);
        Assert.Contains("private readonly DrawPayloadColor _color", drawingSource);
        Assert.Contains("internal Color CanonicalColor => _color.Value", drawingSource);
        Assert.Contains("internal DrawColor ToSdrColor() => _color.ToSdrColor()", drawingSource);
        Assert.Contains("internal static Color FromLinearBt2020", File.ReadAllText(Path.Combine(root, "src", "Irix.Core", "Color.cs")));
        Assert.Contains("internal interface IFrameBrushResolver", resourceSource);
        Assert.Contains("private readonly List<DrawMaterial> _brushes", resourceSource);
        Assert.Contains("internal ResourceHandle AddBrush(DrawMaterial material)", resourceSource);
        Assert.Contains("internal DrawMaterial ResolveBrush(ResourceHandle handle)", resourceSource);
        Assert.DoesNotContain("ResolveBrush(ResourceHandle handle);", ExtractSourceBetween(resourceSource, "public interface IFrameResourceResolver", "internal interface IFrameBrushResolver"));
        Assert.Contains("DrawCommand.FromCanonicalColor", recorderSource);
        Assert.Contains("styleColor.Value.Value", recorderSource);
        Assert.Contains("command.Material", d3d12Source);
        Assert.Contains("D3D12CompositionLayerRectPayload(\n    DrawCommandKind Kind,\n    DrawRect Rect,\n    DrawMaterial Material", normalizedD3D12Source);
        Assert.Contains("D3D12CompositionLayerTextPayload(\n    DrawRect Rect,\n    DrawMaterial Material", normalizedD3D12Source);
        Assert.Contains("DrawCommand.FromMaterial", d3d12Source);
        Assert.Contains("ApplyOpacity(payload.Material", d3d12Source);
        Assert.Contains("ColorOutputMapping.SdrSrgb", d3d12Source);
        Assert.Contains("outputMapping.MapToSdr(command.Material, D3D12MaterialCapabilities)", d3d12Source);
        Assert.Contains("DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient", d3d12Source);
        Assert.Contains("D3D12TextMaterialCapabilities => DrawMaterialBackendCapabilities.SolidColor", d3d12Source);
        Assert.Contains("private const int LinearGradientClampFallbackSegmentCount = 16", d3d12Source);
        Assert.Contains("CanRepresentLinearGradientAsSingleVertexRect", d3d12Source);
        Assert.Contains("ResolveLinearGradientRepresentativeColor", d3d12Source);
        Assert.Contains("IsDegenerateLinearGradient", d3d12Source);
        Assert.Contains("TopLeftColor", d3d12Renderer2DSource);
        Assert.Contains("BottomRightColor", d3d12Renderer2DSource);
        Assert.Contains("AppendPhysicalFillMaterialRect", d3d12Source);
        Assert.Contains("AppendPhysicalLinearGradientRect", d3d12Source);
        Assert.Contains("AppendPhysicalSegmentedLinearGradientRect", d3d12Source);
        Assert.Contains("AddPhysicalGradientRectData", d3d12Source);
        Assert.Contains("outputMapping.MapToSdr(command.Material, D3D12TextMaterialCapabilities)", d3d12Source);
        Assert.Contains("diagnostics.AddMaterialOutput", d3d12Source);
        Assert.Contains("ColorOutputMapping.SdrSrgb", windowBackendSource);
        Assert.Contains("outputMapping.MapToSdr(command)", windowBackendSource);
        Assert.DoesNotContain("command.ToSdrColor()", d3d12Source);
        Assert.DoesNotContain("command.ToSdrColor()", windowBackendSource);
        Assert.DoesNotContain("var srgb = styleColor.Value.ToSrgb()", recorderSource);
        Assert.Equal(["SdrSrgb"], Enum.GetNames<ColorOutputKind>());
        Assert.Equal(["None", "SolidColor", "LinearGradient"], Enum.GetNames<DrawMaterialBackendCapabilities>());
        Assert.Equal(["None", "UnsupportedNonSolidMaterial", "UnsupportedMaterialKind"], Enum.GetNames<DrawMaterialFallbackReason>());
        Assert.DoesNotContain("ScRgb", drawingSource);
        Assert.DoesNotContain("Rec2100", drawingSource);
        Assert.DoesNotContain("ColorOutputKind.Hdr", drawingSource);
        Assert.DoesNotContain("MapToHdr", drawingSource);
        Assert.DoesNotContain("OutputMappingContext", drawingSource);
        Assert.DoesNotContain("ToneMapping", drawingSource);
        Assert.Contains("DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM", d3d12RendererSource);
        Assert.Contains("DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM", d3d12Renderer2DSource);
        Assert.Contains("DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM", d3d12GlyphAtlasSource);
        AssertDoesNotContainAny(
            d3d12RendererSource + d3d12Renderer2DSource + d3d12GlyphAtlasSource,
            "DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16",
            "DXGI_FORMAT_R16G16B16A16",
            "SetColorSpace1",
            "DXGI_COLOR_SPACE_TYPE.",
            "DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020",
            "DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709",
            "DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P2020",
            "DXGI_HDR_METADATA_HDR10");
        Assert.DoesNotContain("public enum DrawMaterialKind", drawingSource);
        Assert.DoesNotContain("public readonly struct DrawMaterial", drawingSource);
        Assert.DoesNotContain("Hdr", drawingSource);
        Assert.DoesNotContain("DisplayP3", drawingSource);
        Assert.DoesNotContain("SourceSpace", drawingSource);
    }

    // ── R13-25: Source-level guards — no string API in core/rendering ──

    [Fact]
    public void Irix_Core_has_no_FromText_string_factory()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("FromText(string", content);
            Assert.DoesNotContain("string? Text", content);
        }
    }

    [Fact]
    public void Irix_Core_has_no_style_string_or_global_layout_property_keys()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualPropertyKey.cs"));

        Assert.DoesNotContain("override string ToString", source);
        Assert.DoesNotContain("PropertyValue.FromText", source);
        Assert.DoesNotContain("FontFamily", source);
        Assert.DoesNotContain("TextStyle", source);
        Assert.DoesNotContain("FontSize", source);
        Assert.DoesNotContain("FontWeight", source);
        Assert.DoesNotContain("Wrapping", source);
        Assert.DoesNotContain("public static readonly VirtualPropertyKey FillColor", source);
        Assert.DoesNotContain("public static readonly VirtualPropertyKey TextColor", source);
        Assert.DoesNotContain("public static readonly VirtualPropertyKey Opacity", source);

        Assert.DoesNotContain("ButtonHeight", source);
        Assert.DoesNotContain("RectangleHeight", source);
        Assert.DoesNotContain("MinimumButtonWidth", source);
        Assert.DoesNotContain("ButtonTextWidthFactor", source);
        Assert.DoesNotContain("ButtonHorizontalPadding", source);
        Assert.DoesNotContain("HorizontalPadding", source);
        Assert.DoesNotContain("VerticalPadding", source);
        Assert.DoesNotContain("ItemSpacing", source);
        Assert.DoesNotContain("TextHeight", source);
        Assert.DoesNotContain("Min" + "Width", source);
        Assert.DoesNotContain("Max" + "Width", source);
        Assert.DoesNotContain("Min" + "Height", source);
        Assert.DoesNotContain("Max" + "Height", source);
    }

    [Fact]
    public void Irix_Core_has_no_legacy_attribute_property_api_names()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("Virtual" + "Attribute", content);
            Assert.DoesNotContain("Attribute" + "Value", content);
            Assert.DoesNotContain("Attribute" + "ChangeSet", content);
            Assert.DoesNotContain("Layout" + "Width", content);
            Assert.DoesNotContain("Layout" + "Height", content);
            Assert.DoesNotContain("State" + "Hovered", content);
            Assert.DoesNotContain("State" + "Pressed", content);
            Assert.DoesNotContain("State" + "Focused", content);
        }
    }

    [Fact]
    public void Core_ir_types_are_not_record_structs()
    {
        var coreDir = Path.Combine(FindRepoRoot(), "src", "Irix.Core");
        var source = File.ReadAllText(Path.Combine(coreDir, "VirtualNodeModels.cs"));
        var metadataSource = File.ReadAllText(Path.Combine(coreDir, "VirtualPropertyKey.cs"));

        Assert.DoesNotContain("record struct VirtualNodeTree", source);
        Assert.DoesNotContain("record struct VirtualNode", source);
        Assert.DoesNotContain("record struct VirtualNodeProperty", source);
        Assert.DoesNotContain("record struct VirtualNodePatch", source);
        Assert.DoesNotContain("record struct StylePropertyMetadata", metadataSource);
    }

    [Fact]
    public void Framework_internal_hot_path_sources_do_not_use_record_structs()
    {
        var root = FindRepoRoot();
        var sourceDirs = new[]
        {
            Path.Combine(root, "src", "Irix.Core"),
            Path.Combine(root, "src", "Irix.Drawing"),
            Path.Combine(root, "src", "Irix.Rendering"),
            Path.Combine(root, "src", "Irix.Platform"),
            Path.Combine(root, "src", "Irix.Platform.Windows")
        };

        foreach (var file in sourceDirs.SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotMatch(RecordStructPattern(), content);
        }
    }

    [Fact]
    public void Framework_internal_hot_path_sources_do_not_use_record_with_syntax()
    {
        var root = FindRepoRoot();
        var sourceDirs = new[]
        {
            Path.Combine(root, "src", "Irix.Core"),
            Path.Combine(root, "src", "Irix.Drawing"),
            Path.Combine(root, "src", "Irix.Rendering"),
            Path.Combine(root, "src", "Irix.Platform"),
            Path.Combine(root, "src", "Irix.Platform.Windows")
        };

        foreach (var file in sourceDirs.SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotMatch(@"\bwith\s*\{", content);
        }
    }

    [Fact]
    public void Framework_internal_hot_path_sources_do_not_define_string_state_members()
    {
        var root = FindRepoRoot();
        var sourceDirs = new[]
        {
            Path.Combine(root, "src", "Irix.Core"),
            Path.Combine(root, "src", "Irix.Drawing"),
            Path.Combine(root, "src", "Irix.Rendering")
        };

        foreach (var file in sourceDirs.SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)))
        {
            foreach (var line in File.ReadLines(file))
            {
                Assert.DoesNotMatch(StringStateMemberPattern(), line);
            }
        }
    }

    [Fact]
    public void Poc_record_structs_are_limited_to_mvu_authoring_state_files()
    {
        var pocDir = Path.Combine(FindRepoRoot(), "src", "Irix.Poc");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ControlVisualState.cs",
            "CounterApplication.cs",
            "CounterApplication.optional-diagnostics.cs",
            "ScrollController.cs",
            "ScrollFeedback.cs"
        };

        foreach (var file in Directory.GetFiles(pocDir, "*.cs", SearchOption.AllDirectories))
        {
            if (allowedFiles.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            Assert.DoesNotMatch(RecordStructPattern(), content);
        }
    }

    [Fact]
    public void Poc_runtime_input_path_does_not_use_delegate_hit_test_resolver()
    {
        var root = FindRepoRoot();
        var programSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "Program.cs"));

        Assert.DoesNotContain("Func<int, int, ActionId>", programSource);
        Assert.DoesNotContain("DelegateActionHitTestResolver", programSource);
        Assert.DoesNotContain("TryMapInputForRuntime(\r\n        RawInputEvent inputEvent,\r\n        InputOwnershipState ownershipState,\r\n        Func<int, int, ActionId>", programSource);
    }

    [Fact]
    public void Retained_diff_and_dirty_range_hot_paths_do_not_allocate_hash_collections()
    {
        var root = FindRepoRoot();
        var performanceDir = Path.Combine(root, "src", "Irix.Core", "Performance");
        var scratchSource = File.ReadAllText(Path.Combine(performanceDir, "FrameScratchArena.cs"));
        var scratchListSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchList.cs"));
        var scratchSpanSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchSpan.cs"));
        var scratchMapSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchNodeKeyIndexMap.cs"));
        var scratchSetSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchIntSet.cs"));
        var renderScratchSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RenderScratchBuffer.cs"));
        var differSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Core", "VirtualNodeDiffer.cs"));
        var retainedTreeSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Core", "RetainedTree.cs"));
        var renderPipelineSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RenderPipeline.cs"));
        var layoutBuilderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutTreeBuilder.cs"));
        var layoutNodeReaderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutNodeReader.cs"));
        var styleOnlyLayoutPatcherSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "StyleOnlyLayoutPatcher.cs"));
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));
        var rangeUtilsSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RangeUtils.cs"));

        Assert.Contains("RentIntSpan", scratchSource);
        Assert.Contains("RentNodeIndexSpan", scratchSource);
        Assert.Contains("RentVirtualNodePatchList", scratchSource);
        Assert.Contains("RentNodeKeyIndexMap", scratchSource);
        Assert.Contains("public static ScratchList<T> Create(Span<T> initialBuffer)", scratchListSource);
        Assert.Contains("Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>())", scratchSpanSource);
        Assert.Contains("Return(pooled, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>())", scratchListSource);
        Assert.Contains("internal ref struct ScratchIntSet", scratchSetSource);
        Assert.Contains("internal ref struct ScratchNodeKeyIndexMap", scratchMapSource);
        Assert.Contains("internal readonly struct Entry", scratchMapSource);
        Assert.DoesNotContain("NodeKey[]", scratchMapSource);
        Assert.DoesNotContain("int[]? _values", scratchMapSource);
        Assert.DoesNotContain("byte[]? _occupied", scratchMapSource);
        Assert.Contains("CreateLayoutElementList", renderScratchSource);
        Assert.Contains("CreateLayoutTreeNodeList", renderScratchSource);
        Assert.Contains("CreateRangeList", renderScratchSource);
        Assert.Contains("CreateDirtyIndexList", renderScratchSource);
        Assert.Contains("CreateDirtyIndexSet", renderScratchSource);
        Assert.DoesNotContain("RentDirtyIndexList", renderScratchSource);
        Assert.DoesNotContain("RentLayoutTreeNodeList", renderScratchSource);
        Assert.DoesNotContain("new FrameScratchArena()", renderScratchSource);
        Assert.DoesNotContain("new FrameScratchArena().", renderScratchSource);

        Assert.DoesNotContain("new Dictionary<NodeKey", differSource);
        Assert.DoesNotContain("new HashSet<NodeKey>", differSource);
        Assert.DoesNotContain("Dictionary<NodeKey", differSource);
        Assert.DoesNotContain("HashSet<NodeKey>", differSource);
        Assert.DoesNotContain("new List<", differSource);
        Assert.Contains("KeyedLinearThreshold", differSource);
        Assert.Contains("CreateNodeKeyIndexMap", differSource);
        Assert.Contains("RetainedTree only accepts canonical diff batches", retainedTreeSource);
        Assert.DoesNotContain("Legacy manual patch fallback", retainedTreeSource);
        Assert.DoesNotContain("ApplyRecursive", retainedTreeSource);
        Assert.DoesNotContain("new Dictionary<int", retainedTreeSource);
        Assert.DoesNotContain("new HashSet<", retainedTreeSource);
        Assert.DoesNotContain("new List<VirtualNode>", retainedTreeSource);

        Assert.DoesNotContain("new HashSet<int>", renderPipelineSource);
        Assert.DoesNotContain("HashSet<int>", renderPipelineSource);
        Assert.DoesNotContain("new List<LayoutDirtyClassification>", renderPipelineSource);

        Assert.Contains("BuildElements", layoutBuilderSource);
        Assert.DoesNotContain("public IReadOnlyList<LayoutElement> Build(", layoutBuilderSource);
        Assert.DoesNotContain("new HashSet<int>", layoutBuilderSource);
        Assert.DoesNotContain("HashSet<int>", layoutBuilderSource);
        Assert.DoesNotContain("new List<LayoutElement>", layoutBuilderSource);
        Assert.DoesNotContain("new List<LayoutTreeNode>", layoutBuilderSource);
        Assert.DoesNotContain("new List<ScrollContainerDiag>", layoutBuilderSource);
        Assert.DoesNotContain("RentLayoutElementList", layoutBuilderSource);
        Assert.DoesNotContain("RentLayoutTreeNodeList", layoutBuilderSource);
        Assert.DoesNotContain("RentDirtyIndexList", layoutBuilderSource);
        Assert.DoesNotContain("LayoutTreeNode[] Children", layoutBuilderSource);
        Assert.DoesNotContain("Children[]", layoutBuilderSource);
        Assert.DoesNotContain("return [new LayoutTreeNode", layoutBuilderSource);
        Assert.DoesNotContain("children.ToArray()", layoutBuilderSource);
        Assert.DoesNotContain("CountVirtualNodes", layoutBuilderSource);
        Assert.Contains("LayoutElementRange", layoutBuilderSource);
        Assert.Contains("subtreeStart", layoutBuilderSource);
        Assert.Contains("subtreeCount", layoutBuilderSource);
        Assert.Contains("AdvanceDirtyCursor", layoutBuilderSource);
        Assert.Contains("CollectDirtyRangesFromElementRanges", layoutBuilderSource);
        Assert.DoesNotContain("CollectDirtyRangesRecursive", layoutBuilderSource);
        Assert.Contains("Debug.Assert(consumed == _elementRanges.Count", layoutBuilderSource);

        Assert.Contains("int SubtreeStart", layoutModelSource);
        Assert.Contains("int SubtreeCount", layoutModelSource);
        Assert.DoesNotContain("FirstChildIndex", layoutModelSource);
        Assert.DoesNotContain("ChildCount", layoutModelSource);
        Assert.DoesNotContain("LayoutTreeNode[] Children", layoutModelSource);
        Assert.Contains("internal readonly struct LayoutTreeResult", layoutModelSource);
        Assert.DoesNotContain("internal sealed class LayoutTreeResult", layoutModelSource);
        Assert.Contains("internal readonly struct LayoutElementList", layoutModelSource);
        Assert.Contains("internal readonly struct LayoutTreeNodeList", layoutModelSource);
        Assert.Contains("internal readonly struct LayoutElementRangeList", layoutModelSource);
        Assert.Contains("private readonly LayoutElementList _elements", layoutModelSource);
        Assert.Contains("private readonly LayoutTreeNodeList _treeNodes", layoutModelSource);
        Assert.Contains("private readonly LayoutElementRangeList _elementRanges", layoutModelSource);
        Assert.DoesNotContain("private readonly LayoutElement[]? _elements", layoutModelSource);
        Assert.DoesNotContain("private readonly LayoutTreeNode[]? _treeNodes", layoutModelSource);
        Assert.DoesNotContain("private readonly LayoutElementRange[]? _elementRanges", layoutModelSource);
        Assert.Contains("ElementsToList", layoutBuilderSource);
        Assert.Contains("TreeNodesToList", layoutBuilderSource);
        Assert.Contains("ElementRangesToList", layoutBuilderSource);
        Assert.DoesNotContain("ElementsToArray", layoutBuilderSource);
        Assert.DoesNotContain("TreeNodesToArray", layoutBuilderSource);
        Assert.DoesNotContain("ElementRangesToArray", layoutBuilderSource);
        Assert.Contains("private LayoutTreeResult? _retainedLayoutResult", renderPipelineSource);
        Assert.DoesNotContain("_retainedLayout;", renderPipelineSource);

        Assert.DoesNotContain("new List<", rangeUtilsSource);

        Assert.Contains("previousTree.CreateReader()", differSource);
        Assert.Contains("nextTree.CreateReader()", differSource);
        Assert.Contains("VirtualNodeReader", differSource);
        Assert.DoesNotContain("private static bool IsDefaultTree", differSource);
        Assert.DoesNotContain("ReadOnlySpan<VirtualNode> oldChildren", differSource);
        Assert.DoesNotContain("ReadOnlySpan<VirtualNode> newChildren", differSource);

        var diffInnerSource = ExtractSourceBetween(
            differSource,
            "private static void DiffNode",
            "private static bool PropertiesEqual");
        Assert.DoesNotContain("ToArray()", diffInnerSource);

        var layoutRecursiveSource = ExtractSourceBetween(
            layoutBuilderSource,
            "private int LayoutNode",
            "private static IndexRangeList CollectDirtyRanges");
        Assert.DoesNotContain("elements.ToArray()", layoutRecursiveSource);
        Assert.DoesNotContain("scrollDiags.ToArray()", layoutRecursiveSource);

        var canonicalApplySource = ExtractSourceBetween(
            retainedTreeSource,
            "private ApplyResult ApplyCanonicalRootBatch",
            "private static IReadOnlyList<int> SortAndDeduplicateDirty");
        Assert.DoesNotContain("new List<", canonicalApplySource);
        Assert.DoesNotContain("new Dictionary<", canonicalApplySource);
        Assert.DoesNotContain("new HashSet<", canonicalApplySource);
        Assert.DoesNotContain("ToArray()", canonicalApplySource);
        Assert.Contains("BuildParentIndexTable", retainedTreeSource);
        Assert.Contains("FindParentIndex(ReadOnlySpan<NodeIndexEntry>", retainedTreeSource);
    }

    [Fact]
    public void Scratch_primitives_are_split_by_type_and_stack_first()
    {
        var performanceDir = Path.Combine(FindRepoRoot(), "src", "Irix.Core", "Performance");

        Assert.True(File.Exists(Path.Combine(performanceDir, "FrameScratchArena.cs")));
        Assert.True(File.Exists(Path.Combine(performanceDir, "ScratchList.cs")));
        Assert.True(File.Exists(Path.Combine(performanceDir, "ScratchSpan.cs")));
        Assert.True(File.Exists(Path.Combine(performanceDir, "ScratchNodeKeyIndexMap.cs")));
        Assert.True(File.Exists(Path.Combine(performanceDir, "ScratchIntSet.cs")));
        Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "FrameScratchArena.cs")));

        var scratchListSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchList.cs"));
        var scratchMapSource = File.ReadAllText(Path.Combine(performanceDir, "ScratchNodeKeyIndexMap.cs"));
        var root = FindRepoRoot();
        var renderPipelineSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "RenderPipeline.cs"));
        var layoutBuilderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutTreeBuilder.cs"));
        var layoutNodeReaderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutNodeReader.cs"));
        var styleOnlyLayoutPatcherSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "StyleOnlyLayoutPatcher.cs"));
        var layoutModelSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutModels.cs"));

        Assert.Contains("public static ScratchList<T> Create(Span<T> initialBuffer)", scratchListSource);
        Assert.Contains("private Span<T> _initialBuffer", scratchListSource);
        Assert.Contains("_initialBuffer[.._count].Clear()", scratchListSource);
        Assert.Contains("private Span<Entry> _entries", scratchMapSource);
        Assert.Contains("returning without clear does not retain refs", scratchMapSource);
        Assert.Contains("Span<LayoutDirtyClassification> classificationStorage = stackalloc", renderPipelineSource);
        Assert.Contains("Span<int> dirtySetStorage = stackalloc", renderPipelineSource);
        Assert.Contains("Span<LayoutElement> elementStorage = stackalloc", layoutBuilderSource);
        Assert.Contains("Span<LayoutTreeNode> treeNodeStorage = stackalloc", layoutBuilderSource);
        Assert.Contains("Span<ScrollContainerDiag> scrollDiagStorage = stackalloc", layoutBuilderSource);
        Assert.Contains("Span<int> sortedDirtyStorage = stackalloc", layoutBuilderSource);
        Assert.Contains("Span<(int Start, int Count)> rangeStorage = stackalloc", layoutBuilderSource);
        Assert.Contains("new VirtualNodeTree(root)", layoutBuilderSource);
        Assert.Contains("VirtualNodeReader", layoutBuilderSource);
        Assert.Contains("VirtualNodeReader", layoutNodeReaderSource);
        Assert.Contains("VirtualNodeReader", styleOnlyLayoutPatcherSource);
        Assert.Contains("int SubtreeStart", layoutModelSource);
        Assert.Contains("int SubtreeCount", layoutModelSource);
        Assert.DoesNotContain("LayoutTreeNode[] Children", layoutModelSource);
    }

    [Fact]
    public void Input_ownership_diagnostics_use_ring_buffer_not_remove_at()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "InputOwnershipState.cs"));
        var diagnosticSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "InputOwnershipState.optional-diagnostics.cs"));

        Assert.DoesNotMatch(RecordStructPattern(), source);
        Assert.Contains("partial void RecordHoverChanged", source);
        Assert.DoesNotContain("InputOwnershipEvent[]", source);
        Assert.DoesNotContain("DiagnosticEvents", source);
        Assert.DoesNotContain("AddDiagnosticEvent", source);
        Assert.StartsWith("#if IRIX_DIAGNOSTICS", diagnosticSource);
        Assert.Contains("InputOwnershipEvent[]", diagnosticSource);
        Assert.DoesNotContain("RemoveAt(0)", diagnosticSource);
        Assert.DoesNotContain("new List<InputOwnershipEvent>", diagnosticSource);
        Assert.DoesNotContain("List<InputOwnershipEvent> _diagnosticEvents", diagnosticSource);
    }

    [Fact]
    public void Ref_struct_types_stay_limited_to_builder_reader_and_context_boundaries()
    {
        Assert.True(typeof(VirtualNodePropertyListBuilder).IsByRefLike);
        Assert.True(typeof(VirtualNodeChildrenBuilder).IsByRefLike);
        Assert.True(typeof(VirtualNodeTreeReader).IsByRefLike);
        Assert.True(typeof(VirtualNodeReader).IsByRefLike);
        Assert.False(typeof(VirtualNode).IsByRefLike);
        Assert.False(typeof(VirtualNodeTree).IsByRefLike);
        Assert.False(typeof(VirtualNodeProperty).IsByRefLike);
        Assert.False(typeof(VirtualNodePatch).IsByRefLike);
        Assert.False(typeof(PatchBatch).IsByRefLike);
        Assert.False(typeof(RenderFrameBatch).IsByRefLike);

        var root = FindRepoRoot();
        var propertyReaderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "PropertyReader.cs"));
        var layoutSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutTreeBuilder.cs"));
        var layoutNodeReaderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutNodeReader.cs"));
        var styleOnlyLayoutPatcherSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "StyleOnlyLayoutPatcher.cs"));

        Assert.Contains("internal readonly ref struct PropertyReader", propertyReaderSource);
        Assert.Contains("internal ref struct LayoutContext", layoutSource);
        Assert.Contains("VirtualNodeReader", layoutNodeReaderSource);
        Assert.Contains("VirtualNodeReader", styleOnlyLayoutPatcherSource);
    }

    [Fact]
    public void Ref_struct_names_do_not_enter_batch_or_retained_state_sources()
    {
        var root = FindRepoRoot();
        var sourceFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                return name.Contains("Batch", StringComparison.Ordinal)
                    || name.Contains("Retained", StringComparison.Ordinal)
                    || name.Contains("Segmented", StringComparison.Ordinal);
            });

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("VirtualNodeTreeReader", content);
            Assert.DoesNotContain("VirtualNodeReader", content);
            Assert.DoesNotContain("VirtualNodePropertyListBuilder", content);
            Assert.DoesNotContain("VirtualNodeChildrenBuilder", content);
            Assert.DoesNotContain("PropertyReader", content);
            Assert.DoesNotContain("LayoutContext", content);
        }
    }

    [Fact]
    public void Core_property_api_has_no_round15_removed_names()
    {
        var coreDir = Path.Combine(FindRepoRoot(), "src", "Irix.Core");
        var sourceFiles = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("VirtualNodeProperty.Opacity", content);
            Assert.DoesNotContain("VirtualPropertyKey.Opacity", content);
            Assert.DoesNotContain("ActionIdValue", content);
            Assert.DoesNotContain("public double Number", content);
            Assert.DoesNotContain("public bool Boolean", content);
            Assert.DoesNotContain("ReferenceEquals(Properties", content);
            Assert.DoesNotContain("Rectangle(double width", content);
        }
    }

    [Fact]
    public void VirtualNode_authoring_helpers_do_not_use_array_params()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.DoesNotContain("params VirtualNodeProperty[]", source);
        Assert.DoesNotContain("params VirtualNode[]", source);
        Assert.Contains("params scoped ReadOnlySpan<VirtualNodeProperty>", source);
        Assert.Contains("params scoped ReadOnlySpan<VirtualNode>", source);
    }

    [Fact]
    public void VirtualNode_exposes_typed_property_publication_without_readonly_list_wrappers()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("internal readonly struct VirtualNodePropertyList", source);
        Assert.Contains("CompactKindAction", source);
        Assert.Contains("CompactKindControlBundle", source);
        Assert.Contains("HoveredBit", source);
        Assert.Contains("internal readonly struct VirtualNodeChildList", source);
        Assert.Contains("public VirtualNodePropertyList Properties", source);
        Assert.Contains("public VirtualNodeChildList Children", source);
        Assert.DoesNotContain("private readonly VirtualNodeProperty _property", source);
        Assert.DoesNotContain("private readonly VirtualNode[]? _children", source);
        Assert.DoesNotContain("IReadOnlyList<VirtualNode>", source);
        Assert.DoesNotContain("Array.AsReadOnly", source);
        Assert.DoesNotContain("public ReadOnlySpan<VirtualNodeProperty> Properties", source);
        Assert.DoesNotContain("public ReadOnlySpan<VirtualNode> Children", source);
        Assert.DoesNotContain("_propertiesView", source);
        Assert.DoesNotContain("_childrenView", source);
    }

    [Fact]
    public void VirtualNode_child_publication_supports_tree_owned_slab_ranges()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("internal ref struct VirtualNodeTreePublicationBuilder", source);
        Assert.Contains("ReserveChildRange", source);
        Assert.Contains("PublishReservedChildren", source);
        Assert.Contains("FromOwnedArrayRange", source);
        Assert.Contains("private readonly int _start;", source);
        Assert.Contains("private readonly int _count;", source);
        Assert.Contains("return items[_start + index];", source);
        Assert.Contains("_items.AsSpan(_start, _count)", source);
        Assert.DoesNotContain("internal sealed class VirtualNodeChildSlab", source);
        Assert.DoesNotContain("VirtualNodeChildSlab? _owner", source);
    }

    [Fact]
    public void VirtualNode_child_publication_builder_publishes_array_ranges()
    {
        var builder = new VirtualNodeTreePublicationBuilder(childCapacity: 4);
        var first = VirtualNodeFactory.Rectangle(new NodeKey(1), VirtualNodeProperty.Width(10));
        var second = VirtualNodeFactory.Rectangle(new NodeKey(2), VirtualNodeProperty.Width(20));
        var prefix = builder.PublishChildren(first, second);
        var tail = builder.PublishChildren([first]);
        var reserved = builder.ReserveChildRange(1, out var reservedStart);
        reserved[0] = second;
        var reservedList = builder.PublishReservedChildren(reservedStart, reserved.Length);

        Assert.Equal(4, builder.WrittenChildCount);
        Assert.Equal(4, builder.ChildCapacity);
        Assert.Equal(2, prefix.Length);
        Assert.Equal(new NodeKey(1), prefix[0].Key);
        Assert.Equal(new NodeKey(2), prefix[1].Key);
        Assert.Single(tail.ToArray());
        Assert.Equal(new NodeKey(1), tail[0].Key);
        Assert.Single(reservedList.ToArray());
        Assert.Equal(new NodeKey(2), reservedList[0].Key);
    }

    [Fact]
    public void VirtualNode_builders_keep_inline_and_freezing_boundaries_internal()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("[InlineArray(InlineCapacity)]", source);
        Assert.DoesNotContain("MemoryMarshal.CreateSpan", source);
        Assert.DoesNotContain("public readonly VirtualNodeProperty[] ToArray", source);
        Assert.Contains("internal VirtualNode[] ToArray()", source);
    }

    [Fact]
    public void Control_button_lowering_freezes_only_final_publication_arrays()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "ControlVisualState.cs"));

        Assert.Contains("CountButtonProperties", source);
        Assert.Contains("CreateButtonPropertyList", source);
        Assert.Contains("VirtualNodeChildList CreateButtonChildren", source);
        Assert.Contains("scoped ref VirtualNodeTreePublicationBuilder publication", source);
        Assert.Contains("publication.PublishChildren", source);
        Assert.Contains("VirtualNode.CreateFromOwnedChildrenUnsafe(VirtualNodeKind.Container", source);
        Assert.Contains("StyleDeclarationMapper.WriteVirtualNodeProperties", source);
        Assert.DoesNotContain("CreateButtonPropertyArray", source);
        Assert.DoesNotContain("CreateButtonChildrenFromOwnedPropertyArraysUnsafe", source);
        Assert.DoesNotContain("SplitButtonProperties", source);
        Assert.DoesNotContain("private static VirtualNodeProperty[] Trim", source);
        Assert.Contains("CreateLargeButtonPropertyList", source);
        Assert.Contains("const int StackPropertyLimit = 8", source);
        Assert.DoesNotContain("var container = new VirtualNodeProperty[properties.Length]", source);
        Assert.DoesNotContain("var rectangle = new VirtualNodeProperty[properties.Length]", source);
        Assert.DoesNotContain("var text = new VirtualNodeProperty[properties.Length]", source);
        Assert.DoesNotContain("new VirtualNodeChildrenBuilder()", source);
    }

    [Fact]
    public void Counter_root_view_publishes_root_children_through_tree_publication_range()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "CounterApplication.cs"));

        Assert.Contains("CreateRootChildren", source);
        Assert.Contains("new VirtualNodeTreePublicationBuilder", source);
        Assert.Contains("ReserveChildRange", source);
        Assert.Contains("PublishReservedChildren", source);
        Assert.Contains("VirtualNode.CreateFromOwnedChildrenUnsafe(VirtualNodeKind.Container", source);
        Assert.DoesNotContain("new VirtualNode[headerRows.Length", source);
        Assert.DoesNotContain("VirtualNodeChildList.FromOwnedArray(children)", source);
        Assert.Contains("headerRows.CopyTo(children)", source);
        Assert.Contains("WriteScrollProbeRows", source);
        Assert.DoesNotContain("BuildScrollProbeRows", source);
        Assert.DoesNotContain(".. BuildScrollProbeRows", source);
    }

    [Fact]
    public void VirtualNode_does_not_expose_deep_value_equality()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.DoesNotContain(
            typeof(VirtualNode).GetInterfaces(),
            type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEquatable<>));
        Assert.DoesNotContain(
            typeof(VirtualNode).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name is nameof(Equals) or nameof(GetHashCode));
        Assert.DoesNotContain("IEquatable<VirtualNode>", source);
        Assert.DoesNotContain("IEquatable<VirtualNodeTree>", source);
        Assert.DoesNotContain("IEquatable<VirtualNodePatch>", source);
        Assert.DoesNotContain("operator ==(VirtualNode ", source);
        Assert.DoesNotContain("operator !=(VirtualNode ", source);
        Assert.DoesNotContain("HashCode.Combine(Root", source);
    }

    [Fact]
    public void ApplyResult_does_not_expose_list_reference_equality()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "RetainedTree.cs"));

        Assert.DoesNotContain("IEquatable<ApplyResult>", source);
        Assert.DoesNotContain("EqualityComparer<IReadOnlyList<int>>", source);
        Assert.DoesNotContain("operator ==(ApplyResult", source);
        Assert.DoesNotContain("operator !=(ApplyResult", source);
        Assert.DoesNotContain("PreviousRoot.Equals(other.PreviousRoot)", source);
    }

    [Fact]
    public void Owned_array_virtual_node_creation_is_marked_unsafe()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("CreateFromOwnedArraysUnsafe", source);
        Assert.Contains("Callers must not mutate the arrays after this call.", source);
        Assert.DoesNotContain("CreateFromOwnedArrays(", source);
    }

    [Fact]
    public void VirtualNode_shape_contract_is_guarded_in_source()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("nodes cannot have children", source);
        Assert.Contains("None nodes must be empty", source);
        Assert.Contains("case VirtualNodeKind.None", source);
        Assert.Contains("case VirtualNodeKind.Container", source);
        Assert.Contains("Container nodes cannot have content", source);
        Assert.Contains("case VirtualNodeKind.Content", source);
        Assert.Contains("Content nodes cannot have children", source);
        Assert.Contains("Content nodes require one content resource", source);
        Assert.DoesNotContain("case VirtualNodeKind.Button", source);
        Assert.DoesNotContain("case VirtualNodeKind.ScrollContainer", source);
        Assert.DoesNotContain("static VirtualNode Button", source);
        Assert.DoesNotContain("static VirtualNode ScrollContainer", source);
        Assert.DoesNotContain("SplitControlTemplateProperties", source);
    }

    [Fact]
    public void VirtualNode_property_contract_separates_container_and_content_properties()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualPropertyKey.cs"));

        Assert.Contains("private const VirtualNodeKindFlags ContainerNodes", source);
        Assert.Contains("private const VirtualNodeKindFlags ContentNodes", source);
        Assert.Contains("private const VirtualNodeKindFlags LayoutSizedNodes", source);
        Assert.Contains("VirtualNodeKindFlags.Container | VirtualNodeKindFlags.Content", source);
        Assert.DoesNotContain("VirtualNodeKindFlags.Button", source);
        Assert.DoesNotContain("VirtualNodeKindFlags.ScrollContainer", source);

        _ = VirtualNodeFactory.Container(
            new NodeKey(7),
            [VirtualNodeProperty.Width(100)],
            ReadOnlySpan<VirtualNode>.Empty);

        _ = VirtualNodeFactory.Container(
            new NodeKey(7),
            [VirtualNodeProperty.Action(new ActionId(1))],
            ReadOnlySpan<VirtualNode>.Empty);

        Assert.Throws<ArgumentException>(() => VirtualNodeFactory.Container(
            new NodeKey(7),
            [VirtualNodeProperty.Background(Color.FromSrgb(1, 2, 3))],
            ReadOnlySpan<VirtualNode>.Empty));

        _ = VirtualNodeFactory.Rectangle(new NodeKey(8), VirtualNodeProperty.Background(Color.FromSrgb(1, 2, 3)));
        Assert.Throws<ArgumentException>(() => VirtualNodeFactory.Rectangle(
            new NodeKey(8),
            VirtualNodeProperty.Action(new ActionId(1))));
    }

    [Fact]
    public void Property_metadata_support_and_diagnostics_are_internal()
    {
        Assert.False(typeof(VirtualNodeKind).IsPublic);
        Assert.False(typeof(ContentResourceKind).IsPublic);
        Assert.False(typeof(ContentResource).IsPublic);
        Assert.False(typeof(VirtualNode).IsPublic);
        Assert.False(typeof(VirtualNodeTree).IsPublic);
        Assert.False(typeof(VirtualNodeTreeReader).IsPublic);
        Assert.False(typeof(VirtualNodeReader).IsPublic);
        Assert.False(typeof(VirtualNodeProperty).IsPublic);
        Assert.False(typeof(VirtualNodePatch).IsPublic);
        Assert.False(typeof(VirtualNodeFactory).IsPublic);
        Assert.False(typeof(VirtualNodeBuilder).IsPublic);
        Assert.False(typeof(VirtualNodeDiffer).IsPublic);
        Assert.False(typeof(VirtualPropertyKey).IsPublic);
        Assert.False(typeof(PropertyValue).IsPublic);
        Assert.False(typeof(VirtualTextArena).IsPublic);
        Assert.False(typeof(TextContentResource).IsPublic);
        Assert.False(typeof(TextBufferSnapshot).IsPublic);
        Assert.False(typeof(PatchBatch).IsPublic);
        Assert.False(typeof(RetainedTree).IsPublic);
        Assert.False(typeof(ApplyResult).IsPublic);
        Assert.False(typeof(Runtime<,>).IsPublic);
        Assert.False(typeof(IApplication<,>).IsPublic);
        Assert.False(typeof(IVirtualNodePatchSink).IsPublic);
        Assert.False(typeof(VirtualPropertyMetadata).IsPublic);
        Assert.False(typeof(VirtualPropertyDiagnostics).IsPublic);
        Assert.False(typeof(VirtualNodePropertySupport).IsPublic);
        Assert.False(typeof(StylePropertyId).IsPublic);
        Assert.False(typeof(StyleValue).IsPublic);
        Assert.False(typeof(StyleDeclaration).IsPublic);
        Assert.False(typeof(StyleDeclarationMapper).IsPublic);
        Assert.False(typeof(StylePropertyMetadata).IsPublic);
        Assert.False(typeof(StyleEffect).IsPublic);
        Assert.False(typeof(AnimationChannel).IsPublic);
        Assert.False(typeof(VirtualNodeKindFlags).IsPublic);
        Assert.False(typeof(StylePropertyScope).IsPublic);
        Assert.False(typeof(StyleColor).IsPublic);
        Assert.False(typeof(StyleColorSlot).IsPublic);
        Assert.False(typeof(StyleDeltaPlan).IsPublic);
        Assert.False(typeof(StyleDeltaWork).IsPublic);
        Assert.False(typeof(StyleDeltaPlanner).IsPublic);
        Assert.False(typeof(StyleTransitionCompiler).IsPublic);
        Assert.False(typeof(StyleTransitionCompileRequest).IsPublic);
        Assert.False(typeof(StyleTransitionCompileResult).IsPublic);
    }

    [Fact]
    public void Public_property_authoring_api_is_not_split_by_processing_layer()
    {
        var methods = typeof(VirtualNodeProperty)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain("SetLayoutStyle", methods);
        Assert.DoesNotContain("SetVisualStyle", methods);
        Assert.DoesNotContain("SetCompositeStyle", methods);
        Assert.DoesNotContain("LayoutStyle", methods);
        Assert.DoesNotContain("VisualStyle", methods);
        Assert.DoesNotContain("CompositeStyle", methods);
        Assert.DoesNotContain("BackgroundColor", methods);
        Assert.DoesNotContain("ForegroundColor", methods);
        Assert.DoesNotContain("LayerOpacity", methods);
        Assert.DoesNotContain("TranslateX", methods);
        Assert.DoesNotContain("TranslateY", methods);
    }

    [Fact]
    public void Internal_style_preflight_does_not_create_public_authoring_or_scheduler_surface()
    {
        var root = FindRepoRoot();
        var coreSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Core", "VirtualPropertyKey.cs"));
        var styleDeclarationSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Core", "StyleDeclaration.cs"));
        var controlVisualStateSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "ControlVisualState.cs"));
        var counterTransitionBridgeSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "CounterStyleTransitionBridge.cs"));
        var transitionSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "StyleTransitionCompiler.cs"));
        var styleDeltaSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "StyleDeltaPlan.cs"));

        Assert.Contains("internal enum PropertyDomain", coreSource);
        Assert.Contains("Composition = 5", coreSource);
        Assert.Contains("public ulong CompositionMask", coreSource);
        Assert.Contains("PropertyDomain.Composition", coreSource);
        Assert.Contains("internal enum StylePropertyId", styleDeclarationSource);
        Assert.Contains("internal readonly struct StyleDeclaration", styleDeclarationSource);
        Assert.Contains("ToVirtualNodeProperty", styleDeclarationSource);
        Assert.DoesNotContain("StyleEffect", styleDeclarationSource);
        Assert.DoesNotContain("StyleDeltaWork", styleDeclarationSource);
        Assert.DoesNotContain("InvalidationKind", styleDeclarationSource);
        Assert.DoesNotContain("LayoutRebuildReason", styleDeclarationSource);
        Assert.DoesNotContain("AnimationChannel", styleDeclarationSource);
        Assert.Contains("StyleDeclarationMapper.ToVirtualNodeProperties", controlVisualStateSource);
        Assert.Contains("StyleDeclarationMapper.WriteVirtualNodeProperties", controlVisualStateSource);
        Assert.DoesNotContain("VirtualNodeProperty.Hovered", controlVisualStateSource);
        Assert.DoesNotContain("VirtualNodeProperty.Pressed", controlVisualStateSource);
        Assert.DoesNotContain("VirtualNodeProperty.Focused", controlVisualStateSource);
        Assert.Contains("StyleDeclarationMapper.ToVirtualNodeProperties", counterTransitionBridgeSource);
        Assert.DoesNotContain("VirtualNodeProperty.LayerOpacity", counterTransitionBridgeSource);
        Assert.DoesNotContain("VirtualNodeProperty.TranslateX", counterTransitionBridgeSource);
        Assert.DoesNotContain("VirtualNodeProperty.TranslateY", counterTransitionBridgeSource);

        Assert.DoesNotContain("public enum StyleDeltaWork", styleDeltaSource);
        Assert.DoesNotContain("public readonly struct StyleDeltaPlan", styleDeltaSource);
        Assert.DoesNotContain("public static class StyleDeltaPlanner", styleDeltaSource);
        Assert.DoesNotContain("public static class StyleTransitionCompiler", transitionSource);
        Assert.DoesNotContain("Theme", transitionSource);
        Assert.DoesNotContain("Cascade", transitionSource);
        Assert.DoesNotContain("Scheduler", transitionSource);
        Assert.DoesNotContain("Task", transitionSource);
        Assert.DoesNotContain("async", transitionSource);
        Assert.DoesNotContain("SetCompositionAnimationDeclaration", transitionSource);
        Assert.Contains("CompositionAnimationDeclaration", transitionSource);
        Assert.Contains("StyleDeltaPlanner.Plan", transitionSource);
    }

    [Fact]
    public void Public_semantic_linear_gradient_authoring_stays_renderer_independent()
    {
        var root = FindRepoRoot();
        var coreDir = Path.Combine(root, "src", "Irix.Core");
        var drawingDir = Path.Combine(root, "src", "Irix.Drawing");
        var propertyKeySource = File.ReadAllText(Path.Combine(coreDir, "VirtualPropertyKey.cs"));
        var nodeModelsSource = File.ReadAllText(Path.Combine(coreDir, "VirtualNodeModels.cs"));
        var styleDeclarationSource = File.ReadAllText(Path.Combine(coreDir, "StyleDeclaration.cs"));
        var drawingSource = NormalizeLineEndings(File.ReadAllText(Path.Combine(drawingDir, "DrawingPrimitives.cs")));
        var resourceSource = File.ReadAllText(Path.Combine(drawingDir, "FrameDrawingResources.cs"));

        var stylePropertyNames = Enum.GetNames<StylePropertyId>();
        Assert.DoesNotContain("Brush", stylePropertyNames);
        Assert.DoesNotContain("Material", stylePropertyNames);
        Assert.DoesNotContain("Gradient", stylePropertyNames);
        Assert.DoesNotContain("RadialGradient", stylePropertyNames);
        Assert.DoesNotContain("Image", stylePropertyNames);

        var styleValueMembers = typeof(StyleValue)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();
        AssertDoesNotContainAny(
            styleValueMembers,
            "FromBrush",
            "FromMaterial",
            "FromGradient",
            "FromImage",
            "TryGetBrush",
            "TryGetMaterial",
            "TryGetGradient",
            "TryGetImage",
            "GetRequiredBrush",
            "GetRequiredMaterial",
            "GetRequiredGradient",
            "GetRequiredImage");

        var propertyValueKinds = Enum.GetNames<PropertyValueKind>();
        Assert.Contains("Paint", propertyValueKinds);
        Assert.DoesNotContain("Brush", propertyValueKinds);
        Assert.DoesNotContain("Material", propertyValueKinds);
        Assert.DoesNotContain("Gradient", propertyValueKinds);
        Assert.DoesNotContain("Image", propertyValueKinds);

        Assert.True(typeof(Color).IsPublic);
        Assert.True(typeof(SrgbColor).IsPublic);
        Assert.True(typeof(Paint).IsPublic);
        Assert.True(typeof(PaintKind).IsPublic);
        Assert.True(typeof(LinearGradientDirection).IsPublic);
        Assert.True(typeof(BorderStroke).IsPublic);
        Assert.Equal(["None", "SolidColor", "LinearGradient"], Enum.GetNames<PaintKind>());
        Assert.Equal(
            ["None", "LeftToRight", "TopToBottom", "TopLeftToBottomRight", "TopRightToBottomLeft"],
            Enum.GetNames<LinearGradientDirection>());

        var gradient = Paint.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            LinearGradientDirection.LeftToRight);
        var background = VirtualNodeProperty.Background(gradient);
        Assert.Equal(VirtualPropertyKey.Background, background.Key);
        Assert.Equal(PropertyValueKind.Paint, background.Value.Kind);
        Assert.Equal(gradient, background.Value.GetRequiredPaint());

        var borderStroke = new BorderStroke(gradient, 2f);
        var border = VirtualNodeProperty.Border(borderStroke);
        Assert.Equal(VirtualPropertyKey.Border, border.Key);
        Assert.Equal(PropertyValueKind.BorderStroke, border.Value.Kind);
        Assert.Equal(borderStroke, border.Value.GetRequiredBorderStroke());

        var virtualPropertyKeyBlock = ExtractSourceBetween(
            propertyKeySource,
            "internal readonly struct VirtualPropertyKey",
            "internal readonly struct StylePropertyMetadata");
        AssertDoesNotContainAny(virtualPropertyKeyBlock, "Brush", "Material", "Gradient", "Image");

        var propertyValueBlock = ExtractSourceBetween(
            nodeModelsSource,
            "internal readonly struct PropertyValue",
            "internal readonly struct StyleColor");
        AssertDoesNotContainAny(
            propertyValueBlock,
            "FromBrush",
            "FromMaterial",
            "FromGradient",
            "FromImage",
            "TryGetBrush",
            "TryGetMaterial",
            "TryGetGradient",
            "TryGetImage",
            "GetRequiredBrush",
            "GetRequiredMaterial",
            "GetRequiredGradient",
            "GetRequiredImage");

        var virtualNodePropertyMethods = typeof(VirtualNodeProperty)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToArray();
        Assert.Contains("Background", virtualNodePropertyMethods);
        Assert.Contains("Border", virtualNodePropertyMethods);
        AssertDoesNotContainAny(virtualNodePropertyMethods, "Brush", "Material", "Gradient", "Image");
        Assert.Contains(
            nameof(VirtualPropertyKey.Background),
            GetPublicVirtualPropertyKeyFields().Select(field => field.Name));
        Assert.Contains(
            nameof(VirtualPropertyKey.Border),
            GetPublicVirtualPropertyKeyFields().Select(field => field.Name));

        var styleDeclarationBlock = ExtractSourceBetween(
            styleDeclarationSource,
            "internal readonly struct StyleDeclaration",
            "internal static class StyleDeclarationMapper");
        AssertDoesNotContainAny(styleDeclarationBlock, "Brush", "Material", "Gradient", "Image");

        var frameResourceResolverBlock = ExtractSourceBetween(
            resourceSource,
            "public interface IFrameResourceResolver",
            "internal interface IFrameBrushResolver");
        Assert.DoesNotContain("ResolveBrush", frameResourceResolverBlock);
        Assert.DoesNotContain("DrawMaterial", frameResourceResolverBlock);
        Assert.Contains("internal interface IFrameBrushResolver", resourceSource);
        Assert.DoesNotContain("public interface IFrameBrushResolver", resourceSource);

        Assert.False(typeof(DrawMaterialKind).IsPublic);
        Assert.False(typeof(DrawMaterial).IsPublic);
        Assert.False(typeof(DrawPoint).IsPublic);
        Assert.Equal(["None", "SolidColor", "LinearGradient"], Enum.GetNames<DrawMaterialKind>());

        var materialKindBlock = ExtractSourceBetween(
            drawingSource,
            "internal enum DrawMaterialKind",
            "internal readonly struct DrawMaterial");
        Assert.Contains("LinearGradient", materialKindBlock);
        AssertDoesNotContainAny(materialKindBlock, "RadialGradient", "Image", "Texture");

        var materialBlock = ExtractSourceBetween(
            drawingSource,
            "internal readonly struct DrawMaterial",
            "internal readonly struct DrawPayloadColor");
        Assert.Contains("public static DrawMaterial SolidColor(Color color)", materialBlock);
        Assert.Contains("public static DrawMaterial LinearGradient(Color startColor, Color endColor, DrawPoint startPoint, DrawPoint endPoint)", materialBlock);
        Assert.Contains("public Color FallbackColor => Kind switch", materialBlock);
        AssertDoesNotContainAny(materialBlock, "RadialGradient", "Image", "Texture");
    }

    [Fact]
    public void Public_material_authoring_policy_stays_semantic_not_renderer_owned()
    {
        var root = FindRepoRoot();
        var styleDesign = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Style-System.md")));
        var colorDesign = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Color-Pipeline.md")));
        var coreSources = Directory
            .GetFiles(Path.Combine(root, "src", "Irix.Core"), "*.cs", SearchOption.AllDirectories)
            .Select(path => NormalizeLineEndings(File.ReadAllText(path)))
            .ToArray();
        var drawingSources = Directory
            .GetFiles(Path.Combine(root, "src", "Irix.Drawing"), "*.cs", SearchOption.AllDirectories)
            .Select(path => NormalizeLineEndings(File.ReadAllText(path)))
            .ToArray();

        Assert.Contains("## Public Material Authoring Policy Preflight", styleDesign);
        Assert.Contains("Semantic token boundary", styleDesign);
        Assert.Contains("Public UI code describes background paint through `Paint` and `VirtualNodeProperty.Background`, or inward border intent through `BorderStroke` and `VirtualNodeProperty.Border`", styleDesign);
        Assert.Contains("public authoring exposes semantic `Paint`, typed `BorderStroke`, `VirtualNodeProperty.Background`, and `VirtualNodeProperty.Border`", colorDesign);

        Assert.True(typeof(BorderStroke).IsPublic);
        Assert.False(typeof(DrawMaterialKind).IsPublic);
        Assert.False(typeof(DrawMaterialBackendCapabilities).IsPublic);
        Assert.False(typeof(DrawMaterialFallbackReason).IsPublic);
        Assert.False(typeof(DrawMaterialOutputMappingResult).IsPublic);
        Assert.False(typeof(DrawMaterialOutputDiagnostics).IsPublic);

        var publicStyleMethods = typeof(StyleDeclaration)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(FormatMethodSignature)
            .ToArray();
        AssertDoesNotContainAny(
            publicStyleMethods,
            "DrawMaterial",
            "DrawMaterialKind",
            "DrawMaterialBackendCapabilities",
            "ResourceHandle",
            "Brush",
            "Material",
            "Gradient",
            "Image");

        var publicStyleValueMethods = typeof(StyleValue)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(FormatMethodSignature)
            .ToArray();
        AssertDoesNotContainAny(
            publicStyleValueMethods,
            "DrawMaterial",
            "ResourceHandle",
            "Brush",
            "Material",
            "Gradient",
            "Image");

        var publicVirtualNodePropertyMethods = typeof(VirtualNodeProperty)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(FormatMethodSignature)
            .ToArray();
        AssertDoesNotContainAny(
            publicVirtualNodePropertyMethods,
            "DrawMaterial",
            "DrawMaterialKind",
            "DrawMaterialBackendCapabilities",
            "Brush",
            "Material",
            "Image");

        var publicPropertyValueMethods = typeof(PropertyValue)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(FormatMethodSignature)
            .ToArray();
        AssertDoesNotContainAny(
            publicPropertyValueMethods,
            "DrawMaterial",
            "ResourceHandle",
            "Brush",
            "Material",
            "Gradient",
            "Image");

        foreach (var source in coreSources)
        {
            Assert.DoesNotContain("using Irix.Drawing;", source);
            Assert.DoesNotContain("DrawMaterial", source);
            Assert.DoesNotContain("IFrameBrushResolver", source);
            Assert.DoesNotContain("DrawMaterialBackendCapabilities", source);
            Assert.DoesNotContain("DrawMaterialFallbackReason", source);
        }

        foreach (var source in drawingSources)
        {
            Assert.DoesNotContain("public interface IFrameBrushResolver", source);
            Assert.DoesNotContain("public ResourceHandle AddBrush", source);
            Assert.DoesNotContain("public DrawMaterial ResolveBrush", source);
        }
    }

    [Fact]
    public void Stored_display_scale_values_are_normalized_at_ingress()
    {
        var root = FindRepoRoot();
        var sourceFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("_displayScale = scale;", source);
            Assert.DoesNotContain("_displayScale = displayScale;", source);
            Assert.DoesNotContain("displayScale = newScale;", source);
            Assert.DoesNotContain("var displayScale = screen.Scale;", source);
            Assert.DoesNotContain("var displayScale = platformHost.Screens[0].Scale;", source);
            Assert.DoesNotContain("new DisplayScale(newDpi / 96f, newDpi / 96f);", source);
        }
    }

    [Fact]
    public void Public_hit_test_api_names_distinguish_physical_and_logical_pixels()
    {
        var compositorMethods = typeof(DrawingBackendCompositor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains("TryGetActionIdAtPhysicalPixel", compositorMethods);
        Assert.DoesNotContain("TryGetActionIdAt", compositorMethods);

        var renderingDir = Path.Combine(FindRepoRoot(), "src", "Irix.Rendering");
        var sourceFiles = Directory.GetFiles(renderingDir, "*.cs", SearchOption.AllDirectories);
        foreach (var file in sourceFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("TryGetCandidateActionIdAt(", source);
        }
    }

    [Fact]
    public void Irix_Core_has_no_ResolveString_text_api()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Core"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("ResolveString", content);
        }
    }

    [Fact]
    public void Irix_Rendering_has_no_string_ActionId()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Rendering"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("string ActionId", content);
            Assert.DoesNotContain("string? ActionId", content);
            Assert.DoesNotContain("out string actionId", content);
        }
    }

    [Fact]
    public void Irix_Rendering_has_no_text_string_materialization()
    {
        var sourceFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "src", "Irix.Rendering"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("ResolveString", content);
            Assert.DoesNotContain("string? Text", content);
            Assert.DoesNotContain("string Text", content);
            Assert.DoesNotContain("Resolve(command.Text).ToString()", content);
        }
    }

    [Fact]
    public void DrawingBackendCompositorShadowProbe_records_typed_backend_calls()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Rendering", "DrawingBackendCompositorShadowProbe.cs"));

        Assert.Contains("internal readonly struct DrawingBackendCall", source);
        Assert.Contains("IReadOnlyList<DrawingBackendCall> Calls", source);
        Assert.Contains("List<DrawingBackendCall> _calls", source);
        Assert.Contains("DrawingBackendCall.Execute(commands.Length)", source);
        Assert.DoesNotContain("IReadOnlyList<string> Calls", source);
        Assert.DoesNotContain("List<string> _calls", source);
        Assert.DoesNotContain("_calls.Add(\"BeginFrame\")", source);
        Assert.DoesNotContain("_calls.Add($\"Execute:{commands.Length}\")", source);
        Assert.DoesNotContain("_calls.Add(\"EndFrame\")", source);
    }

    [Fact]
    public void StyleOnlyPatchPlanDiagnosticSnapshot_uses_typed_case_identifier()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Rendering", "StyleOnlyPatchPlan.cs"));

        Assert.Contains("internal enum StyleOnlyPatchPlanCase : byte", source);
        Assert.Contains("StyleOnlyPatchPlanCase Case", source);
        Assert.Contains("FromPlan(StyleOnlyPatchPlanCase @case", source);
        Assert.DoesNotContain("string CaseName", source);
        Assert.DoesNotContain("public string CaseName", source);
        Assert.DoesNotContain("FromPlan(string caseName", source);
    }

    [Fact]
    public void CounterLayoutDiagnostics_uses_typed_dirty_classifications()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "CounterApplication.optional-diagnostics.cs"));

        Assert.Contains("internal readonly struct CounterLayoutDiagnostics : IEquatable<CounterLayoutDiagnostics>", source);
        Assert.Contains("LayoutDirtyClassificationList LastDirtyClassifications", source);
        Assert.DoesNotContain("string LastDirtyClassifications", source);
        Assert.DoesNotContain("new CounterLayoutDiagnostics(long LayoutRebuildCount, LayoutRebuildReason LastLayoutRebuildReason, string LastDirtyClassifications)", source);
    }

    [Fact]
    public void Viewport_diagnostics_use_typed_scale_and_rebuild_state()
    {
        var counterSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "CounterApplication.optional-diagnostics.cs"));
        var snapshotSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "DiagnosticsSnapshots.optional-diagnostics.cs"));

        Assert.Contains("ViewportScaleMode ScaleMode", counterSource);
        Assert.DoesNotContain("string ScaleMode", counterSource);
        Assert.Contains("internal enum ViewportDpiAwareness : byte", snapshotSource);
        Assert.Contains("internal enum ViewportScaleMode : byte", snapshotSource);
        Assert.Contains("LayoutRebuildReason LayoutRebuildReason", snapshotSource);
        Assert.Contains("ViewportDpiAwareness DpiAwareness", snapshotSource);
        Assert.Contains("ViewportScaleMode ScaleMode", snapshotSource);
        Assert.DoesNotContain("string LayoutRebuildReason", snapshotSource);
        Assert.DoesNotContain("string DpiAwareness", snapshotSource);
        Assert.DoesNotContain("string ScaleMode", snapshotSource);
    }

    [Fact]
    public void Scroll_feedback_uses_typed_container_id()
    {
        var feedbackSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "ScrollFeedback.cs"));
        var translatorSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "WindowDrawCommandTranslator.cs"));

        Assert.Contains("internal readonly struct ScrollContainerId", feedbackSource);
        Assert.Contains("ScrollContainerId ContainerId", feedbackSource);
        Assert.DoesNotContain("string ContainerId", feedbackSource);
        Assert.Contains("new ScrollContainerId(diagnostics.DfsIndex)", translatorSource);
        Assert.DoesNotContain("ContainerId: $\"dfs:{diagnostics.DfsIndex}\"", translatorSource);
    }

    [Fact]
    public void Render_style_preset_diagnostics_use_typed_id_not_string_name()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Rendering", "RenderStylePreset.cs"));
        var formatterSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "DiagnosticsFormatter.optional-diagnostics.cs"));

        Assert.Contains("internal readonly struct RenderStylePresetId", source);
        Assert.Contains("RenderStylePresetId Default", source);
        Assert.Contains("BuildStylePresetDiagnosticLines(RenderStylePresetId presetId, RenderStylePreset preset)", formatterSource);
        Assert.Contains("FormatStylePresetName(RenderStylePresetId presetId)", formatterSource);
        Assert.DoesNotContain("const string DefaultName", source);
        Assert.DoesNotContain("string DefaultName", source);
        Assert.DoesNotContain("BuildStylePresetDiagnosticLines(string presetName", formatterSource);
    }

    [Fact]
    public void Input_diagnostics_snapshot_uses_typed_events_not_string_lines()
    {
        var snapshotSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "DiagnosticsSnapshots.optional-diagnostics.cs"));
        var runnerSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Poc", "InputDiagnosticRunner.optional-diagnostics.cs"));

        Assert.Contains("IReadOnlyList<InputDiagnosticButtonState> ButtonStates", snapshotSource);
        Assert.Contains("IReadOnlyList<InputDiagnosticOwnershipStep> OwnershipSteps", snapshotSource);
        Assert.Contains("IReadOnlyList<InputOwnershipEvent> Events", snapshotSource);
        Assert.Contains("IReadOnlyList<InputDirtyReasonDiagnostic> DirtyReasons", snapshotSource);
        Assert.DoesNotContain("IReadOnlyList<string> OrderedDiagnosticLines", snapshotSource);
        Assert.DoesNotContain("IReadOnlyList<string> OwnershipLines", snapshotSource);
        Assert.DoesNotContain("IReadOnlyList<string> ButtonVisualStateLines", snapshotSource);
        Assert.DoesNotContain("IReadOnlyList<string> EventLines", snapshotSource);
        Assert.DoesNotContain("IReadOnlyList<string> DirtyReasonLines", snapshotSource);
        Assert.DoesNotContain("new List<string>()", runnerSource);
    }

    [Fact]
    public void Backend_clip_text_diagnostics_use_platform_device_error_value()
    {
        var root = FindRepoRoot();
        var snapshotSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "DiagnosticsSnapshots.optional-diagnostics.cs"));
        var diagnosticSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Platform", "DeviceErrorDiagnostic.cs"));
        var platformWindowsSource = string.Concat(Directory.GetFiles(Path.Combine(root, "src", "Irix.Platform.Windows"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        var pocSource = string.Concat(Directory.GetFiles(Path.Combine(root, "src", "Irix.Poc"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DeviceErrorDiagnostic>());
        Assert.Contains("internal readonly struct DeviceErrorDiagnostic", diagnosticSource);
        Assert.Contains("DeviceErrorSite Site", diagnosticSource);
        Assert.Contains("DeviceErrorKind Kind", diagnosticSource);
        Assert.Contains("DeviceErrorDiagnostic DeviceError", snapshotSource);
        Assert.DoesNotContain("FromMessage", diagnosticSource);
        Assert.DoesNotContain("FromNullable", diagnosticSource);
        Assert.DoesNotContain("string? Message", diagnosticSource);
        Assert.DoesNotContain("private readonly string", diagnosticSource);
        Assert.DoesNotContain("string DeviceErrorReason", snapshotSource);
        Assert.DoesNotContain("public string DeviceErrorReason", snapshotSource);
        Assert.DoesNotContain("DeviceErrorReason", platformWindowsSource);
        Assert.DoesNotContain("DeviceErrorReason", pocSource);
    }

    [Fact]
    public void Irix_Drawing_TextStyle_uses_value_font_family()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Drawing", "DrawingPrimitives.cs"));

        Assert.Contains("public enum TextFontFamily : byte", source);
        Assert.Contains("TextFontFamily FontFamily", source);
        Assert.Contains("public TextFontFamily FontFamily", source);
        Assert.DoesNotContain("string FontFamily", source);
        Assert.DoesNotContain("public string FontFamily", source);
        Assert.DoesNotContain("string.IsNullOrWhiteSpace(FontFamily)", source);
        Assert.DoesNotContain("FontFamily: \"", source);
    }

    [Fact]
    public void Irix_Platform_WindowContentElement_uses_text_slice_boundary()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Platform", "PlatformAbstractions.cs"));

        Assert.Contains("TextSlice Text = default", source);
        Assert.Contains("public TextSlice Text", source);
        Assert.Contains("void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver)", source);
        Assert.DoesNotContain("string? Text", source);
        Assert.DoesNotContain("string Text", source);
        Assert.DoesNotContain("public string Text", source);
    }

    // ── Allocation baseline: layout + record hot path ────────────

    [Fact]
    public void Render_pipeline_steady_state_allocation_baseline()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = new VirtualNode(
            VirtualNodeKind.Container,
            key: 1,
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Header", new NodeKey(2)),
                VirtualNodeTestBuilder.Button(_arena, "Click", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(1))),
                .. Enumerable.Range(0, 10).Select(i => VirtualNodeBuilder.Text(_arena, $"Row {i}", new NodeKey((uint)(100 + i))))
            ]);

        // Warmup: let the pipeline retain state and pools stabilize
        var snapshot = _arena.GetOrCreateSnapshot();
        for (var i = 0; i < 3; i++)
        {
            pipeline.Build(root, viewport, snapshot);
        }

        // Measure steady-state allocation for a single Build call
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        pipeline.Build(root, viewport, snapshot);

        var after = GC.GetAllocatedBytesForCurrentThread();
        var allocated = after - before;

        // Baseline guard: retained warm builds should stay comfortably below the old 16 KB ceiling.
        Assert.True(allocated < 8_192,
            $"Steady-state Build allocated {allocated} bytes, expected < 8192");
    }

    private static string RecordStructPattern() => @"\b(?:readonly\s+record\s+struct|record\s+readonly\s+struct|record\s+struct)\b";

    private static string StringStateMemberPattern() => @"^\s*(?:public|internal|protected|private)\s+(?:static\s+)?(?:(?:readonly|const)\s+)?string\??\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:\{|[=;]|=>)";

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Irix.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repo root (Irix.slnx)");
    }

    private static IEnumerable<FieldInfo> GetPublicVirtualPropertyKeyFields() =>
        typeof(VirtualPropertyKey).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(VirtualPropertyKey));

    private static string ExtractSourceBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find source marker: {startMarker}");
        Assert.True(end > start, $"Could not find source marker: {endMarker}");
        return source[start..end];
    }

    private static void AssertDoesNotContainAny(string source, params string[] forbidden)
    {
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, source);
        }
    }

    private static void AssertDoesNotContainAny(IReadOnlyCollection<string> source, params string[] forbidden)
    {
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, source);
        }
    }

    private static string FormatMethodSignature(MethodInfo method) =>
        $"{method.ReturnType.FullName} {method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})";

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private sealed class NullBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
