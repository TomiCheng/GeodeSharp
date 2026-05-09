using Geode.Client.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCache"/> implementation. One instance per
/// registered name (cached by <see cref="GeodeCacheFactory"/>).
/// </summary>
internal sealed class GeodeCache : IGeodeCache
{
    private readonly GeodeClientOptions _options;
    private readonly Lazy<Task> _initialization;

    public GeodeCache(string name, GeodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        Name = name;
        _options = options;
        _initialization = new Lazy<Task>(
            InitializeCoreAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name { get; }

    public bool IsClosed { get; private set; }

    public Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        // ct is observed inside InitializeCoreAsync; the Lazy<Task>
        // pattern means the *first* caller's ct dictates cancellation
        // for everyone awaiting that init. Acceptable trade for
        // simplicity until we see a real ct-mismatch problem.
        return _initialization.Value;
    }

    private Task InitializeCoreAsync()
    {
        // TODO: open TcrConnection(s) per Pool options, run handshake,
        //       store membership id, register with the connection pool
        //       once the pool layer lands.
        throw new NotImplementedException("TODO: GeodeCache.InitializeCoreAsync");
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        // TODO: drain in-flight ops, send CloseConnection (MessageType 18),
        //       dispose connections. Until init runs there is nothing
        //       to tear down, so closing is idempotent and safe.
        IsClosed = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Forward to CloseAsync; idempotent until connection logic lands.
        await CloseAsync().ConfigureAwait(false);
    }
}
