/*
namespace Geode.Client.Protocol;

/// <summary>
/// Per-cluster registry mapping member-id objects
/// (<c>ClientProxyMembershipID</c> / <c>DiskStoreId</c>) to compact
/// <see cref="ushort"/> ids used in version-stamp wire encoding.
/// Mirrors cppcache <c>MemberListForVersionStamp</c>
/// (<c>cppcache/src/MemberListForVersionStamp.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: empty shell.</b> Holds the field shape
/// cppcache uses (two parallel dicts + monotonic counter) and the
/// two public methods (<see cref="Add"/> / <see cref="GetDsMember"/>)
/// as NIE stubs. Real body lands when
/// <see cref="VersionTag.ReadMembers"/> reads
/// <c>ClientProxyMembershipID.ReadEssentialData</c> bytes and feeds
/// them through here (Phase 4+).
/// </para>
/// <para>
/// <b>Member type placeholder.</b> cppcache stores
/// <c>shared_ptr&lt;DSMemberForVersionStamp&gt;</c>; we use
/// <see cref="object"/>? until the type lands. Concrete subtypes
/// will be the C# equivalents of cppcache's
/// <c>ClientProxyMembershipID</c> and <c>DiskStoreId</c>.
/// </para>
/// </remarks>
// _memberCounter stays placeholder until Add body lands.
#pragma warning disable CS0649
internal sealed class MemberListForVersionStamp
{
    /// <summary>cppcache <c>m_members1</c>: numeric id → member.</summary>
    private readonly Dictionary<uint, object?> _members1 = new();

    /// <summary>cppcache <c>m_members2</c>: string-key → (member, id).</summary>
    private readonly Dictionary<string, DistributedMemberWithIntIdentifier> _members2 = new();

    /// <summary>cppcache <c>m_memberCounter</c>: monotonic id allocator.</summary>
    private uint _memberCounter;

    /// <summary>cppcache <c>mutex_</c> (boost::shared_mutex). We use a
    /// plain <see cref="object"/> lock until contention shows we need
    /// a reader-writer lock.</summary>
    private readonly object _lock = new();

    /// <summary>
    /// Register a member-id object, return the compact ushort id.
    /// Mirrors cppcache <c>add(member)</c>
    /// (<c>cppcache/src/MemberListForVersionStamp.cpp:36-50</c>).
    /// </summary>
    /// <remarks>
    /// <b>Phase 1.3 — hashKey dedup deferred.</b> cppcache looks up
    /// <c>member.getHashKey()</c> in <see cref="_members2"/> and
    /// returns the previously-allocated id if the same member was
    /// registered before. Phase 1.3 always allocates a fresh
    /// monotonic id — wasteful when the same member appears across
    /// chunks but unobservable in the single-chunk RemoveAll decode
    /// path. Phase 4+ adds the dedup once
    /// <c>ClientProxyMembershipID.HashKey</c> exists.
    /// </remarks>
    public ushort Add(object? member)
    {
        ArgumentNullException.ThrowIfNull(member);
        lock (_lock)
        {
            // TODO Phase 4+ — hashKey-based dedup; see remarks.
            _memberCounter++;
            var id = (ushort)_memberCounter;
            _members1[_memberCounter] = member;
            _ = _members2;  // reserved for hashKey dedup (Phase 4+)
            return id;
        }
    }

    /// <summary>
    /// Look up a member-id object by its compact ushort id. Mirrors
    /// cppcache <c>getDSMember(memberId)</c>
    /// (<c>cppcache/src/MemberListForVersionStamp.cpp:53-61</c>).
    /// </summary>
    public object? GetDsMember(ushort memberId)
    {
        lock (_lock)
        {
            return _members1.TryGetValue(memberId, out var member) ? member : null;
        }
    }
}

/// <summary>
/// Member + compact-id pair stored in
/// <see cref="MemberListForVersionStamp"/>'s string-keyed dict.
/// Mirrors cppcache <c>DistributedMemberWithIntIdentifier</c>.
/// </summary>
internal sealed record DistributedMemberWithIntIdentifier(
    object? Member,
    ushort Identifier);

*/