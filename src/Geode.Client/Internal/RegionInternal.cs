using Geode.Client.Options;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract internal layer between the public <see cref="IRegion"/>
/// interface and the concrete region implementations
/// (<see cref="LocalRegion"/> &#x2192;
/// <see cref="Services.ThinClientRegion"/>). Mirrors cppcache
/// <c>RegionInternal</c> (<c>cppcache/src/RegionInternal.hpp:131</c>).
/// </summary>
/// <remarks>
/// <para>
/// cppcache uses this layer to expose internal-only operations that
/// the public <c>Region</c> interface doesn't surface (event flags,
/// version tags, tombstones, internal Put / Get variants that take
/// <c>EventId</c> + <c>VersionTag</c>). All of those land in their
/// respective phases — Phase 1.2 keeps the layer empty so the
/// inheritance chain matches cppcache for future ports.
/// </para>
/// <para>
/// cppcache ctor takes <c>(CacheImpl*, RegionAttributes)</c>; the
/// cache back-pointer is deferred — first consumer that needs it
/// (likely the serialization registry in Phase 2 or stats in Phase
/// 1.5) will add it.
/// </para>
/// </remarks>
internal abstract class RegionInternal : IRegion
{
    protected RegionInternal(CacheXmlRegionAttributesOptions attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        Attributes = attributes;
    }

    /// <summary>
    /// XML-declared region attributes. Mirrors cppcache
    /// <c>RegionInternal::m_regionAttributes</c>.
    /// </summary>
    protected CacheXmlRegionAttributesOptions Attributes { get; }

    // ── IRegion (forward to derived) ───────────────────────────
    public abstract string Name { get; }
    public abstract string FullPath { get; }

    /// <summary>
    /// Mirrors cppcache <c>RegionAttributes::getPoolName()</c>; the
    /// reference (if any) into <c>CacheXmlOptions.Pools</c>.
    /// </summary>
    public string PoolName => Attributes.PoolName;

    public abstract Task PutAsync(object key, object value, CancellationToken ct = default);
    public abstract Task<object?> GetAsync(object key, CancellationToken ct = default);
    public abstract Task<bool> RemoveAsync(object key, CancellationToken ct = default);
    public abstract Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default);
    public abstract Task ClearAsync(CancellationToken ct = default);
    public abstract Task InvalidateAsync(object key, CancellationToken ct = default);

    // TODO future phases — internal-only API surface that cppcache
    // RegionInternal exposes; add as their respective phases ship:
    //   Phase 2+:  putNoThrow_remote / getNoThrow_remote (EventId-aware)
    //              versionStamp / tombstoneList / cacheImpl back-ref
    //   Phase 4:   single-hop / partitioned-region helpers
    //   Sub-region phase: createSubRegion / getSubRegion / subRegions
}
