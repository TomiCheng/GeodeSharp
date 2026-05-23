/*
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-scoped service that maintains client-side metadata for
/// partitioned regions (bucket → server location mapping) so the pool
/// can route PR ops single-hop to the bucket primary instead of going
/// via any server. Mirrors cppcache <c>ClientMetadataService</c>
/// (<c>cppcache/src/ClientMetadataService.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// Phase 4 (PR single-hop) entry point. cppcache full surface includes
/// metadata refresh thread, bucket-server resolution, server-to-keys
/// grouping for batched ops, and primary-timeout/secondary-fallback
/// handling. This class currently has only the lifecycle stubs
/// (<see cref="StartAsync"/> / <see cref="StopAsync"/>) so
/// <see cref="ThinClientPoolDM.StartBackgroundThreads"/> can wire the
/// call site now; the body grows with Phase 4 work.
/// </remarks>
internal sealed class ClientMetadataService(
    ThinClientPoolDM pool,
    ILogger<ClientMetadataService> logger)
{
    private readonly ThinClientPoolDM _pool = pool;
    private readonly ILogger<ClientMetadataService> _logger = logger;

    /// <summary>
    /// Launch the background metadata-refresh task. Mirrors cppcache
    /// <c>ClientMetadataService::start()</c>
    /// (<c>ClientMetadataService.cpp</c>) — spawns the
    /// <c>svc()</c> loop that processes the metadata-refresh queue.
    /// </summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // TODO Phase 4: spawn the metadata-refresh background task; the
        // loop pulls region paths off the refresh queue and issues
        // GetClientPRMetadataRequest / GetClientPartitionAttributesRequest.
        // Currently a no-op so ThinClientPoolDM.StartBackgroundThreads can
        // wire the call site (walking-skeleton).
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the background metadata-refresh task. Mirrors cppcache
    /// <c>ClientMetadataService::stop()</c>.
    /// </summary>
    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // TODO Phase 4: signal the refresh loop to exit, await its
        // graceful shutdown, drop cached metadata. Currently a no-op so
        // ThinClientPoolDM.DestroyAsync can wire the call site
        // (walking-skeleton).
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drop any cached bucket → server mapping that points at the
    /// endpoint named <paramref name="endpointName"/>, called by
    /// <see cref="ThinClientPoolDM"/> after an IO / timeout failure
    /// against that endpoint. Mirrors cppcache
    /// <c>ClientMetadataService::removeBucketServerLocation</c>.
    /// </summary>
    public void RemoveBucketServerLocation(string endpointName)
    {
        // TODO Phase 4: locate every BucketServerLocation whose name
        // matches and evict it from the bucket → primary/secondary maps;
        // the next op against the same bucket will trigger a metadata
        // refresh. No-op until Phase 4 builds the maps (walking-skeleton).
        _ = endpointName;
    }
}

*/