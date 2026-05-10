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
| `Apache::Geode::Client::IGeodeCache` | `Geode.Client.IGeodeCache` | ✅ | 0 | Async + `IAsyncDisposable` |
| `Apache::Geode::Client::IRegion<TKey,TValue>` | `Geode.Client.IRegion<TKey,TValue>` | 🔨 | 1.2 | Empty marker; methods land in 1.2 |
| `Apache::Geode::Client::IQueryService` | `Geode.Client.IQueryService` | 🔨 | 1.4 | Empty marker; `NewQuery<T>` in 1.4 |
| `Apache::Geode::Client::IQuery<T>` | `Geode.Client.IQuery<T>` | 🔨 | 1.4 | Empty marker; `ExecuteAsync` in 1.4 |
| `Apache::Geode::Client::CacheFactory` (static factory) | `Geode.Client.IGeodeCacheFactory` + `AddGeodeClient` DI ext | ✅ | 0 | Replaced static factory with DI |
| `Apache::Geode::Client::GeodeException` | `Geode.Client.GeodeException` | ✅ | 0 | |
| `Apache::Geode::Client::Cache` (concrete) | (no public concrete) | 🚫 | — | Hidden behind `IGeodeCache` |
| `cache.xml` configuration | `Geode.Client.Options.GeodeClientOptions` + sub-options | ✅ | 0 | mirror-then-prune; see `Options/` folder |
| _additional clicache types to be enumerated as we encounter them_ | | ⏳ | | TODO: full sweep of `D:\github\geode-native\clicache\src\` |

## 2. Internal implementation 🔒 (corresponds to cppcache `cppcache/src/`)

These are `internal sealed` (or `internal abstract`) classes. Names
mirror cppcache file-for-file unless explicitly noted, per the
"Three-bucket porting rule" bucket 2.

### Cache & region core

| cppcache | C# | Bucket | Status | Phase | Notes |
| --- | --- | --- | --- | --- | --- |
| `CacheImpl` | `Geode.Client.Services.GeodeCache` | 2 | 🔨 | 1.1 | `InitializeCoreAsync` is the next entry point |
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
| `TcrConnectionManager` | `Geode.Client.Internal.TcrConnectionManager` | 2 | ⏳ | 1.5 | |
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
