using Geode.Client.Protocol.Serialization;

namespace Geode.Client.Protocol;

/// <summary>
/// Wire-decoded representation of a Geode internal-distributed-member
/// identity. Used as the member-id payload inside a
/// <see cref="VersionTag"/>'s <c>HAS_MEMBER_ID</c> /
/// <c>HAS_PREVIOUS_MEMBER_ID</c> branches. Mirrors cppcache
/// <c>ClientProxyMembershipID</c>
/// (<c>cppcache/src/ClientProxyMembershipID.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the same as <see cref="ClientProxyMembershipIdBuilder"/>.</b>
/// The builder produces the bytes <i>we send</i> in the handshake;
/// this class decodes member-id bytes <i>the server sends</i> back
/// inside version tags. Same logical entity, opposite directions.
/// cppcache uses one class for both via <c>toData</c>/<c>fromData</c>;
/// our port split them because the encode path (handshake) was
/// already in place before the decode path (version stamps) was
/// needed.
/// </para>
/// <para>
/// <b>Phase 1.3.b status: empty shell.</b> Fields mirror cppcache
/// <c>m_*</c>; <see cref="ReadEssentialData"/> is NIE. Phase 4+
/// fills in the decoder body — needs to read host address bytes,
/// hostPort, dsName, uniqueTag, vmViewId per the Java
/// <c>InternalDistributedMember</c> wire format.
/// </para>
/// <para>
/// <b>DSMemberForVersionStamp base dropped.</b> cppcache derives
/// from <c>DSMemberForVersionStamp</c> (which is itself
/// <c>CacheableKey</c>); that lets <c>MemberListForVersionStamp</c>
/// store it polymorphically. Our <c>MemberListForVersionStamp</c>
/// stub takes <see cref="object"/>?, so the inheritance isn't
/// needed until Phase 4+.
/// </para>
/// </remarks>
// Phase 1.3.b: fields below are placeholder shells; decoder body
// lands Phase 4+.
#pragma warning disable CS0169
#pragma warning disable CS0414
#pragma warning disable CS0649
internal sealed class ClientProxyMembershipID(SerializationRegistry serializationRegistry)
{
    /// <summary>cppcache <c>kVmKindLoner = 13</c>.</summary>
    private const byte VM_KIND_LONER = 13;

    /// <summary>cppcache <c>kDcPort = 12334</c> — dummy port used in
    /// readEssentialData's initObjectVars call.</summary>
    private const int DC_PORT = 12334;

    private string _memIdStr = "";
    private string _clientId = "";
    private string _dsName = "";
    private uint _hostPort;
    private byte[] _hostAddr = [];
    private string _uniqueTag = "";
    private string _hashKey = "";
    private uint _vmViewId;

    /// <summary>
    /// Read the "essential" member-id payload (used inside a
    /// VersionTag's member-id slot — a leaner subset than full
    /// <c>fromData</c>). Mirrors cppcache
    /// <c>ClientProxyMembershipID::readEssentialData</c>
    /// (<c>cppcache/src/ClientProxyMembershipID.cpp:222-256</c>).
    /// </summary>
    /// <remarks>
    /// Wire format:
    /// <code>
    /// ArrayLen length            (i32 VL-encoded)
    /// length bytes  hostAddress  (raw IP bytes)
    /// i32           hostPort
    /// u8            flag         (ignored)
    /// u8            vmKind       (== VM_KIND_LONER(13) → loner branch)
    /// CacheableString uniqueTag  (loner) OR vmViewIdStr (non-loner; parse to int)
    /// CacheableString dsName
    /// </code>
    /// cppcache then calls <c>initObjectVars</c> with the parsed
    /// values + dummies; we inline the field assignments.
    /// </remarks>
    internal void ReadEssentialData(DataInput reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var length = reader.ReadArrayLen();
        _hostAddr = length > 0 ? reader.ReadBytesOnly(length).ToArray() : [];
        _hostPort = (uint)reader.ReadInt32();
        reader.AdvanceCursor(1);  // skip flag
        var vmKind = reader.ReadByte();

        string? uniqueTag = null;
        uint vmViewId = 0;
        if (vmKind == VM_KIND_LONER)
        {
            uniqueTag = serializationRegistry.ReadObject(reader) as string;
        }
        else
        {
            var vmViewIdStr = serializationRegistry.ReadObject(reader) as string;
            if (!uint.TryParse(vmViewIdStr, out vmViewId))
            {
                throw new GeodeException(
                    $"ClientProxyMembershipID.ReadEssentialData: " +
                    $"could not parse vmViewId '{vmViewIdStr}'.");
            }
        }

        var dsName = serializationRegistry.ReadObject(reader) as string ?? "";

        _dsName = dsName;
        _uniqueTag = uniqueTag ?? "";
        _vmViewId = vmViewId;
        _ = vmKind;          // stored implicitly via the branch above
        _ = DC_PORT;         // reserved for the full initObjectVars port (Phase 4+)
    }
}
