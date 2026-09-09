namespace ZeroCircuitBreaker;

public sealed partial class CircuitBreaker
{
    private void ExecuteCore<TState>(
        TState state,
        Action<TState, CancellationToken> operation,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        try
        {
            operation(state, cancellationToken);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        CompleteSuccess(token);
    }

    private TResult ExecuteCore<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, TResult> operation,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        TResult result;
        try
        {
            result = operation(state, cancellationToken);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        CompleteSuccess(token);
        return result;
    }

    private TResult ExecuteCore<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, TResult> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        TResult result;
        try
        {
            result = operation(state, cancellationToken);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        if (isFailureResult(result))
        {
            CompleteFailure(token);
        }
        else
        {
            CompleteSuccess(token);
        }

        return result;
    }

    private async ValueTask ExecuteAsyncCore<TState>(
        TState state,
        Func<TState, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        try
        {
            await operation(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        CompleteSuccess(token);
    }

    private async ValueTask<TResult> ExecuteAsyncCore<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        TResult result;
        try
        {
            result = await operation(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        CompleteSuccess(token);
        return result;
    }

    private async ValueTask<TResult> ExecuteAsyncCore<TState, TResult>(
        TState state,
        Func<TState, CancellationToken, ValueTask<TResult>> operation,
        Func<TResult, bool> isFailureResult,
        CancellationToken cancellationToken)
    {
        var token = AcquireOrThrow();
        TResult result;
        try
        {
            result = await operation(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            CompleteException(token, exception, cancellationToken, useCallToken: true);
            throw;
        }

        if (isFailureResult(result))
        {
            CompleteFailure(token);
        }
        else
        {
            CompleteSuccess(token);
        }

        return result;
    }
}
