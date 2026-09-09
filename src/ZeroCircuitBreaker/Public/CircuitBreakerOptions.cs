namespace ZeroCircuitBreaker;

/// <summary>Configures a circuit breaker.</summary>
/// <remarks>A breaker snapshots these values during construction; changing or reusing this object cannot reconfigure an existing breaker.</remarks>
public sealed class CircuitBreakerOptions
{
    /// <summary>Gets the consecutive failure threshold. The default is 5.</summary>
    public int FailureThreshold { get; init; } = 5;
    /// <summary>Gets the duration for which an open circuit rejects operations. The default is 30 seconds.</summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets an optional replacement classifier for non-caller-cancellation exceptions.</summary>
    /// <remarks>Caller-requested cancellation never reaches this classifier and cannot be classified as a dependency failure.</remarks>
    public Func<Exception, bool>? IsFailureException { get; init; }
    /// <summary>Gets an optional synchronous callback invoked after each logical state boundary is fully published.</summary>
    /// <remarks>The callback may re-enter the breaker. Exceptions from the callback are suppressed and do not roll back the transition.</remarks>
    public Action<CircuitStateChange>? OnStateChanged { get; init; }
}
