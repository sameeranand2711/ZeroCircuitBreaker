namespace ZeroCircuitBreaker;

/// <summary>Contains a diagnostic snapshot of a circuit.</summary>
/// <remarks>Snapshots are informational and must not be used to decide whether work may be admitted.</remarks>
public readonly struct CircuitBreakerSnapshot
{
    internal CircuitBreakerSnapshot(CircuitState state, int consecutiveFailures, TimeSpan? retryAfter)
    {
        State = state;
        ConsecutiveFailures = consecutiveFailures;
        RetryAfter = retryAfter;
    }

    /// <summary>Gets the observed public state.</summary>
    public CircuitState State { get; }
    /// <summary>Gets the observed consecutive failure count.</summary>
    public int ConsecutiveFailures { get; }
    /// <summary>Gets the diagnostic remaining break duration when Open, or <see langword='null'/> otherwise.</summary>
    public TimeSpan? RetryAfter { get; }
}
