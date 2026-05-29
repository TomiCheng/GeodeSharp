namespace Geode.Client.Internal;

/// <summary>
/// cppcache reference enum — mirrors <c>GfErrType</c>
/// (<c>cppcache/src/ErrType.hpp:27-116</c>) value-for-value as a
/// porting / grep aid. <b>Not threaded through the runtime.</b>
/// </summary>
/// <remarks>
/// <para>
/// cppcache uses <c>GfErrType</c> as the return type of every
/// <c>*NoThrow</c> CRUD entry; control flow inside
/// <c>updateNoThrow&lt;TAction&gt;</c> branches on the code, then the
/// top layer (<c>throwExceptionIfError</c>) converts a non-success
/// code back to a typed C++ exception (paired with a thread-local
/// message stash). The C# port skips that round-trip — exceptions
/// propagate directly via <see cref="Task"/> from the source site, so
/// the equivalent of <c>updateNoThrow</c> in C# just lets exceptions
/// fly through instead of marshalling them through err codes.
/// </para>
/// <para>
/// This enum stays for: (a) cppcache greps land here when reading
/// the C++ source alongside the port, (b) future translation tables
/// (e.g. server-sent error codes on the wire) can spell the
/// cppcache symbol they map to. Members are otherwise <b>never read
/// at runtime</b>.
/// </para>
/// <para>
/// Naming follows the existing internal-enum precedent
/// (<see cref="CacheEventFlags"/>): cppcache <c>GF_*</c>
/// <c>SCREAMING_SNAKE_CASE</c> → C# <c>PascalCase</c> with the
/// <c>GF_</c> prefix stripped. Each member's xmldoc carries the
/// original <c>GF_*</c> name so cppcache greps land here.
/// Numeric gaps (<c>122</c>, <c>125</c>) match cppcache — those slots
/// are deliberately empty in <c>ErrType.hpp</c> (one is the commented-out
/// <c>GF_CACHE_REDUNDANCY_FAILURE</c>).
/// </para>
/// </remarks>
internal enum GfErrType
{
    /// <summary>cppcache <c>GF_NOERR</c>: success — no error.</summary>
    NoErr = 0,

    /// <summary>cppcache <c>GF_DEADLK</c>: deadlock detected.</summary>
    Deadlk = 1,

    /// <summary>cppcache <c>GF_EACCES</c>: permission problem.</summary>
    EAccess = 2,

    /// <summary>cppcache <c>GF_ECONFL</c>: class creation conflict.</summary>
    EConfl = 3,

    /// <summary>cppcache <c>GF_EINVAL</c>: invalid argument.</summary>
    EInval = 4,

    /// <summary>cppcache <c>GF_ENOENT</c>: entity does not exist.</summary>
    ENoEnt = 5,

    /// <summary>cppcache <c>GF_ENOMEM</c>: insufficient memory.</summary>
    ENoMem = 6,

    /// <summary>cppcache <c>GF_ERANGE</c>: index out of range.</summary>
    ERange = 7,

    /// <summary>cppcache <c>GF_ETYPE</c>: type mismatch.</summary>
    EType = 8,

    /// <summary>cppcache <c>GF_NOTOBJ</c>: invalid object reference.</summary>
    NotObj = 9,

    /// <summary>cppcache <c>GF_NOTCON</c>: not connected to Geode.</summary>
    NotCon = 10,

    /// <summary>cppcache <c>GF_NOTOWN</c>: lock not owned by process / thread.</summary>
    NotOwn = 11,

    /// <summary>cppcache <c>GF_NOTSUP</c>: operation not supported.</summary>
    NotSup = 12,

    /// <summary>cppcache <c>GF_SCPGBL</c>: attempt to exit global scope.</summary>
    ScpGbl = 13,

    /// <summary>cppcache <c>GF_SCPEXC</c>: maximum scopes exceeded.</summary>
    ScpExc = 14,

    /// <summary>cppcache <c>GF_TIMEOUT</c>: operation timed out.</summary>
    Timeout = 15,

    /// <summary>cppcache <c>GF_OVRFLW</c>: arithmetic overflow.</summary>
    OvrFlw = 16,

    /// <summary>cppcache <c>GF_IOERR</c>: paging file I/O error.</summary>
    IOErr = 17,

    /// <summary>cppcache <c>GF_EINTR</c>: interrupted Geode call.</summary>
    EIntr = 18,

    /// <summary>cppcache <c>GF_MSG</c>: message could not be handled.</summary>
    Msg = 19,

