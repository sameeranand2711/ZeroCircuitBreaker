using ZeroCircuitBreaker.Internal;

namespace ZeroCircuitBreaker;

/// <summary>Represents one admitted operation in the non-throwing API.</summary>
/// <remarks>
/// An acquired lease must be completed exactly once. Copies share the same completion gate; duplicate and default-lease
/// completion attempts throw <see cref='InvalidOperationException'/> without changing circuit state. A first completion
/// that became stale at a newer logical boundary returns normally without changing that newer generation.
/// </remarks>
public readonly struct CircuitLease
{
    private readonly LeaseCompletion? _completion;

    internal CircuitLease(LeaseCompletion completion) => _completion = completion;

    /// <summary>Completes the lease successfully.</summary>
    /// <exception cref='InvalidOperationException'>The lease is the default value or has already been completed through this value or a copy.</exception>
    public void CompleteSuccess()
    {
        var completion = GetCompletion();
        completion.Claim();
        completion.Breaker.CompleteSuccess(completion.Token);
    }

    /// <summary>Completes the lease as an unconditional dependency failure.</summary>
    /// <exception cref='InvalidOperationException'>The lease is the default value or has already been completed through this value or a copy.</exception>
    public void CompleteFailure()
    {
        var completion = GetCompletion();
        completion.Claim();
        completion.Breaker.CompleteFailure(completion.Token);
    }

    /// <summary>Completes the lease by classifying an exception using cancellation and the configured exception classifier.</summary>
    /// <param name='exception'>The exception to classify. The exception is not thrown by this method.</param>
    /// <exception cref='ArgumentNullException'><paramref name='exception'/> is <see langword='null'/>.</exception>
    /// <exception cref='InvalidOperationException'>The lease is the default value or has already been completed through this value or a copy.</exception>
    public void CompleteFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var completion = GetCompletion();
        completion.Claim();
        completion.Breaker.CompleteException(completion.Token, exception, default, useCallToken: false);
    }

    private LeaseCompletion GetCompletion() =>
        _completion ?? throw new InvalidOperationException();
}
