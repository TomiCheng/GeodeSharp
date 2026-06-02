using Geode.Client.Protocol;

namespace Geode.Client.Internal.Entry;

/// <summary>
/// Per-entry version stamp: entry-version + region-version + originating
/// member id for concurrency-checks. Mirrors cppcache <c>VersionStamp</c>
/// (<c>cppcache/src/VersionStamp.hpp:37</c>).
/// </summary>
internal class VersionStamp
{
    // cppcache fields (VersionStamp.hpp:75-79). Entry/region versions are
    //   split into high/low parts on the wire; recombined by the getters.
    private ushort _memberId;
    private byte _entryVersionHighByte;
    private ushort _entryVersionLowBytes;
    private ushort _regionVersionHighBytes;
    private uint _regionVersionLowBytes;

    /// <summary>cppcache <c>getEntryVersion</c> (VersionStamp.cpp:48): <c>(high &lt;&lt; 16) | low</c>.</summary>
    public int GetEntryVersion() => (_entryVersionHighByte << 16) | _entryVersionLowBytes;

    /// <summary>cppcache <c>getRegionVersion</c> (VersionStamp.cpp:52): <c>(high &lt;&lt; 32) | low</c>.</summary>
    public long GetRegionVersion() => ((long)_regionVersionHighBytes << 32) | _regionVersionLowBytes;

    /// <summary>cppcache <c>getMemberId</c> (VersionStamp.cpp:57).</summary>
    public ushort GetMemberId() => _memberId;

    /// <summary>
    /// Seed this stamp from an incoming <see cref="VersionTag"/>. Mirrors
    /// cppcache <c>setVersions(shared_ptr&lt;VersionTag&gt;)</c> (VersionStamp.cpp:32-39)
    /// — entry version split back into high byte + low bytes.
    /// </summary>
    public void SetVersions(VersionTag versionTag)
    {
        var entryVersion = versionTag.EntryVersion;
        _entryVersionLowBytes = (ushort)(entryVersion & 0xffff);
        _entryVersionHighByte = (byte)((entryVersion & 0xff0000) >> 16);
        _regionVersionHighBytes = (ushort)versionTag.RegionVersionHighBytes;
        _regionVersionLowBytes = (uint)versionTag.RegionVersionLowBytes;
        _memberId = versionTag.InternalMemId;
    }

    /// <summary>
    /// Copy versions from another stamp (tombstone-resurrection carries the
    /// prior history). Mirrors cppcache <c>setVersions(VersionStamp&amp;)</c>
    /// (VersionStamp.cpp:41-47).
    /// </summary>
    public void SetVersions(VersionStamp other)
    {
        _entryVersionLowBytes = other._entryVersionLowBytes;
        _entryVersionHighByte = other._entryVersionHighByte;
        _regionVersionHighBytes = other._regionVersionHighBytes;
        _regionVersionLowBytes = other._regionVersionLowBytes;
        _memberId = other._memberId;
    }

    /// <summary>
    /// Check an incoming version tag against this stamp for conflict /
    /// delta-base. Mirrors cppcache <c>processVersionTag</c>
    /// (VersionStamp.cpp:65 + checkForConflict / checkForDeltaConflict) — the
    /// concurrency-checks conflict logic, Phase 2+.
    /// </summary>
    public void ProcessVersionTag(LocalRegion region, object key, VersionTag versionTag, bool deltaCheck)
        => throw new NotImplementedException(
            "VersionStamp.ProcessVersionTag: pending concurrency-checks conflict detection (Phase 2+).");
}
