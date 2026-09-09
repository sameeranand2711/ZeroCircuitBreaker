using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ZeroCircuitBreaker.Benchmarks;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class CircuitBenchmarkConfigAttribute : Attribute, IConfigSource
{
    public CircuitBenchmarkConfigAttribute()
    {
        Config = ManualConfig
            .CreateEmpty()
            .AddJob(Job.Default
                .WithId("Agent07")
                .WithLaunchCount(1)
                .WithWarmupCount(3)
                .WithIterationCount(8));
    }

    public IConfig Config { get; }
}

[AttributeUsage(AttributeTargets.Class)]
internal sealed class TransitionBenchmarkConfigAttribute : Attribute, IConfigSource
{
    public TransitionBenchmarkConfigAttribute()
    {
        Config = ManualConfig
            .CreateEmpty()
            .AddJob(Job.Default
                .WithId(nameof(TransitionBenchmarkConfigAttribute))
                .WithLaunchCount(1)
                .WithWarmupCount(5)
                .WithIterationCount(15)
                .WithInvocationCount(1)
                .WithUnrollFactor(1));
    }

    public IConfig Config { get; }
}
