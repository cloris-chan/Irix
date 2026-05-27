namespace Irix.Rendering;

internal static class CompositionAnimationMarkerEvaluator
{
    public static void EvaluateTransformOpacity(
        in CompositionLayerAnimation animation,
        bool hasPreviousSample,
        CompositionTimestamp startTimestamp,
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates,
        List<CompositionAnimationMarkerEvent> events)
    {
        Evaluate(
            animation.Markers,
            animation.InstanceId,
            CompositionAnimationMarkerOwnerKind.TransformOpacity,
            animation.LayerId,
            animation.TargetKey,
            hasPreviousSample,
            startTimestamp,
            previousSample,
            currentSample,
            markerStates,
            events);
    }

    public static void EvaluateScrollPresentation(
        in CompositionScrollLayerAnimation animation,
        bool hasPreviousSample,
        CompositionTimestamp startTimestamp,
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates,
        List<CompositionAnimationMarkerEvent> events)
    {
        Evaluate(
            animation.Markers,
            animation.InstanceId,
            CompositionAnimationMarkerOwnerKind.ScrollPresentation,
            animation.LayerId,
            animation.TargetKey,
            hasPreviousSample,
            startTimestamp,
            previousSample,
            currentSample,
            markerStates,
            events);
    }

    private static void Evaluate(
        ReadOnlySpan<CompositionAnimationMarker> markers,
        CompositionAnimationInstanceId instanceId,
        CompositionAnimationMarkerOwnerKind ownerKind,
        CompositionLayerId layerId,
        NodeKey targetKey,
        bool hasPreviousSample,
        CompositionTimestamp startTimestamp,
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates,
        List<CompositionAnimationMarkerEvent> events)
    {
        if (markers.Length == 0 || !instanceId.IsValid)
        {
            return;
        }

        for (var i = 0; i < markers.Length; i++)
        {
            ref readonly var marker = ref markers[i];
            if (!ShouldFire(marker, hasPreviousSample, startTimestamp, previousSample, currentSample, markerStates, out var kind))
            {
                continue;
            }

            events.Add(new CompositionAnimationMarkerEvent(
                instanceId,
                marker.Id,
                marker.RuntimeEventId,
                kind,
                ownerKind,
                layerId,
                targetKey,
                currentSample.Timestamp,
                currentSample.Elapsed,
                currentSample.Progress,
                currentSample.Iteration,
                currentSample.Direction));
            RecordMarkerFired(marker.Id, currentSample, markerStates);
        }
    }

    private static bool ShouldFire(
        in CompositionAnimationMarker marker,
        bool hasPreviousSample,
        CompositionTimestamp startTimestamp,
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates,
        out CompositionAnimationMarkerEventKind kind)
    {
        kind = marker.Trigger.Kind switch
        {
            CompositionAnimationMarkerTriggerKind.Progress => CompositionAnimationMarkerEventKind.Progress,
            CompositionAnimationMarkerTriggerKind.ElapsedTime => CompositionAnimationMarkerEventKind.ElapsedTime,
            CompositionAnimationMarkerTriggerKind.ProgressRange => CompositionAnimationMarkerEventKind.ProgressRangeEntered,
            _ => CompositionAnimationMarkerEventKind.Tick
        };

        if (marker.Trigger.Kind == CompositionAnimationMarkerTriggerKind.EveryTick)
        {
            return true;
        }

        if (!AllowsMarker(marker, currentSample, markerStates))
        {
            return false;
        }

        if (!hasPreviousSample)
        {
            return marker.Trigger.Kind == CompositionAnimationMarkerTriggerKind.ElapsedTime
                ? currentSample.Elapsed == marker.Trigger.Elapsed
                : IsCurrentSampleAtOrInside(marker.Trigger, currentSample, startTimestamp);
        }

        if (previousSample.Timestamp == currentSample.Timestamp)
        {
            return false;
        }

        return marker.Trigger.Kind switch
        {
            CompositionAnimationMarkerTriggerKind.Progress => CrossedProgress(previousSample, currentSample, marker.Trigger.Progress),
            CompositionAnimationMarkerTriggerKind.ElapsedTime => CrossedElapsed(previousSample, currentSample, marker.Trigger.Elapsed),
            CompositionAnimationMarkerTriggerKind.ProgressRange => EnteredProgressRange(previousSample, currentSample, marker.Trigger.RangeStart, marker.Trigger.RangeEnd),
            _ => false
        };
    }

