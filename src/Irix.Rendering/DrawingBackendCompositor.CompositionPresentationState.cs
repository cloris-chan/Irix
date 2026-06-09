namespace Irix.Rendering;

public sealed partial class DrawingBackendCompositor
{
    private sealed class CompositionPresentationState
    {
        private PendingCompositionScrollPresentationRetainedFrameUpdate _pendingScrollPresentationRetainedFrameUpdate;

        public CompositionAnimationPlan? AnimationPlan { get; private set; }

        public CompositionAnimationPresentationSetPlan? AnimationPresentationPlan { get; private set; }

        public CompositionScrollPresentationPlan? ScrollPresentationPlan { get; private set; }

        public CompositionAnimationMarkerPlaybackState[] AnimationMarkerStates { get; private set; } = [];

        public CompositionAnimationMarkerPlaybackState[][] AnimationPresentationMarkerStates { get; private set; } = [];

        public CompositionAnimationMarkerPlaybackState[] ScrollPresentationMarkerStates { get; private set; } = [];

        public CompositionTimelineSample LastAnimationSample { get; private set; }

        public CompositionTimelineSample[] LastAnimationPresentationSamples { get; private set; } = [];

        public CompositionTimelineSample LastScrollPresentationSample { get; private set; }

        public bool HasLastAnimationSample { get; private set; }

        public bool[] HasLastAnimationPresentationSamples { get; private set; } = [];

        public bool HasLastScrollPresentationSample { get; private set; }

        public bool HasAnimation => AnimationPlan is not null || AnimationPresentationPlan is not null;

        public bool HasActiveRetainedRefreshScrollPresentationPlan => ScrollPresentationPlan is not null;

        public void SetAnimationPlan(in CompositionAnimationPlan plan)
        {
            AnimationPlan = plan;
            AnimationPresentationPlan = null;
            ScrollPresentationPlan = null;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            AnimationMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
            ClearAnimationPresentationPlaybackState();
            ScrollPresentationMarkerStates = [];
        }

        public void SetAnimationPresentationPlan(in CompositionAnimationPresentationSetPlan plan)
        {
            AnimationPlan = null;
            AnimationPresentationPlan = plan;
            ScrollPresentationPlan = null;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            AnimationMarkerStates = [];
            AnimationPresentationMarkerStates = CreatePresentationMarkerPlaybackStates(plan);
            LastAnimationPresentationSamples = new CompositionTimelineSample[plan.Count];
            HasLastAnimationPresentationSamples = new bool[plan.Count];
            ScrollPresentationMarkerStates = [];
        }

        public void SetScrollPresentationPlan(in CompositionScrollPresentationPlan plan)
        {
            AnimationPlan = null;
            AnimationPresentationPlan = null;
            ScrollPresentationPlan = plan;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            AnimationMarkerStates = [];
            ClearAnimationPresentationPlaybackState();
            ScrollPresentationMarkerStates = CreateMarkerPlaybackStates(plan.LayerAnimation.Markers);
        }

        public void ClearAnimation()
        {
            AnimationPlan = null;
            AnimationPresentationPlan = null;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            AnimationMarkerStates = [];
            ClearAnimationPresentationPlaybackState();
        }

        public void ClearAnimationPresentationPlan()
        {
            AnimationPresentationPlan = null;
            ClearAnimationPresentationPlaybackState();
        }

        public void ClearScrollPresentation()
        {
            ScrollPresentationPlan = null;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            ScrollPresentationMarkerStates = [];
        }

        public void ClearAll()
        {
            AnimationPlan = null;
            AnimationPresentationPlan = null;
            ScrollPresentationPlan = null;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            AnimationMarkerStates = [];
            ClearAnimationPresentationPlaybackState();
            ScrollPresentationMarkerStates = [];
        }

        public void PrepareScrollPresentationRetainedFrameUpdate(in CompositionScrollPresentationPlan plan, int commandCount)
        {
            _pendingScrollPresentationRetainedFrameUpdate = new PendingCompositionScrollPresentationRetainedFrameUpdate(plan, commandCount);
        }

        public void ClearPendingScrollPresentationRetainedFrameUpdate()
        {
            _pendingScrollPresentationRetainedFrameUpdate = default;
        }

        public bool TryTakePendingScrollPresentationRetainedFrameUpdate(out PendingCompositionScrollPresentationRetainedFrameUpdate pending)
        {
            pending = _pendingScrollPresentationRetainedFrameUpdate;
            _pendingScrollPresentationRetainedFrameUpdate = default;
            return pending.HasValue;
        }

