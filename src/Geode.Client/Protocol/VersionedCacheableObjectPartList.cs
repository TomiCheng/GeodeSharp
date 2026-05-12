namespace Geode.Client.Protocol;

/// <summary>
/// Per-key result list shipped back in the chunked replies of bulk
/// ops (<see cref="MessageType.PutAll"/>,
/// <see cref="MessageType.RemoveAll"/>,
/// <see cref="MessageType.GetAll70"/>). Mirrors cppcache
/// <c>VersionedCacheableObjectPartList</c>
/// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: members only, no decoder.</b> Field shape
/// mirrors cppcache (1:1 with the <c>m_*</c> instance members) so
/// the wire decoder (<c>fromData</c>) and merge (<c>addAll</c>) can
/// land later without re-shaping. Phase 1.3 bulk ops discard per-key
/// results; Phase 4+ (client-side caching / concurrency checks) wires
/// them through.
/// </para>
/// <para>
/// Inherits from <see cref="CacheableObjectPartList"/> (cppcache
/// <c>CacheableObjectPartList</c> base), which holds keys / values
/// / exceptions / region back-ref / tracker maps. This class adds
/// the version-tag tier on top.
/// </para>
/// </remarks>
// Phase 1.3.b: fields below are placeholder shells until the wire
// decoder (fromData / addAll) lands in Phase 4+. They mirror
// cppcache m_* members 1:1 so the decoder body slots in without
// re-shaping.
#pragma warning disable CS0169 // field never used — see file note
#pragma warning disable CS0414 // field assigned but never used — same
#pragma warning disable CS0649 // field never assigned — same
internal sealed class VersionedCacheableObjectPartList : CacheableObjectPartList
{
    /// <summary>cppcache <c>m_regionIsVersioned</c>.</summary>
    private bool _regionIsVersioned;

    /// <summary>cppcache <c>m_serializeValues</c>.</summary>
    private bool _serializeValues;

    /// <summary>cppcache <c>m_hasTags</c> — true once any
    /// <c>VersionTag</c> has been read into <see cref="_versionTags"/>.</summary>
    private bool _hasTags;

    /// <summary>cppcache <c>m_hasKeys</c> — true once any key has
    /// been read into <see cref="_tempKeys"/> (GetAll path).</summary>
    private bool _hasKeys;

    /// <summary>
    /// Per-key version tags read off the wire. Element type
    /// <see cref="object"/>? until the <c>VersionTag</c> decoder
    /// class lands (Phase 4+). Mirrors cppcache <c>m_versionTags</c>
    /// (<c>std::vector&lt;std::shared_ptr&lt;VersionTag&gt;&gt;</c>).
    /// </summary>
    private readonly List<object?> _versionTags = new();

    /// <summary>
    /// Per-key miss-flag byte: <c>0</c> = present, <c>3</c> = key
    /// absent on server, <c>2</c> = exception, etc. Mirrors cppcache
    /// <c>m_byteArray</c> (<c>std::vector&lt;uint8_t&gt;</c>).
    /// </summary>
    private readonly List<byte> _byteArray = new();

    /// <summary>
    /// Server's endpoint-memory id at the time the chunk arrived.
    /// Used by single-hop (Phase 4) to attribute version tags to the
    /// right server. Mirrors cppcache <c>m_endpointMemId</c>.
    /// </summary>
    private ushort _endpointMemId;

    /// <summary>
    /// Keys read off the wire (GetAll path only). Element type
    /// <see cref="object"/> — keys are already <c>object</c>-typed in
    /// our codec (cppcache wraps them in <c>CacheableKey</c> which we
    /// don't mirror as a separate class). Mirrors cppcache
    /// <c>m_tempKeys</c>.
    /// </summary>
    private readonly List<object> _tempKeys = new();

    /// <summary>
    /// The accumulated per-key version-tag list. Mirrors cppcache
    /// <c>getVersionedTagptr()</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp:154-156</c>)
    /// &#x2014; returns the inner list directly so callers
    /// (chunked-reply handlers, <c>Reset</c>) can mutate it without
    /// going through a dedicated method.
    /// </summary>
    internal IList<object?> VersionTags => _versionTags;

    /// <summary>
    /// Number of accumulated entries. Mirrors cppcache
    /// <c>VersionedCacheableObjectPartList::size()</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp:220-231</c>):
    /// returns <see cref="_tempKeys"/> size when keys are tracked,
    /// <see cref="_versionTags"/> size when only tags are tracked,
    /// or <c>-1</c> when neither flag is set (called too early).
    /// </summary>
    /// <remarks>
    /// Phase 1.3 never sets <see cref="_hasKeys"/> /
    /// <see cref="_hasTags"/> (decoder body not written yet), so
    /// this always returns <c>-1</c> — callers' <c>Size &gt; 0</c>
    /// guards short-circuit correctly.
    /// </remarks>
    internal int Size
    {
        get
        {
            if (_hasKeys) return _tempKeys.Count;
            if (_hasTags) return _versionTags.Count;
            return -1;
        }
    }

    /// <summary>
    /// Lock around concurrent <c>fromData</c> / <c>addAll</c>.
    /// cppcache <c>m_responseLock</c> is a
    /// <c>std::recursive_mutex&amp;</c>; .NET equivalent is a plain
    /// <c>lock</c> object (recursive entry by the same task is
    /// not the same as recursive thread entry, but Phase 1.3's
    /// chunked path drains chunks sequentially on one task, so any
    /// lock suffices). Field kept for cppcache parity; not exercised
    /// yet.
    /// </summary>
    private readonly object _responseLock = new();
}