    private static bool IsCurrentSampleAtOrInside(
        in CompositionAnimationMarkerTrigger trigger,
        in CompositionTimelineSample currentSample,
        CompositionTimestamp startTimestamp)
    {
        return trigger.Kind switch
        {
            CompositionAnimationMarkerTriggerKind.Progress => currentSample.Timestamp != startTimestamp && trigger.Progress != 0f
                ? false
                : currentSample.Direction == CompositionPlaybackDirection.Reverse
                    ? currentSample.Progress <= trigger.Progress
                    : currentSample.Progress >= trigger.Progress,
            CompositionAnimationMarkerTriggerKind.ElapsedTime => currentSample.Elapsed >= trigger.Elapsed,
            CompositionAnimationMarkerTriggerKind.ProgressRange => currentSample.Timestamp == startTimestamp
                ? Contains(trigger.RangeStart, trigger.RangeEnd, 0f) && Contains(trigger.RangeStart, trigger.RangeEnd, currentSample.Progress)
                : Contains(trigger.RangeStart, trigger.RangeEnd, currentSample.Progress),
            _ => false
        };
    }

    private static bool AllowsMarker(
        in CompositionAnimationMarker marker,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates)
    {
        for (var i = 0; i < markerStates.Length; i++)
        {
            if (markerStates[i].MarkerId == marker.Id)
            {
                return markerStates[i].Allows(marker, currentSample);
            }
        }

        return true;
    }

    private static void RecordMarkerFired(
        CompositionAnimationMarkerId markerId,
        in CompositionTimelineSample currentSample,
        Span<CompositionAnimationMarkerPlaybackState> markerStates)
    {
        for (var i = 0; i < markerStates.Length; i++)
        {
            if (markerStates[i].MarkerId == markerId)
            {
                markerStates[i].Record(currentSample);
                return;
            }
        }
    }

    private static bool CrossedProgress(
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        float progress)
    {
        if (previousSample.Iteration != currentSample.Iteration)
        {
            return CrossedProgressToBoundary(previousSample, progress) || CrossedProgressFromBoundary(currentSample, progress);
        }

        return currentSample.Direction == CompositionPlaybackDirection.Reverse
            ? previousSample.Progress >= progress && currentSample.Progress <= progress
            : previousSample.Progress <= progress && currentSample.Progress >= progress;
    }

    private static bool CrossedProgressToBoundary(in CompositionTimelineSample sample, float progress)
    {
        return sample.Direction == CompositionPlaybackDirection.Reverse
            ? sample.Progress > progress
            : sample.Progress < progress;
    }

    private static bool CrossedProgressFromBoundary(in CompositionTimelineSample sample, float progress)
    {
        return sample.Direction == CompositionPlaybackDirection.Reverse
            ? sample.Progress <= progress
            : sample.Progress >= progress;
    }

    private static bool CrossedElapsed(
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        CompositionDuration elapsed)
    {
        return previousSample.Elapsed < elapsed && currentSample.Elapsed >= elapsed;
    }

    private static bool EnteredProgressRange(
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        float rangeStart,
        float rangeEnd)
    {
        var previousInside = Contains(rangeStart, rangeEnd, previousSample.Progress);
        var currentInside = Contains(rangeStart, rangeEnd, currentSample.Progress);
        if (!previousInside && currentInside)
        {
            return true;
        }

        if (previousInside)
        {
            return false;
        }

        return previousSample.Iteration != currentSample.Iteration
            ? IntersectsAcrossIteration(previousSample, currentSample, rangeStart, rangeEnd)
            : Intersects(previousSample.Progress, currentSample.Progress, rangeStart, rangeEnd);
    }

    private static bool IntersectsAcrossIteration(
        in CompositionTimelineSample previousSample,
        in CompositionTimelineSample currentSample,
        float rangeStart,
        float rangeEnd)
    {
        return previousSample.Direction == CompositionPlaybackDirection.Reverse
            ? Intersects(previousSample.Progress, 0f, rangeStart, rangeEnd)
                || (currentSample.Direction == CompositionPlaybackDirection.Reverse
                    ? Intersects(1f, currentSample.Progress, rangeStart, rangeEnd)
                    : Intersects(0f, currentSample.Progress, rangeStart, rangeEnd))
            : Intersects(previousSample.Progress, 1f, rangeStart, rangeEnd)
                || (currentSample.Direction == CompositionPlaybackDirection.Reverse
                    ? Intersects(1f, currentSample.Progress, rangeStart, rangeEnd)
                    : Intersects(0f, currentSample.Progress, rangeStart, rangeEnd));
    }

    private static bool Intersects(float a, float b, float rangeStart, float rangeEnd)
    {
        var min = Math.Min(a, b);
        var max = Math.Max(a, b);
        return max >= rangeStart && min <= rangeEnd;
    }

    private static bool Contains(float rangeStart, float rangeEnd, float progress)
    {
        return progress >= rangeStart && progress <= rangeEnd;
    }
}
