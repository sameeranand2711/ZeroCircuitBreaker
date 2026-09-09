using System.Reflection;
using Breaker = global::ZeroCircuitBreaker.CircuitBreaker;

namespace ZeroCircuitBreaker.ConcurrencyTests;

public sealed class TransitionDescriptorProgressTests
{
    [Fact]
    public void Helper_removes_a_descriptor_whose_source_became_stale_before_reservation()
    {
        var breaker = new Breaker();

        // This reproduces a reachable scheduler interleaving without timing luck:
        // A reads generation 0, B completes Reset() to generation 1, A installs its
        // now-stale descriptor and is suspended before attempting the packed-state CAS.
        breaker.Reset();

        var breakerType = typeof(Breaker);
        var assembly = breakerType.Assembly;
        var epochType = assembly.GetType("ZeroCircuitBreaker.Internal.FailureEpoch", throwOnError: true)!;
        var descriptorType = assembly.GetType("ZeroCircuitBreaker.Internal.TransitionDescriptor", throwOnError: true)!;
        var targetEpoch = Activator.CreateInstance(epochType, nonPublic: true)!;
        var descriptor = Activator.CreateInstance(
            descriptorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                0L, // generation 0, Closed
                7L, // generation 1, Transitioning
                5L, // generation 1, Open
                CircuitState.Closed,
                CircuitState.Open,
                CircuitTransitionReason.ManuallyOpened,
                targetEpoch,
                0L
            ],
            culture: null)!;

        var transitionField = breakerType.GetField(
            "_transition",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        transitionField.SetValue(breaker, descriptor);

        var helpTransition = breakerType.GetMethod(
            "HelpTransition",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        helpTransition.Invoke(breaker, [descriptor]);

        // A public reader cannot make progress while this slot remains occupied.
        // Helpability requires any observer to remove the unused stale descriptor.
        Assert.Null(transitionField.GetValue(breaker));
    }
}
