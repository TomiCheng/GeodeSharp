namespace Geode.Client;

/// <summary>
/// Resolves <see cref="IGeodeCache"/> instances by name. Equivalent to
/// the BCL keyed-DI lookup but hides the <c>IServiceProvider</c> seam
/// from consumers.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="IGeodeCache"/> per name (cached for the lifetime of
/// the factory). Use <see cref="Get()"/> for the unnamed default
/// registration; <see cref="Get(string)"/> for named registrations.
/// </para>
/// <para>
/// <b>Options changes after registration are NOT propagated.</b> The
/// underlying <see cref="GeodeClientOptions"/> snapshot is captured
/// when the named cache is first resolved and reused for the lifetime
/// of the factory. Mutating <c>appsettings.json</c>, calling
/// <c>OptionsMonitor.OnChange</c>, or replacing config providers at
/// runtime has no effect on already-built caches — the open
/// connection / handshake / pool state is bound to that snapshot.
/// To pick up new options, restart the host or rebuild the service
/// provider.
/// </para>
/// </remarks>
public interface IGeodeCacheFactory
{
    /// <summary>
    /// Get the unnamed default cache (registered via
    /// <c>AddGeodeClient(...)</c> without a name argument).
    /// </summary>
    /// <remarks>
    /// Synchronous and cheap — no socket is opened here. The first
    /// region / query / ping operation on the returned cache will
    /// trigger the connect + handshake.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No unnamed cache was registered.
    /// </exception>
    IGeodeCache Get();

    /// <summary>
    /// Get a named cache (registered via
    /// <c>AddGeodeClient(name, ...)</c>).
    /// </summary>
    /// <remarks>
    /// Synchronous and cheap — no socket is opened here. The first
    /// region / query / ping operation on the returned cache will
    /// trigger the connect + handshake.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No cache was registered with the given name.
    /// </exception>
    IGeodeCache Get(string name);
}
