/*
using Geode.Client.Options;

namespace Geode.Client.Services;

/// <summary>
/// Per-cache <see cref="Microsoft.Extensions.DependencyInjection.AsyncServiceScope"/>
/// state object: carries the cache <see cref="Name"/> and the resolved
/// <see cref="GeodeClientOptions"/> for that name into every scoped
/// service that needs them.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists. <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>
/// always resolves the unnamed default instance — useless for our
/// multi-cluster scenario where each <c>AddGeodeClient(..., "name")</c>
/// registers a distinct named bind. <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// can <c>.Get(name)</c> but the consumer needs to know which name to
/// pass — and a scoped service has no clean way to learn its enclosing
/// cache's name.
/// </para>
/// <para>
/// <see cref="Services.GeodeCacheFactory"/> resolves the right name +
/// options pair, calls <see cref="Initialize"/> once on the per-cache
/// scope, and downstream scoped services
/// (<see cref="Protocol.ClientProxyMembershipIdBuilder"/>,
/// <see cref="Protocol.TcrConnection"/>, eventually pool / metrics /
/// auth) inject this object instead of <c>IOptions</c> / <c>IOptionsMonitor</c>
/// directly.
/// </para>
/// <para>
/// Registered as <b>scoped</b>. Mutation is one-shot: the factory
/// initialises before any scope-internal consumer reads, and the
/// scope's lifetime ends with the cache.
/// </para>
/// </remarks>
internal sealed class CacheScopeContext
{
    /// <summary>Cache name (default name = <see cref="string.Empty"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Bound options for <see cref="Name"/>.</summary>
    public GeodeClientOptions Options { get; private set; } = new();

    private bool _initialized;

    /// <summary>
    /// Bind <paramref name="name"/> + <paramref name="options"/> into
    /// this scope. Called exactly once by
    /// <see cref="Services.GeodeCacheFactory"/> before any other
    /// scope-internal consumer resolves.
    /// </summary>
    public void Initialize(string name, GeodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        if (_initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(CacheScopeContext)} already initialised for cache '{Name}'; double-initialise indicates a factory bug.");
        }
        Name = name;
        Options = options;
        _initialized = true;
    }
}

*/