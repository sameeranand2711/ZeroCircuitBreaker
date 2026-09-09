namespace ZeroCircuitBreaker.Benchmarks;

internal sealed class ManualBenchmarkTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Volatile.Read(ref _timestamp);

    internal void Advance(TimeSpan elapsed) =>
        Interlocked.Add(ref _timestamp, elapsed.Ticks);
}
