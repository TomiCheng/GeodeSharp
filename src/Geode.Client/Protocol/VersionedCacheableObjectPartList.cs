using Geode.Client.Internal;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
// _endpointMemId stays placeholder until Phase 4 single-hop wires
// the server-side member id through ctor; CS0649 suppresses the
// "never assigned" warning until then.
#pragma warning disable CS0649
internal sealed class VersionedCacheableObjectPartList(
    IServiceProvider serviceProvider,
    SerializationRegistry serializationRegistry,
    ILogger<VersionedCacheableObjectPartList> logger,
    RegionInternal region) : CacheableObjectPartList(region)
{
    // ── Version-tag entryType wire flags (FromData step 6) ─────
    // cppcache static const uint8_t FLAG_NULL_TAG = 0; etc.
    // (cppcache/src/VersionedCacheableObjectPartList.cpp:33-36).
    private const byte FLAG_NULL_TAG = 0;
    private const byte FLAG_FULL_TAG = 1;
    private const byte FLAG_TAG_WITH_NEW_ID = 2;
    private const byte FLAG_TAG_WITH_NUMBER_ID = 3;

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
    /// Per-key version tags read off the wire. Mirrors cppcache
    /// <c>m_versionTags</c>
    /// (<c>std::vector&lt;std::shared_ptr&lt;VersionTag&gt;&gt;</c>);
    /// element nullable to represent the FLAG_NULL_TAG slot.
    /// </summary>
    private readonly List<VersionTag?> _versionTags = [];

    /// <summary>
    /// Per-key miss-flag byte: <c>0</c> = present, <c>3</c> = key
    /// absent on server, <c>2</c> = exception, etc. Mirrors cppcache
    /// <c>m_byteArray</c> (<c>std::vector&lt;uint8_t&gt;</c>).
    /// </summary>
    private readonly List<byte> _byteArray = [];

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
    private readonly List<object> _tempKeys = [];

    /// <summary>
    /// The accumulated per-key version-tag list. Mirrors cppcache
    /// <c>getVersionedTagptr()</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp:154-156</c>)
    /// &#x2014; returns the inner list directly so callers
    /// (chunked-reply handlers, <c>Reset</c>) can mutate it without
    /// going through a dedicated method.
    /// </summary>
    internal IList<VersionTag?> VersionTags => _versionTags;

    /// <summary>
    /// Number of (miss-flag, value) entries decoded in this chunk's
    /// objects section. Used by <see cref="Services.ChunkedGetAllResponse"/>
    /// to advance the shared <c>KeysOffset</c> across chunks — cppcache
    /// passes <c>m_keysOffset</c> as <c>uint32_t*</c> so the cursor is
    /// shared by reference between chunks; .NET prefers an explicit
    /// post-read read-back.
    /// </summary>
    internal int ConsumedObjectCount => _byteArray.Count;

    /// <summary>
    /// Wire the shared GetAll accumulators into this per-chunk instance
    /// before <see cref="FromData"/> runs. Mirrors cppcache's
    /// <c>VersionedCacheableObjectPartList</c> 10-arg ctor
    /// (<c>cppcache/src/ThinClientRegion.cpp:3640-3643</c>): the
    /// chunked-response handler creates a fresh
    /// <see cref="VersionedCacheableObjectPartList"/> per chunk but
    /// passes pointers / shared_ptrs to the same outer accumulators so
    /// each chunk merges into the same dict.
    /// </summary>
    /// <remarks>
    /// <c>keys</c> is the original keys we sent (so step 5 of
    /// <see cref="FromData"/> can index by position with
    /// <c>Keys[index + KeysOffset]</c>); <c>values</c> /
    /// <c>exceptions</c> / <c>resultKeys</c> are the accumulating
    /// dictionaries / list. <c>addToLocalCache</c> stays
    /// <c>false</c> for Phase 1.3 (no client-side caching) — gating
    /// step 7's NIE on this flag keeps the GetAll round-trip alive.
    /// </remarks>
    internal void Initialize(
        IReadOnlyList<object> keys,
        int keysOffset,
        Dictionary<object, object?> values,
        Dictionary<object, GeodeException?>? exceptions,
        List<object>? resultKeys,
        bool addToLocalCache)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(values);
        Keys = keys;
        KeysOffset = keysOffset;
        Values = values;
        Exceptions = exceptions;
        ResultKeys = resultKeys;
        AddToLocalCache = addToLocalCache;
    }

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
    /// Decode the wire bytes of one
    /// <c>VersionedCacheableObjectPartList</c> chunk into this
    /// instance's fields. Mirrors cppcache
    /// <c>fromData(DataInput&amp;)</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp:247</c>).
    /// </summary>
    /// <remarks>
    /// Phase 4+ — full decoder lands with the
    /// <c>VersionTag</c> wire-format work
    /// (FLAG_NULL_TAG / FLAG_FULL_TAG / FLAG_TAG_WITH_NEW_ID /
    /// FLAG_TAG_WITH_NUMBER_ID branches).
    /// </remarks>
    internal void FromData(BigEndianBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // cppcache wraps the whole body in
        //   std::lock_guard<recursive_mutex> guard(m_responseLock);
        // .NET equivalent is a single `lock` over the response lock
        // object. Phase 1.3 chunked drain is single-task / sequential
        // so the lock is uncontended today; Phase 4+ background chunk
        // processors (multiple chunks merged concurrently) make it
        // necessary.
        lock (_responseLock)
        {
            // Mirrors cppcache VersionedCacheableObjectPartList::fromData
            // (cppcache/src/VersionedCacheableObjectPartList.cpp:92-305).
            //
            // ─── Step 1: read flags byte → 6 bool fields ──────────
            // cppcache VersionedCacheableObjectPartList.cpp:95-101.
            // hasObjects + persistent are loop-locals (decide subsequent
            // reads + which VersionTag subtype to construct); _hasKeys /
            // _hasTags / _regionIsVersioned / _serializeValues land on
            // the instance for later AddAll / Size / Reset to read.
            var flags = reader.ReadByte();
            _hasKeys = (flags & 0x01) == 0x01;
            var hasObjects = (flags & 0x02) == 0x02;
            _hasTags = (flags & 0x04) == 0x04;
            _regionIsVersioned = (flags & 0x08) == 0x08;
            _serializeValues = (flags & 0x10) == 0x10;
            var persistent = (flags & 0x20) == 0x20;
            _ = hasObjects;     // consumed in step 5+7
            _ = persistent;     // consumed in step 6 (VersionTag vs DiskVersionTag)

            // ─── Step 2: init Values if null ──────────────────────
            // cppcache lazy-inits HashMapOfCacheable when caller didn't
            // supply one via the 6-ctor path (VersionedCacheableObjectPartList.cpp:108-111).
            // Phase 1.3 only has the default ctor so Values is always null
            // here; init keeps the shape parity for future GetAll-style
            // ctors that may supply a pre-existing dict.
            Values ??= [];

            // ─── Step 3: empty-message diagnostic ──────────────────
            // cppcache logs and falls through; the downstream hasKeys /
            // hasObjects / hasTags branches skip naturally when their
            // flags are off, so no explicit short-circuit return needed.
            if (!_hasKeys && !hasObjects && !_hasTags)
            {
                logger.LogDebug(
                    "VersionedCacheableObjectPartList::fromData: Looks like message has no data. Returning,");
            }


            // ─── Step 4: read keys section ─────────────────────────
            // cppcache VersionedCacheableObjectPartList.cpp:119-167.
            // localKeys collects this chunk's keys for step 5 (objects)
            // and step 6 (version tags) to index by position. Phase 1.3
            // RemoveAll reply never sets _hasKeys (server doesn't echo
            // keys), so localKeys stays empty here and the hasKeys NIE
            // branch is unreachable today; GetAll (Phase 1.3.c) will
            // exercise it.
            var localKeys = new List<object>();
            if (_hasKeys)
            {
                // cppcache VersionedCacheableObjectPartList.cpp:121-131.
                var keyCount = (int)reader.ReadUnsignedVL();
                for (var i = 0; i < keyCount; i++)
                {
                    var key = serializationRegistry.ReadObject(reader)
                        ?? throw new GeodeException(
                            "VersionedCacheableObjectPartList.FromData: null key " +
                            "in keys section.");
                    ResultKeys?.Add(key);
                    _tempKeys.Add(key);
                    localKeys.Add(key);
                }
            }
            else if (Keys is not null)
            {
                logger.LogDebug(
                    "VersionedCacheableObjectPartList::fromData: m_keys NOT nullptr");
            }
            else if (hasObjects)
            {
                if (Keys is null && ResultKeys is null)
                {
                    logger.LogError(
                        "VersionedCacheableObjectPartList::fromData: Exception: hasObjects " +
                        "is true and m_keys and m_resultKeys are also nullptr");
                    throw new GeodeException(
                        "VersionedCacheableObjectPartList: " +
                        "hasObjects is true and m_keys is also nullptr");
                }
                logger.LogDebug(
                    "VersionedCacheableObjectPartList::fromData m_keys or m_resultKeys not null");
            }
            else
            {
                logger.LogDebug(
                    "VersionedCacheableObjectPartList::fromData m_hasKeys, m_keys, hasObjects all are nullptr");
            }


            // ─── Step 5: read objects section ──────────────────────
            // cppcache VersionedCacheableObjectPartList.cpp:169-183.
            // Each entry is preceded by a 1-byte miss flag
            // (_byteArray[i]; 0 = present, 2 = exception, 3 = miss on
            // server). Phase 1.3 RemoveAll reply has hasObjects=false
            // (server doesn't echo values, only version tags), so the
            // branch is dead today; GetAll (Phase 1.3.c) will exercise it.
            if (hasObjects)
            {
                var objCount = (int)reader.ReadUnsignedVL();
                _byteArray.Clear();
                for (var i = 0; i < objCount; i++)
                {
                    _byteArray.Add(0);
                }
                for (var index = 0; index < objCount; index++)
                {
                    // cppcache key selection: if caller supplied Keys
                    // (GetAll path) and we didn't read fresh keys in
                    // step 4, index into Keys[KeysOffset + i]; otherwise
                    // use the localKeys we just built in step 4.
                    var key = (Keys is not null && !_hasKeys)
                        ? Keys[index + KeysOffset]
                        : localKeys[index];
                    ReadObjectPart(index, reader, key);
                }
            }


            // ─── Step 6: read version tags section ─────────────────
            // cppcache VersionedCacheableObjectPartList.cpp:185-249.
            // Shared `len` between hasObjects (step 5) and hasTags
            // sections: cppcache declares it at function-top with 0.
            // Phase 1.3 step 5 NIEs before assigning, so len stays 0 if
            // hasObjects was true (won't reach here anyway). When hasTags
            // is true we read a fresh len off the wire.
            var len = 0;

            // MemberListForVersionStamp resolved via DI inside
            // NewVersionTag (Scoped, per-cache — mirrors cppcache
            // CacheImpl::m_memberListForVersionStamp). cppcache passes
            // it positionally to VersionTag::fromData via region-back-ref;
            // .NET resolves it through ActivatorUtilities.CreateInstance
            // so we don't need a local variable here any more.

            if (_hasTags)
            {
                // ReadUnsignedVL still NIE today; surfaces immediately
                // when the server actually ships a versioned RemoveAll
                // reply. Phase 1.3.c (PutAll/GetAll) or Phase 4+ supplies
                // the VL decoder body.
                len = (int)reader.ReadUnsignedVL();

                // cppcache m_versionTags.resize(len): List<T> has no
                // Resize, replicate via Clear + Add-null loop so the
                // index-assignments inside the for-loop are safe.
                _versionTags.Clear();
                for (var i = 0; i < len; i++)
                {
                    _versionTags.Add(null);
                }

                var ids = new List<ushort>();
                for (var index = 0; index < len; index++)
                {
                    var entryType = reader.ReadByte();
                    VersionTag? versionTag = null;
                    switch (entryType)
                    {
                        case FLAG_NULL_TAG:
                            // Null sentinel — leave versionTag = null.
                            break;

                        case FLAG_FULL_TAG:
                            versionTag = NewVersionTag(persistent);
                            versionTag.FromData(reader);
                            versionTag.ReplaceNullMemberId(_endpointMemId);
                            break;

                        case FLAG_TAG_WITH_NEW_ID:
                            versionTag = NewVersionTag(persistent);
                            versionTag.FromData(reader);
                            ids.Add(versionTag.InternalMemId);
                            break;

                        case FLAG_TAG_WITH_NUMBER_ID:
                            versionTag = NewVersionTag(persistent);
                            versionTag.FromData(reader);
                            var idNumber = (int)reader.ReadUnsignedVL();
                            versionTag.InternalMemId = ids[idNumber];
                            break;

                        default:
                            // cppcache default: break (silently drop).
                            break;
                    }
                    _versionTags[index] = versionTag;
                }
            }
            else
            {
                // hasTags=false: null-fill `len` entries (size carried
                // from step 5's hasObjects path). Phase 1.3 RemoveAll has
                // hasObjects=false so len=0 — no-op. cppcache assigns by
                // index assuming pre-sized vector; we Clear+Add to keep
                // it safe under either path.
                _versionTags.Clear();
                for (var index = 0; index < len; index++)
                {
                    _versionTags.Add(null);
                }
            }


            // ─── Step 7: putLocal merge ────────────────────────────
            // cppcache VersionedCacheableObjectPartList.cpp:251-301.
            // For each per-key entry: write value into the client-side
            // local cache (when AddToLocalCache=true) and reconcile any
            // concurrent-modification conflict by demoting to the higher-
            // version old value.
            //
            // Phase 1.3 reality: hasObjects is the gate, and step 5's NIE
            // prevents reaching here at all when hasObjects=true. When
            // hasObjects=false (our actual Phase 1.3 RemoveAll path) this
            // branch is naturally skipped. Body lands with the client-
            // side cache (Phase 4+).
            // Phase 1.3 gate: cppcache walks every entry through Region.PutLocal
            // (caching-enabled side) or Region.GetEntry (concurrent-version
            // reconciliation side) regardless of AddToLocalCache, but every
            // observable mutation funnels through PutLocal which we don't
            // have yet (no client-side cache map). For proxy-mode regions
            // (AddToLocalCache=false, the Phase 1.3 norm) the step is a
            // pure no-op semantically — Values is already filled by step 5
            // and step 7's only job is the cache-side bookkeeping.
            //
            // We gate the NIE on AddToLocalCache so Phase 1.3 GetAll
            // (which always lands here with AddToLocalCache=false because
            // ThinClientRegion ANDs the requested true with the region's
            // caching-enabled attribute, and MVP regions are proxy) skips
            // cleanly. Phase 4+ flips the flag and lands the real merge.
            if (hasObjects && AddToLocalCache)
            {
                // TODO Phase 4+ — needs:
                //   1. Region.PutLocal(name, isCreate, key, value, out oldValue,
                //      isLocalOnly, ref updateCount, destroyTracker, versionTag)
                //   2. Region.GetEntry(key, out oldValue) for the
                //      AddToLocalCache=false branch
                //   3. GfErrType.GF_CACHE_CONCURRENT_MODIFICATION_EXCEPTION
                //      handling — replace Values[key] with oldValue when
                //      the local cache already has a higher version.
                // Sketch:
                //   for (var index = 0; index < len; index++) {
                //       var key = (Keys is not null && !_hasKeys)
                //           ? Keys[index + KeysOffset] : localKeys[index];
                //       var value = Values!.GetValueOrDefault(key);
                //       if (_byteArray[index] == 3) continue;  // miss
                //       if (AddToLocalCache) { ... PutLocal ... }
                //       else                  { ... GetEntry ... }
                //   }
                throw new NotImplementedException(
                    "VersionedCacheableObjectPartList.FromData step 7 (putLocal " +
                    "merge) pending Phase 4+ (client-side caching). " +
                    "Phase 1.3 should never hit this — AddToLocalCache is " +
                    "AND-gated against region.CachingEnabled which is false " +
                    "for proxy-mode MVP regions.");
            }
        } // end lock (_responseLock)
    }

    /// <summary>
    /// Construct the right <see cref="VersionTag"/> subtype for the
    /// current region's persistence mode. Mirrors cppcache's
    /// <c>persistent ? new DiskVersionTag(...) : new VersionTag(...)</c>
    /// dispatch
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.cpp:199-235</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="MemberListForVersionStamp"/> is now resolved through
    /// DI (Scoped, registered in <c>GeodeClientExtensions.AddCore</c>),
    /// not passed positionally — cppcache fetches it from
    /// <c>CacheImpl::m_memberListForVersionStamp</c> per call which is
    /// effectively a per-cache singleton. The earlier "pass null"
    /// shape broke <see cref="ActivatorUtilities.CreateInstance"/>'s
    /// ctor matcher (a runtime-null arg has no type to bind against).
    /// </remarks>
    private VersionTag NewVersionTag(bool persistent)
    {
        return persistent
            ? ActivatorUtilities.CreateInstance<DiskVersionTag>(serviceProvider)
            : ActivatorUtilities.CreateInstance<VersionTag>(serviceProvider);
    }

    /// <summary>
    /// Decode one (miss-flag, value) pair into <see cref="_byteArray"/>
    /// + <see cref="Values"/> / <see cref="Exceptions"/>. Mirrors
    /// cppcache <c>readObjectPart</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.cpp:43-90</c>).
    /// </summary>
    /// <remarks>
    /// Three wire shapes selected by the 1-byte miss flag:
    /// <list type="bullet">
    ///   <item><c>0 / 3</c> &#x2014; normal entry or "key absent on
    ///         server" (3); ordinary object follows.</item>
    ///   <item><c>2</c> &#x2014; server-side exception for this
    ///         key; serialised Java blob + class name string follow.
    ///         Phase 1.3.c stub &#x2014; <see cref="BigEndianBinaryReader.ReadString"/>
    ///         is NIE so this branch surfaces immediately when
    ///         exercised.</item>
    ///   <item><c>_serializeValues == true</c> &#x2014; raw bytes
    ///         (Java-serialised payload preserved opaque); GetAll
    ///         specific.</item>
    /// </list>
    /// </remarks>
    private void ReadObjectPart(int index, BigEndianBinaryReader reader, object key)
    {
        var objType = reader.ReadByte();
        _byteArray[index] = objType;

        if (objType == 2)
        {
            // Exception branch (cppcache lines 50-63). Skip the Java
            // exception serialised blob (length-prefixed array), read
            // the class-name string, attribute it to the key.
            reader.AdvanceCursor(reader.ReadArrayLen());
            var exMsg = reader.ReadString() ?? "<unknown server-side exception>";
            // TODO Phase 3 — NotAuthorizedException specialisation
            //   (cppcache differentiates "org.apache.geode.security.NotAuthorizedException"
            //   to throw NotAuthorizedException vs CacheServerException).
            Exceptions ??= [];
            Exceptions[key] = new GeodeException($"Exception at remote server: {exMsg}");
            return;
        }

        if (_serializeValues)
        {
            // Raw-bytes branch (cppcache lines 64-82). Used by GetAll
            // when the caller asked for un-deserialised payload — we
            // store byte[] directly in Values.
            var skipLen = reader.ReadArrayLen();
            var bytes = skipLen > 0
                ? reader.ReadBytesOnly(skipLen).ToArray()
                : [];
            Values![key] = bytes;
            return;
        }

        // Default branch (cppcache lines 83-89): dispatch through
        // SerializationRegistry. Null result is a legitimate value
        // for "miss" (objType==3) — store as null.
        var value = serializationRegistry.ReadObject(reader);
        Values![key] = value;
    }

    /// <summary>
    /// Merge <paramref name="other"/>'s entries into this instance.
    /// Mirrors cppcache <c>addAll</c>
    /// (<c>cppcache/src/VersionedCacheableObjectPartList.hpp:185-218</c>):
    /// concatenate <c>m_tempKeys</c>, OR-in
    /// <c>m_regionIsVersioned</c>, append <c>m_versionTags</c>.
    /// </summary>
    internal void AddAll(VersionedCacheableObjectPartList other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // ── Merge keys ─────────────────────────────────────────
        // cppcache wraps this in null guards for both sides because
        // its m_tempKeys can be nullptr; our List<object> is
        // non-null by ctor, so a Count check suffices. Setting
        // _hasKeys=true mirrors the LOGDEBUG path cppcache takes
        // when _hasKeys was previously false but keys arrive.
        if (other._tempKeys.Count > 0)
        {
            if (!_hasKeys)
            {
                logger.LogDebug(" VCOPL::addAll m_hasKeys should be true here");
                _hasKeys = true;
            }
            _tempKeys.AddRange(other._tempKeys);
        }

        // ── OR-in region-versioned flag ────────────────────────
        _regionIsVersioned |= other._regionIsVersioned;

        // ── Merge version tags ─────────────────────────────────
        var size = other._versionTags.Count;
        logger.LogDebug(" VCOPL::addAll other->m_versionTags.size() = {Size} ", size);
        if (size > 0)
        {
            _versionTags.AddRange(other._versionTags);
            _hasTags = true;
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
    private readonly Lock _responseLock = new();
}
