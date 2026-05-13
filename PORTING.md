# C++ ↔ C# class mapping

> Mapping between cppcache classes and the C# port. Each row records
> the porting bucket (see [CLAUDE.md](CLAUDE.md) "Three-bucket porting
> rule"), the C# visibility (public API surface vs internal
> implementation), and the implementation status.
>
> **This is a living document.** Add a row whenever you encounter a
> new cppcache class while working on a feature. Update the status
> column when the implementation moves forward.

## Status legend

| Symbol | Meaning |
| --- | --- |
| ✅ | Implemented (skeleton + body) |
| 🔨 | Skeleton only (interface declared, body throws / empty) |
| ⏳ | Planned for a future phase, not yet stubbed |
| 🚫 | Bucket 1 — BCL covers it, will not be ported |
| ❌ | Out of scope (cut from MVP / not implemented) |

## Visibility legend

| Symbol | Meaning |
| --- | --- |
| 🌐 | **Public** — part of `Geode.Client` public API surface (corresponds to cppcache `clicache/`) |
| 🔒 | **Internal** — implementation detail (`internal` modifier; corresponds to cppcache `cppcache/src/`) |
| — | N/A (bucket 1 / 3 wrapper / not a class) |

---

## 1. Public API surface 🌐 (corresponds to cppcache `clicache/`)

These are the types a consumer of the NuGet package can `using`. Names
follow the cppcache `clicache/` C++/CLI managed wrapper where one
exists; they are translated, not ported.

