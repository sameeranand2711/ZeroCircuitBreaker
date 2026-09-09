namespace ZeroCircuitBreaker;

/// <summary>Identifies the public state of a circuit.</summary>
public enum CircuitState
{
    /// <summary>Protected operations are admitted.</summary>
    Closed = 0,
    /// <summary>Protected operations are rejected until the break duration elapses.</summary>
    Open = 1,
    /// <summary>One probe is in progress and other operations are rejected.</summary>
    HalfOpen = 2
}
