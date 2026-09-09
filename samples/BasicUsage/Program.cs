using ZeroCircuitBreaker;

var breaker = new CircuitBreaker(new CircuitBreakerOptions
{
    FailureThreshold = 2,
    BreakDuration = TimeSpan.FromSeconds(10),
    OnStateChanged = static change =>
        Console.WriteLine($"Circuit: {change.PreviousState} -> {change.CurrentState} ({change.Reason})")
});

using var applicationStopping = new CancellationTokenSource();

// Reuse this breaker for every call to the same logical dependency. Results are
// still returned when the predicate classifies them as dependency failures.
for (var attempt = 1; attempt <= 2; attempt++)
{
    var statusCode = breaker.Execute(
        static () => 503,
        static status => status >= 500,
        applicationStopping.Token);

    Console.WriteLine($"Attempt {attempt} returned HTTP {statusCode}.");
}

try
{
    breaker.Execute(static () => Console.WriteLine("This work must not run while Open."));
}
catch (CircuitBreakerOpenException rejection)
{
    Console.WriteLine($"Rejected in {rejection.State}; retry after {rejection.RetryAfter}.");
}

// Manual reset is useful when the application knows the dependency has recovered.
breaker.Reset();
var recoveredStatus = breaker.Execute(static () => 200);
Console.WriteLine($"After reset, the dependency returned HTTP {recoveredStatus}.");
