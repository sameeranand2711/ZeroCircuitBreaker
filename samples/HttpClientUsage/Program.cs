using System.Net;
using ZeroCircuitBreaker;

using var httpClient = new HttpClient(new SequenceHandler(
    HttpStatusCode.ServiceUnavailable,
    HttpStatusCode.ServiceUnavailable,
    HttpStatusCode.OK))
{
    BaseAddress = new Uri("https://catalog.example/")
};

// CatalogClient and its breaker are long-lived for this logical dependency.
var catalog = new CatalogClient(httpClient);

using (var canceledCall = new CancellationTokenSource())
{
    canceledCall.Cancel();
    try
    {
        await catalog.GetStatusAsync(canceledCall.Token);
    }
    catch (OperationCanceledException) when (canceledCall.IsCancellationRequested)
    {
        Console.WriteLine("Caller cancellation propagated and did not count as a dependency failure.");
    }
}

for (var attempt = 1; attempt <= 2; attempt++)
{
    var status = await catalog.GetStatusAsync(CancellationToken.None);
    Console.WriteLine($"Catalog attempt {attempt}: {(int)status} {status}.");
}

try
{
    await catalog.GetStatusAsync(CancellationToken.None);
}
catch (CircuitBreakerOpenException rejection)
{
    Console.WriteLine($"Catalog request rejected in {rejection.State}; retry after {rejection.RetryAfter}.");
}

catalog.ResetCircuit();
var recovered = await catalog.GetStatusAsync(CancellationToken.None);
Console.WriteLine($"Catalog after manual recovery: {(int)recovered} {recovered}.");

internal sealed class CatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly CircuitBreaker _breaker;

    internal CatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _breaker = new CircuitBreaker(new CircuitBreakerOptions
        {
            FailureThreshold = 2,
            BreakDuration = TimeSpan.FromSeconds(30),
            IsFailureException = static exception => exception is HttpRequestException
        });
    }

    internal async Task<HttpStatusCode> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await _breaker.ExecuteTaskAsync(
            token => _httpClient.GetAsync("products", token),
            static result => (int)result.StatusCode >= 500,
            cancellationToken);

        return response.StatusCode;
    }

    internal void ResetCircuit() => _breaker.Reset();
}

// An in-process handler keeps the sample runnable without external network access.
internal sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
{
    private int _nextStatus = -1;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var index = Math.Min(Interlocked.Increment(ref _nextStatus), statuses.Length - 1);
        return Task.FromResult(new HttpResponseMessage(statuses[index])
        {
            RequestMessage = request
        });
    }
}
