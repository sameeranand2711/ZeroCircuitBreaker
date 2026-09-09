namespace ZeroCircuitBreaker.Internal;

internal sealed class FailureEpoch
{
    private const int SealedMask = int.MinValue;
    private const int CountMask = int.MaxValue;

    internal int State;

    internal int Count => GetCount(Volatile.Read(ref State));
    internal bool IsSealed => IsSealedState(Volatile.Read(ref State));

    internal static int GetCount(int state) => state & CountMask;
    internal static bool IsSealedState(int state) => (state & SealedMask) != 0;
    internal static int CreateSealedState(int count) => count | SealedMask;
}
