using Geode.Client.Protocol;
using Geode.Client.Services;

namespace Geode.Client.Internal;

/// <summary>
/// Server's reply payload to a commit / 2PC AFTER_COMMIT message — a
/// bundle of per-region changes (<c>RegionCommit</c>) the client must
/// apply to local cache state once the server signals success. Mirrors
/// cppcache <c>TXCommitMessage</c>
/// (<c>cppcache/src/TXCommitMessage.hpp/.cpp</c>); fixed-id serializable
/// (<c>DSFid::TXCommitMessage</c>).
/// </summary>
/// <remarks>
/// cppcache <c>toData</c> is empty — client never sends this, only
/// receives. So C# port also skips a serialize-out path; only
/// <see cref="FromData"/> + <see cref="Apply"/> matter.
/// </remarks>
internal sealed class TXCommitMessage(MemberListForVersionStamp memberListForVersionStamp)
{
    private readonly MemberListForVersionStamp _memberListForVersionStamp = memberListForVersionStamp;

    public void FromData(DataInput input)
    {
        // TODO: cppcache TXCommitMessage::fromData (TXCommitMessage.cpp:37-104)
        //   — skips pId / txIdent / memId / optional lockId / totalMaxSize /
        //     farsideBaseMembershipId / tid / seqId / large-mod-count /
        //     shadow-keys flag, then reads regionSize × RegionCommit, then
        //     optional trailing ClientProxyMembershipID via FixedID byte,
        //     then trailing object array (ignored).
        throw new NotImplementedException();
    }

    public void Apply(GeodeCache cache)
    {
        throw new NotImplementedException();
        // cppcache TXCommitMessage::apply (TXCommitMessage.cpp:113-117):
        //
        //   void TXCommitMessage::apply(Cache* cache) {
        //     for (const auto& region : regions_) {
        //       region->apply(cache);
        //     }
        //   }
        //
        // Delegate target lives on a different C# type — RegionCommit.Apply(cache)
        // (cppcache RegionCommit.cpp, not yet ported). Run /cpp-stub on it
        // separately when the RegionCommit class lands.
    }
}
