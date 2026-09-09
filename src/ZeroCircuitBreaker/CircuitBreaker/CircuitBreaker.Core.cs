namespace ZeroCircuitBreaker;

using ZeroCircuitBreaker.Internal;

public sealed partial class CircuitBreaker
{
    private const int StateMask = 0b11;
    private const int TransitioningStateCode = 0b11;
    private const ulong GenerationMask = (1UL << 62) - 1;

    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly Func<Exception, bool>? _isFailureException;
    private readonly Action<CircuitStateChange>? _onStateChanged;
    private readonly TimeProvider _timeProvider;

    private long _packedState;
    private FailureEpoch _failureEpoch;
    private long _openTimestamp;
    private TransitionDescriptor? _transition;

    internal void CompleteSuccess(OperationToken token)
    {
        if (token.IsProbe)
        {
            TryTransition(
                token.PackedState,
                CircuitState.Closed,
                CircuitTransitionReason.HalfOpenProbeSucceeded,
                0,
                out _,
                out _);
            return;
        }

        ResetFailureCount(token);
    }

    internal void CompleteFailure(OperationToken token)
    {
        if (token.IsProbe)
        {
            TryTransition(
                token.PackedState,
                CircuitState.Open,
                CircuitTransitionReason.HalfOpenProbeFailed,
                _timeProvider.GetTimestamp(),
                out _,
                out _);
            return;
        }

        RecordClosedFailure(token);
    }

    internal void CompleteException(
        OperationToken token,
        Exception exception,
        CancellationToken callToken,
        bool useCallToken)
    {
        if (IsCallerCancellation(exception, callToken, useCallToken))
        {
            CompleteSuccess(token);
            return;
        }

        if (_isFailureException?.Invoke(exception) ?? true)
        {
            CompleteFailure(token);
        }
        else
        {
            CompleteSuccess(token);
        }
    }

    private OperationToken AcquireOrThrow()
    {
        if (TryAcquireToken(out var token, out var rejection))
        {
            return token;
        }

        throw new CircuitBreakerOpenException(rejection);
    }

    private bool TryAcquireToken(out OperationToken token, out CircuitRejection rejection)
    {
        while (true)
        {
            var packed = ReadStablePackedState();
            var state = GetPublicState(packed);
            if (state == CircuitState.Closed)
            {
                var epoch = Volatile.Read(ref _failureEpoch);
                if (Volatile.Read(ref _packedState) != packed ||
                    Volatile.Read(ref _transition) is not null ||
                    !ReferenceEquals(Volatile.Read(ref _failureEpoch), epoch))
                {
                    continue;
                }

                if (epoch.IsSealed)
                {
                    TryTransition(
                        packed,
                        CircuitState.Open,
                        CircuitTransitionReason.FailureThresholdReached,
                        _timeProvider.GetTimestamp(),
                        out _,
                        out _);
                    continue;
                }

                token = new OperationToken(packed, epoch, isProbe: false);
                rejection = default;
                return true;
            }

            if (state == CircuitState.HalfOpen)
            {
                token = default;
                rejection = new CircuitRejection(CircuitState.HalfOpen, null);
                return false;
            }

            var openTimestamp = Volatile.Read(ref _openTimestamp);
            if (Volatile.Read(ref _packedState) != packed ||
                Volatile.Read(ref _transition) is not null)
            {
                continue;
            }

            var retryAfter = GetRetryAfter(openTimestamp);
            if (retryAfter > TimeSpan.Zero)
            {
                token = default;
                rejection = new CircuitRejection(CircuitState.Open, retryAfter);
                return false;
            }

            if (TryTransition(
                packed,
                CircuitState.HalfOpen,
                CircuitTransitionReason.BreakDurationElapsed,
                0,
                out var halfOpenPacked,
                out var halfOpenEpoch))
            {
                token = new OperationToken(halfOpenPacked, halfOpenEpoch, isProbe: true);
                rejection = default;
                return true;
            }
        }
    }

