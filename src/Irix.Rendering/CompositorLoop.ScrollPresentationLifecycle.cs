namespace Irix.Rendering;

internal sealed partial class CompositorLoop
{
    private sealed class ScrollPresentationLifecycle
    {
        private readonly Lock _gate = new();
        private CompositionScrollPresentationSchedule _schedule;
        private CompositionScrollPresentationDeclaration _activeDeclaration;
        private CompositionScrollPresentationDeclaration _heldDeclaration;
        private ScrollPresentationRetargetHold _pendingRetargetHold;
        private bool _tickQueued;
        private int _generation;
        private RenderCompletionWaitGroup? _idleWaitGroup;

        public bool HasActive(NodeKey targetKey)
        {
            lock (_gate)
            {
                return _schedule.IsActive && _schedule.TargetKey == targetKey;
            }
        }

        public Task? AddIdleWaiterIfBusy()
        {
            lock (_gate)
            {
                if (!_schedule.IsActive && !_tickQueued)
                {
                    return null;
                }

                _idleWaitGroup ??= new RenderCompletionWaitGroup();
                return _idleWaitGroup.AddWaiter();
            }
        }

        public int Activate(in CompositionScrollPresentationDeclaration declaration)
        {
            RenderCompletionWaitGroup? supersededWaitGroup;
            int generation;
            lock (_gate)
            {
                supersededWaitGroup = _idleWaitGroup;
                _idleWaitGroup = null;
                generation = NextGeneration();
                _schedule = new CompositionScrollPresentationSchedule(
                    generation,
                    declaration.TargetKey,
                    declaration.Timeline.StartTimestamp,
                    declaration.Timeline.StartTimestamp + declaration.Timeline.Duration);
                _activeDeclaration = declaration;
                _heldDeclaration = default;
                _pendingRetargetHold = default;
                _tickQueued = false;
            }

            supersededWaitGroup?.Complete(null);
            return generation;
        }

        public bool TryBeginTick(int generation, out CompositionScrollPresentationSchedule schedule)
        {
            lock (_gate)
            {
                if (_schedule.IsActive && _schedule.Generation == generation && !IsTickSuppressed(generation))
                {
                    _tickQueued = false;
                    schedule = _schedule;
                    return true;
                }

                schedule = default;
                return false;
            }
        }

        public void Complete(int generation)
        {
            RenderCompletionWaitGroup? idleWaitGroup = null;
            lock (_gate)
            {
                if (_schedule.IsActive && _schedule.Generation == generation)
                {
                    ClearPresentationState(clearHeld: true, advanceGeneration: false);
                    idleWaitGroup = TakeIdleWaitGroup();
                }
            }

            idleWaitGroup?.Complete(null);
        }

        public bool Cancel()
        {
            RenderCompletionWaitGroup? idleWaitGroup;
            var canceled = false;
            lock (_gate)
            {
                canceled = IsBusy;
                ClearPresentationState(clearHeld: true, advanceGeneration: true);
                idleWaitGroup = TakeIdleWaitGroup();
            }

            idleWaitGroup?.Complete(null);
            return canceled;
        }

        public bool CompleteForDispose()
        {
            RenderCompletionWaitGroup? idleWaitGroup;
            var canceled = false;
            lock (_gate)
            {
                canceled = IsBusy;
                ClearPresentationState(clearHeld: true, advanceGeneration: false);
                idleWaitGroup = TakeIdleWaitGroup();
            }

            idleWaitGroup?.Complete(null);
            return canceled;
        }

        public ScrollPresentationRetargetHold BeginHoldForRetarget(NodeKey targetKey)
        {
            if (targetKey == NodeKey.None)
            {
                return default;
            }

            lock (_gate)
            {
                if (!_schedule.IsActive || _schedule.TargetKey != targetKey)
                {
                    return default;
                }

                _pendingRetargetHold = new ScrollPresentationRetargetHold(_schedule.Generation, targetKey, _activeDeclaration);
                return _pendingRetargetHold;
            }
        }

        public void CompleteHoldForRetarget(
            in ScrollPresentationRetargetHold hold,
            in CompositionScrollPresentationSample sample)
        {
            if (!hold.HasValue)
            {
                return;
            }

            RenderCompletionWaitGroup? idleWaitGroup = null;
            lock (_gate)
            {
                if (_pendingRetargetHold.Generation != hold.Generation
                    || _pendingRetargetHold.TargetKey != hold.TargetKey)
                {
                    return;
                }

                _heldDeclaration = sample.HasValue
                    ? CreateHeldDeclaration(hold.Declaration, sample.PresentedScrollY)
                    : default;
                _schedule = default;
                _activeDeclaration = default;
                _pendingRetargetHold = default;
                _tickQueued = false;
                NextGeneration();
                idleWaitGroup = TakeIdleWaitGroup();
            }

            idleWaitGroup?.Complete(null);
        }

