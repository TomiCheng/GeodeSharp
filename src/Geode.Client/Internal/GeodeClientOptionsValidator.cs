/*
using Geode.Client.Options;
using Microsoft.Extensions.Options;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="IValidateOptions{TOptions}"/> for
/// <see cref="GeodeClientOptions"/>. Wired into every
/// <c>AddGeodeClient</c> overload via <c>ValidateOnStart()</c> so a
/// misconfigured pool fails at host build time instead of leaking out
/// as a cryptic <see cref="NullReferenceException"/> deep inside
/// <c>Cache.InitializeCoreAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Thin wrapper — the actual rules live on each options class as
/// <c>Validate(string prefix)</c> methods (see
/// <see cref="GeodeClientOptions.Validate"/>, recursing into
/// sub-options). This class's only job is to bridge the
/// <see cref="IValidateOptions{TOptions}"/> contract: build a prefix
/// from the (possibly null) name and wrap the failure
/// <see cref="IEnumerable{T}"/> into a <see cref="ValidateOptionsResult"/>.
/// </para>
/// <para>
/// The same logic is reused by
/// <c>IGeodeCacheFactory.Create</c> after running the caller-supplied
/// <c>action</c> on a cloned options instance — validation lives on
/// the options so both entry points share one truth.
/// </para>
/// <para>
/// Single instance handles all named registrations: this validator
/// returns the same verdict for any <c>name</c> (named, default, or
/// keyed). cppcache has no equivalent — its config layer is XML +
/// runtime checks scattered across <c>CacheImpl::create</c>; we
/// hoist the checks up into one DI-time gate.
/// </para>
/// </remarks>
internal sealed class GeodeClientOptionsValidator : IValidateOptions<GeodeClientOptions>
{
    public ValidateOptionsResult Validate(string? name, GeodeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var prefix = string.IsNullOrEmpty(name)
            ? nameof(GeodeClientOptions)
            : $"{nameof(GeodeClientOptions)}[{name}]";

        var failures = options.Validate(prefix).ToList();
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

*/