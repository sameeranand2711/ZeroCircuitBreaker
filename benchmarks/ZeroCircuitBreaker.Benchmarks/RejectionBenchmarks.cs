using BenchmarkDotNet.Attributes;

namespace ZeroCircuitBreaker.Benchmarks;

[CircuitBenchmarkConfig]
[MemoryDiagnoser]
public class RejectionBenchmarks
{
    private static readonly Action NoOp = static () => { };
    private readonly CircuitBreaker _open;

    public RejectionBenchmarks()
    {
        _open = new CircuitBreaker(new CircuitBreakerOptions
        {
            BreakDuration = TimeSpan.FromDays(1)
        });
        _open.Open();
    }

    [Benchmark(Baseline = true)]
    public bool OpenTryAcquireRejection() =>
        _open.TryAcquire(out _, out _);

    [Benchmark]
    public CircuitState OpenThrowingRejection()
    {
        try
        {
            _open.Execute(NoOp);
        }
        catch (CircuitBreakerOpenException exception)
        {
            return exception.State;
        }

        throw new InvalidOperationException("The open breaker unexpectedly admitted work.");
    }
}
