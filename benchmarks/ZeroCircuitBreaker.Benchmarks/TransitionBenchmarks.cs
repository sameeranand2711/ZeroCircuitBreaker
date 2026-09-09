using BenchmarkDotNet.Attributes;

namespace ZeroCircuitBreaker.Benchmarks;

[TransitionBenchmarkConfig]
[MemoryDiagnoser]
public class TransitionBenchmarks
{
    private const int OperationCount = 32768;
    private static readonly Func<int> ReturnOne = static () => 1;
    private static readonly Func<int, bool> AlwaysFailure = static _ => true;

    private CircuitBreaker[] _thresholdBreakers = null!;
    private CircuitBreaker[] _halfOpenTransitionBreakers = null!;
    private CircuitBreaker _halfOpenContentionBreaker = null!;

    [IterationSetup(Target = nameof(ThresholdTransition))]
    public void PrepareThresholdTransition()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 1
        };
        _thresholdBreakers = new CircuitBreaker[OperationCount];
        for (var index = 0; index < _thresholdBreakers.Length; index++)
        {
            _thresholdBreakers[index] = new CircuitBreaker(options);
        }
    }

    [IterationSetup(Target = nameof(HalfOpenTransition))]
    public void PrepareHalfOpenTransition()
    {
        var time = new ManualBenchmarkTimeProvider();
        var options = new CircuitBreakerOptions
        {
            BreakDuration = TimeSpan.FromSeconds(1)
        };
        _halfOpenTransitionBreakers = new CircuitBreaker[OperationCount];
        for (var index = 0; index < _halfOpenTransitionBreakers.Length; index++)
        {
            var breaker = new CircuitBreaker(options, time);
            breaker.Open();
            _halfOpenTransitionBreakers[index] = breaker;
        }

        time.Advance(TimeSpan.FromSeconds(1));
    }

    [IterationSetup(Target = nameof(HalfOpenContention))]
    public void PrepareHalfOpenContention()
    {
        var time = new ManualBenchmarkTimeProvider();
        _halfOpenContentionBreaker = new CircuitBreaker(
            new CircuitBreakerOptions { BreakDuration = TimeSpan.FromSeconds(1) },
            time);
        _halfOpenContentionBreaker.Open();
        time.Advance(TimeSpan.FromSeconds(1));
        if (!_halfOpenContentionBreaker.TryAcquire(out _))
        {
            throw new InvalidOperationException("Failed to reserve the half-open probe.");
        }
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    public int ThresholdTransition()
    {
        var sum = 0;
        foreach (var breaker in _thresholdBreakers)
        {
            sum += breaker.Execute(ReturnOne, AlwaysFailure);
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    public int HalfOpenTransition()
    {
        var admitted = 0;
        foreach (var breaker in _halfOpenTransitionBreakers)
        {
            admitted += breaker.TryAcquire(out _) ? 1 : 0;
        }

        return admitted;
    }

    [Benchmark(OperationsPerInvoke = OperationCount)]
    public int HalfOpenContention()
    {
        var rejected = 0;
        for (var index = 0; index < OperationCount; index++)
        {
            rejected += _halfOpenContentionBreaker.TryAcquire(out _, out _) ? 0 : 1;
        }

        return rejected;
    }
}
