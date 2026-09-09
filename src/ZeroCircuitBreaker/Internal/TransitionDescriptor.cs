namespace ZeroCircuitBreaker.Internal;

internal sealed class TransitionDescriptor
{
    internal TransitionDescriptor(
        long sourcePacked,
        long transitioningPacked,
        long targetPacked,
        CircuitState previousState,
        CircuitState targetState,
        CircuitTransitionReason reason,
        FailureEpoch targetEpoch,
        long targetOpenTimestamp)
    {
        SourcePacked = sourcePacked;
        TransitioningPacked = transitioningPacked;
        TargetPacked = targetPacked;
        PreviousState = previousState;
        TargetState = targetState;
        Reason = reason;
        TargetEpoch = targetEpoch;
        TargetOpenTimestamp = targetOpenTimestamp;
    }

    internal long SourcePacked { get; }
    internal long TransitioningPacked { get; }
    internal long TargetPacked { get; }
    internal CircuitState PreviousState { get; }
    internal CircuitState TargetState { get; }
    internal CircuitTransitionReason Reason { get; }
    internal FailureEpoch TargetEpoch { get; }
    internal long TargetOpenTimestamp { get; }
    internal int ReservationWon;
    internal int CallbackClaimed;
}
