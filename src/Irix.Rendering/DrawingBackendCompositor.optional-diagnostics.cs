#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

public sealed partial class DrawingBackendCompositor
{
    private CompositionExecutionStatus _lastCompositionExecutionStatus;

    internal CompositionExecutionStatus LastCompositionExecutionStatus
    {
        get
        {
            lock (_compositionStateLock)
            {
                return _lastCompositionExecutionStatus;
            }
        }
    }

    partial void RecordCompositionExecutionSkipped(
        byte kind,
        byte reason,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount,
        int commandCount)
    {
        SetCompositionExecutionStatus(CompositionExecutionStatus.Skipped(
            ToCompositionExecutionKind(kind),
            ToCompositionExecutionSkipReason(reason),
            requiredCapabilities,
            backendCapabilities,
            framePacing,
            layerCount,
            commandCount));
    }

    partial void RecordCompositionExecutionCompleted(
        byte kind,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount,
        int commandCount)
    {
        SetCompositionExecutionStatus(CompositionExecutionStatus.Executed(
            ToCompositionExecutionKind(kind),
            requiredCapabilities,
            backendCapabilities,
            framePacing,
            layerCount,
            commandCount));
    }

    private void SetCompositionExecutionStatus(in CompositionExecutionStatus status)
    {
        lock (_compositionStateLock)
        {
            _lastCompositionExecutionStatus = status;
        }
    }

    private static CompositionExecutionKind ToCompositionExecutionKind(byte kind)
    {
        return kind switch
        {
            CompositionExecutionKindTransformOpacityTick => CompositionExecutionKind.TransformOpacityTick,
            CompositionExecutionKindScrollPresentationTick => CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionKindRetainedUpdateScrollPresentation => CompositionExecutionKind.RetainedUpdateScrollPresentation,
            CompositionExecutionKindAnimationPresentationTick => CompositionExecutionKind.AnimationPresentationTick,
            _ => CompositionExecutionKind.None
        };
    }

    private static CompositionExecutionSkipReason ToCompositionExecutionSkipReason(byte reason)
    {
        return reason switch
        {
            CompositionExecutionSkipReasonNoActivePlan => CompositionExecutionSkipReason.NoActivePlan,
            CompositionExecutionSkipReasonBackendDoesNotImplementComposition => CompositionExecutionSkipReason.BackendDoesNotImplementComposition,
            CompositionExecutionSkipReasonMissingBackendCapability => CompositionExecutionSkipReason.MissingBackendCapability,
            CompositionExecutionSkipReasonMissingRetainedFrame => CompositionExecutionSkipReason.MissingRetainedFrame,
            CompositionExecutionSkipReasonInvalidPlanForRetainedFrame => CompositionExecutionSkipReason.InvalidPlanForRetainedFrame,
            CompositionExecutionSkipReasonDeviceLostRecovered => CompositionExecutionSkipReason.DeviceLostRecovered,
            _ => CompositionExecutionSkipReason.None
        };
    }
}
#endif
