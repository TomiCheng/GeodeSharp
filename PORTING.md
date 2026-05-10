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
| `ThinClientRegion` | `Geode.Client.Services.ThinClientRegion<TKey,TValue>` | 2 | ⏳ | 1.2 | |
| `Region` (base) | merged into `IRegion<TKey,TValue>` | 2 | ⏳ | 1.2 | C# unifies abstract base + interface |

### Distribution managers (Phase 1.5)

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `ThinClientBaseDM` | `Geode.Client.Internal.Dm.ThinClientBaseDM` | 2 | ⏳ | 1.5 | Abstract base; chunk queue + lifecycle + auth hooks |
| `ThinClientDistributionManager` | `Geode.Client.Internal.Dm.ThinClientDistributionManager` | 2 | ⏳ | 1.5 | Simple single-endpoint; used by locator path |
| `ThinClientPoolDM` | `Geode.Client.Internal.Dm.ThinClientPoolDM` | 2 | ⏳ | 1.5 | Pool variant; multi-inheritance flattened to composition |
| `ThinClientStickyManager` | `Geode.Client.Internal.Dm.ThinClientStickyManager` | 2 | ⏳ | 6 | `AsyncLocal<T>` instead of TSS |

### Connection / endpoint

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `TcrConnection` | `Geode.Client.Protocol.TcrConnection` | 2 | 🔨 | 1.1 | Frame I/O works; handshake bytes done; `InitializeCoreAsync` not wired yet |
| `Pool` (cppcache `include/geode/Pool.hpp`, public abstract) | `Geode.Client.Internal.IPool` | 2 | 🔨 | 1.5 | Held internal — no MVP consumer use case; lift to public later if monitoring / advanced lifecycle hooks need it. Sole implementor will be `ThinClientPoolDM` |
| `PoolManager` + `PoolManagerImpl` (cppcache abstract + Pimpl body) | `Geode.Client.Internal.PoolManager` | 2 | 🔨 | 1.5 | Pimpl collapsed; no separate `IPoolManager` interface — only one implementor, internal use only |
| `TcrConnectionManager` | `Geode.Client.Internal.TcrConnectionManager` | 2 | 🔨 | 1.5 | Empty shell with TODO + cppcache member notes; will own 3 background tasks + ping `PeriodicTimer` |
| `TcrEndpoint` | `Geode.Client.Internal.TcrEndpoint` | 2 | ⏳ | 1.5 | per-server state |
| `TcrPoolEndPoint` | `Geode.Client.Internal.TcrPoolEndPoint` | 2 | ⏳ | 1.5 | endpoint variant for pool mode |
| `ConnectionQueue<T>` | (wrapper over `Channel<T>`) | 3 | ⏳ | 1.5 | thin wrapper that adds timed-get-or-create |
| `ThinClientLocatorHelper` | `Geode.Client.Internal.ThinClientLocatorHelper` | 2 | ⏳ | 1.5 | locator wire protocol |

### Wire protocol primitives

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `TcrMessage` | `Geode.Client.Protocol.TcrMessage` | 2 | ✅ | 1.1 | unit tested |
| `TcrMessageReply` | merged into `TcrMessage` | 2 | ✅ | 1.1 | C# uses one class for both directions |
| (request builders, partial files in cppcache) | `Geode.Client.Protocol.TcrMessageBuilder` (+ `.Get` / `.Put` / `.Ping` partials) | 2 | ✅ | 1.1 | unit tested |
| `TcrPart` | `Geode.Client.Protocol.TcrPart` | 2 | ✅ | 1.1 | unit tested |
| (part builder) | `Geode.Client.Protocol.TcrPartBuilder` | 2 | ✅ | 1.1 | unit tested |
| `MessageType` enum | `Geode.Client.Protocol.MessageType` | 2 | ✅ | 1.1 | full enum with upstream gaps preserved |
| `DSCode` | `Geode.Client.Protocol.DSCode` | 2 | ✅ | 1.1 | |
| `ProtocolVersion` | `Geode.Client.Protocol.ProtocolVersion` | 2 | ✅ | 1.1 | |
| `ClientProxyMembershipID` | `Geode.Client.Protocol.ClientProxyMembershipIdBuilder` | 2 | ✅ | 1.1 | unit tested |
| big-endian byte I/O macros / helpers | `BigEndianBinaryReader` / `BigEndianBinaryWriter` | 2 | ✅ | 1.1 | unit tested |

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
| `Xerces-C` (cache.xml parser) | cut entirely | per Configuration policy |
| `apache::geode::client::Properties` | `IDictionary<string, string>` | |

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
