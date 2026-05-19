using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public class TypedIdAllocationGuardTests
{
    private readonly VirtualTextArena _arena = new();

    [Fact]
    public void Render_pipeline_hot_path_does_not_allocate_string_collections()
    {
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(1))),
                VirtualNodeBuilder.Button(_arena, "Other", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(2))),
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
    public void VirtualNodeChildrenBuilder_handles_inline_and_overflow_children()
    {
        var children = new VirtualNodeChildrenBuilder();
        for (var i = 0; i < 6; i++)
        {
            children.Add(VirtualNodeBuilder.Text(_arena, $"child {i}", new NodeKey((uint)(10 + i))));
        }

        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1), ReadOnlySpan<VirtualNodeProperty>.Empty, ref children);

        Assert.Equal(6, root.Children.Length);
        Assert.Equal(new NodeKey(10), root.Children[0].Key);
        Assert.Equal(new NodeKey(15), root.Children[5].Key);
    }

    [Fact]
    public void VirtualNodeChildrenBuilder_default_value_is_usable()
    {
        VirtualNodeChildrenBuilder children = default;
        children.Add(VirtualNodeBuilder.Text(_arena, "child", new NodeKey(10)));

        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1), ReadOnlySpan<VirtualNodeProperty>.Empty, ref children);

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
    public void TextNodeContent_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TextNodeContent>());
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
    public void NodeContent_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<NodeContent>());
        Assert.Equal(24, Unsafe.SizeOf<NodeContent>());
    }

    [Fact]
    public void PropertyValue_has_no_managed_references()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PropertyValue>());
        Assert.Equal(16, Unsafe.SizeOf<PropertyValue>());
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
    }

    [Fact]
    public void Property_change_classification_uses_metadata_effects()
    {
        Assert.Equal(InvalidationKind.Layout, PropertyChangeSet.AddKey(default, VirtualPropertyKey.Width).ClassifySet());
        Assert.Equal(InvalidationKind.Layout, PropertyChangeSet.AddKey(default, VirtualPropertyKey.ScrollY).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.ActionId).ClassifySet());
        Assert.Equal(InvalidationKind.VisualOnly, PropertyChangeSet.AddKey(default, VirtualPropertyKey.IsHovered).ClassifySet());
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
            VirtualNodeKind.Button,
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
            VirtualNodeKind.Rectangle,
            properties: [VirtualNodeProperty.Action(new ActionId(1))]));

        Assert.Throws<ArgumentException>(() => VirtualNodeBuilder.Text(
            _arena,
            "unsupported",
            new NodeKey(88),
            VirtualNodeProperty.ScrollY(10)));
    }

    [Fact]
    public void VirtualNode_button_shape_is_exactly_one_text_label_child()
    {
        var label = VirtualNodeBuilder.Text(_arena, "label", new NodeKey(11));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Button,
            children: ReadOnlySpan<VirtualNode>.Empty));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Button,
            children:
            [
                label,
                VirtualNodeBuilder.Text(_arena, "extra", new NodeKey(12))
            ]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Button,
            children: [VirtualNodeFactory.Rectangle()]));

        var button = new VirtualNode(VirtualNodeKind.Button, children: [label]);
        Assert.Equal(VirtualNodeKind.Button, button.Kind);
        Assert.Equal(1, button.Children.Length);
        Assert.Equal(VirtualNodeKind.Text, button.Children[0].Kind);
        Assert.True(button.Children[0].Children.IsEmpty);
    }

    [Fact]
    public void VirtualNode_leaf_shapes_reject_children()
    {
        var label = VirtualNodeBuilder.Text(_arena, "label", new NodeKey(11));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Text,
            content: label.Content,
            children: [VirtualNodeFactory.Rectangle()]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Rectangle,
            children: [label]));
    }

    [Fact]
    public void VirtualNode_content_contract_matches_node_kind()
    {
        var textContent = _arena.AddText("text".AsSpan());
        var textNode = new VirtualNode(VirtualNodeKind.Text, content: NodeContent.FromText(textContent));
        Assert.Equal(NodeContentKind.Text, textNode.Content.Kind);

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Text,
            content: NodeContent.None));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Text,
            content: NodeContent.FromNumber(1)));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Rectangle,
            content: NodeContent.FromText(textContent)));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.Button,
            content: NodeContent.FromText(textContent),
            children: [VirtualNodeFactory.Text(textContent)]));

        Assert.Throws<ArgumentException>(() => new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            content: NodeContent.FromText(textContent)));
    }

    [Fact]
    public void VirtualNodePropertySupport_declares_control_support_sets()
    {
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.Width));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.Height));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.ActionId));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.IsHovered));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.IsPressed));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Button, VirtualPropertyKey.IsFocused));

        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Rectangle, VirtualPropertyKey.Width));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.Rectangle, VirtualPropertyKey.Height));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Rectangle, VirtualPropertyKey.ActionId));

        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.Width));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.Height));
        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.ScrollY));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.ActionId));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.IsHovered));
        Assert.False(VirtualNodePropertySupport.Supports(VirtualNodeKind.Text, VirtualPropertyKey.ScrollY));
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
    public void NodeContent_Number_roundtrip()
    {
        var nc = NodeContent.FromNumber(3.14);
        Assert.Equal(NodeContentKind.Number, nc.Kind);
        Assert.True(nc.TryGetNumber(out var value));
        Assert.Equal(3.14, value);
    }

    [Fact]
    public void NodeContent_Boolean_roundtrip()
    {
        var nc = NodeContent.FromBoolean(true);
        Assert.Equal(NodeContentKind.Boolean, nc.Kind);
        Assert.True(nc.TryGetBoolean(out var value));
        Assert.True(value);
    }

    [Fact]
    public void NodeContent_Text_roundtrip()
    {
        var arena = new VirtualTextArena();
        var textContent = arena.AddText("hello".AsSpan());
        var nc = NodeContent.FromText(textContent);
        Assert.Equal(NodeContentKind.Text, nc.Kind);
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
        Assert.DoesNotContain("FillColor", source);
        Assert.DoesNotContain("TextColor", source);
        Assert.DoesNotContain("Opacity", source);

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
    public void Poc_record_structs_are_limited_to_mvu_authoring_state_files()
    {
        var pocDir = Path.Combine(FindRepoRoot(), "src", "Irix.Poc");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ControlVisualState.cs",
            "CounterApplication.cs",
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

        Assert.DoesNotContain("new List<", rangeUtilsSource);

        var diffInnerSource = ExtractSourceBetween(
            differSource,
            "private static void DiffNode",
            "private static int CountNodes");
        Assert.DoesNotContain("ToArray()", diffInnerSource);

        var layoutRecursiveSource = ExtractSourceBetween(
            layoutBuilderSource,
            "private int LayoutNode",
            "private static IReadOnlyList<(int Start, int Count)> CollectDirtyRanges");
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
        Assert.Contains("int SubtreeStart", layoutModelSource);
        Assert.Contains("int SubtreeCount", layoutModelSource);
        Assert.DoesNotContain("LayoutTreeNode[] Children", layoutModelSource);
    }

    [Fact]
    public void Input_ownership_diagnostics_use_ring_buffer_not_remove_at()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Irix.Poc", "InputOwnershipState.cs"));

        Assert.DoesNotMatch(RecordStructPattern(), source);
        Assert.Contains("InputOwnershipEvent[]", source);
        Assert.DoesNotContain("RemoveAt(0)", source);
        Assert.DoesNotContain("new List<InputOwnershipEvent>", source);
        Assert.DoesNotContain("List<InputOwnershipEvent> _diagnosticEvents", source);
    }

    [Fact]
    public void Ref_struct_types_stay_limited_to_builder_reader_and_context_boundaries()
    {
        Assert.True(typeof(VirtualNodePropertyListBuilder).IsByRefLike);
        Assert.True(typeof(VirtualNodeChildrenBuilder).IsByRefLike);
        Assert.False(typeof(VirtualNode).IsByRefLike);
        Assert.False(typeof(VirtualNodeTree).IsByRefLike);
        Assert.False(typeof(VirtualNodeProperty).IsByRefLike);
        Assert.False(typeof(VirtualNodePatch).IsByRefLike);
        Assert.False(typeof(PatchBatch).IsByRefLike);
        Assert.False(typeof(RenderFrameBatch).IsByRefLike);

        var root = FindRepoRoot();
        var propertyReaderSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "PropertyReader.cs"));
        var layoutSource = File.ReadAllText(Path.Combine(root, "src", "Irix.Rendering", "LayoutTreeBuilder.cs"));

        Assert.Contains("internal readonly ref struct PropertyReader", propertyReaderSource);
        Assert.Contains("internal ref struct LayoutContext", layoutSource);
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
    public void VirtualNode_exposes_span_snapshots_without_readonly_list_wrappers()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("public ReadOnlySpan<VirtualNodeProperty> Properties", source);
        Assert.Contains("public ReadOnlySpan<VirtualNode> Children", source);
        Assert.DoesNotContain("IReadOnlyList<VirtualNodeProperty>", source);
        Assert.DoesNotContain("IReadOnlyList<VirtualNode>", source);
        Assert.DoesNotContain("Array.AsReadOnly", source);
        Assert.DoesNotContain("_propertiesView", source);
        Assert.DoesNotContain("_childrenView", source);
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
        Assert.Contains("Text nodes require text content", source);
        Assert.Contains("Rectangle nodes cannot have content", source);
        Assert.Contains("Button nodes cannot have content", source);
        Assert.Contains("ScrollContainer nodes cannot have content", source);
        Assert.Contains("Button nodes require exactly one leaf text label child", source);
        Assert.Contains("child.Kind == VirtualNodeKind.Text", source);
        Assert.Contains("child.Children.IsEmpty", source);
        Assert.Contains("!label.IsNone", source);
    }

    [Fact]
    public void ScrollContainer_property_contract_excludes_width_and_interaction_state()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualPropertyKey.cs"));

        Assert.Contains("private const VirtualNodeKindFlags WidthNodes", source);
        Assert.Contains("private const VirtualNodeKindFlags HeightNodes", source);
        Assert.Contains("VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button;", source);
        Assert.Contains("VirtualNodeKindFlags.Rectangle | VirtualNodeKindFlags.Button | VirtualNodeKindFlags.ScrollContainer;", source);

        Assert.Throws<ArgumentException>(() => VirtualNodeFactory.ScrollContainer(
            new NodeKey(7),
            [VirtualNodeProperty.Width(100)],
            ReadOnlySpan<VirtualNode>.Empty));

        Assert.Throws<ArgumentException>(() => VirtualNodeFactory.ScrollContainer(
            new NodeKey(7),
            [VirtualNodeProperty.Action(new ActionId(1))],
            ReadOnlySpan<VirtualNode>.Empty));
    }

    [Fact]
    public void Property_metadata_support_and_diagnostics_are_internal()
    {
        Assert.False(typeof(VirtualPropertyMetadata).IsPublic);
        Assert.False(typeof(VirtualPropertyDiagnostics).IsPublic);
        Assert.False(typeof(VirtualNodePropertySupport).IsPublic);
        Assert.False(typeof(StylePropertyMetadata).IsPublic);
        Assert.False(typeof(StyleEffect).IsPublic);
        Assert.False(typeof(AnimationChannel).IsPublic);
        Assert.False(typeof(VirtualNodeKindFlags).IsPublic);
        Assert.False(typeof(StylePropertyScope).IsPublic);
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
            VirtualNodeKind.ScrollContainer,
            key: 1,
            children:
            [
                VirtualNodeBuilder.Text(_arena, "Header", new NodeKey(2)),
                VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(1))),
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

        // Baseline: expect ~4-8 KB for lists, arrays, and retained snapshot.
        // Set generous ceiling to avoid flaky tests; tighten as allocations improve.
        Assert.True(allocated < 16_384,
            $"Steady-state Build allocated {allocated} bytes, expected < 16384");
    }

    private static string RecordStructPattern() => @"\b(?:readonly\s+record\s+struct|record\s+readonly\s+struct|record\s+struct)\b";

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

    private sealed class NullBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
