using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol;

/// <summary>
/// Variant of <see cref="VersionTag"/> for persistent regions —
/// reads member ids as <c>DiskStoreId</c> rather than
/// <c>InternalDistributedMember</c>. Mirrors cppcache
/// <c>DiskVersionTag</c>
/// (<c>cppcache/src/DiskVersionTag.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: empty shell.</b> Only difference from
/// <see cref="VersionTag"/> is the <see cref="ReadMembers"/> override
/// (uses <c>DiskStoreId.FromData</c> instead of
/// <c>InternalDistributedMember.FromData</c>) and the
/// <c>DSFid</c> value (<see cref="DSFid.DiskVersionTag"/> = 2131
/// vs <see cref="DSFid.VersionTag"/> = -120).
/// </para>
/// <para>
/// Phase 1.3 RemoveAll doesn't target persistent regions, so this
/// class is unreachable today &#x2014;
/// <see cref="VersionedCacheableObjectPartList.FromData"/> step 6
/// always constructs a plain <see cref="VersionTag"/>. The
/// dispatch (and a <c>DiskStoreId</c> wire decoder) lands when
/// persistent regions become a target.
/// </para>
/// <para>
/// <b>Logger category mismatch tolerated.</b> Ctor takes
/// <see cref="ILogger{T}"/> typed against <see cref="VersionTag"/>
/// to satisfy the base ctor signature; logs from a
/// <c>DiskVersionTag</c> would appear under the
/// <c>VersionTag</c> category. Acceptable for an internal class.
/// </para>
/// </remarks>
internal sealed class DiskVersionTag(
    IServiceProvider serviceProvider,
    ILogger<VersionTag> logger,
    MemberListForVersionStamp? memberListForVersionStamp = null)
    : VersionTag(serviceProvider, logger, memberListForVersionStamp)
{
    /// <inheritdoc cref="VersionTag.ReadMembers" />
    /// <remarks>
    /// cppcache reads <c>DiskStoreId</c> instances and routes them
    /// through the same member-list registry as
    /// <see cref="VersionTag.ReadMembers"/>; the result is the same
    /// <c>m_internalMemId</c> / <c>m_previousMemId</c> ushort slots
    /// pointing into the registry.
    /// </remarks>
    protected override void ReadMembers(ushort flags, DataInput reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _ = flags;
        throw new NotImplementedException(
            "DiskVersionTag.ReadMembers pending Phase 4+ (persistent regions).");
    }
}

