using Geode.Client.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCache"/> implementation. One instance per
/// registered name (cached by <see cref="GeodeCacheFactory"/>).
/// </summary>
internal sealed class GeodeCache : IGeodeCache
{
    private readonly GeodeClientOptions _options;

    public GeodeCache(string name, GeodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        Name = name;
        _options = options;
    }

    public string Name { get; }

    public bool IsClosed { get; private set; }

    /// <summary>
    /// Open connection(s), perform handshake, prime the pool. Called by
    /// <see cref="GeodeCacheFactory"/> the first time this cache is
    /// resolved.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        // TODO: open TcrConnection(s) per Pool options, run handshake,
        //       store membership id, register with the connection pool
        //       once the pool layer lands.
        throw new NotImplementedException("TODO: GeodeCache.InitializeAsync");
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        // TODO: drain in-flight ops, send CloseConnection (MessageType 18),
        //       dispose connections. Until InitializeAsync runs there is
        //       nothing to tear down, so closing is idempotent and safe.
        IsClosed = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Forward to CloseAsync; idempotent until connection logic lands.
        await CloseAsync().ConfigureAwait(false);
    }
}