        public void ApplyPreparedScrollPresentationRetainedFrameUpdate(in CompositionScrollPresentationPlan plan)
        {
            AnimationPlan = null;
            AnimationPresentationPlan = null;
            ScrollPresentationPlan = plan;
            AnimationMarkerStates = [];
            ClearAnimationPresentationPlaybackState();
        }

        public void DiscardPreparedScrollPresentationRetainedFrameUpdate()
        {
            ScrollPresentationPlan = null;
            ScrollPresentationMarkerStates = [];
        }

        public void RemoveAnimationPresentationTargets(
            in CompositionAnimationPresentationSetPlan previousPlan,
            ReadOnlySpan<NodeKey> removedTargetKeys,
            in CompositionAnimationPresentationSetPlan remainingPlan)
        {
            AnimationPresentationPlan = remainingPlan;
            RemapAnimationPresentationPlaybackState(previousPlan, removedTargetKeys, remainingPlan.Count);
        }

        public void ClearSamples()
        {
            HasLastAnimationSample = false;
            Array.Clear(HasLastAnimationPresentationSamples);
            HasLastScrollPresentationSample = false;
        }

        public void SetLastAnimationSample(in CompositionTimelineSample sample)
        {
            LastAnimationSample = sample;
            HasLastAnimationSample = true;
        }

        public void SetLastScrollPresentationSample(in CompositionTimelineSample sample)
        {
            LastScrollPresentationSample = sample;
            HasLastScrollPresentationSample = true;
        }

        private void ClearAnimationPresentationPlaybackState()
        {
            AnimationPresentationMarkerStates = [];
            LastAnimationPresentationSamples = [];
            HasLastAnimationPresentationSamples = [];
        }

        private void RemapAnimationPresentationPlaybackState(
            in CompositionAnimationPresentationSetPlan plan,
            ReadOnlySpan<NodeKey> removedTargetKeys,
            int remainingCount)
        {
            if (remainingCount <= 0)
            {
                ClearAnimationPresentationPlaybackState();
                return;
            }

            var markerStates = new CompositionAnimationMarkerPlaybackState[remainingCount][];
            var samples = new CompositionTimelineSample[remainingCount];
            var hasSamples = new bool[remainingCount];
            var targetIndex = 0;
            for (var i = 0; i < plan.Count; i++)
            {
                var layerAnimation = plan.GetPlan(i).LayerAnimation;
                if (ContainsTargetKey(removedTargetKeys, layerAnimation.TargetKey))
                {
                    continue;
                }

                markerStates[targetIndex] = i < AnimationPresentationMarkerStates.Length
                    ? AnimationPresentationMarkerStates[i]
                    : CreateMarkerPlaybackStates(layerAnimation.Markers);
                if (i < LastAnimationPresentationSamples.Length)
                {
                    samples[targetIndex] = LastAnimationPresentationSamples[i];
                }

                hasSamples[targetIndex] = i < HasLastAnimationPresentationSamples.Length
                    && HasLastAnimationPresentationSamples[i];
                targetIndex++;
            }

            AnimationPresentationMarkerStates = markerStates;
            LastAnimationPresentationSamples = samples;
            HasLastAnimationPresentationSamples = hasSamples;
        }

        private static CompositionAnimationMarkerPlaybackState[] CreateMarkerPlaybackStates(ReadOnlySpan<CompositionAnimationMarker> markers)
        {
            if (markers.Length == 0)
            {
                return [];
            }

            var states = new CompositionAnimationMarkerPlaybackState[markers.Length];
            for (var i = 0; i < markers.Length; i++)
            {
                states[i] = new CompositionAnimationMarkerPlaybackState(markers[i].Id);
            }

            return states;
        }

        private static CompositionAnimationMarkerPlaybackState[][] CreatePresentationMarkerPlaybackStates(
            in CompositionAnimationPresentationSetPlan plan)
        {
            if (plan.IsEmpty)
            {
                return [];
            }

            var states = new CompositionAnimationMarkerPlaybackState[plan.Count][];
            for (var i = 0; i < plan.Count; i++)
            {
                states[i] = CreateMarkerPlaybackStates(plan.GetPlan(i).LayerAnimation.Markers);
            }

            return states;
        }
    }
}
