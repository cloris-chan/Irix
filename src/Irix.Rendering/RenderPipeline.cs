using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal sealed partial class RenderPipeline(LayoutStyle layoutStyle, DrawingStyle drawingStyle, ControlVisualStateResolver visualStateResolver)
{
    private const int InlineDirtyClassificationCapacity = 32;

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

    public bool HasLastRetainedInputSnapshot { get; private set; }

    public RenderPipelineRetainedInputSnapshot LastRetainedInputSnapshot { get; private set; }

    /// <summary>
    /// The layout tree result from the last Build call, if available.
    /// Exposes scrollable container diagnostics and tree structure.
    /// </summary>
    public LayoutTreeResult? LastLayoutResult => _retainedLayoutResult;

    /// <summary>
    /// The MaxScrollY from the first scrollable container in the last Build call.
    /// 0 if no scrollable container or no scroll needed.
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
        return BuildCore(root, viewportBounds, textSnapshot, dirtyNodes, prevTextSnapshot, previousRoot);
    }

    private RenderFrameBatch BuildCore(
        VirtualNode root,
        PixelRectangle viewportBounds,
        TextBufferSnapshot textSnapshot,
        IReadOnlyList<int>? dirtyNodes,
        TextBufferSnapshot? prevTextSnapshot,
        VirtualNode previousRoot)
    {
        OnPipelineAllocationStarted();
        LastViewport = viewportBounds;
        var hadRetainedLayout = _retainedLayoutResult.HasValue;
        var classifyOldRoot = hadRetainedLayout && previousRoot.Kind != VirtualNodeKind.None ? previousRoot : _retainedRoot;
        var classifyOldSnapshot = hadRetainedLayout && previousRoot.Kind != VirtualNodeKind.None ? prevTextSnapshot : _retainedTextSnapshot;
        var treeChanged = !hadRetainedLayout || !VirtualNodeStructuralComparer.Equals(classifyOldRoot, root, classifyOldSnapshot ?? textSnapshot, textSnapshot);
        var viewportChanged = _retainedViewport != viewportBounds;
        var hasDirty = dirtyNodes is { Count: > 0 };
        OnPipelineAllocationPhaseStarted();
        var dirtyClassifications = hasDirty && hadRetainedLayout
            ? ClassifyDirtyNodes(classifyOldRoot, root, dirtyNodes!, classifyOldSnapshot ?? textSnapshot, textSnapshot)
            : [];
        LastDirtyClassifications = dirtyClassifications;
        LastLayoutRebuildReason = ResolveLayoutRebuildReason(hadRetainedLayout, treeChanged, viewportChanged, hasDirty, dirtyClassifications);
        OnPipelineClassificationAllocated();

        if (treeChanged || viewportChanged || hasDirty)
        {
            if (TryApplyStyleOnlyLayoutSkip(root, textSnapshot, dirtyNodes, dirtyClassifications, viewportChanged, previousRoot, classifyOldSnapshot, out var patchedLayoutResult))
            {
                _retainedLayoutResult = patchedLayoutResult;
                _retainedRoot = root;
                _retainedTextSnapshot = textSnapshot;
                _retainedViewport = viewportBounds;

                LastMaxScrollY = ResolveRootMaxScrollY(patchedLayoutResult.ScrollDiagnostics);
            }
            else
            {
                OnPipelineAllocationPhaseStarted();
                LayoutRebuildCount++;
                var layoutResult = _layoutTreeBuilder.BuildLayoutTree(root, viewportBounds, dirtyNodes);
                _retainedLayoutResult = layoutResult;
                _retainedRoot = root;
                _retainedTextSnapshot = textSnapshot;
                _retainedViewport = viewportBounds;

                LastMaxScrollY = ResolveRootMaxScrollY(layoutResult.ScrollDiagnostics);
                OnPipelineLayoutAllocated(_layoutTreeBuilder);
            }
        }

        var retainedLayoutResult = _retainedLayoutResult!.Value;
        var layout = retainedLayoutResult.Elements;
        var dirtyElementRanges = hasDirty
            ? retainedLayoutResult.DirtyElementRanges
            : null;

        LastDirtyElementRanges = dirtyElementRanges ?? [];

        OnPipelineAllocationPhaseStarted();
        var result = _drawCommandRecorder.Record(layout, dirtyElementRanges, _retainedTextSnapshot);
        LastDirtyCommandRanges = result.DirtyCommandRanges;
        LastElementCommandRanges = result.ElementCommandRanges;
        OnPipelineRecordAllocated(_drawCommandRecorder);

        OnPipelineAllocationPhaseStarted();
        var hitTargets = BuildHitTargets(layout, result.ElementCommandRanges);
        OnPipelineHitTargetsAllocated();

        OnPipelineSnapshotAllocationStarted();
        OnPipelineSnapshotPhaseStarted();
        var batch = new RenderFrameBatch(result.Commands, hitTargets, result.Resources, result.DirtyCommandRanges);
        OnPipelineFrameBatchAllocated();

        OnPipelineSnapshotPhaseStarted();
        LastRetainedInputSnapshot = CreateRetainedInputSnapshot(
            retainedLayoutResult,
            result.ElementCommandRanges,
            hitTargets,
            _retainedRoot,
            _retainedViewport,
            LastDirtyClassifications,
            LastDirtyElementRanges,
            LastDirtyCommandRanges,
            LastLayoutRebuildReason,
            classifyOldSnapshot,
            _retainedTextSnapshot);
        HasLastRetainedInputSnapshot = true;
        OnPipelineRetainedInputAllocated();
        OnPipelineSnapshotAllocated();

        // Update retained render frame: try partial apply when dirty ranges exist,
        // which only succeeds when resources are the same instance (same frame scope).
        // Falls back to full apply when resources differ or no dirty ranges.
        OnPipelineAllocationPhaseStarted();
        if (!hasDirty || result.DirtyCommandRanges.Count == 0 || !_retainedFrame.TryApplyPartial(batch))
        {
            _retainedFrame.ReleaseResources();
            _retainedFrame.ApplyFull(batch);
            _retainedFrame.RetainResources();
        }
        OnPipelineRetainedFrameAllocated();

        return batch;
    }

    private bool TryApplyStyleOnlyLayoutSkip(
        VirtualNode root,
        TextBufferSnapshot textSnapshot,
        IReadOnlyList<int>? dirtyNodes,
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        bool viewportChanged,
        VirtualNode previousRoot,
        TextBufferSnapshot? classifyOldSnapshot,
        out LayoutTreeResult patchedLayout)
    {
        patchedLayout = default;
        if (viewportChanged
            || dirtyNodes is not { Count: > 0 }
            || _retainedLayoutResult is null
            || !StyleOnlyPatchEligibility.IsLayoutReuseEligible(dirtyClassifications, viewportChanged))
        {
            return false;
        }

        if (previousRoot.Kind != VirtualNodeKind.None
            && !VirtualNodeStructuralComparer.Equals(_retainedRoot, previousRoot, _retainedTextSnapshot, classifyOldSnapshot ?? _retainedTextSnapshot))
        {
            return false;
        }

        var rootPatch = RetainedRootMetadataPatcher.ProjectControlMetadata(
            _retainedRoot,
            root,
            dirtyClassifications,
            _retainedTextSnapshot,
            textSnapshot);
        if (!rootPatch.Succeeded)
        {
            return false;
        }

        return StyleOnlyLayoutPatcher.TryBuildPatchedLayout(
            _retainedLayoutResult.Value,
            root,
            dirtyNodes,
            out patchedLayout);
    }

    partial void OnPipelineAllocationStarted();
    partial void OnPipelineAllocationPhaseStarted();
    partial void OnPipelineClassificationAllocated();
    partial void OnPipelineLayoutAllocated(LayoutTreeBuilder layoutTreeBuilder);
    partial void OnPipelineRecordAllocated(DrawCommandRecorder drawCommandRecorder);
    partial void OnPipelineHitTargetsAllocated();
    partial void OnPipelineSnapshotAllocationStarted();
    partial void OnPipelineSnapshotPhaseStarted();
    partial void OnPipelineFrameBatchAllocated();
    partial void OnPipelineRetainedInputAllocated();
    partial void OnPipelineSnapshotAllocated();
    partial void OnPipelineRetainedFrameAllocated();

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

    private static double ResolveRootMaxScrollY(IReadOnlyList<ScrollContainerDiag> scrollDiagnostics)
    {
        foreach (var diagnostic in scrollDiagnostics)
        {
            if (diagnostic.DfsIndex == 0)
            {
                return diagnostic.MaxScrollY;
            }
        }

        return 0;
    }

    private static IReadOnlyList<LayoutDirtyClassification> ClassifyDirtyNodes(VirtualNode previousRoot, VirtualNode nextRoot, IReadOnlyList<int> dirtyNodes, TextBufferSnapshot? prevSnapshot, TextBufferSnapshot? nextSnapshot)
    {
        var scratch = new RenderScratchBuffer();
        Span<int> dirtySetStorage = stackalloc int[InlineDirtyClassificationCapacity];
        Span<LayoutDirtyClassification> classificationStorage = stackalloc LayoutDirtyClassification[InlineDirtyClassificationCapacity];
        using var dirtySet = scratch.CreateDirtyIndexSet(dirtySetStorage);
        using var classifications = scratch.CreateLayoutDirtyClassificationList(classificationStorage);
        for (var i = 0; i < dirtyNodes.Count; i++)
        {
            if (!dirtySet.Add(dirtyNodes[i]))
            {
                continue;
            }

            var dirtyNode = dirtyNodes[i];
            var reason = TryFindNode(previousRoot, dirtyNode, out var previousNode) && TryFindNode(nextRoot, dirtyNode, out var nextNode)
                ? ClassifyNodeChange(previousNode, nextNode, prevSnapshot, nextSnapshot)
                : new DirtyNodeClassification(LayoutRebuildReason.TreeStructure, InvalidationKind.TreeStructure);
            classifications.Add(new LayoutDirtyClassification(dirtyNode, reason.Reason, reason.InvalidationKind));
        }

        return classifications.ToArray();
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

    private static DirtyNodeClassification ClassifyContentChange(ContentResource previousContent, ContentResource nextContent)
    {
        return previousContent.Kind == ContentResourceKind.Text || nextContent.Kind == ContentResourceKind.Text
            ? new DirtyNodeClassification(LayoutRebuildReason.TextSizeAffecting, InvalidationKind.TextMeasure)
            : new DirtyNodeClassification(LayoutRebuildReason.LayoutAffecting, InvalidationKind.Layout);
    }

    private static DirtyNodeClassification ClassifyPropertyChanges(ReadOnlySpan<VirtualNodeProperty> previousProperties, ReadOnlySpan<VirtualNodeProperty> nextProperties)
    {
        var plan = StyleDeltaPlanner.Plan(previousProperties, nextProperties);
        return new DirtyNodeClassification(plan.LayoutRebuildReason, plan.InvalidationKind);
    }

    private static bool PropertiesEqual(ReadOnlySpan<VirtualNodeProperty> previousProperties, ReadOnlySpan<VirtualNodeProperty> nextProperties)
    {
        if (previousProperties.Length != nextProperties.Length)
        {
            return false;
        }

        for (var i = 0; i < previousProperties.Length; i++)
        {
            if (previousProperties[i] != nextProperties[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChildrenShapeChanged(ReadOnlySpan<VirtualNode> previousChildren, ReadOnlySpan<VirtualNode> nextChildren)
    {
        if (previousChildren.Length != nextChildren.Length)
        {
            return true;
        }

        for (var i = 0; i < previousChildren.Length; i++)
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

    private readonly struct DirtyNodeClassification(LayoutRebuildReason Reason, InvalidationKind InvalidationKind) : IEquatable<DirtyNodeClassification>
    {

        public LayoutRebuildReason Reason { get; } = Reason;
        public InvalidationKind InvalidationKind { get; } = InvalidationKind;

        public static DirtyNodeClassification None => default;

        public static DirtyNodeClassification Max(DirtyNodeClassification left, DirtyNodeClassification right)
        {
            return ReasonPriority(left.Reason) >= ReasonPriority(right.Reason) ? left : right;
        }

        public bool Equals(DirtyNodeClassification other)
        {
            return Reason == other.Reason
                && InvalidationKind == other.InvalidationKind;
        }

        public override bool Equals(object? obj) => obj is DirtyNodeClassification other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Reason, InvalidationKind);

        public static bool operator ==(DirtyNodeClassification left, DirtyNodeClassification right) => left.Equals(right);

        public static bool operator !=(DirtyNodeClassification left, DirtyNodeClassification right) => !left.Equals(right);
    }

    internal static IReadOnlyList<HitTestTarget> BuildHitTargets(IReadOnlyList<LayoutElement> layoutElements)
    {
        return BuildHitTargets(layoutElements, []);
    }

    internal static IReadOnlyList<HitTestTarget> BuildHitTargets(
        IReadOnlyList<LayoutElement> layoutElements,
        ReadOnlySpan<ElementCommandRange> elementCommandRanges)
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
        for (var i = 0; i < layoutElements.Count; i++)
        {
            var element = layoutElements[i];
            if (!element.ActionId.IsNone)
            {
                var commandRange = (uint)i < (uint)elementCommandRanges.Length ? elementCommandRanges[i] : default;
                var commandStart = commandRange.CommandCount > 0 ? commandRange.CommandStart : -1;
                hitTargets[index++] = new HitTestTarget(
                    element.Bounds,
                    element.ActionId,
                    element.ClipBounds,
                    commandStart,
                    commandRange.CommandCount);
            }
        }

        return hitTargets;
    }

    internal static RenderPipelineRetainedInputSnapshot CreateRetainedInputSnapshot(
        LayoutTreeResult layoutResult,
        ElementCommandRange[] elementCommandRanges,
        IReadOnlyList<HitTestTarget> hitTargets,
        VirtualNode retainedRoot,
        PixelRectangle viewport,
        IReadOnlyList<LayoutDirtyClassification> dirtyClassifications,
        IReadOnlyList<(int Start, int Count)> dirtyElementRanges,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges,
        LayoutRebuildReason layoutRebuildReason,
        TextBufferSnapshot? previousTextSnapshot,
        TextBufferSnapshot? textSnapshot) =>
        new(
            layoutResult,
            elementCommandRanges,
            hitTargets,
            retainedRoot,
            viewport,
            dirtyClassifications,
            dirtyElementRanges,
            dirtyCommandRanges,
            LayoutRebuildReason: layoutRebuildReason,
            PreviousTextSnapshot: previousTextSnapshot,
            TextSnapshot: textSnapshot);

    internal static bool TryResolveCompositionTarget(
        ReadOnlySpan<LayoutTreeNode> treeNodes,
        ReadOnlySpan<ElementCommandRange> elementCommandRanges,
        int commandCount,
        NodeKey key,
        out CompositionTarget target)
    {
        if (key == NodeKey.None || treeNodes.IsEmpty || elementCommandRanges.IsEmpty || commandCount <= 0)
        {
            target = default;
            return false;
        }

        foreach (ref readonly var node in treeNodes)
        {
            if (node.Key != key || !TryResolveCommandRange(node, elementCommandRanges, commandCount, out var commandStart, out var resolvedCommandCount))
            {
                continue;
            }

            target = new CompositionTarget(
                new CompositionLayerId(checked((int)node.Key.Value)),
                node.Key,
                node.Kind,
                node.DfsIndex,
                commandStart,
                resolvedCommandCount);
            return true;
        }

        target = default;
        return false;
    }

    internal static bool TryResolveScrollCompositionTarget(
        LayoutTreeResult layoutResult,
        ReadOnlySpan<ElementCommandRange> elementCommandRanges,
        int commandCount,
        NodeKey key,
        out ScrollCompositionTarget target)
    {
        if (key == NodeKey.None || layoutResult.TreeNodes.Length == 0 || elementCommandRanges.IsEmpty || commandCount <= 0)
        {
            target = default;
            return false;
        }

        foreach (ref readonly var node in layoutResult.TreeNodes.AsSpan())
        {
            if (node.Key != key
                || node.Kind != VirtualNodeKind.Container
                || !TryFindScrollDiagnostic(layoutResult.ScrollDiagnostics, node.DfsIndex, out var diagnostic)
                || !TryResolveScrollCompositionLayers(
                    layoutResult.Elements,
                    elementCommandRanges,
                    commandCount,
                    node.ElementStart,
                    node.ElementCount,
                    checked((int)node.Key.Value),
                    out var firstLayer,
                    out var additionalLayers))
            {
                continue;
            }

            target = new ScrollCompositionTarget(
                node.Key,
                node.DfsIndex,
                diagnostic.ScrollY,
                diagnostic.MaxScrollY,
                firstLayer,
                additionalLayers);
            return target.IsValidForCommandCount(commandCount);
        }

        target = default;
        return false;
    }

    private static bool TryResolveScrollCompositionLayers(
        IReadOnlyList<LayoutElement> elements,
        ReadOnlySpan<ElementCommandRange> elementCommandRanges,
        int commandCount,
        int elementStart,
        int elementCount,
        int baseLayerId,
        out ScrollCompositionLayerTarget firstLayer,
        out ScrollCompositionLayerTarget[] additionalLayers)
    {
        firstLayer = default;
        additionalLayers = [];
        if (baseLayerId <= 0
            || elementStart < 0
            || elementCount <= 0
            || elementStart >= elements.Count
            || elementStart + elementCount > elements.Count
            || elementStart + elementCount > elementCommandRanges.Length)
        {
            return false;
        }

        var layers = new List<ScrollCompositionLayerTarget>(4);
        var currentClip = default(PixelRectangle);
        var currentStart = 0;
        var currentEnd = 0;
        var hasCurrent = false;
        for (var i = elementStart; i < elementStart + elementCount; i++)
        {
            var commandRange = elementCommandRanges[i];
            if (commandRange.CommandCount <= 0)
            {
                continue;
            }

            var commandStart = commandRange.CommandStart;
            var commandEnd = commandStart + commandRange.CommandCount;
            var clipBounds = elements[i].ClipBounds;
            if (clipBounds.Width <= 0
                || clipBounds.Height <= 0
                || commandStart < 0
                || commandEnd <= commandStart
                || commandEnd > commandCount)
            {
                return false;
            }

            if (!hasCurrent)
            {
                currentClip = clipBounds;
                currentStart = commandStart;
                currentEnd = commandEnd;
                hasCurrent = true;
                continue;
            }

            if (clipBounds == currentClip && commandStart == currentEnd)
            {
                currentEnd = commandEnd;
                continue;
            }

            if (!TryAppendScrollCompositionLayer(layers, baseLayerId, currentStart, currentEnd - currentStart, currentClip))
            {
                return false;
            }

            currentClip = clipBounds;
            currentStart = commandStart;
            currentEnd = commandEnd;
        }

        if (!hasCurrent || !TryAppendScrollCompositionLayer(layers, baseLayerId, currentStart, currentEnd - currentStart, currentClip))
        {
            return false;
        }

        firstLayer = layers[0];
        if (layers.Count > 1)
        {
            additionalLayers = new ScrollCompositionLayerTarget[layers.Count - 1];
            for (var i = 1; i < layers.Count; i++)
            {
                additionalLayers[i - 1] = layers[i];
            }
        }

        return true;
    }

    private static bool TryAppendScrollCompositionLayer(
        List<ScrollCompositionLayerTarget> layers,
        int baseLayerId,
        int commandStart,
        int commandCount,
        in PixelRectangle clipBounds)
    {
        var layerIdValue = baseLayerId + layers.Count;
        if (layerIdValue <= 0)
        {
            return false;
        }

        var layer = new ScrollCompositionLayerTarget(
            new CompositionLayerId(layerIdValue),
            commandStart,
            commandCount,
            clipBounds);
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i].LayerId == layer.LayerId)
            {
                return false;
            }
        }

        layers.Add(layer);
        return true;
    }

    private static bool TryFindScrollDiagnostic(IReadOnlyList<ScrollContainerDiag> diagnostics, int dfsIndex, out ScrollContainerDiag diagnostic)
    {
        foreach (var candidate in diagnostics)
        {
            if (candidate.DfsIndex == dfsIndex)
            {
                diagnostic = candidate;
                return true;
            }
        }

        diagnostic = default;
        return false;
    }

    private static bool TryResolveCommandRange(
        in LayoutTreeNode node,
        ReadOnlySpan<ElementCommandRange> elementCommandRanges,
        int commandCount,
        out int commandStart,
        out int resolvedCommandCount)
    {
        commandStart = 0;
        resolvedCommandCount = 0;
        if (node.ElementCount <= 0
            || node.ElementStart < 0
            || node.ElementStart >= elementCommandRanges.Length
            || node.ElementStart + node.ElementCount > elementCommandRanges.Length)
        {
            return false;
        }

        var firstRange = elementCommandRanges[node.ElementStart];
        var lastRange = elementCommandRanges[node.ElementStart + node.ElementCount - 1];
        var start = firstRange.CommandStart;
        var end = lastRange.CommandStart + lastRange.CommandCount;
        if (firstRange.CommandCount <= 0
            || lastRange.CommandCount <= 0
            || start < 0
            || end <= start
            || end > commandCount)
        {
            return false;
        }

        commandStart = start;
        resolvedCommandCount = end - start;
        return true;
    }
}
internal readonly struct RenderPipelineRetainedInputSnapshot(
    LayoutTreeResult LayoutResult,
    ElementCommandRange[] ElementCommandRanges,
    IReadOnlyList<HitTestTarget> HitTargets,
    VirtualNode RetainedRoot,
    PixelRectangle Viewport,
    IReadOnlyList<LayoutDirtyClassification> DirtyClassifications,
    IReadOnlyList<(int Start, int Count)> DirtyElementRanges,
    IReadOnlyList<(int Start, int Count)> DirtyCommandRanges,
    LayoutRebuildReason LayoutRebuildReason,
    TextBufferSnapshot? PreviousTextSnapshot = null,
    TextBufferSnapshot? TextSnapshot = null)
{
    public LayoutTreeResult LayoutResult { get; } = LayoutResult;
    public ElementCommandRange[] ElementCommandRanges { get; } = ElementCommandRanges;
    public IReadOnlyList<HitTestTarget> HitTargets { get; } = HitTargets;
    public VirtualNode RetainedRoot { get; } = RetainedRoot;
    public PixelRectangle Viewport { get; } = Viewport;
    public IReadOnlyList<LayoutDirtyClassification> DirtyClassifications { get; } = DirtyClassifications;
    public IReadOnlyList<(int Start, int Count)> DirtyElementRanges { get; } = DirtyElementRanges;
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges { get; } = DirtyCommandRanges;
    public LayoutRebuildReason LayoutRebuildReason { get; } = LayoutRebuildReason;
    public TextBufferSnapshot? PreviousTextSnapshot { get; } = PreviousTextSnapshot;
    public TextBufferSnapshot? TextSnapshot { get; } = TextSnapshot;
    public int CommandCount { get; } = ResolveCommandCount(ElementCommandRanges);

    public bool TryGetCompositionTarget(NodeKey key, out CompositionTarget target)
    {
        return TryGetCompositionTarget(key, CommandCount, out target);
    }

    internal bool TryGetCompositionTarget(NodeKey key, int commandCount, out CompositionTarget target)
    {
        return RenderPipeline.TryResolveCompositionTarget(
            LayoutResult.TreeNodes,
            ElementCommandRanges,
            commandCount,
            key,
            out target);
    }

    public bool TryGetScrollCompositionTarget(NodeKey key, out ScrollCompositionTarget target)
    {
        return TryGetScrollCompositionTarget(key, CommandCount, out target);
    }

    internal bool TryGetScrollCompositionTarget(NodeKey key, int commandCount, out ScrollCompositionTarget target)
    {
        return RenderPipeline.TryResolveScrollCompositionTarget(
            LayoutResult,
            ElementCommandRanges,
            commandCount,
            key,
            out target);
    }

    private static int ResolveCommandCount(ReadOnlySpan<ElementCommandRange> elementCommandRanges)
    {
        var commandCount = 0;
        foreach (ref readonly var range in elementCommandRanges)
        {
            commandCount = Math.Max(commandCount, range.CommandStart + Math.Max(range.CommandCount, 0));
        }

        return commandCount;
    }
}
