namespace Geode.Client;

/// <summary>
/// Resolves <see cref="IGeodeCache"/> instances by name. Equivalent to
/// the BCL keyed-DI lookup but hides the <c>IServiceProvider</c> seam
/// from consumers.
/// </summary>
/// <remarks>
/// One <see cref="IGeodeCache"/> per name (cached for the lifetime of
/// the factory). Use <see cref="Get()"/> for the unnamed default
/// registration; <see cref="Get(string)"/> for named registrations.
/// </remarks>
public interface IGeodeCacheFactory
{
    /// <summary>
    /// Get the unnamed default cache (registered via
    /// <c>AddGeodeClient(...)</c> without a name argument).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No unnamed cache was registered.
    /// </exception>
    IGeodeCache Get();

    /// <summary>
    /// Get a named cache (registered via
    /// <c>AddGeodeClient(name, ...)</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No cache was registered with the given name.
    /// </exception>
    IGeodeCache Get(string name);
}