    /// <summary>cppcache <c>GF_DISKFULL</c>: disk full.</summary>
    DiskFull = 20,

    /// <summary>cppcache <c>GF_NOSERVER_FOUND</c>: no server found.</summary>
    NoServerFound = 21,

    /// <summary>cppcache <c>GF_SERVER_FAILED</c>: server failure.</summary>
    ServerFailed = 22,

    /// <summary>cppcache <c>GF_CLIENT_WAIT_TIMEOUT</c>: client wait timed out.</summary>
    ClientWaitTimeout = 23,

    /// <summary>cppcache <c>GF_CLIENT_WAIT_TIMEOUT_REFRESH_PRMETADATA</c>: wait timeout, refresh partitioned-region metadata.</summary>
    ClientWaitTimeoutRefreshPrMetadata = 24,

    /// <summary>cppcache <c>GF_CACHE_REGION_NOT_FOUND</c>: no region with the specified name.</summary>
    CacheRegionNotFound = 101,

    /// <summary>cppcache <c>GF_CACHE_REGION_INVALID</c>: region is not valid.</summary>
    CacheRegionInvalid = 102,

    /// <summary>cppcache <c>GF_CACHE_REGION_KEYS_NOT_STRINGS</c>: entry keys are not strings.</summary>
    CacheRegionKeysNotStrings = 103,

    /// <summary>cppcache <c>GF_CACHE_REGION_ENTRY_NOT_BYTES</c>: entry value is not a byte array.</summary>
    CacheRegionEntryNotBytes = 104,

    /// <summary>cppcache <c>GF_CACHE_REGION_NOT_GLOBAL</c>: distributed locks not supported.</summary>
    CacheRegionNotGlobal = 105,

    /// <summary>cppcache <c>GF_CACHE_PROXY</c>: errors detected in CacheProxy processing.</summary>
    CacheProxy = 106,

    /// <summary>cppcache <c>GF_CACHE_ILLEGAL_ARGUMENT_EXCEPTION</c>: IllegalArgumentException in CacheProxy.</summary>
    CacheIllegalArgumentException = 107,

    /// <summary>cppcache <c>GF_CACHE_ILLEGAL_STATE_EXCEPTION</c>: IllegalStateException in CacheProxy.</summary>
    CacheIllegalStateException = 108,

    /// <summary>cppcache <c>GF_CACHE_TIMEOUT_EXCEPTION</c>: TimeoutException in CacheProxy.</summary>
    CacheTimeoutException = 109,

    /// <summary>cppcache <c>GF_CACHE_WRITER_EXCEPTION</c>: CacheWriterException in CacheProxy.</summary>
    CacheWriterException = 110,

    /// <summary>cppcache <c>GF_CACHE_REGION_EXISTS_EXCEPTION</c>: RegionExistsException in CacheProxy.</summary>
    CacheRegionExistsException = 111,

    /// <summary>cppcache <c>GF_CACHE_CLOSED_EXCEPTION</c>: CacheClosedException in CacheProxy.</summary>
    CacheClosedException = 112,

    /// <summary>cppcache <c>GF_CACHE_LEASE_EXPIRED_EXCEPTION</c>: LeaseExpiredException in CacheProxy.</summary>
    CacheLeaseExpiredException = 113,

    /// <summary>cppcache <c>GF_CACHE_LOADER_EXCEPTION</c>: CacheLoaderException in CacheProxy.</summary>
    CacheLoaderException = 114,

    /// <summary>cppcache <c>GF_CACHE_REGION_DESTROYED_EXCEPTION</c>: RegionDestroyedException in CacheProxy.</summary>
    CacheRegionDestroyedException = 115,

    /// <summary>cppcache <c>GF_CACHE_ENTRY_DESTROYED_EXCEPTION</c>: EntryDestroyedException in CacheProxy.</summary>
    CacheEntryDestroyedException = 116,

    /// <summary>cppcache <c>GF_CACHE_STATISTICS_DISABLED_EXCEPTION</c>: StatisticsDisabledException in CacheProxy.</summary>
    CacheStatisticsDisabledException = 117,

    /// <summary>cppcache <c>GF_CACHE_CONCURRENT_MODIFICATION_EXCEPTION</c>: ConcurrentModificationException in CacheProxy.</summary>
    CacheConcurrentModificationException = 118,

