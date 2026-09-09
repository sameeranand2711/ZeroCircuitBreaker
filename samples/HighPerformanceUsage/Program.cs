using ZeroCircuitBreaker;

var dependency = new CounterDependency();
var breaker = new CircuitBreaker();
using var applicationStopping = new CancellationTokenSource();

// State passing allows a static delegate and avoids a captured closure.
var updatedValue = breaker.Execute(
    (Dependency: dependency, Amount: 5),
    static (state, token) => state.Dependency.Add(state.Amount, token),
    applicationStopping.Token);

Console.WriteLine($"State-passing result: {updatedValue}.");

// TryAcquire avoids an exception when rejection is an expected control-flow case.
if (breaker.TryAcquire(out var lease, out var rejection))
{
    int currentValue;
    try
    {
        currentValue = await dependency.ReadAsync(applicationStopping.Token);
    }
    catch (Exception exception)
    {
        lease.CompleteFailure(exception);
        throw;
    }

    lease.CompleteSuccess();
    Console.WriteLine($"Lease-protected result: {currentValue}.");
}
else
{
    Console.WriteLine($"Rejected in {rejection.State}; retry after {rejection.RetryAfter}.");
}

breaker.Open();
if (!breaker.TryAcquire(out _, out rejection))
{
    Console.WriteLine($"Non-throwing rejection: {rejection.State}; retry after {rejection.RetryAfter}.");
}

breaker.Reset();
var valueTaskResult = await breaker.ExecuteAsync(
    dependency,
    static (state, token) => state.ReadAsync(token),
    applicationStopping.Token);

Console.WriteLine($"ValueTask state-passing result: {valueTaskResult}.");

internal sealed class CounterDependency
{
    private int _value;

    internal int Add(int amount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Interlocked.Add(ref _value, amount);
    }

    internal ValueTask<int> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Volatile.Read(ref _value));
    }
}
