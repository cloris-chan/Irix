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
            1 => CompositionExecutionKind.TransformOpacityTick,
            2 => CompositionExecutionKind.ScrollPresentationTick,
            3 => CompositionExecutionKind.RetainedUpdateScrollPresentation,
            _ => CompositionExecutionKind.None
        };
    }

    private static CompositionExecutionSkipReason ToCompositionExecutionSkipReason(byte reason)
    {
        return reason switch
        {
            1 => CompositionExecutionSkipReason.NoActivePlan,
            2 => CompositionExecutionSkipReason.BackendDoesNotImplementComposition,
            3 => CompositionExecutionSkipReason.MissingBackendCapability,
            4 => CompositionExecutionSkipReason.MissingRetainedFrame,
            5 => CompositionExecutionSkipReason.InvalidPlanForRetainedFrame,
            6 => CompositionExecutionSkipReason.DeviceLostRecovered,
            _ => CompositionExecutionSkipReason.None
        };
    }
}
#endif
