using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;

namespace ZeroCircuitBreaker.Benchmarks;

[CircuitBenchmarkConfig]
[MemoryDiagnoser]
public class ExecutionBenchmarks
{
    private static readonly Func<int> ReturnValue = ReturnValueMethod;
    private static readonly Func<int, bool> AlwaysFailure = static _ => true;
    private static readonly Func<CancellationToken, Task<int>> CompletedTask =
        static _ => Task.FromResult(42);
    private static readonly Func<CancellationToken, ValueTask<int>> CompletedValueTask =
        static _ => ValueTask.FromResult(42);

    private readonly CircuitBreaker _closed = new();
    private readonly CircuitBreaker _failureCounting = new(new CircuitBreakerOptions
    {
        FailureThreshold = int.MaxValue
    });
    private int _state = 42;

    [Benchmark(Baseline = true)]
    public int DirectInvocation() => ReturnValueMethod();

    [Benchmark]
    public int ClosedSuccess() => _closed.Execute(ReturnValue);

    [Benchmark]
    public int ClosedHandledFailure() =>
        _failureCounting.Execute(ReturnValue, AlwaysFailure);

    [Benchmark]
    public Task<int> TaskBasedExecution() =>
        _closed.ExecuteTaskAsync(CompletedTask);

    [Benchmark]
    public ValueTask<int> ValueTaskBasedExecution() =>
        _closed.ExecuteAsync(CompletedValueTask);

    [Benchmark]
    public int CapturedLambda() => _closed.Execute(() => _state);

    [Benchmark]
    public int StaticStatePassingDelegate() =>
        _closed.Execute(_state, static (state, _) => state);

    [Benchmark]
    public CircuitBreakerSnapshot SnapshotCapture() => _closed.GetSnapshot();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReturnValueMethod() => 42;
}
