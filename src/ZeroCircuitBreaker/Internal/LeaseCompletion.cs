namespace ZeroCircuitBreaker.Internal;

internal sealed class LeaseCompletion
{
    private int _completed;

    internal LeaseCompletion(CircuitBreaker breaker, OperationToken token)
    {
        Breaker = breaker;
        Token = token;
    }

    internal CircuitBreaker Breaker { get; }
    internal OperationToken Token { get; }

    internal void Claim()
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
        {
            throw new InvalidOperationException();
        }
    }
}
