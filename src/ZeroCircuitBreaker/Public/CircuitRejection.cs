namespace ZeroCircuitBreaker;

/// <summary>Describes a non-throwing acquisition rejection.</summary>
public readonly struct CircuitRejection
{
    internal CircuitRejection(CircuitState state, TimeSpan? retryAfter)
    {
        State = state;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the state that rejected acquisition.</summary>
    public CircuitState State { get; }
    /// <summary>Gets the remaining duration for Open, or <see langword='null'/> for HalfOpen.</summary>
    public TimeSpan? RetryAfter { get; }
}
