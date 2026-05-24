namespace Geode.Client.Protocol;

/// <summary>
/// Fixed serialisation ids for built-in wire types — the
/// <c>compId</c> that follows <see cref="DSCode.FixedIDByte"/> /
/// <see cref="DSCode.FixedIDShort"/> in a serialised stream.
/// Mirrors cppcache <c>enum class DSFid : int32_t</c>
/// (<c>cppcache/include/geode/internal/DSFixedId.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// Negative values are intentional &#x2014; the wire protocol uses
/// signed-int regions to separate system-internal classes
/// (negative) from user-visible / GetAll-style classes (positive).
/// Keep <see cref="int"/> backing storage so the round-trip with
/// cppcache <c>int32_t</c> stays bit-exact.
/// </para>
/// <para>
/// Each entry's wire role is documented inline. Not every entry
/// has a C# decoder yet &#x2014; Phase 1.3 only consumes
/// <see cref="VersionedObjectPartList"/> (via the chunked-reply
/// path of <c>RemoveAll</c> / <c>PutAll</c> / <c>GetAll70</c>);
/// the rest are listed for cppcache parity and to avoid an
/// "incremental enum" pattern where every new decoder grows the
/// type.
/// </para>
/// </remarks>
internal enum DSFid : int
{
    GatewaySenderEventCallbackArgument = -135,
    ClientHealthStats = -126,
    VersionTag = -120,
    CollectionTypeImpl = -59,
    LocatorListRequest = -54,
    ClientConnectionRequest = -53,
    QueueConnectionRequest = -52,
    LocatorListResponse = -51,
    ClientConnectionResponse = -50,
    QueueConnectionResponse = -49,
    ClientReplacementRequest = -48,
    GetAllServersRequest = -43,
    GetAllServersResponse = -42,
    VersionedObjectPartList = 7,
    EnumInfo = 9,
    CacheableObjectPartList = 25,
    CacheableUndefined = 31,
    Struct = 32,
    EventId = 36,
    InterestResultPolicy = 37,
    ClientProxyMembershipId = 38,
    InternalDistributedMember = 92,
    TXCommitMessage = 110,
    DiskVersionTag = 2131,
    DiskStoreId = 2133,
}
