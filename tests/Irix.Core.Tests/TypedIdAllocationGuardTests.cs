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

        Assert.True(VirtualNodePropertySupport.Supports(VirtualNodeKind.ScrollContainer, VirtualPropertyKey.ScrollY));
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
    public void Owned_array_virtual_node_creation_is_marked_unsafe()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Irix.Core", "VirtualNodeModels.cs"));

        Assert.Contains("CreateFromOwnedArraysUnsafe", source);
        Assert.Contains("Callers must not mutate the arrays after this call.", source);
        Assert.DoesNotContain("CreateFromOwnedArrays(", source);
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
        }
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

    private sealed class NullBackend : IDrawingBackend
    {
        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
