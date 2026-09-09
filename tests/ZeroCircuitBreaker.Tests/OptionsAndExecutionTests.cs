using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.Tests;

public sealed class OptionsAndExecutionTests
{
    [Fact]
    public void Options_have_documented_defaults()
    {
        var options = new CircuitBreakerOptions();

        Assert.Equal(5, options.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakDuration);
        Assert.Null(options.IsFailureException);
        Assert.Null(options.OnStateChanged);
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new Breaker(null!));
    }

    [Fact]
    public void Constructor_rejects_null_time_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new Breaker(new CircuitBreakerOptions(), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_nonpositive_failure_threshold(int threshold)
    {
        var options = new CircuitBreakerOptions { FailureThreshold = threshold };

        Assert.Throws<ArgumentOutOfRangeException>(() => new Breaker(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_nonpositive_break_duration(int ticks)
    {
        var options = new CircuitBreakerOptions { BreakDuration = TimeSpan.FromTicks(ticks) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new Breaker(options));
    }

    [Fact]
    public void Closed_execute_invokes_action_and_returns_result()
    {
        var breaker = new Breaker();
        var invoked = false;

        breaker.Execute(() => invoked = true);
        var result = breaker.Execute(() => 42);

        Assert.True(invoked);
        Assert.Equal(42, result);
        Assert.Equal(CircuitState.Closed, breaker.GetSnapshot().State);
    }

    [Fact]
    public void State_passing_sync_overloads_forward_state_and_token()
    {
        var breaker = new Breaker();
        using var cancellation = new CancellationTokenSource();
        var state = new InvocationState();

        breaker.Execute(
            state,
            static (value, token) =>
            {
                value.Token = token;
                value.Calls++;
            },
            cancellation.Token);

        var result = breaker.Execute(
            state,
            static (value, token) =>
            {
                value.Token = token;
                return ++value.Calls;
            },
            cancellation.Token);

        var failedResult = breaker.Execute(
            state,
            static (value, token) =>
            {
                value.Token = token;
                return ++value.Calls;
            },
            static value => value == 3,
            cancellation.Token);

        Assert.Equal(2, result);
        Assert.Equal(3, failedResult);
        Assert.Equal(cancellation.Token, state.Token);
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public async Task ValueTask_overloads_execute_and_forward_state_and_token()
    {
        var breaker = new Breaker();
        using var cancellation = new CancellationTokenSource();
        var state = new InvocationState();

        await breaker.ExecuteAsync(
            state,
            static (value, token) =>
            {
                value.Token = token;
                value.Calls++;
                return ValueTask.CompletedTask;
            },
            cancellation.Token);

        var result = await breaker.ExecuteAsync(
            state,
            static (value, token) =>
            {
                value.Token = token;
                return ValueTask.FromResult(++value.Calls);
            },
            cancellation.Token);

        var failedResult = await breaker.ExecuteAsync(
            state,
            static (value, token) =>
            {
                value.Token = token;
                return ValueTask.FromResult(++value.Calls);
            },
            static value => value == 3,
            cancellation.Token);

        Assert.Equal(2, result);
        Assert.Equal(3, failedResult);
        Assert.Equal(cancellation.Token, state.Token);
        Assert.Equal(1, breaker.GetSnapshot().ConsecutiveFailures);
    }

    [Fact]
    public async Task Task_convenience_overloads_complete_after_circuit_accounting()
    {
        var breaker = new Breaker(new CircuitBreakerOptions { FailureThreshold = 1 });

        await breaker.ExecuteTaskAsync(static _ => Task.CompletedTask);
        var result = await breaker.ExecuteTaskAsync(static _ => Task.FromResult(7));
        var failedResult = await breaker.ExecuteTaskAsync(
            static _ => Task.FromResult(9),
            static value => value == 9);

        Assert.Equal(7, result);
        Assert.Equal(9, failedResult);
        Assert.Equal(CircuitState.Open, breaker.GetSnapshot().State);
    }

    [Fact]
    public void Null_delegates_are_rejected_before_admission()
    {
        var breaker = new Breaker();

        Assert.Throws<ArgumentNullException>(() => breaker.Execute((Action)null!));
        Assert.Throws<ArgumentNullException>(() => breaker.Execute((Func<int>)null!));
        Assert.Throws<ArgumentNullException>(() => breaker.Execute(() => 1, null!));
        Assert.Throws<ArgumentNullException>(() => breaker.Execute(1, (Action<int, CancellationToken>)null!));
        Assert.Throws<ArgumentNullException>(() => breaker.Execute(1, (Func<int, CancellationToken, int>)null!));
        Assert.Throws<ArgumentNullException>(() => breaker.Execute(1, static (_, _) => 1, null!));
    }

    [Fact]
    public async Task Null_async_delegates_are_rejected_before_admission()
    {
        var breaker = new Breaker();

        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteTaskAsync(null!).AsTaskForAssertion());
        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteTaskAsync<int>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteTaskAsync(static _ => Task.FromResult(1), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteAsync<int>(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => breaker.ExecuteAsync(static _ => ValueTask.FromResult(1), null!).AsTask());
    }

    private sealed class InvocationState
    {
        public int Calls { get; set; }
        public CancellationToken Token { get; set; }
    }
}

internal static class TaskAssertionExtensions
{
    public static Task AsTaskForAssertion(this Task task) => task;
}
