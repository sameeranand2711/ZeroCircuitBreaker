using BenchmarkDotNet.Attributes;

namespace ZeroCircuitBreaker.Benchmarks;

[CircuitBenchmarkConfig]
[MemoryDiagnoser]
public class CallbackBenchmarks
{
    private readonly CircuitBreaker _withoutCallback = new();
    private readonly CircuitBreaker _withCallback;
    private int _callbackCount;

    public CallbackBenchmarks()
    {
        _withCallback = new CircuitBreaker(new CircuitBreakerOptions
        {
            OnStateChanged = _ => _callbackCount++
        });
    }

    [Benchmark(Baseline = true)]
    public void StateChangeWithoutCallback() => _withoutCallback.Reset();

    [Benchmark]
    public void StateChangeWithCallback() => _withCallback.Reset();
}
