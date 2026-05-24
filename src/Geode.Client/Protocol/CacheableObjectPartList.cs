using Geode.Client.Internal;

namespace Geode.Client.Protocol;

/// <summary>
/// Base list of object parts shipped back in chunked replies of
/// <c>GetAll</c> / <c>registerInterest</c>. Mirrors cppcache
/// <c>CacheableObjectPartList</c>
/// (<c>cppcache/src/CacheableObjectPartList.hpp</c>); see also
/// the Java side <c>GetAll.ObjectPartList</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: members only, no decoder.</b> Field shape
/// mirrors cppcache 1:1 so the <c>fromData</c> decoder can land
/// without re-shaping. Phase 1.3's only bulk-op consumer
/// (<c>RemoveAll</c>) drops per-key results on the floor &#x2014;
/// this class exists as the base of
/// <see cref="VersionedCacheableObjectPartList"/> for cppcache-shape
/// parity.
/// </para>
/// <para>
/// cppcache derives from <c>DataSerializableFixedId</c> with
/// <c>DSFid.CacheableObjectPartList</c>; Phase 1.3 has no
/// <c>DSFid</c> enum yet, so the fixed-id link lands when the wire
/// decoder does.
/// </para>
/// </remarks>
// Phase 1.3.b: protected fields below are placeholder shells; the
// wire decoder + ctors that actually populate them land in Phase 4+.
#pragma warning disable CS0169 // field never used — see remarks above
#pragma warning disable CS0414 // field assigned but never used — same
internal class CacheableObjectPartList(RegionInternal region)
{
    /// <summary>cppcache <c>m_keys</c>
    /// (<c>const std::vector&lt;CacheableKey&gt;*</c>). Keys are
    /// <c>object</c>-typed in our codec (cppcache wraps them in
    /// <c>CacheableKey</c> which we don't mirror).</summary>
    protected IReadOnlyList<object>? Keys;

    /// <summary>cppcache <c>m_keysOffset</c>
    /// (<c>uint32_t*</c>). cppcache uses a pointer so multiple
    /// readers can advance the same cursor; we'll switch to an
    /// explicit <c>ref int</c> parameter on <c>FromData</c> when the
    /// decoder lands.</summary>
    protected int KeysOffset;

    /// <summary>cppcache <c>m_values</c>
    /// (<c>HashMapOfCacheable</c>) — key→value map populated by
    /// <c>FromData</c>. Value type <see cref="object"/>? until PDX /
    /// custom serialisation lands (Phase 2).</summary>
    protected Dictionary<object, object?>? Values;

    /// <summary>cppcache <c>m_exceptions</c>
    /// (<c>HashMapOfException</c>) — key→exception map for the
    /// failed entries in a partial-result reply. cppcache wraps
    /// the wire-decoded class name in <c>CacheServerException</c>
    /// (or <c>NotAuthorizedException</c>); our port unifies on
    /// <see cref="GeodeException"/>.</summary>
    protected Dictionary<object, GeodeException?>? Exceptions;

    /// <summary>cppcache <c>m_resultKeys</c>
    /// (<c>std::shared_ptr&lt;std::vector&lt;CacheableKey&gt;&gt;</c>).
    /// Keys actually returned by the server (subset of
    /// <see cref="Keys"/> for GetAll partial paths).</summary>
    protected List<object>? ResultKeys;

    /// <summary>cppcache <c>m_region</c>
    /// (<c>ThinClientRegion*</c>) — back-ref so the decoder can
    /// look up region attributes (cachingEnabled, concurrency
    /// checks) and dispatch local-cache writes (<c>putLocal</c>).
    /// Typed as <see cref="RegionInternal"/> (not public
    /// <see cref="IRegion"/>) so Phase 4+ <c>PutLocal</c> calls
    /// reach without a downcast.</summary>
    protected RegionInternal Region { get; } = region;

    /// <summary>cppcache <c>m_updateCountMap</c>
    /// (<c>MapOfUpdateCounters*</c>) — per-key update counters
    /// for the client-side caching tracker. Phase 4+.</summary>
    protected object? UpdateCountMap;

    /// <summary>cppcache <c>m_destroyTracker</c> — destroy-op
    /// tracker id for transactional GetAll. Phase 4+.</summary>
    protected int DestroyTracker;

    /// <summary>cppcache <c>m_addToLocalCache</c> — whether the
    /// decoded entries should be merged into the client's local
    /// cache. Phase 4+ when client-side caching lands.</summary>
    protected bool AddToLocalCache;
}
