namespace ZeroCircuitBreaker;

/// <summary>Represents rejection from a throwing execution API.</summary>
public sealed class CircuitBreakerOpenException : Exception
{
    internal CircuitBreakerOpenException(CircuitRejection rejection)
        : base(string.Concat(rejection.State, ' ', rejection.RetryAfter))
    {
        State = rejection.State;
        RetryAfter = rejection.RetryAfter;
    }

    /// <summary>Gets the rejecting state.</summary>
    public CircuitState State { get; }
    /// <summary>Gets the remaining duration for Open, or <see langword='null'/> for HalfOpen.</summary>
    public TimeSpan? RetryAfter { get; }
}
