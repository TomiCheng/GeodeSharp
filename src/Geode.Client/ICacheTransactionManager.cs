namespace Geode.Client;

/// <summary>
/// Manages client-side Geode transactions for the current async flow.
/// </summary>
public interface ICacheTransactionManager
{
    /// <summary>Starts a new transaction on the current async flow.</summary>
    void Begin();

    /// <summary>Performs the prepare phase of a two-phase commit on the current transaction.</summary>
    Task PrepareAsync(CancellationToken ct = default);

    /// <summary>Commits the current transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Rolls back the current transaction.</summary>
    Task RollbackAsync(CancellationToken ct = default);

    /// <summary>Suspends the current transaction and returns its id; the async flow is left with no active transaction.</summary>
    ITransactionId Suspend();

    /// <summary>Resumes a previously-suspended transaction onto the current async flow.</summary>
    Task ResumeAsync(ITransactionId transactionId, CancellationToken ct = default);

    /// <summary>Attempts to resume a suspended transaction; returns <see langword="false"/> when none is suspended for <paramref name="transactionId"/>.</summary>
    Task<bool> TryResumeAsync(ITransactionId transactionId, CancellationToken ct = default);

    /// <summary>Attempts to resume a suspended transaction, waiting up to <paramref name="waitTime"/> for it to become suspended.</summary>
    Task<bool> TryResumeAsync(ITransactionId transactionId, TimeSpan waitTime, CancellationToken ct = default);

    /// <summary>Whether <paramref name="transactionId"/> is currently suspended locally.</summary>
    bool IsSuspended(ITransactionId transactionId);

    /// <summary>Whether <paramref name="transactionId"/> identifies a currently active (or suspended) transaction.</summary>
    bool Exists(ITransactionId transactionId);

    /// <summary>Whether the current async flow has an active transaction.</summary>
    bool Exists();

    /// <summary>The current async flow's transaction id, or <see langword="null"/> when none is active.</summary>
    ITransactionId? TransactionId { get; }
}
