/*
using Geode.Client.Options;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract local-only region machinery. Mirrors cppcache
/// <c>LocalRegion</c> (<c>cppcache/src/LocalRegion.hpp:119</c>) — owns
/// the in-memory entry map (<c>m_entries</c>), name / full-path,
/// listener / writer / loader hooks, persistence manager, expiry
/// task plumbing.
/// </summary>
/// <remarks>
/// <para>
/// MVP is proxy-only (no client-side caching), so the in-memory map +
/// callback machinery is all deferred. The class still exists in the
/// hierarchy so <see cref="ThinClientRegion"/> sits at the
/// same depth as cppcache; once <c>caching-enabled</c> is honoured
/// (Phase 2+), the local-cache code lands here without disturbing the
/// derived class.
/// </para>
/// <para>
/// cppcache ctor signature: <c>(name, CacheImpl*, parentRegion,
/// RegionAttributes, CacheStatistics, enableTimeStatistics)</c>. We
/// keep <c>name</c> + <c>parent</c> + <c>attributes</c>; cache back-ref,
/// stats, and time-stats flag are deferred until something actually
/// reads them.
/// </para>
/// </remarks>
internal abstract class LocalRegion : RegionInternal
{
    protected LocalRegion(
        string name,
        RegionInternal? parent,
        CacheRegionAttributesOptions attributes)
        : base(attributes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Parent = parent;

        // cppcache LocalRegion.cpp:75-79 — root: "/" + name; sub: parent.FullPath + "/" + name.
        FullPath = parent is null
            ? "/" + name
            : parent.FullPath + "/" + name;
    }

    /// <summary>
    /// Parent region in the sub-region tree, or <c>null</c> for a
    /// root region. Mirrors cppcache <c>LocalRegion::m_parentRegion</c>.
    /// </summary>
    protected RegionInternal? Parent { get; }

    public override string Name { get; }
    public override string FullPath { get; }

    // 4 IRegion ops still abstract — concrete dispatch lives in
    // ThinClientRegion (Phase 1.2.e). When local caching
    // lands, base impls go here that consult m_entries first and
    // delegate to the derived class for server roundtrips.

    // TODO future phases — fields cppcache LocalRegion holds that
    // we'll grow into:
    //   m_entries (EntriesMap)            — Phase 2+ (caching-enabled)
    //   m_listener / m_writer / m_loader  — niche, may stay cut
    //   m_persistenceManager              — not implemented (CLAUDE.md)
    //   expiry_task_id_                   — server-side, not client
    //   m_destroyPending                  — Phase 1.5 lifecycle
    //   m_attachedPool                    — Phase 1.2.e wiring
}

*/