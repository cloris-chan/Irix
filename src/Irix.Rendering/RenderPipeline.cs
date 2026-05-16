using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed class RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle, ControlVisualStateResolver visualStateResolver)
{
    private readonly LayoutTreeBuilder _layoutTreeBuilder = new(layoutStyle);
    private readonly DrawCommandRecorder _drawCommandRecorder = new(drawingStyle, visualStateResolver);

    public RenderPipeline()
        : this(RenderStylePreset.Default)
    {
    }

    public RenderPipeline(RenderStylePreset stylePreset)
        : this(stylePreset.Layout, stylePreset.Drawing, stylePreset.VisualStates)
    {
    }

    public RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle)
        : this(layoutStyle, drawingStyle, ControlVisualStateResolver.Default)
    {
    }

    private VirtualNode _retainedRoot;
    private TextBufferSnapshot _retainedTextSnapshot;
    private LayoutTreeResult? _retainedLayoutResult;
    private IReadOnlyList<LayoutElement>? _retainedLayout;
    private PixelRectangle _retainedViewport;
    private readonly RetainedRenderFrame _retainedFrame = new();

    /// <summary>
    /// The dirty element ranges from the last Build call, if any.
    /// Each tuple is (startIndex, count) into the flat LayoutElement array.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyElementRanges { get; private set; } = [];

    /// <summary>
    /// The dirty draw command ranges from the last Build call, if any.
    /// Each tuple is (startIndex, count) into the DrawCommand batch.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges { get; private set; } = [];

    /// <summary>
    /// The element→command range mapping from the last Build call.
    /// <c>LastElementCommandRanges[elementIndex]</c> gives (commandStart, commandCount).
    /// </summary>
    public ElementCommandRange[] LastElementCommandRanges { get; private set; } = [];

    /// <summary>
    /// The retained render frame from the last Build call.
    /// Contains the retained command buffer, resource resolver, and dirty ranges.
    /// </summary>
    public RetainedRenderFrame RetainedFrame => _retainedFrame;

    public PixelRectangle LastViewport { get; private set; }

    public long LayoutRebuildCount { get; private set; }

    public LayoutRebuildReason LastLayoutRebuildReason { get; private set; }

    public IReadOnlyList<LayoutDirtyClassification> LastDirtyClassifications { get; private set; } = [];

    public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; private set; }

    /// <summary>
    /// The layout tree result from the last Build call, if available.
    /// Exposes scroll container diagnostics and tree structure.
    /// </summary>
    public LayoutTreeResult? LastLayoutResult => _retainedLayoutResult;

    /// <summary>
    /// The MaxScrollY from the first ScrollContainer in the last Build call.
    /// 0 if no ScrollContainer or no scroll needed.
    /// </summary>
    public double LastMaxScrollY { get; private set; }

    /// <summary>
    /// Build a render frame for the given root and viewport.
    /// When <paramref name="dirtyNodes"/> is non-null, the layout tree is rebuilt
    /// and dirty element/command ranges are computed. When null (render request),
    /// reuses the retained layout if tree and viewport match.
    /// </summary>
    public RenderFrameBatch Build(VirtualNode root, PixelRectangle viewportBounds, TextBufferSnapshot textSnapshot, IReadOnlyList<int>? dirtyNodes = null, TextBufferSnapshot? prevTextSnapshot = null, VirtualNode previousRoot = default)
    {
        LastViewport = viewportBounds;
        var hadRetainedLayout = _retainedLayout is not null;
        var classifyOldRoot = hadRetainedLayout && previousRoot.Kind != VirtualNodeKind.None ? previousRoot : _retainedRoot;
        var classifyOldSnapshot = hadRetainedLayout && previousRoot.Kind != VirtualNodeKind.None ? prevTextSnapshot : _retainedTextSnapshot;
        var treeChanged = _retainedLayout is null || !VirtualNodeDiffer.NodesEqual(classifyOldRoot, root, classifyOldSnapshot ?? textSnapshot, textSnapshot);
        var viewportChanged = _retainedViewport != viewportBounds;
        var hasDirty = dirtyNodes is { Count: > 0 };
        var dirtyClassifications = hasDirty && hadRetainedLayout
            ? ClassifyDirtyNodes(classifyOldRoot, root, dirtyNodes!, classifyOldSnapshot ?? textSnapshot, textSnapshot)
            : [];
        LastDirtyClassifications = dirtyClassifications;
        LastLayoutRebuildReason = ResolveLayoutRebuildReason(hadRetainedLayout, treeChanged, viewportChanged, hasDirty, dirtyClassifications);

        if (treeChanged || viewportChanged || hasDirty)
        {
            LayoutRebuildCount++;
            _retainedLayoutResult = _layoutTreeBuilder.BuildLayoutTree(root, viewportBounds, dirtyNodes);
            _retainedLayout = _retainedLayoutResult.Elements;
            _retainedRoot = root;
            _retainedTextSnapshot = textSnapshot;
            _retainedViewport = viewportBounds;

            // Extract MaxScrollY from the first ScrollContainer's diagnostics
            LastMaxScrollY = _retainedLayoutResult.ScrollDiagnostics.Count > 0
                ? _retainedLayoutResult.ScrollDiagnostics[0].MaxScrollY
                : 0;
        }

        var layout = _retainedLayout!;
        var dirtyElementRanges = hasDirty && _retainedLayoutResult is not null
            ? _retainedLayoutResult.DirtyElementRanges
            : null;

        LastDirtyElementRanges = dirtyElementRanges ?? [];

        var result = _drawCommandRecorder.Record(layout, dirtyElementRanges, _retainedTextSnapshot);
        LastDirtyCommandRanges = result.DirtyCommandRanges;
        LastElementCommandRanges = result.ElementCommandRanges;

        var hitTargets = BuildHitTargets(layout);
        var batch = new RenderFrameBatch(result.Commands, hitTargets, result.Resources, result.DirtyCommandRanges);
        LastRetainedInputSnapshot = new RenderPipelineRetainedInputSnapshot(
            _retainedLayoutResult!,
            [.. result.ElementCommandRanges],
            [.. hitTargets],
            _retainedRoot,
            _retainedViewport,
            [.. LastDirtyClassifications],
            [.. LastDirtyElementRanges],
            [.. LastDirtyCommandRanges],
            LastLayoutRebuildReason,
            _retainedTextSnapshot);

        // Update retained render frame: try partial apply when dirty ranges exist,
        // which only succeeds when resources are the same instance (same frame scope).
        // Falls back to full apply when resources differ or no dirty ranges.
        if (!hasDirty || result.DirtyCommandRanges.Count == 0 || !_retainedFrame.TryApplyPartial(batch))
        {
            _retainedFrame.ApplyFull(batch);
        }

        return batch;
    }

    private static LayoutRebuildReason ResolveLayoutRebuildReason(
        bool hadRetainedLayout,
        bool treeChanged,
        bool viewportChanged,
        bool hasDirty,
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications)
    {
        if (!hadRetainedLayout)
        {
            return LayoutRebuildReason.TreeStructure;
        }

        if (viewportChanged)
        {
            return LayoutRebuildReason.ViewportChanged;
        }

        if (hasDirty)
        {
            var reason = LayoutRebuildReason.None;
            foreach (var classification in dirtyClassifications)
            {
                reason = MaxReason(reason, classification.Reason);
            }

            return reason == LayoutRebuildReason.None ? LayoutRebuildReason.StyleOnly : reason;
        }

        return treeChanged ? LayoutRebuildReason.TreeStructure : LayoutRebuildReason.None;
    }

    private static IReadOnlyList<LayoutDirtyClassification> ClassifyDirtyNodes(VirtualNode previousRoot, VirtualNode nextRoot, IReadOnlyList<int> dirtyNodes, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        var classifications = new List<LayoutDirtyClassification>(dirtyNodes.Count);
        var seen = new HashSet<int>();
        foreach (var dirtyNode in dirtyNodes)
        {
            if (!seen.Add(dirtyNode))
            {
                continue;
            }

            var reason = TryFindNode(previousRoot, dirtyNode, out var previousNode) && TryFindNode(nextRoot, dirtyNode, out var nextNode)
                ? ClassifyNodeChange(previousNode, nextNode, prevSnapshot, nextSnapshot)
                : new DirtyNodeClassification(LayoutRebuildReason.TreeStructure, InvalidationKind.TreeStructure);
            classifications.Add(new LayoutDirtyClassification(dirtyNode, reason.Reason, reason.InvalidationKind));
        }

        return classifications;
    }

    private static DirtyNodeClassification ClassifyNodeChange(VirtualNode previousNode, VirtualNode nextNode, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        if (previousNode.Kind != nextNode.Kind || previousNode.Key != nextNode.Key || ChildrenShapeChanged(previousNode.Children, nextNode.Children))
        {
            return new DirtyNodeClassification(LayoutRebuildReason.TreeStructure, InvalidationKind.TreeStructure);
        }

        var classification = DirtyNodeClassification.None;
        if (!VirtualNodeDiffer.ContentEqual(previousNode.Content, nextNode.Content, prevSnapshot, nextSnapshot))
        {
            classification = DirtyNodeClassification.Max(classification, ClassifyContentChange(previousNode.Content, nextNode.Content));
        }

        if (!PropertiesEqual(previousNode.Properties, nextNode.Properties))
        {
            classification = DirtyNodeClassification.Max(classification, ClassifyPropertyChanges(previousNode.Properties, nextNode.Properties));
        }

        return classification.Reason == LayoutRebuildReason.None
            ? new DirtyNodeClassification(LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)
            : classification;
    }

    private static DirtyNodeClassification ClassifyContentChange(NodeContent previousContent, NodeContent nextContent)
    {
        return previousContent.Kind == NodeContentKind.Text || nextContent.Kind == NodeContentKind.Text
            ? new DirtyNodeClassification(LayoutRebuildReason.TextSizeAffecting, InvalidationKind.TextMeasure)
            : new DirtyNodeClassification(LayoutRebuildReason.LayoutAffecting, InvalidationKind.Layout);
    }

    private static DirtyNodeClassification ClassifyPropertyChanges(IReadOnlyList<VirtualNodeProperty> previousProperties, IReadOnlyList<VirtualNodeProperty> nextProperties)
    {
        var changeSet = GetChangedPropertySet(previousProperties, nextProperties);
        var invalidationKind = changeSet.ClassifySet();
        return new DirtyNodeClassification(invalidationKind.ToLayoutRebuildReason(), invalidationKind);
    }

    private static PropertyChangeSet GetChangedPropertySet(IReadOnlyList<VirtualNodeProperty> previousProperties, IReadOnlyList<VirtualNodeProperty> nextProperties)
    {
        var changeSet = default(PropertyChangeSet);

        foreach (var property in previousProperties)
        {
            if (!TryFindProperty(nextProperties, property.Key, out var nextProperty)
                || property.Value != nextProperty.Value)
            {
                changeSet = PropertyChangeSet.AddKey(changeSet, property.Key);
            }
        }

        foreach (var property in nextProperties)
        {
            if (!TryFindProperty(previousProperties, property.Key, out _))
            {
                changeSet = PropertyChangeSet.AddKey(changeSet, property.Key);
            }
        }

        return changeSet;
    }

    private static bool TryFindProperty(IReadOnlyList<VirtualNodeProperty> properties, VirtualPropertyKey key, out VirtualNodeProperty property)
    {
        foreach (var candidate in properties)
        {
            if (candidate.Key == key)
            {
                property = candidate;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static bool PropertiesEqual(IReadOnlyList<VirtualNodeProperty> previousProperties, IReadOnlyList<VirtualNodeProperty> nextProperties)
    {
        if (previousProperties.Count != nextProperties.Count)
        {
            return false;
        }

        for (var i = 0; i < previousProperties.Count; i++)
        {
            if (previousProperties[i] != nextProperties[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChildrenShapeChanged(IReadOnlyList<VirtualNode> previousChildren, IReadOnlyList<VirtualNode> nextChildren)
    {
        if (previousChildren.Count != nextChildren.Count)
        {
            return true;
        }

        for (var i = 0; i < previousChildren.Count; i++)
        {
            if (previousChildren[i].Kind != nextChildren[i].Kind || previousChildren[i].Key != nextChildren[i].Key)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindNode(VirtualNode root, int targetIndex, out VirtualNode node)
    {
        var currentIndex = 0;
        return TryFindNodeRecursive(root, targetIndex, ref currentIndex, out node);
    }

    private static bool TryFindNodeRecursive(VirtualNode candidate, int targetIndex, ref int currentIndex, out VirtualNode node)
    {
        if (currentIndex == targetIndex)
        {
            node = candidate;
            return true;
        }

        currentIndex++;
        foreach (var child in candidate.Children)
        {
            if (TryFindNodeRecursive(child, targetIndex, ref currentIndex, out node))
            {
                return true;
            }
        }

        node = default;
        return false;
    }

    private static LayoutRebuildReason MaxReason(LayoutRebuildReason left, LayoutRebuildReason right)
    {
        return ReasonPriority(left) >= ReasonPriority(right) ? left : right;
    }

    private static int ReasonPriority(LayoutRebuildReason reason)
    {
        return reason switch
        {
            LayoutRebuildReason.ViewportChanged => 5,
            LayoutRebuildReason.TreeStructure => 4,
            LayoutRebuildReason.LayoutAffecting => 3,
            LayoutRebuildReason.TextSizeAffecting => 2,
            LayoutRebuildReason.StyleOnly => 1,
            _ => 0
        };
    }

    private readonly record struct DirtyNodeClassification(LayoutRebuildReason Reason, InvalidationKind InvalidationKind)
    {
        public static DirtyNodeClassification None => default;

        public static DirtyNodeClassification Max(DirtyNodeClassification left, DirtyNodeClassification right)
        {
            return ReasonPriority(left.Reason) >= ReasonPriority(right.Reason) ? left : right;
        }
    }

    private static IReadOnlyList<HitTestTarget> BuildHitTargets(IReadOnlyList<LayoutElement> layoutElements)
    {
        if (layoutElements.Count == 0)
        {
            return [];
        }

        var hitTargetCount = 0;
        foreach (var element in layoutElements)
        {
            if (!element.ActionId.IsNone)
            {
                hitTargetCount++;
            }
        }

        if (hitTargetCount == 0)
        {
            return [];
        }

        var hitTargets = new HitTestTarget[hitTargetCount];
        var index = 0;
        foreach (var element in layoutElements)
        {
            if (!element.ActionId.IsNone)
            {
                hitTargets[index++] = new HitTestTarget(element.Bounds, element.ActionId, element.ClipBounds);
            }
        }

        return hitTargets;
    }
}

internal sealed record RenderPipelineRetainedInputSnapshot(
    LayoutTreeResult LayoutResult,
    ElementCommandRange[] ElementCommandRanges,
    IReadOnlyList<HitTestTarget> HitTargets,
    VirtualNode RetainedRoot,
    PixelRectangle Viewport,
    IReadOnlyList<LayoutDirtyClassification> DirtyClassifications,
    IReadOnlyList<(int Start, int Count)> DirtyElementRanges,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges,
    LayoutRebuildReason LayoutRebuildReason,
    TextBufferSnapshot? TextSnapshot = null);