| cppcache (clicache) | C# | Status | Phase | Notes |
| --- | --- | --- | --- | --- |
| `RegionService` (top abstract) | `Geode.Client.IRegionService` | 🔨 | 0 | Lifecycle surface only today (`IsClosed` / `CloseAsync` / `IAsyncDisposable`); region/query/PDX methods land in 1.2 / 1.4 / 2 |
| `GeodeCache` (mid abstract) | `Geode.Client.IGeodeCache : IRegionService` | 🔨 | 0 | Adds `Name` + `EnsureInitializedAsync`; PDX config accessors land in Phase 2 |
| `Cache` (concrete) | _no separate public interface_; `Geode.Client.Services.Cache` is the impl (see §2) | 🔨 | 1.x | cppcache `Cache` adds `createRegionFactory` / `getCacheTransactionManager` / `getPoolManager` / `createAuthenticatedView` etc. — most live on `IGeodeCache` directly when their phase ships; revisit splitting into a separate "ICache" interface only if multi-user (Phase 3) requires it |
| `Apache::Geode::Client::IRegion<TKey,TValue>` | `Geode.Client.IRegion<TKey,TValue>` | 🔨 | 1.2 | Empty marker; methods land in 1.2 |
| `Apache::Geode::Client::IQueryService` | `Geode.Client.IQueryService` | 🔨 | 1.4 | Empty marker; `NewQuery<T>` in 1.4 |
| `Apache::Geode::Client::IQuery<T>` | `Geode.Client.IQuery<T>` | 🔨 | 1.4 | Empty marker; `ExecuteAsync` in 1.4 |
| `PoolFactory` | _undecided_ | ⏳ | 1.5 | Decided: `PoolManager.createFactory()` is **not** ported — pools are not built off the manager. Undecided: whether a separate `PoolFactory` type is needed at all. Pool construction may go through DI / `AddGeodeClient`, but final shape pending. |
| `Apache::Geode::Client::CacheFactory` | `Geode.Client.IGeodeCacheFactory` | ✅ | 0 | Same role (gateway to `Cache` instances), not the same mechanics — see *CacheFactory ↔ IGeodeCacheFactory* note below |
| `Apache::Geode::Client::GeodeException` | `Geode.Client.GeodeException` | ✅ | 0 | |
| `cache.xml` configuration | `Geode.Client.Options.GeodeClientOptions` + sub-options | ✅ | 0 | mirror-then-prune; see `Options/` folder |
| _additional clicache types to be enumerated as we encounter them_ | | ⏳ | | TODO: full sweep of `D:\github\geode-native\clicache\src\` |

### Note: `CacheFactory` ↔ `IGeodeCacheFactory`

Same role (the public entry point that produces / hands out `Cache`
instances) but the mechanics differ — this is a "translate +
modernise" mapping (per CLAUDE.md), not a literal port.

| Aspect | cppcache `CacheFactory` | C# `IGeodeCacheFactory` |
| --- | --- | --- |
| **Pattern** | Fluent builder | DI-resolved factory |
| **Construction** | `CacheFactory()` / `CacheFactory(props)` + chained `set(k, v)` | `services.AddGeodeClient(...)` at composition root |
| **Resolution** | `factory.create()` returns a fresh `Cache` | `factory.Get(name)` looks up the cache registered under that name |
| **Lifetime** | Caller owns the returned `Cache` | DI container owns; resolved instances are singletons-per-name |
| **Number of caches** | One per `create()` call; no built-in registry | Multiple named caches in one process; registry keyed by name |
| **Configuration source** | `Properties` bag (typically loaded from `.ini`) | `IConfiguration` / `IOptions<GeodeClientOptions>` |

cppcache supports multiple `Cache` instances ([CacheFactory.cpp:65](https://github.com/apache/geode-native/blob/develop/cppcache/src/CacheFactory.cpp) constructs a fresh one per call; nothing is `static`). It just doesn't ship a registry — callers track instances themselves. The C# port adds the registry layer because DI named-options is the .NET-idiomatic way to expose multiple cluster connections from one app.

## 2. Internal implementation 🔒 (corresponds to cppcache `cppcache/src/`)

These are `internal sealed` (or `internal abstract`) classes. Names
mirror cppcache file-for-file unless explicitly noted, per the
"Three-bucket porting rule" bucket 2.

### Cache & region core

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `Cache` (façade) + `CacheImpl` (Pimpl body) | `Geode.Client.Services.Cache` (single class, implements public `IGeodeCache`) | 2 | 🔨 | 1.1 | cppcache's Pimpl split (`Cache` → `m_cacheImpl`) is collapsed — .NET doesn't need the binary-compatibility shim. `InitializeCoreAsync` is the next entry point |
| (DI factory layer) | `Geode.Client.Services.GeodeCacheFactory` | — | ✅ | 0 | New, no cppcache analogue |
| `ThinClientRegion` | `Geode.Client.Services.ThinClientRegion` (non-generic) | 2 | ✅ | 1.2–1.3.c | All bulk + single-key ops end-to-end (Put / Get / Remove / ContainsKey / Clear / Invalidate / RemoveAll / PutAll / GetAll). Stays non-generic to mirror cppcache native; typed surface goes through `RegionView` wrapper. Sub-region path / caching-enabled local map deferred (Phase 2+) |
| `LocalRegion` | `Geode.Client.Internal.LocalRegion` (abstract) | 2 | 🔨 | 1.2 | Empty placeholder layer; just holds Name / FullPath / Parent. Local-cache machinery (`m_entries` / listener / writer / loader) deferred to Phase 2+ when `caching-enabled` is honoured |
| `RegionInternal` | `Geode.Client.Internal.RegionInternal` (abstract) | 2 | 🔨 | 1.2 | Empty placeholder layer; holds `Attributes` and forwards `PoolName`. Internal-only API surface (EventId-aware ops, version stamps, tombstones) deferred to Phase 2+ |
| `Region` (base) | `Geode.Client.IRegion` (non-generic) + `Geode.Client.IRegion<TKey,TValue>` (typed overlay) | 2 | 🔨 | 1.2 | Non-generic interface holds the real op surface (`object` keys / values); typed interface is overload-only sugar |
| (no cppcache analogue) | `Geode.Client.Services.RegionView<TKey,TValue>` | — | ✅ | 1.2 | Compile-time-only typed wrapper; new instance per `Cache.GetRegion<K,V>(name)` call. cppcache splits typed/untyped across native + clicache layers; C# folds both into one |

### Distribution managers (Phase 1.5)

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `ThinClientBaseDM` | `Geode.Client.Internal.ThinClientBaseDM` | 2 | 🔨 | 1.5 | Abstract base shell: lifecycle, chunk Channel, security hooks (default empty), pure-abstract `SendSyncRequestAsync` / `SendRequestToEndpointAsync` |
| `ThinClientDistributionManager` | `Geode.Client.Internal.ThinClientDistributionManager` | 2 | ⏳ | 1.5 | Simple single-endpoint; used by locator path |
| `ThinClientPoolDM` | `Geode.Client.Internal.ThinClientPoolDM` | 2 | 🔨 | 1.5 | Pool variant shell: inherits `ThinClientBaseDM`, implements `IPool`. Field placeholders for endpoint registry, connection queue, three background workers, locator helper, redundancy / sticky / metadata managers. Method prototypes throw NotImplementedException |
| `ThinClientStickyManager` | `Geode.Client.Internal.Dm.ThinClientStickyManager` | 2 | ⏳ | 6 | `AsyncLocal<T>` instead of TSS |

### Connection / endpoint

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `TcrConnection` | `Geode.Client.Protocol.TcrConnection` | 2 | 🔨 | 1.1 | Frame I/O works; handshake bytes done; `InitializeCoreAsync` not wired yet |
| `Pool` (cppcache `include/geode/Pool.hpp`, public abstract) | `Geode.Client.Internal.IPool` | 2 | 🔨 | 1.5 | Held internal — no MVP consumer use case; lift to public later if monitoring / advanced lifecycle hooks need it. Sole implementor will be `ThinClientPoolDM` |
| `PoolManager` + `PoolManagerImpl` (cppcache abstract + Pimpl body) | `Geode.Client.Internal.PoolManager` | 2 | 🔨 | 1.5 | Pimpl collapsed; no separate `IPoolManager` interface — only one implementor, internal use only |
| `TcrConnectionManager` | `Geode.Client.Internal.TcrConnectionManager` | 2 | 🔨 | 1.5 | Empty shell with TODO + cppcache member notes; will own 3 background tasks + ping `PeriodicTimer` |
| `TcrEndpoint` | `Geode.Client.Internal.TcrEndpoint` | 2 | 🔨 | 1.5 | Per-server state shell: per-endpoint conn pool, health flags, auth token, subscription receiver placeholders. Method prototypes throw NotImplementedException |
| `TcrPoolEndPoint` | `Geode.Client.Internal.TcrPoolEndPoint` | 2 | ⏳ | 1.5 | endpoint variant for pool mode |
| `ConnectionQueue<T>` | (wrapper over `Channel<T>`) | 3 | ⏳ | 1.5 | thin wrapper that adds timed-get-or-create |
| `ThinClientLocatorHelper` | `Geode.Client.Internal.ThinClientLocatorHelper` | 2 | ⏳ | 1.5 | locator wire protocol |

### Wire protocol primitives

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `TcrMessage` | `Geode.Client.Protocol.TcrMessage` | 2 | ✅ | 1.1 | unit tested |
| `TcrMessageReply` | merged into `TcrMessage` | 2 | ✅ | 1.1 | C# uses one class for both directions |
| (request builders, partial files in cppcache) | `Geode.Client.Protocol.TcrMessageBuilder` (+ `.Get` / `.Put` / `.Ping` / `.ContainsKey` / `.Destroy` / `.ClearRegion` / `.Invalidate` / `.RemoveAll` / `.PutAll` / `.GetAll` / `.CloseConnection` partials) | 2 | ✅ | 1.1–1.3.c | unit tested; new partials track sub-phases |
| `TcrPart` | `Geode.Client.Protocol.TcrPart` | 2 | ✅ | 1.1 | unit tested |
| (part builder) | `Geode.Client.Protocol.TcrPartBuilder` | 2 | ✅ | 1.1 | unit tested |
| `MessageType` enum | `Geode.Client.Protocol.MessageType` | 2 | ✅ | 1.1 | full enum with upstream gaps preserved |
| `DSCode` | `Geode.Client.Protocol.DSCode` | 2 | ✅ | 1.1 | |
| `ProtocolVersion` | `Geode.Client.Protocol.ProtocolVersion` | 2 | ✅ | 1.1 | |
| `ClientProxyMembershipID` (builder) | `Geode.Client.Protocol.ClientProxyMembershipIdBuilder` | 2 | ✅ | 1.1 | unit tested |
| `ClientProxyMembershipID` (decoder used by VersionTag) | `Geode.Client.Protocol.ClientProxyMembershipID` | 2 | ✅ | 1.3.b | `ReadEssentialData` decoder; primary ctor takes `SerializationRegistry` |
| big-endian byte I/O macros / helpers | `BigEndianBinaryReader` / `BigEndianBinaryWriter` | 2 | ✅ | 1.1 | unit tested |

### Chunked reply / version tags (Phase 1.3.b + 1.3.c)

Bulk ops (`RemoveAll` / `PutAll` / `GetAll70`) ship their reply over multiple wire chunks; these types decode that stream.

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `TcrChunkedResult` | `Geode.Client.Protocol.TcrChunkedResult` (abstract) | 2 | ✅ | 1.3.b | `HandleChunk(payload, isLastChunk)` + `Reset()`; cppcache `finalize` / `binary_semaphore` / `m_ex` / `m_dsmemId` collapsed (Task/await + natural exception propagation) |
| `TcrMessageHelper` | `Geode.Client.Protocol.TcrMessageHelper` | 2 | ✅ | 1.3.b | `ReadChunkPartHeader` classifies a chunk into NullObject / Object / Exception / Bytes |
| `ChunkObjectType` | `Geode.Client.Protocol.TcrMessageHelper.ChunkObjectType` enum | 2 | ✅ | 1.3.b | NullObject / Object / Exception / Bytes |
| `ChunkedRemoveAllResponse` | `Geode.Client.Services.ChunkedRemoveAllResponse` | 2 | ✅ | 1.3.b | only accumulates version tags (Phase 1.3 drops them); 5-step HandleChunk |
| `ChunkedPutAllResponse` | `Geode.Client.Services.ChunkedPutAllResponse` | 2 | ✅ | 1.3.c | structurally identical to RemoveAll; log strings differ |
| `ChunkedGetAllResponse` | `Geode.Client.Services.ChunkedGetAllResponse` | 2 | ✅ | 1.3.c | extra ctor params: caller's `IReadOnlyList<object> keys` (positional reverse-lookup) + `bool addToLocalCache`; `Values` accumulator surfaces as `IReadOnlyDictionary<object, object?>`; no NullObject / Bytes branches (cppcache GetAll is Object-or-Exception only) |
| `CacheableObjectPartList` | `Geode.Client.Protocol.CacheableObjectPartList` | 2 | 🔨 | 1.3.b | base class — fields only; full decoder lives on `VersionedCacheableObjectPartList` |
| `VersionedCacheableObjectPartList` | `Geode.Client.Protocol.VersionedCacheableObjectPartList` | 2 | ✅ | 1.3.b–1.3.c | 7-step `FromData` decoder; 1.3.c added `Initialize(keys, keysOffset, values, exceptions?, resultKeys?, addToLocalCache)` + `ConsumedObjectCount` accessor for GetAll's shared-accumulator pattern; Step 7 (`putLocal` merge) NIE gated on `AddToLocalCache` (Phase 4+) |
| `VersionTag` | `Geode.Client.Protocol.VersionTag` | 2 | ✅ | 1.3.b | 8-step `FromData` + 2-step `ReadMembers`; primary ctor `(IServiceProvider, ILogger, MemberListForVersionStamp)`; Phase 1.3.c: `MemberListForVersionStamp` now DI-resolved (not positional) |
| `DiskVersionTag` | `Geode.Client.Protocol.DiskVersionTag` | 2 | 🔨 | 1.3.b | inherits `VersionTag`; `ReadMembers` override NIE — persistent regions only (Phase 4+) |
| `MemberListForVersionStamp` | `Geode.Client.Protocol.MemberListForVersionStamp` | 2 | ✅ | 1.3.b–1.3.c | Scoped DI registration added 1.3.c (mirrors cppcache `CacheImpl::m_memberListForVersionStamp` instance scope); hashKey dedup deferred Phase 4 |
| `DSFid` enum | `Geode.Client.Protocol.DSFid` | 2 | ✅ | 1.3.b | 25 entries; `VersionedObjectPartList = 7` / `DiskVersionTag = 2131` etc. |

### DSCode coverage (built-in type-code catalogue)

Every value the wire's <code>SerializationRegistry</code> dispatch can
encounter, sorted by DSCode number. "Status" = `✅` registered today,
`⏳` planned/deferred, `❌` won't port (wire-internal or
rarely-used Java type). Phase column matches PROGRESS.md.

#### Done — built-in scalars / strings / bytes / arrays / collections

| DSCode | cppcache | CLR | Status | Phase | Notes |
|---:|---|---|:---:|---|---|
| 10 | `CacheableLinkedList` | `LinkedList<T>` | ✅ | 1.3.0 Tier B-2 | wire identical to ArrayList; own adapter branch (not `IList<T>`) |
| 26 | `BooleanArray` | `bool[]` | ✅ | 1.3.0 Tier B-1 | |
| 27 | `CharArray` | `char[]` | ✅ | 1.3.0 Tier B-1 | u16 BE per element (Java `char[]`, not UTF-8) |
| 41 | `NullObj` | `null` | ✅ | 1.2 | inlined in registry (no standalone converter) |
| 42 | `CacheableString` | `string` | ✅ | 1.3.0 Tier A | non-ASCII short; modified UTF-8 (one `StringDataConverter` covers 42/87/88/89) |
| 46 | `CacheableBytes` | `byte[]` | ✅ | 1.3.0 Tier A | VL length + raw bytes; not a valid `TKey` |
| 47 | `CacheableInt16Array` | `short[]` | ✅ | 1.3.0 Tier B-1 | |
| 48 | `CacheableInt32Array` | `int[]` | ✅ | 1.3.0 Tier B-1 | VL boundary unit tests live here, shared with sibling arrays |
| 49 | `CacheableInt64Array` | `long[]` | ✅ | 1.3.0 Tier B-1 | |
| 50 | `CacheableFloatArray` | `float[]` | ✅ | 1.3.0 Tier B-1 | NaN / ±Infinity bit-pattern preserved |
| 51 | `CacheableDoubleArray` | `double[]` | ✅ | 1.3.0 Tier B-1 | |
| 52 | `CacheableObjectArray` | `object[]` | ✅ | 1.3.0 Tier B-2 | hard-coded `"java.lang.Object"` class header; per-element re-entry |
| 53 | `CacheableBoolean` | `bool` | ✅ | 1.2 | walking-skeleton converter |
| 54 | `CacheableCharacter` | `char` | ✅ | 1.3.0 Tier A | UTF-16 code unit, 2-byte BE |
| 55 | `CacheableByte` | `byte` | ✅ | 1.3.0 Tier A | unsigned (.NET convention); wire bit-pattern interop with Java signed byte |
| 56 | `CacheableInt16` | `short` | ✅ | 1.3.0 Tier A | |
| 57 | `CacheableInt32` | `int` | ✅ | 1.2 | walking-skeleton converter |
| 58 | `CacheableInt64` | `long` | ✅ | 1.3.0 Tier A | |
| 59 | `CacheableFloat` | `float` | ✅ | 1.3.0 Tier A | IEEE-754 BE; NaN / ±∞ shape == Java |
| 60 | `CacheableDouble` | `double` | ✅ | 1.3.0 Tier A | IEEE-754 BE |
| 61 | `CacheableDate` | `DateTime` | ✅ | 1.3.0 Tier A | 8-byte ms-since-epoch UTC; Read → `Kind=Utc`; Write rejects `Unspecified` |
| 64 | `CacheableStringArray` | `string[]` | ✅ | 1.3.0 Tier B-1 | registry-injected; per-element 42/87/88/89/41 dispatch |
| 65 | `CacheableArrayList` | `List<T>` / `IList<T>` | ✅ | 1.3.0 Tier B-2 | brought `TypedResultAdapter` + open-generic write fallback |
| 66 | `CacheableHashSet` | `HashSet<T>` / `ISet<T>` | ✅ | 1.3.0 Tier B-2 | canonical decode `HashSet<object?>`; null elements travel as DSCode 41 |
| 67 | `CacheableHashMap` | `Dictionary<K,V>` / `IDictionary<K,V>` | ✅ | 1.3.0 Tier B-2 | key/value **interleaved** on wire; null key rejected on read (Java HashMap allows, .NET Dictionary doesn't) |
| 69 | `CacheableNullString` | `null` | ✅ | 1.3.0 | read-only null sentinel; handled by `StringDataConverter` |
| 74 | `CacheableStack` | `Stack<T>` | ✅ | 1.3.0 Tier B-2 | **write reverses** to bottom-to-top wire order; adapter re-reverses on the way out |
| 87 | `CacheableASCIIString` | `string` | ✅ | 1.3.0 Tier A | ASCII, u16 length; via `StringDataConverter` |
| 88 | `CacheableASCIIStringHuge` | `string` | ✅ | 1.3.0 Tier A | ASCII, i32 length |
| 89 | `CacheableStringHuge` | `string` | ✅ | 1.3.0 Tier A | non-ASCII huge — switches to **UTF-16 BE** (not modified UTF-8); cppcache parity |

#### Deferred — clean target exists, awaiting demand or design

| DSCode | cppcache | CLR | Status | Phase | Notes |
|---:|---|---|:---:|---|---|
| 71 | `CacheableVector` | — | ⏳ | — | Java legacy thread-safe ArrayList; no clean .NET equivalent (forcing `List<T>` would clash with `CacheableArrayList`); revisit if real demand |
| 73 | `CacheableLinkedHashSet` | — | ⏳ | — | .NET lacks an insertion-ordered Set; proper mapping needs a new public type (e.g. `Geode.Client.Collections.OrderedSet<T>`) — public API decision, not wire work |

#### Planned future phases

| DSCode | cppcache | CLR | Status | Phase | Notes |
|---:|---|---|:---:|---|---|
| 11 | `Properties` | `IDictionary<string,string>` | ⏳ | 3 | auth-properties payload (handshake credentials etc.) |
| 17 | `PdxType` | `Geode.Client.Pdx.PdxType` | ⏳ | 2 | PDX type metadata |
| 37 | `CacheableUserData4` | (user `DataSerializable` class) | ⏳ | 2+ | superseded by PDX; only port if a real workload still ships DataSerializable |
| 38 | `CacheableUserData2` | same | ⏳ | 2+ | |
| 39 | `CacheableUserData` | same | ⏳ | 2+ | |
| 93 | `PDX` | user PDX-serialised class | ⏳ | 2 | the main custom-object path |
| 94 | `PdxEnum` | enum | ⏳ | 2 | PDX-encoded enum |

#### Won't port

| DSCode | cppcache | Reason |
|---:|---|---|
| 0 | `FixedIDDefault` | wire-layer internal — used as a prefix when serialising `DataSerializableFixedId` objects (EventId / ClientProxyMembershipId / VersionTag / …). NOT a top-level type registered in `SerializationRegistry`; handled inline by the wire builders |
| 1 | `FixedIDByte` | same family |
| 2 | `FixedIDShort` | same family |
| 3 | `FixedIDInt` | same family |
| 4 | `FixedIDNone` | same family |
| 43 | `Class` | sub-marker only — appears inside `CacheableObjectArray`'s class-header bytes (`Class` + the literal `"java.lang.Object"` string); never seen as a top-level Part payload |
| 44 | `JavaSerializable` | Java's native `Serializable` over Geode wire; almost never used in modern deployments; revisit only if a workload requires it |
| 45 | `DataSerializable` | older Geode-specific custom-serialisation; superseded by PDX; same revisit rule as `JavaSerializable` |
| 63 | `CacheableFileName` | rarely used Java type; skip until a workload appears |
| 68 | `CacheableTimeUnit` | rarely used Java enum; skip until a workload appears |
| 70 | `CacheableHashTable` | Java legacy synchronized `Hashtable`; same situation as `Vector` (no clean .NET map + nobody uses it) |
| 72 | `CacheableIdentityHashMap` | identity-equals map; niche on Java side; skip until a workload appears |

### Serialisation (Phase 2 PDX)

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `Cacheable` / `Serializable` family | `IDataSerializable` | 3 | ⏳ | 2 | Wire format ≠ `ISerializable`; thin contract |
| `PdxType` | `Geode.Client.Pdx.PdxType` | 2 | ⏳ | 2 | |
| `PdxTypeRegistry` | `Geode.Client.Pdx.PdxTypeRegistry` | 2 | ⏳ | 2 | |
| `PdxInstance` | `Geode.Client.Pdx.IPdxInstance` | 2 | ⏳ | 2 | |
| `CacheableString` / `CacheableBytes` etc. | (none) | 1 | 🚫 | — | `string` / `byte[]` direct; codec handles DSCode |

### Single-hop / partition routing (Phase 4)

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `ClientMetadataService` | `Geode.Client.Internal.ClientMetadataService` | 2 | ⏳ | 4 | |
| `BucketServerLocation` | `Geode.Client.Internal.BucketServerLocation` (record) | 2 | ⏳ | 4 | |
| `ServerLocation` | `Geode.Client.Internal.ServerLocation` (record) | 3 | ⏳ | 1.5 | direct record, no wrapper |

### Statistics / observability

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `Statistics` framework | `System.Diagnostics.Metrics.Meter` | 1 | 🚫 | — | |
| `PoolStats` | thin wrapper that registers cppcache-named counters into a `Meter` | 3 | ⏳ | 1.5 | |
| `LoggingMacros` / `LOGFINE` | `Microsoft.Extensions.Logging.ILogger` | 1 | 🚫 | — | |

### Bucket 1 — BCL replacements (no port needed)

| cppcache | .NET / BCL replacement | Notes |
| --- | --- | --- |
| `boost::asio::tcp::socket` | `System.Net.Sockets.Socket` / `NetworkStream` | |
| `boost::asio::ssl::stream` | `System.Net.Security.SslStream` | |
| `boost::asio::io_context` + workers | `Task` + `async`/`await` | |
| `std::thread` / `boost::thread` | `Task.Run` | |
| `std::mutex` / `recursive_mutex` | `lock` / `SemaphoreSlim` | |
| `std::condition_variable` | `Channel<T>` / `SemaphoreSlim` | |
| `std::atomic<T>` | `Interlocked` | |
| `std::shared_ptr<T>` | GC | |
| `std::chrono::duration` | `TimeSpan` | |
| `ExpiryTaskManager` + `FunctionExpiryTask` | `PeriodicTimer` | |
| cppcache internal `Task<T>` worker class | `Task.Run` + cancellable loop | name collides with BCL; the cppcache class is internal |
| `LoggingMacros` / `LOGFINE` etc. | `Microsoft.Extensions.Logging.ILogger` | also cross-listed under §Statistics / observability |
| `Statistics` framework | `System.Diagnostics.Metrics.Meter` / EventCounters | also cross-listed under §Statistics / observability |
| `Xerces-C` (cache.xml parser) | cut entirely | per Configuration policy |
| `apache::geode::client::Properties` | `IDictionary<string, string>` | |

### Bucket 3 — thin wrappers (BCL covers most, wrap the gap)

cppcache classes where the BCL has the engine but is missing some
semantics. Wrap **only enough** to add the missing bit; do not
rebuild the whole cppcache class. Domain sections above hold the
per-class status / phase rows; this table is the design-decision
view (what BCL is missing + wrap strategy).

| cppcache | What BCL is missing | Wrap strategy |
| --- | --- | --- |
| `ConnectionQueue<T>` (FIFO + condvar + size cap + timed get) | `Channel<T>` lacks "wait up to T then create new" | thin wrapper around `Channel<T>` exposing `TryGetWithTimeoutAsync` |
| `synchronized_map<K,V>` | `ConcurrentDictionary` has no iterate-with-lock | **don't wrap** — use `ConcurrentDictionary` + snapshot where needed |
| `Cacheable` / `Serializable` family | `ISerializable` doesn't match PDX wire format | introduce `IDataSerializable` interface (Phase 2) |
| `PoolStats` (named counters + sampler) | `Meter` naming / sampling differs | thin wrapper that registers cppcache-named counters into a `Meter` |
| `CacheableString` / `CacheableBytes` | `string` / `byte[]` already exist | **don't wrap** — handle DSCode tag in the codec only |
| `ServerLocation` (host + port + version) | nothing equivalent | **don't wrap** — define a record `ServerLocation(...)` directly |

---

## How to use this file

- **Before coding a new cppcache class**: add a row in the right
  section, mark its bucket and status (usually 🔨 or ⏳), pick a
  visibility (🌐 / 🔒).
- **When status changes**: flip the symbol, optionally bump notes.
- **When a row turns out to be bucket 1**: leave the row, change
  status to 🚫, and move to the bottom bucket-1 table for the
  archaeology trail.
- **Phase column**: matches PROGRESS.md phase numbers.
