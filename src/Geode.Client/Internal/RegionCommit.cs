using Geode.Client.Protocol;
using Geode.Client.Services;

namespace Geode.Client.Internal;

/// <summary>
/// Per-region payload inside a <see cref="TXCommitMessage"/> — the set
/// of entry-level changes to apply to one client-side region after a
/// successful server commit. Mirrors cppcache <c>RegionCommit</c>
/// (<c>cppcache/src/RegionCommit.hpp/.cpp</c>).
/// </summary>
internal sealed class RegionCommit(MemberListForVersionStamp memberListForVersionStamp)
{
    private readonly MemberListForVersionStamp _memberListForVersionStamp = memberListForVersionStamp;
    private string? _regionPath;
    private string? _parentRegionPath;
    // TODO: List<FarSideEntryOp> _farSideEntryOps — FarSideEntryOp class
    //   not yet ported. Until it lands, FromData skips the entry-op batch
    //   and Apply has no ops to iterate.

    public void FromData(DataInput input)
    {
        // TODO: cppcache RegionCommit::fromData (RegionCommit.cpp:30-48)
        //   — readObject regionPath / parentRegionPath / int32 size; if size > 0:
        //   readBoolean largeModCount, readObject dsMember (DSMemberForVersionStamp,
        //   added to _memberListForVersionStamp), then size × FarSideEntryOp.fromData.
        throw new NotImplementedException();
    }

    public void Apply(GeodeCache cache)
    {
        // TODO: cppcache RegionCommit::apply (RegionCommit.cpp:50-63)
        //   foreach entryOp in _farSideEntryOps:
        //     region = cache.GetRegion(_regionPath) ?? cache.GetRegion(_parentRegionPath)
        //     if (region != null) entryOp.Apply(region);
        throw new NotImplementedException();
    }
}
