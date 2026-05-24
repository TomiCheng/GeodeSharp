using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol;

/// <summary>
/// Per-entry server-side version stamp shipped back with bulk-op
/// replies (PutAll / RemoveAll / GetAll) and used by client-side
/// caching to resolve concurrent modifications. Mirrors cppcache
/// <c>VersionTag</c>
/// (<c>cppcache/src/VersionTag.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: members only, no decoder.</b>
/// <see cref="FromData"/> + <see cref="ReplaceNullMemberId"/> throw
/// <see cref="NotImplementedException"/>; bodies land alongside
/// <see cref="VersionedCacheableObjectPartList.FromData"/> step 6
/// (Phase 1.3.c GetAll or Phase 4+ client-side caching).
/// </para>
/// <para>
/// cppcache derives from <c>DataSerializableFixedId</c> with
/// <c>DSFid.VersionTag</c>; we don't have the interface yet
/// (Phase 4 ToData/FromData contract), so the wire methods sit
/// directly on the class.
/// </para>
/// <para>
/// <b>DiskVersionTag dropped.</b> cppcache has a
/// <c>DiskVersionTag</c> subclass for persistent regions
/// (<c>persistent</c> flag in
/// <see cref="VersionedCacheableObjectPartList.FromData"/>). Phase
/// 1.3 RemoveAll doesn't target persistent regions; revisit when
/// the persistent-region scenario lands.
/// </para>
/// </remarks>
// Phase 1.3.b: fields below are placeholder shells until FromData
// lands. They mirror cppcache m_* members 1:1.
#pragma warning disable CS0169 // field never used
#pragma warning disable CS0649 // field never assigned
internal class VersionTag(
    IServiceProvider serviceProvider,
    ILogger<VersionTag> logger,
    MemberListForVersionStamp? memberListForVersionStamp = null)
{
    // ── Wire flag bits (used by FromData) ──────────────────────
    // cppcache static const uint8_t HAS_MEMBER_ID = 0x01; etc.
    protected const byte HAS_MEMBER_ID = 0x01;
    protected const byte HAS_PREVIOUS_MEMBER_ID = 0x02;
    protected const byte VERSION_TWO_BYTES = 0x04;
    protected const byte DUPLICATE_MEMBER_IDS = 0x08;
    protected const byte HAS_RVV_HIGH_BYTE = 0x10;

    // ── Bits-field interpretation (m_bits) ─────────────────────
    protected const byte BITS_POSDUP = 0x01;
    protected const byte BITS_RECORDED = 0x02;
    protected const byte BITS_HAS_PREVIOUS_ID = 0x03;

    // ── Wire fields (mirror cppcache m_*) ──────────────────────
    private ushort _bits;
    private int _entryVersion;
    private short _regionVersionHighBytes;
    private int _regionVersionLowBytes;
    private ushort _internalMemId;
    private ushort _previousMemId;
    private long _timeStamp;

    /// <summary>
    /// cppcache <c>m_memberListForVersionStamp</c>
    /// (<c>MemberListForVersionStamp&amp;</c>). Typed as
    /// <see cref="object"/>? until that class lands — Phase 4+ when
    /// the cluster's member-id resolution table is wired through.
    /// </summary>
    protected readonly MemberListForVersionStamp? MemberListForVersionStamp = memberListForVersionStamp;

    // ── Getters / setters needed by VersionedCacheableObjectPartList.FromData ──
    // cppcache exposes most as inline accessors; we mirror.

    public int EntryVersion => _entryVersion;
    public short RegionVersionHighBytes => _regionVersionHighBytes;
    public int RegionVersionLowBytes => _regionVersionLowBytes;
    public ushort PreviousMemId => _previousMemId;

    /// <summary>
    /// Internal member-id slot, settable from
    /// <see cref="VersionedCacheableObjectPartList.FromData"/>'s
    /// <c>FLAG_TAG_WITH_NUMBER_ID</c> branch (looks up the id from
    /// the ids vector built during this chunk).
    /// </summary>
    public ushort InternalMemId
    {
        get => _internalMemId;
        set => _internalMemId = value;
    }

    /// <summary>
    /// Decode the wire bytes. Mirrors cppcache
    /// <c>VersionTag::fromData</c>
    /// (<c>cppcache/src/VersionTag.cpp</c>).
    /// </summary>
    /// <remarks>
    /// Body lands with the chunked-reply consumer
    /// (<see cref="VersionedCacheableObjectPartList.FromData"/>
    /// step 6) in Phase 1.3.c / Phase 4+. Reads <c>m_bits</c>, then
    /// dispatches on the 5 flag bits (HAS_MEMBER_ID /
    /// HAS_PREVIOUS_MEMBER_ID / VERSION_TWO_BYTES /
    /// DUPLICATE_MEMBER_IDS / HAS_RVV_HIGH_BYTE) to read the
    /// variable-width fields.
    /// </remarks>
    internal virtual void FromData(DataInput reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Mirrors cppcache VersionTag::fromData
        // (cppcache/src/VersionTag.cpp:48-65).
        //
        // ─── Step 1: read flags ────────────────────────────────
        var flags = reader.ReadUInt16();

        // ─── Step 2: read _bits ────────────────────────────────
        _bits = reader.ReadUInt16();

        // ─── Step 3: skip distributedSystemId byte ─────────────
        // cppcache reads + discards. Phase 4+ multi-cluster routing
        // may surface this; Phase 1.3 single-cluster doesn't need it.
        reader.AdvanceCursor(1);

        // ─── Step 4: read _entryVersion (16 or 32 bit) ─────────
        // cppcache masks the result with 0xffff / 0xffffffff to
        // erase any sign-extension; we read unsigned for the 16-bit
        // path and assign directly into int (implicit widening),
        // which has the same effect without the mask.
        if ((flags & VERSION_TWO_BYTES) != 0)
        {
            _entryVersion = reader.ReadUInt16();
        }
        else
        {
            _entryVersion = reader.ReadInt32();
        }

        // ─── Step 5: read _regionVersionHighBytes (optional) ───
        if ((flags & HAS_RVV_HIGH_BYTE) != 0)
        {
            _regionVersionHighBytes = reader.ReadInt16();
        }

        // ─── Step 6: read _regionVersionLowBytes ───────────────
        _regionVersionLowBytes = reader.ReadInt32();

        // ─── Step 7: read _timeStamp (VL unsigned) ─────────────
        _timeStamp = (long)reader.ReadUnsignedVL();

        // ─── Step 8: dispatch ReadMembers (virtual hook) ───────
        // Base VersionTag reads ClientProxyMembershipID;
        // DiskVersionTag override reads DiskStoreId.
        logger.LogTrace(
            "VersionTag::fromData flags=0x{Flags:X4} bits=0x{Bits:X4} " +
            "entryVersion={EntryVersion} regionVersionLow={RegionVersionLow} " +
            "timeStamp={TimeStamp}",
            flags, _bits, _entryVersion, _regionVersionLowBytes, _timeStamp);
        ReadMembers(flags, reader);
    }

    /// <summary>
    /// If <see cref="InternalMemId"/> is zero (server didn't ship a
    /// member id because it's the same as the endpoint's own),
    /// substitute <paramref name="memId"/>. Mirrors cppcache
    /// <c>replaceNullMemberId</c>.
    /// </summary>
    internal void ReplaceNullMemberId(ushort memId)
    {
        // Mirrors cppcache VersionTag::replaceNullMemberId
        // (cppcache/src/VersionTag.cpp:72-79).
        if (_previousMemId == 0)
        {
            _previousMemId = memId;
        }
        if (_internalMemId == 0)
        {
            _internalMemId = memId;
        }
    }

    /// <summary>
    /// Member-id wire decode hook called from inside
    /// <see cref="FromData"/>. Mirrors cppcache
    /// <c>VersionTag::readMembers</c>
    /// (<c>cppcache/src/VersionTag.cpp</c>); subclass
    /// <see cref="DiskVersionTag"/> overrides to read
    /// <c>DiskStoreId</c> instead of <c>InternalDistributedMember</c>.
    /// </summary>
    /// <remarks>
    /// Virtual so the persistent-region path
    /// (<see cref="DiskVersionTag"/>) can swap the member-id codec
    /// without re-implementing <see cref="FromData"/>. Phase 4+ when
    /// the actual member-id wire types land.
    /// </remarks>
    protected virtual void ReadMembers(ushort flags, DataInput reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Mirrors cppcache VersionTag::readMembers
        // (cppcache/src/VersionTag.cpp:80-96).
        //
        // ─── Step 1: HAS_MEMBER_ID → read internal member id ───
        if ((flags & HAS_MEMBER_ID) != 0)
        {
            if (MemberListForVersionStamp is null)
            {
                throw new GeodeException(
                    "VersionTag.ReadMembers: HAS_MEMBER_ID flag set but " +
                    "MemberListForVersionStamp was not provided to ctor.");
            }
            var internalMemId = ActivatorUtilities.CreateInstance<ClientProxyMembershipID>(serviceProvider);
            internalMemId.ReadEssentialData(reader);
            _internalMemId = MemberListForVersionStamp.Add(internalMemId);
        }

        // ─── Step 2: HAS_PREVIOUS_MEMBER_ID → read previous id ─
        // DUPLICATE_MEMBER_IDS short-circuit: previous member is the
        // same as internal — reuse the id we just registered instead
        // of reading + adding the same payload twice.
        if ((flags & HAS_PREVIOUS_MEMBER_ID) != 0)
        {
            if ((flags & DUPLICATE_MEMBER_IDS) != 0)
            {
                _previousMemId = _internalMemId;
            }
            else
            {
                if (MemberListForVersionStamp is null)
                {
                    throw new GeodeException(
                        "VersionTag.ReadMembers: HAS_PREVIOUS_MEMBER_ID flag set " +
                        "but MemberListForVersionStamp was not provided to ctor.");
                }
                var previousMemId = ActivatorUtilities.CreateInstance<ClientProxyMembershipID>(serviceProvider);
                previousMemId.ReadEssentialData(reader);
                _previousMemId = MemberListForVersionStamp.Add(previousMemId);
            }
        }
    }
}
