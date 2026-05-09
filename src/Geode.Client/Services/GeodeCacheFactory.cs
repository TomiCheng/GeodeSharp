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
/// Registered as a singleton by <c>AddGeodeClient</c>. Construction
/// uses <see cref="IOptionsMonitor{TOptions}.Get(string)"/> so the
/// caller's named options bindings light up automatically.
/// </remarks>
internal sealed class GeodeCacheFactory : IGeodeCacheFactory
{
    private readonly IOptionsMonitor<GeodeClientOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, IGeodeCache> _caches = new(StringComparer.Ordinal);

    public GeodeCacheFactory(IOptionsMonitor<GeodeClientOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        _optionsMonitor = optionsMonitor;
    }

    public IGeodeCache Get() => Get(MsOptions.DefaultName);

    public IGeodeCache Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _caches.GetOrAdd(name, static (n, monitor) =>
        {
            var options = monitor.Get(n);
            var cache = new GeodeCache(n, options);
            // TODO: kick off cache.InitializeAsync — needs design call
            //       on whether Get() blocks (sync init), Get() is async
            //       (rename to GetAsync), or init is lazy (first op
            //       triggers connect). Default-name path stays
            //       resolvable from DI either way.
            return cache;
        }, _optionsMonitor);
    }
}
