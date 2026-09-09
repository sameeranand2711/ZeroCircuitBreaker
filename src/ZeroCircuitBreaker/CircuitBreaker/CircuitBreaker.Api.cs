namespace ZeroCircuitBreaker;

using ZeroCircuitBreaker.Internal;

/// <summary>Provides the ZeroCircuitBreaker V1 circuit-breaking API.</summary>
/// <remarks>
/// Instances are thread-safe and intended to be long-lived per logical dependency. Admission and transition ordering are
/// atomic, but no fairness or protected-work execution order is guaranteed. Execution APIs reject Open and HalfOpen calls
/// with <see cref='CircuitBreakerOpenException'/> without invoking protected work. Protected-operation exceptions propagate
/// unchanged after circuit accounting; caller-requested cancellation, identified by the supplied call token, does not count
/// as a dependency failure.
/// </remarks>
public sealed partial class CircuitBreaker
{
    /// <summary>Initializes a breaker with default options and <see cref='TimeProvider.System'/>.</summary>
    public CircuitBreaker()
        : this(new CircuitBreakerOptions(), TimeProvider.System)
    {
    }
    /// <summary>Initializes a breaker with supplied options and <see cref='TimeProvider.System'/>.</summary>
    /// <param name='options'>The configuration to validate and snapshot.</param>
    /// <exception cref='ArgumentNullException'><paramref name='options'/> is <see langword='null'/>.</exception>
    /// <exception cref='ArgumentOutOfRangeException'><see cref='CircuitBreakerOptions.FailureThreshold'/> or <see cref='CircuitBreakerOptions.BreakDuration'/> is not positive.</exception>
    public CircuitBreaker(CircuitBreakerOptions options)
        : this(options, TimeProvider.System)
    {
    }
    /// <summary>Initializes a breaker with supplied options and time.</summary>
    /// <param name='options'>The configuration to validate and snapshot.</param>
    /// <param name='timeProvider'>The provider used for on-demand break-duration calculations.</param>
    /// <exception cref='ArgumentNullException'><paramref name='options'/> or <paramref name='timeProvider'/> is <see langword='null'/>.</exception>
    /// <exception cref='ArgumentOutOfRangeException'><see cref='CircuitBreakerOptions.FailureThreshold'/> or <see cref='CircuitBreakerOptions.BreakDuration'/> is not positive.</exception>
    public CircuitBreaker(CircuitBreakerOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (options.FailureThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (options.BreakDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _failureThreshold = options.FailureThreshold;
        _breakDuration = options.BreakDuration;
        _isFailureException = options.IsFailureException;
        _onStateChanged = options.OnStateChanged;
        _timeProvider = timeProvider;
        _failureEpoch = new FailureEpoch();
    }

    /// <summary>Executes a synchronous operation when the circuit admits it.</summary>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The call token used to identify caller-requested cancellation.</param>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public void Execute(Action operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ExecuteCore(operation, static (value, _) => value(), cancellationToken);
    }
    /// <summary>Executes a synchronous result operation when the circuit admits it.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The call token used to identify caller-requested cancellation.</param>
    /// <returns>The protected operation's result.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public TResult Execute<TResult>(Func<TResult> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCore(operation, static (value, _) => value(), cancellationToken);
    }
    /// <summary>Executes a synchronous result operation and classifies its returned result.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='isFailureResult'>A predicate invoked once; <see langword='true'/> records failure while still returning the result.</param>
    /// <param name='cancellationToken'>The call token used to identify caller-requested cancellation.</param>
    /// <returns>The protected operation's result, including a result classified as failure.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> or <paramref name='isFailureResult'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public TResult Execute<TResult>(Func<TResult> operation, Func<TResult, bool> isFailureResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isFailureResult);
        return ExecuteCore(
            operation,
            static (value, _) => value(),
            isFailureResult,
            cancellationToken);
    }
    /// <summary>Executes a state-passing synchronous operation when the circuit admits it.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public void Execute<TState>(TState state, Action<TState, CancellationToken> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ExecuteCore(state, operation, cancellationToken);
    }
    /// <summary>Executes a state-passing synchronous result operation when the circuit admits it.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>The protected operation's result.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public TResult Execute<TState, TResult>(TState state, Func<TState, CancellationToken, TResult> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteCore(state, operation, cancellationToken);
    }
    /// <summary>Executes a state-passing synchronous result operation and classifies its returned result.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='isFailureResult'>A predicate invoked once; <see langword='true'/> records failure while still returning the result.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>The protected operation's result, including a result classified as failure.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> or <paramref name='isFailureResult'/> is <see langword='null'/>.</exception>
    /// <exception cref='CircuitBreakerOpenException'>The circuit rejects the operation in Open or HalfOpen.</exception>
    public TResult Execute<TState, TResult>(TState state, Func<TState, CancellationToken, TResult> operation, Func<TResult, bool> isFailureResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isFailureResult);
        return ExecuteCore(state, operation, isFailureResult, cancellationToken);
    }

    /// <summary>Executes a Task-based operation and returns a task that completes after circuit accounting.</summary>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A task representing protected work and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public Task ExecuteTaskAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(
            operation,
            static (value, token) => new ValueTask(value(token)),
            cancellationToken).AsTask();
    }
    /// <summary>Executes a Task-based result operation and returns a task that completes after circuit accounting.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A task whose result is the protected operation's result after circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public Task<TResult> ExecuteTaskAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(
            operation,
            static (value, token) => new ValueTask<TResult>(value(token)),
            cancellationToken).AsTask();
    }
    /// <summary>Executes a Task-based result operation and classifies its returned result.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='isFailureResult'>A predicate invoked once; <see langword='true'/> records failure while still returning the result.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A task whose result is returned after classification and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> or <paramref name='isFailureResult'/> is <see langword='null'/>.</exception>
    public Task<TResult> ExecuteTaskAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, Func<TResult, bool> isFailureResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isFailureResult);
        return ExecuteAsync(
            operation,
            static (value, token) => new ValueTask<TResult>(value(token)),
            isFailureResult,
            cancellationToken).AsTask();
    }

    /// <summary>Executes a ValueTask-native operation when the circuit admits it.</summary>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task representing protected work and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(
            operation,
            static (value, token) => value(token),
            cancellationToken);
    }
    /// <summary>Executes a ValueTask-native result operation when the circuit admits it.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task whose result is the protected operation's result after circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync(
            operation,
            static (value, token) => value(token),
            cancellationToken);
    }
    /// <summary>Executes a ValueTask-native result operation and classifies its returned result.</summary>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='isFailureResult'>A predicate invoked once; <see langword='true'/> records failure while still returning the result.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task whose result is returned after classification and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> or <paramref name='isFailureResult'/> is <see langword='null'/>.</exception>
    public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> operation, Func<TResult, bool> isFailureResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isFailureResult);
        return ExecuteAsync(
            operation,
            static (value, token) => value(token),
            isFailureResult,
            cancellationToken);
    }
    /// <summary>Executes a state-passing ValueTask-native operation when the circuit admits it.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task representing protected work and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public ValueTask ExecuteAsync<TState>(TState state, Func<TState, CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsyncCore(state, operation, cancellationToken);
    }
    /// <summary>Executes a state-passing ValueTask-native result operation when the circuit admits it.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task whose result is the protected operation's result after circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> is <see langword='null'/>.</exception>
    public ValueTask<TResult> ExecuteAsync<TState, TResult>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsyncCore(state, operation, cancellationToken);
    }
    /// <summary>Executes a state-passing ValueTask-native result operation and classifies its returned result.</summary>
    /// <typeparam name='TState'>The caller state type.</typeparam>
    /// <typeparam name='TResult'>The result type.</typeparam>
    /// <param name='state'>State passed directly to <paramref name='operation'/>.</param>
    /// <param name='operation'>The protected operation.</param>
    /// <param name='isFailureResult'>A predicate invoked once; <see langword='true'/> records failure while still returning the result.</param>
    /// <param name='cancellationToken'>The token forwarded to the operation and used to identify caller-requested cancellation.</param>
    /// <returns>A value task whose result is returned after classification and circuit accounting.</returns>
    /// <exception cref='ArgumentNullException'><paramref name='operation'/> or <paramref name='isFailureResult'/> is <see langword='null'/>.</exception>
    public ValueTask<TResult> ExecuteAsync<TState, TResult>(TState state, Func<TState, CancellationToken, ValueTask<TResult>> operation, Func<TResult, bool> isFailureResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isFailureResult);
        return ExecuteAsyncCore(state, operation, isFailureResult, cancellationToken);
    }

    /// <summary>Attempts to acquire a lease without throwing on circuit rejection.</summary>
    /// <param name='lease'>Receives an acquired lease on success, or the default value on rejection.</param>
    /// <returns><see langword='true'/> when admitted; otherwise <see langword='false'/>.</returns>
    public bool TryAcquire(out CircuitLease lease) => TryAcquire(out lease, out _);
    /// <summary>Attempts to acquire a lease and returns rejection diagnostics without throwing on circuit rejection.</summary>
    /// <param name='lease'>Receives an acquired lease on success, or the default value on rejection.</param>
    /// <param name='rejection'>Receives the default value on success, or Open/HalfOpen rejection information on failure.</param>
    /// <returns><see langword='true'/> when admitted; otherwise <see langword='false'/>.</returns>
    public bool TryAcquire(out CircuitLease lease, out CircuitRejection rejection)
    {
        if (TryAcquireToken(out var token, out rejection))
        {
            lease = new CircuitLease(new LeaseCompletion(this, token));
            return true;
        }

        lease = default;
        return false;
    }
    /// <summary>Creates a new manually opened generation, resets failures, and restarts the break duration.</summary>
    /// <remarks>Calling this while already Open still creates a new logical boundary and invokes the configured callback.</remarks>
    public void Open() =>
        TransitionManually(CircuitState.Open, CircuitTransitionReason.ManuallyOpened);
    /// <summary>Creates a new manually reset Closed generation and resets failures.</summary>
    /// <remarks>Calling this while already Closed still creates a new logical boundary and invokes the configured callback.</remarks>
    public void Reset() =>
        TransitionManually(CircuitState.Closed, CircuitTransitionReason.ManuallyReset);
    /// <summary>Gets a stable diagnostic snapshot without performing admission or changing state.</summary>
    /// <returns>The observed public state, consecutive failures, and Open retry-after information.</returns>
    public CircuitBreakerSnapshot GetSnapshot() => ReadSnapshot();
}
