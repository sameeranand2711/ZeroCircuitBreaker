namespace ZeroCircuitBreaker.Internal;

internal readonly struct OperationToken
{
    internal OperationToken(long packedState, FailureEpoch epoch, bool isProbe)
    {
        PackedState = packedState;
        Epoch = epoch;
        IsProbe = isProbe;
    }

    internal long PackedState { get; }
    internal FailureEpoch Epoch { get; }
    internal bool IsProbe { get; }
}