    /// <summary>cppcache <c>GF_CACHE_ENTRY_NOT_FOUND</c>: EntryNotFoundException in CacheProxy.</summary>
    CacheEntryNotFound = 119,

    /// <summary>cppcache <c>GF_CACHE_ENTRY_EXISTS</c>: EntryExistsException in CacheProxy.</summary>
    CacheEntryExists = 120,

    /// <summary>cppcache <c>GF_CACHEWRITER_ERROR</c>: exception while invoking a CacheWriter callback.</summary>
    CacheWriterError = 121,

    /// <summary>cppcache <c>GF_CANNOT_PROCESS_GII_REQUEST</c>: non-timeout failure during a get-initial-image batch request.</summary>
    CannotProcessGiiRequest = 123,

    /// <summary>cppcache <c>GF_CACHESERVER_EXCEPTION</c>: Java cache-server exception sent to the thin client.</summary>
    CacheServerException = 124,

    /// <summary>cppcache <c>GF_AUTHENTICATION_FAILED_EXCEPTION</c>: authentication failed.</summary>
    AuthenticationFailedException = 126,

    /// <summary>cppcache <c>GF_NOT_AUTHORIZED_EXCEPTION</c>: unauthorized operation attempted.</summary>
    NotAuthorizedException = 127,

    /// <summary>cppcache <c>GF_AUTHENTICATION_REQUIRED_EXCEPTION</c>: no authentication credentials provided.</summary>
    AuthenticationRequiredException = 128,

    /// <summary>cppcache <c>GF_DUPLICATE_DURABLE_CLIENT</c>: server rejected duplicate durable-client id.</summary>
    DuplicateDurableClient = 129,

    /// <summary>cppcache <c>GF_REMOTE_QUERY_EXCEPTION</c>: query exception on the Java cache server.</summary>
    RemoteQueryException = 130,

    /// <summary>cppcache <c>GF_CACHE_LISTENER_EXCEPTION</c>: exception inside a CacheListener.</summary>
    CacheListenerException = 131,

    /// <summary>cppcache <c>GF_ALL_CONNECTIONS_IN_USE_EXCEPTION</c>: all pool connections in use.</summary>
    AllConnectionsInUseException = 132,

    /// <summary>cppcache <c>GF_CACHE_ENTRY_UPDATED</c>: local entry was updated while a remote modification was in progress.</summary>
    CacheEntryUpdated = 133,

    /// <summary>cppcache <c>GF_CACHE_LOCATOR_EXCEPTION</c>: exception in Locator.</summary>
    CacheLocatorException = 134,

    /// <summary>cppcache <c>GF_INVALID_DELTA</c>: invalid delta payload.</summary>
    InvalidDelta = 135,

    /// <summary>cppcache <c>GF_INTERNAL_FUNCTION_INVOCATION_TARGET_EXCEPTION</c>: internal function-execution target exception.</summary>
    InternalFunctionInvocationTargetException = 136,

    /// <summary>cppcache <c>GF_ROLLBACK_EXCEPTION</c>: transaction rollback exception.</summary>
    RollbackException = 137,

    /// <summary>cppcache <c>GF_COMMIT_CONFLICT_EXCEPTION</c>: transaction commit conflict.</summary>
    CommitConflictException = 138,

    /// <summary>cppcache <c>GF_TRANSACTION_DATA_NODE_HAS_DEPARTED_EXCEPTION</c>: transaction data node departed.</summary>
    TransactionDataNodeHasDepartedException = 139,

    /// <summary>cppcache <c>GF_TRANSACTION_DATA_REBALANCED_EXCEPTION</c>: transaction data rebalanced.</summary>
    TransactionDataRebalancedException = 140,

    /// <summary>cppcache <c>GF_PUTALL_PARTIAL_RESULT_EXCEPTION</c>: PutAll partial result.</summary>
    PutAllPartialResultException = 141,

    /// <summary>cppcache <c>GF_LOW_MEMORY_EXCEPTION</c>: server low-memory.</summary>
    LowMemoryException = 142,

    /// <summary>cppcache <c>GF_QUERY_EXECUTION_LOW_MEMORY_EXCEPTION</c>: query execution aborted due to low memory.</summary>
    QueryExecutionLowMemoryException = 143,

    /// <summary>cppcache <c>GF_FUNCTION_EXCEPTION</c>: function-execution exception.</summary>
    FunctionException = 144,

    /// <summary>cppcache <c>GF_EUNDEF</c>: unknown / unclassified exception.</summary>
    EUndef = 999,
}