        public void CancelHoldForRetarget(in ScrollPresentationRetargetHold hold)
        {
            if (!hold.HasValue)
            {
                return;
            }

            lock (_gate)
            {
                if (_pendingRetargetHold.Generation == hold.Generation
                    && _pendingRetargetHold.TargetKey == hold.TargetKey)
                {
                    _pendingRetargetHold = default;
                }
            }
        }

        public bool TryQueueNextTick(
            int generation,
            CompositionTimestamp lastTickTimestamp,
            CompositionTimestamp afterRenderTimestamp,
            CompositionDuration targetFrameInterval,
            CompositionFramePacing framePacing,
            out CompositionDuration delay)
        {
            lock (_gate)
            {
                if (!_schedule.IsActive || _schedule.Generation != generation || _tickQueued || IsTickSuppressed(generation))
                {
                    delay = default;
                    return false;
                }

                delay = ComputeNextTickDelay(
                    lastTickTimestamp,
                    afterRenderTimestamp,
                    targetFrameInterval,
                    framePacing);
                _tickQueued = true;
                return true;
            }
        }

        public bool TryTakeQueuedTickForDispatch(int generation)
        {
            lock (_gate)
            {
                if (!_schedule.IsActive || _schedule.Generation != generation || !_tickQueued)
                {
                    return false;
                }

                if (IsTickSuppressed(generation))
                {
                    _tickQueued = false;
                    return false;
                }

                _tickQueued = false;
                return true;
            }
        }

        public void CompleteAfterDispatchFailure(int generation)
        {
            RenderCompletionWaitGroup? idleWaitGroup = null;
            lock (_gate)
            {
                if (_schedule.IsActive && _schedule.Generation == generation)
                {
                    ClearPresentationState(clearHeld: true, advanceGeneration: true);
                    idleWaitGroup = TakeIdleWaitGroup();
                }
            }

            idleWaitGroup?.Complete(null);
        }

        public bool TryGetPreservableDeclaration(out CompositionScrollPresentationDeclaration declaration)
        {
            lock (_gate)
            {
                if (_schedule.IsActive)
                {
                    declaration = _activeDeclaration;
                    return declaration.TargetKey == _schedule.TargetKey;
                }

                if (_heldDeclaration.TargetKey != NodeKey.None)
                {
                    declaration = _heldDeclaration;
                    return true;
                }

                declaration = default;
                return false;
            }
        }

        private bool IsBusy => _schedule.IsActive || _tickQueued;

        private bool IsTickSuppressed(int generation)
        {
            return _pendingRetargetHold.Generation == generation;
        }

        private RenderCompletionWaitGroup? TakeIdleWaitGroup()
        {
            var idleWaitGroup = _idleWaitGroup;
            _idleWaitGroup = null;
            return idleWaitGroup;
        }

        private void ClearPresentationState(bool clearHeld, bool advanceGeneration)
        {
            _schedule = default;
            _activeDeclaration = default;
            _pendingRetargetHold = default;
            if (clearHeld)
            {
                _heldDeclaration = default;
            }

            _tickQueued = false;
            if (advanceGeneration)
            {
                NextGeneration();
            }
        }

        private int NextGeneration()
        {
            unchecked
            {
                _generation++;
                if (_generation == 0)
                {
                    _generation++;
                }

                return _generation;
            }
        }

        private static CompositionScrollPresentationDeclaration CreateHeldDeclaration(
            in CompositionScrollPresentationDeclaration declaration,
            double presentedScrollY)
        {
            var heldScrollY = double.IsFinite(presentedScrollY) ? (float)presentedScrollY : 0f;
            if (!float.IsFinite(heldScrollY))
            {
                heldScrollY = 0f;
            }

            return new CompositionScrollPresentationDeclaration(
                declaration.TargetKey,
                new CompositionAnimationTimeline(declaration.Timeline.StartTimestamp, CompositionDuration.Zero),
                CompositionScalarAnimation.Constant(heldScrollY),
                declaration.InstanceId);
        }
    }
}
