using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.Tests;

public sealed class ClassificationTests
{
    [Fact]
    public void Success_resets_consecutive_failures()
    {
        var breaker = CreateBreaker(threshold: 3);
        RecordFailure(breaker);
        RecordFailure(breaker);

        breaker.Execute(static () => { });

        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Exception_filter_replaces_default_classification()
    {
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            IsFailureException = static exception => exception is InvalidOperationException
        });

        Assert.Throws<ArgumentException>(() => breaker.Execute(static () => throw new ArgumentException("ignored")));
        Assert.Equal(0, breaker.GetSnapshot().ConsecutiveFailures);

        Assert.Throws<InvalidOperationException>(() => breaker.Execute(static () => throw new InvalidOperationException("counted")));
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public void Caller_requested_cancellation_never_counts_as_failure()
    {
        var classifierCalls = 0;
        var breaker = new Breaker(new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            IsFailureException = _ =>
            {
                classifierCalls++;
                return true;
            }
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            breaker.Execute(() => throw new OperationCanceledException(cancellation.Token), cancellation.Token));

        Assert.Equal(0, classifierCalls);
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Noncaller_operation_canceled_exception_counts_by_default()
    {
        var breaker = CreateBreaker(threshold: 1);

        Assert.Throws<OperationCanceledException>(() =>
            breaker.Execute(static () => throw new OperationCanceledException()));

        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Result_classified_as_failure_is_still_returned()
    {
        var breaker = CreateBreaker(threshold: 1);

        var result = breaker.Execute(static () => 503, static value => value >= 500);

        Assert.Equal(503, result);
        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Result_predicate_is_evaluated_once()
    {
        var breaker = CreateBreaker(threshold: 2);
        var predicateCalls = 0;

        var result = breaker.Execute(
            static () => 17,
            value =>
            {
                predicateCalls++;
                return value == 17;
            });

        Assert.Equal(17, result);
        Assert.Equal(1, predicateCalls);
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public async Task Task_and_ValueTask_paths_share_exception_and_result_classification()
    {
        var breaker = CreateBreaker(threshold: 3);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            breaker.ExecuteTaskAsync(static _ => Task.FromException(new InvalidOperationException("task"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            breaker.ExecuteAsync(static _ => ValueTask.FromException(new InvalidOperationException("value-task"))).AsTask());

        var result = await breaker.ExecuteAsync(
            static _ => ValueTask.FromResult(500),
            static value => value == 500);

        Assert.Equal(500, result);
        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
    }

    internal static Breaker CreateBreaker(int threshold) =>
        new(new CircuitBreakerOptions
        {
            FailureThreshold = threshold,
            BreakDuration = TimeSpan.FromMinutes(1)
        });

    internal static void RecordFailure(Breaker breaker) =>
        Assert.Throws<InvalidOperationException>(() =>
            breaker.Execute(static () => throw new InvalidOperationException("dependency failure")));
}
