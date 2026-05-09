using System.Collections.Concurrent;
using Geode.Client.Options;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCacheFactory"/>. Lazily constructs one
/// <see cref="GeodeCache"/> per registered name and caches it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>AddGeodeClient</c>. Construction
/// uses <see cref="IOptionsMonitor{TOptions}.Get(string)"/> so the
/// caller's named options bindings light up automatically.
/// </para>
/// <para>
/// <b>No hot reload.</b> We deliberately do not subscribe to
/// <c>IOptionsMonitor&lt;T&gt;.OnChange</c>. A built
/// <see cref="GeodeCache"/> owns an open TCP/TLS connection, handshake
/// state, membership id, and (eventually) a connection pool — those
/// cannot be swapped under live <c>IRegion&lt;K, V&gt;</c> references
/// without breaking in-flight ops. <see cref="IOptionsMonitor{T}"/> is
/// chosen only for its <c>Get(name)</c> + singleton-lifetime support;
/// the change-notification half is intentionally unused.
/// </para>
/// </remarks>
internal sealed class GeodeCacheFactory(IOptionsMonitor<GeodeClientOptions> optionsMonitor)
    : IGeodeCacheFactory
{
    private readonly ConcurrentDictionary<string, IGeodeCache> _caches = new(StringComparer.Ordinal);

    public IGeodeCache Get() => Get(MsOptions.DefaultName);

    public IGeodeCache Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Sync, no I/O: just snapshot the named options and wrap them.
        // The cache itself initialises lazily — the first wire-touching
        // op (region get/put, ping, query) awaits
        // GeodeCache.EnsureInitializedAsync.
        return _caches.GetOrAdd(name, static (n, monitor) =>
        {
            var options = monitor.Get(n);
            return (IGeodeCache)new GeodeCache(n, options);
        }, optionsMonitor);
    }
}
