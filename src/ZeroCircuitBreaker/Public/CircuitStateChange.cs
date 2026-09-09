namespace ZeroCircuitBreaker;

/// <summary>Describes a fully published circuit state transition.</summary>
public readonly struct CircuitStateChange
{
    internal CircuitStateChange(CircuitState previousState, CircuitState currentState, CircuitTransitionReason reason)
    {
        PreviousState = previousState;
        CurrentState = currentState;
        Reason = reason;
    }

    /// <summary>Gets the state before the transition.</summary>
    public CircuitState PreviousState { get; }
    /// <summary>Gets the state after the transition.</summary>
    public CircuitState CurrentState { get; }
    /// <summary>Gets the transition reason.</summary>
    public CircuitTransitionReason Reason { get; }
}