    private void ResetFailureCount(OperationToken token)
    {
        while (true)
        {
            if (!IsCurrentClosedToken(token))
            {
                return;
            }

            var epochState = Volatile.Read(ref token.Epoch.State);
            if (FailureEpoch.IsSealedState(epochState) ||
                FailureEpoch.GetCount(epochState) == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref token.Epoch.State, 0, epochState) == epochState)
            {
                return;
            }
        }
    }

    private void RecordClosedFailure(OperationToken token)
    {
        while (true)
        {
            if (!IsCurrentClosedToken(token))
            {
                return;
            }

            var epochState = Volatile.Read(ref token.Epoch.State);
            if (FailureEpoch.IsSealedState(epochState))
            {
                TryOpenSealedEpoch(token);
                return;
            }

            var count = FailureEpoch.GetCount(epochState);
            var nextCount = count + 1;
            var reachesThreshold = nextCount >= _failureThreshold;
            var nextState = reachesThreshold
                ? FailureEpoch.CreateSealedState(_failureThreshold)
                : nextCount;
            if (!IsCurrentClosedToken(token))
            {
                return;
            }

            if (Interlocked.CompareExchange(ref token.Epoch.State, nextState, epochState) != epochState)
            {
                continue;
            }

            if (reachesThreshold)
            {
                TryOpenSealedEpoch(token);
            }

            return;
        }
    }

    private void TryOpenSealedEpoch(OperationToken token)
    {
        if (!ReferenceEquals(Volatile.Read(ref _failureEpoch), token.Epoch) ||
            !token.Epoch.IsSealed)
        {
            return;
        }

        TryTransition(
            token.PackedState,
            CircuitState.Open,
            CircuitTransitionReason.FailureThresholdReached,
            _timeProvider.GetTimestamp(),
            out _,
            out _);
    }

    private bool IsCurrentClosedToken(OperationToken token) =>
        !token.IsProbe &&
        Volatile.Read(ref _packedState) == token.PackedState &&
        GetStateCode(token.PackedState) == (int)CircuitState.Closed &&
        Volatile.Read(ref _transition) is null &&
        ReferenceEquals(Volatile.Read(ref _failureEpoch), token.Epoch);

    private void TransitionManually(CircuitState targetState, CircuitTransitionReason reason)
    {
        while (true)
        {
            var source = ReadStablePackedState();
            var timestamp = targetState == CircuitState.Open
                ? _timeProvider.GetTimestamp()
                : 0;
            if (TryTransition(source, targetState, reason, timestamp, out _, out _))
            {
                return;
            }
        }
    }

    private bool TryTransition(
        long sourcePacked,
        CircuitState targetState,
        CircuitTransitionReason reason,
        long targetOpenTimestamp,
        out long targetPacked,
        out FailureEpoch targetEpoch)
    {
        var previousState = GetPublicState(sourcePacked);
        var generation = NextGeneration(sourcePacked);
        var transitioningPacked = Pack(generation, TransitioningStateCode);
        targetPacked = Pack(generation, (int)targetState);
        targetEpoch = new FailureEpoch();
        var descriptor = new TransitionDescriptor(
            sourcePacked,
            transitioningPacked,
            targetPacked,
            previousState,
            targetState,
            reason,
            targetEpoch,
            targetOpenTimestamp);

        var existing = Interlocked.CompareExchange(ref _transition, descriptor, null);
        if (existing is not null)
        {
            HelpTransition(existing);
            return false;
        }

        var observed = Interlocked.CompareExchange(
            ref _packedState,
            transitioningPacked,
            sourcePacked);
        if (observed == sourcePacked || observed == transitioningPacked)
        {
            Volatile.Write(ref descriptor.ReservationWon, 1);
        }
        else if (observed != targetPacked ||
                 Volatile.Read(ref descriptor.ReservationWon) == 0)
        {
            Interlocked.CompareExchange(ref _transition, null, descriptor);
            return false;
        }

        HelpTransition(descriptor);
        return Volatile.Read(ref descriptor.ReservationWon) != 0;
    }

    private long ReadStablePackedState()
    {
        while (true)
        {
            var descriptor = Volatile.Read(ref _transition);
            if (descriptor is not null)
            {
                HelpTransition(descriptor);
                continue;
            }

            var packed = Volatile.Read(ref _packedState);
            if (GetStateCode(packed) != TransitioningStateCode)
            {
                return packed;
            }
        }
    }

    private void HelpTransition(TransitionDescriptor descriptor)
    {
        if (!ReferenceEquals(Volatile.Read(ref _transition), descriptor))
        {
            return;
        }

        var observed = Interlocked.CompareExchange(
            ref _packedState,
            descriptor.TransitioningPacked,
            descriptor.SourcePacked);
        if (observed == descriptor.SourcePacked ||
            observed == descriptor.TransitioningPacked)
        {
            Volatile.Write(ref descriptor.ReservationWon, 1);
        }
        else if (Volatile.Read(ref descriptor.ReservationWon) == 0)
        {
            Interlocked.CompareExchange(ref _transition, null, descriptor);
            return;
        }

        var packed = Volatile.Read(ref _packedState);
        if (packed == descriptor.TransitioningPacked)
        {
            Volatile.Write(ref _failureEpoch, descriptor.TargetEpoch);
            Volatile.Write(ref _openTimestamp, descriptor.TargetOpenTimestamp);
            Interlocked.CompareExchange(
                ref _packedState,
                descriptor.TargetPacked,
                descriptor.TransitioningPacked);
            packed = Volatile.Read(ref _packedState);
        }

        if (packed != descriptor.TargetPacked)
        {
            return;
        }

        Interlocked.CompareExchange(ref _transition, null, descriptor);
        if (Interlocked.CompareExchange(ref descriptor.CallbackClaimed, 1, 0) == 0)
        {
            PublishStateChange(descriptor);
        }
    }

    private void PublishStateChange(TransitionDescriptor descriptor)
    {
        if (_onStateChanged is null)
        {
            return;
        }

        try
        {
            _onStateChanged(new CircuitStateChange(
                descriptor.PreviousState,
                descriptor.TargetState,
                descriptor.Reason));
        }
        catch
        {
        }
    }

    private CircuitBreakerSnapshot ReadSnapshot()
    {
        while (true)
        {
            var packed = ReadStablePackedState();
            var state = GetPublicState(packed);
            var epoch = Volatile.Read(ref _failureEpoch);
            var openTimestamp = Volatile.Read(ref _openTimestamp);
            var failures = state == CircuitState.Closed ? epoch.Count : 0;
            var retryAfter = state == CircuitState.Open
                ? GetRetryAfter(openTimestamp)
                : (TimeSpan?)null;
            if (Volatile.Read(ref _packedState) != packed ||
                Volatile.Read(ref _transition) is not null ||
                !ReferenceEquals(Volatile.Read(ref _failureEpoch), epoch))
            {
                continue;
            }

            return new CircuitBreakerSnapshot(state, failures, retryAfter);
        }
    }

    private TimeSpan GetRetryAfter(long openTimestamp)
    {
        var elapsed = _timeProvider.GetElapsedTime(
            openTimestamp,
            _timeProvider.GetTimestamp());
        var remaining = _breakDuration - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static bool IsCallerCancellation(
        Exception exception,
        CancellationToken callToken,
        bool useCallToken)
    {
        if (exception is not OperationCanceledException cancellation)
        {
            return false;
        }

        return useCallToken
            ? callToken.IsCancellationRequested
            : cancellation.CancellationToken.IsCancellationRequested;
    }

    private static long Pack(ulong generation, int stateCode) =>
        unchecked((long)(((generation & GenerationMask) << 2) | (uint)stateCode));

    private static ulong NextGeneration(long packed) =>
        (GetGeneration(packed) + 1UL) & GenerationMask;

    private static ulong GetGeneration(long packed) =>
        unchecked((ulong)packed) >> 2;

    private static int GetStateCode(long packed) => (int)(packed & StateMask);

    private static CircuitState GetPublicState(long packed) =>
        GetStateCode(packed) switch
        {
            (int)CircuitState.Closed => CircuitState.Closed,
            (int)CircuitState.Open => CircuitState.Open,
            (int)CircuitState.HalfOpen => CircuitState.HalfOpen,
            _ => throw new InvalidOperationException()
        };
}
