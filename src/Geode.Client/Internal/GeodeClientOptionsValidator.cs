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
/// Phase 1.1 closing item (Phase 1.1 工作清單最後一項，PROGRESS.md
/// "接到 Cache" 段落)：fail-fast on
/// <see cref="CacheXmlOptions.Pools"/> shape problems — empty list,
/// missing pool name, no locators / servers, bad host / port,
/// inconsistent <see cref="CacheXmlPoolOptions.MinConnections"/> /
/// <see cref="CacheXmlPoolOptions.MaxConnections"/>.
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

        var failures = new List<string>();
        var prefix = string.IsNullOrEmpty(name)
            ? "GeodeClientOptions"
            : $"GeodeClientOptions[{name}]";

        // CacheXml null and empty Pools list collapse to the same
        // failure: "client doesn't know where to connect". Phase 1.1's
        // InitializeCoreAsync requires at least one pool.
        var pools = options.CacheXml?.Pools;
        if (pools is null || pools.Count == 0)
        {
            failures.Add(
                $"{prefix}.CacheXml.Pools must contain at least one pool.");
        }
        else
        {
            for (var i = 0; i < pools.Count; i++)
            {
                var pool = pools[i];

                if (string.IsNullOrWhiteSpace(pool.Name))
                {
                    failures.Add(
                        $"{prefix}.CacheXml.Pools[{i}].Name must not be null, empty, or whitespace.");
                }

                if (pool.Locators.Count + pool.Servers.Count  == 0)
                {
                    failures.Add(
                        $"{prefix}.CacheXml.Pools[{i}] must have at least one locator or server.");
                }

                ValidateHostPorts($"{prefix}.CacheXml.Pools[{i}].Locators", pool.Locators, failures);
                ValidateHostPorts($"{prefix}.CacheXml.Pools[{i}].Servers", pool.Servers, failures);

                // MinConnections == 0 is allowed (cppcache permits 0 = pure lazy).
                if (pool.MinConnections < 0)
                {
                    failures.Add(
                        $"{prefix}.CacheXml.Pools[{i}].MinConnections must be >= 0 (got {pool.MinConnections}).");
                }

                // MaxConnections == null means "unbounded" — skip the comparison.
                if (pool.MaxConnections is int max && max < pool.MinConnections)
                {
                    failures.Add(
                        $"{prefix}.CacheXml.Pools[{i}].MaxConnections ({max}) must be >= MinConnections ({pool.MinConnections}).");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Per-entry validation for a list of <see cref="CacheXmlHostPort"/>:
    /// <see cref="CacheXmlHostPort.Host"/> non-empty,
    /// <see cref="CacheXmlHostPort.Port"/> in <c>[1, 65535]</c>. Same
    /// shape applies to both <c>Locators</c> and <c>Servers</c>; the
    /// <paramref name="pathPrefix"/> distinguishes which list a failure
    /// came from.
    /// </summary>
    private static void ValidateHostPorts(
        string pathPrefix,
        List<CacheXmlHostPort> entries,
        List<string> failures)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (string.IsNullOrWhiteSpace(entry.Host))
            {
                failures.Add(
                    $"{pathPrefix}[{i}].Host must not be null, empty, or whitespace.");
            }

            if (entry.Port is < 1 or > 65535)
            {
                failures.Add(
                    $"{pathPrefix}[{i}].Port must be in the range [1, 65535] (got {entry.Port}).");
            }
        }
    }
}
