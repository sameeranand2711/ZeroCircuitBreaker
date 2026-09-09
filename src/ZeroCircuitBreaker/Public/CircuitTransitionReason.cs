namespace ZeroCircuitBreaker;

/// <summary>Identifies why a circuit crossed a logical state boundary.</summary>
public enum CircuitTransitionReason
{
    /// <summary>The consecutive failure threshold was reached.</summary>
    FailureThresholdReached = 0,
    /// <summary>The configured break duration elapsed.</summary>
    BreakDurationElapsed = 1,
    /// <summary>The half-open probe succeeded.</summary>
    HalfOpenProbeSucceeded = 2,
    /// <summary>The half-open probe failed.</summary>
    HalfOpenProbeFailed = 3,
    /// <summary>The circuit was opened manually.</summary>
    ManuallyOpened = 4,
    /// <summary>The circuit was reset manually.</summary>
    ManuallyReset = 5
}
