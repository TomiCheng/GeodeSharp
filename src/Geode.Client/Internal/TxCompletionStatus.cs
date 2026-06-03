namespace Geode.Client.Internal;

/// <summary>
/// Transaction completion status passed to the server's
/// <c>TcrMessageTxSynchronization</c> AFTER_COMMIT message body.
/// Mirrors cppcache <c>enum status</c>
/// (<c>cppcache/src/CacheTransactionManagerImpl.hpp:35</c>).
/// </summary>
/// <remarks>
/// Naming: cppcache <c>SCREAMING_SNAKE_CASE</c> → C# <c>PascalCase</c>;
/// per-member xmldoc carries the cppcache <c>STATUS_*</c> origin for
/// grep parity. Integer values are wire-protocol-significant — the
/// server reads them from the AFTER_COMMIT body to decide commit vs
/// rollback path; do NOT renumber.
/// </remarks>
internal enum TxCompletionStatus
{
    /// <summary>cppcache <c>STATUS_COMMITTED = 3</c>: signal server to apply the tx.</summary>
    Committed = 3,

    /// <summary>cppcache <c>STATUS_ROLLEDBACK = 4</c>: signal server to discard the tx.</summary>
    RolledBack = 4,
}
