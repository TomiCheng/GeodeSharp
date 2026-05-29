using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Per-entry version stamp held inside <see cref="MapEntry"/>: encodes
/// the entry-version + region-version + originating member id used for
/// concurrency-checks (race-loser / concurrent-modification detection)
/// when a <see cref="Protocol.VersionTag"/> arrives from the server.
/// Mirrors cppcache <c>VersionStamp</c>
/// (<c>cppcache/src/VersionStamp.hpp:37</c>). Skeleton only — fields +
/// <c>processVersionTag</c> / <c>setVersions</c> land with the
/// concurrency-checks feature (Phase 2+).
/// </summary>
internal sealed class VersionStamp
{
    public void ProcessVersionTag(LocalRegion region, object key, VersionTag versionTag, bool deltaCheck) => throw new NotImplementedException();
    public void SetVersions(VersionTag versionTag) => throw new NotImplementedException();
    public void SetVersions(VersionStamp other) => throw new NotImplementedException();
}
