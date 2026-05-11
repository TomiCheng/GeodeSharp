using Geode.Client.Internal;
using Geode.Client.Options;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// Concrete proxy-mode region implementation. Mirrors cppcache
/// <c>ThinClientRegion</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:51</c>): inherits the local
/// machinery (here: empty placeholder
/// <see cref="LocalRegion"/> / <see cref="RegionInternal"/>) and adds
/// server roundtrips via a <see cref="ThinClientBaseDM"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1.2 skeleton: fields + ctor in place, all 4 IRegion ops
/// throw <see cref="NotImplementedException"/>. Wire dispatch
/// (<c>SendSyncRequestAsync</c> via <see cref="ThinClientBaseDM"/>)
/// lands in Phase 1.2.e alongside the operation builders and the
/// reply decoder.
/// </para>
/// <para>
/// Note the type is non-generic — <c>TKey, TValue</c> live only on
/// the public <see cref="IRegion{TKey, TValue}"/> view, exposed
/// through <see cref="RegionView{TKey, TValue}"/>. The wire path
/// is <c>object</c>-typed; strong typing is compile-time only.
/// </para>
/// </remarks>
internal sealed class ThinClientRegion : LocalRegion
{
    private readonly ThinClientBaseDM _dm;
    private readonly ILogger<ThinClientRegion> _logger;

    public ThinClientRegion(
        string name,
        RegionInternal? parent,
        CacheXmlRegionAttributesOptions attributes,
        ThinClientBaseDM dm,
        ILogger<ThinClientRegion> logger)
        : base(name, parent, attributes)
    {
        ArgumentNullException.ThrowIfNull(dm);
        ArgumentNullException.ThrowIfNull(logger);
        _dm = dm;
        _logger = logger;
    }

    /// <summary>
    /// Distribution manager this region dispatches to. Mirrors
    /// cppcache <c>ThinClientRegion::m_tcrdm</c>; pool-mode MVP
    /// always carries a <see cref="ThinClientPoolDM"/> here.
    /// </summary>
    internal ThinClientBaseDM DistributionManager => _dm;

    public override Task PutAsync(object key, object value, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: build TcrMessageBuilder.Put(...) with this
        //   region's FullPath, dispatch via _dm.SendSyncRequestAsync,
        //   inspect reply.MessageType (Reply OK / Exception → throw).
        //   Mirrors cppcache ThinClientRegion::putNoThrow_remote
        //   (ThinClientRegion.cpp).
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.PutAsync");
    }

    public override Task<object?> GetAsync(object key, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: TcrMessageBuilder.Get(FullPath, key) →
        //   _dm.SendSyncRequestAsync → decode Response (DSCode-aware).
        //   Mirrors cppcache ThinClientRegion::getNoThrow_remote.
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.GetAsync");
    }

    public override Task<bool> RemoveAsync(object key, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: TcrMessageBuilder.Destroy(FullPath, key) →
        //   _dm.SendSyncRequestAsync → reply means key existed; absent-key
        //   surfaces as a specific Exception subtype.
        //   Mirrors cppcache ThinClientRegion::destroyNoThrow_remote.
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.RemoveAsync");
    }

    public override Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default)
    {
        // TODO Phase 1.2.e: TcrMessageBuilder.ContainsKey(FullPath, key)
        //   → _dm.SendSyncRequestAsync → reply Part 0 = bool. Mirrors
        //   cppcache ThinClientRegion::containsKeyOnServer.
        throw new NotImplementedException("TODO Phase 1.2.e: ThinClientRegion.ContainsKeyAsync");
    }
}
