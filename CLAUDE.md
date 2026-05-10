# GeodeSharp — Project Context

> This file is Claude Code's long-term project memory. Read it once at
> the start of every session, confirm the current phase, then start
> work.
>
> **This is a living document.** Update it at the end of each phase
> with what was learned; phase boundaries are deliberately fuzzy and
> may be adjusted as needed.

---

## One-line goal

Build a **pure-managed, zero-runtime-dependency, cross-platform** Apache
Geode client targeting **.NET 10 (LTS)** and ship it on NuGet.

Upstream reference: <https://github.com/apache/geode-native>
(We do **not** port the C++/CLI `clicache/` — too restricted and
Windows-only.)

Project naming (two layers, deliberately separate):
- GitHub repo / local folder: `GeodeSharp` (https://github.com/TomiCheng/GeodeSharp)
- Solution: `geode-dotnet.sln`
- NuGet PackageId / Root namespace / AssemblyName: `Geode.Client`
- Source folder: `src/Geode.Client/`; tests `tests/Geode.Client.Tests/`
  and `tests/Geode.Client.IntegrationTests/`; sample
  `samples/Geode.Client.Sample/`

"GeodeSharp" is the project / repo name (the human-facing identifier);
the assembly layer uses `Geode.Client` (consistent with industry
convention: the .NET client for Apache Geode). Code, `using`
directives, and `<PackageReference>` entries always use `Geode.Client`;
GeodeSharp is reserved for talking about the project itself.

---

## Architectural decisions (settled — do not relitigate)

### Why not the alternatives

- **Route A (port C++/CLI to .NET 10):** rejected. Microsoft has stated
  C++/CLI on .NET Core is supported for compatibility only, with no
  future investment, Windows-only, no AOT, no SDK-style projects.
- **Route B1 (keep native cppcache, add a P/Invoke wrapper):** rejected.
  Forces us to maintain native binaries per RID, loses the "pure
  managed" benefit, and the C ABI shim is a project of its own.
- **Route B2 (pure managed, speak the wire protocol ourselves):**
  ✅ **adopted.**

### B2's trade-offs and how we cope

The Geode wire protocol has **no normative spec** (Apache's own wiki
admits this). It has to be reverse-engineered from `cppcache/src/` and
Java `geode-core`.

**Mitigation 1:** treat cppcache as the "executable spec" — read it
rather than designing the protocol from scratch.
**Mitigation 2:** scope features in phases. Ship the MVP first, then
fill out the rest incrementally.

### Port + modernise

cppcache `clicache/` has already validated all the interface shapes,
naming, and semantics. Our work is "translate + modernise", not
"design from scratch":

- **Keep:** type names (`IRegion`, `IGeodeCache`, `IQueryService`),
  method names (Put / Get / Remove), core concepts (Region, Pool,
  QueryService).
- **Modernise:** sync → async, `gcnew` → record/class, cache.xml →
  `IOptions<T>`, static factory → DI.

### Three-bucket porting rule

For every cppcache class we encounter, decide which bucket it falls
into and act accordingly. When in doubt, default to **bucket 2**
(mirror) — same logic as the "mirror then prune" config policy.

The actual class-by-class mapping (cppcache name → C# name, bucket,
visibility, status, phase) lives in [PORTING.md](PORTING.md). Add a
row whenever you encounter a new cppcache class.

#### Bucket 1: BCL fully covers it → **don't implement**

cppcache built these because C++ standard / boost gave them the
primitives but not the abstraction. .NET has the abstraction
out-of-the-box. Use the BCL type directly; do not port the cppcache
class.

| cppcache | .NET / BCL replacement                              |
| -------- | --------------------------------------------------- |
| `boost::asio::tcp::socket`            | `System.Net.Sockets.Socket` / `NetworkStream` |
| `boost::asio::ssl::stream`            | `System.Net.Security.SslStream`               |
| `boost::asio::io_context` + workers   | `Task` + `async`/`await`                      |
| `std::thread` / `boost::thread`       | `Task.Run`                                    |
| `std::mutex` / `std::recursive_mutex` | `lock` / `SemaphoreSlim`                      |
| `std::condition_variable`             | `Channel<T>` / `SemaphoreSlim`                |
| `std::atomic<T>`                      | `Interlocked`                                 |
| `std::shared_ptr<T>`                  | GC                                            |
| `std::chrono::duration`               | `TimeSpan`                                    |
| `ExpiryTaskManager` + `FunctionExpiryTask` | `PeriodicTimer`                          |
| cppcache internal `Task<T>` (worker)  | `Task.Run` + cancellable loop                 |
| `LoggingMacros` / `LOGFINE` etc.      | `Microsoft.Extensions.Logging.ILogger`        |
| `Statistics` framework                | `System.Diagnostics.Metrics.Meter` / EventCounters |
| `Xerces-C` (cache.xml parser)         | Cut entirely (per Configuration policy)       |
| `apache::geode::client::Properties`   | `IDictionary<string, string>`                 |

#### Bucket 2: domain logic / wire protocol → **mirror the architecture**

These are what we are actually writing. Match cppcache class names,
file layout, inheritance, and method names; modernise only the
mechanics (sync → async, multi-inheritance → composition, etc.).

Examples: `ThinClientBaseDM`, `DistributionManager`, `PoolDM`,
`TcrEndpoint`, `TcrPoolEndPoint`, `TcrConnection`,
`TcrConnectionManager`, `ThinClientLocatorHelper`, `TcrMessage`,
`Cache`, `CacheImpl`, `Region`, `ThinClientRegion`,
`ClientMetadataService` (Phase 4), `ThinClientStickyManager`
(Phase 6), `PdxType` / `PdxTypeRegistry` (Phase 2).

#### Bucket 3: BCL partially covers, semantics incomplete → **thin wrapper**

Use the BCL type as the engine; wrap **only enough** to add the
missing semantics. Do not rebuild the whole cppcache class.

| cppcache                            | What BCL is missing                  | Wrap strategy |
| ----------------------------------- | ------------------------------------ | ------------- |
| `ConnectionQueue<T>` (FIFO + condvar + size cap + timed get) | `Channel<T>` lacks "wait up to T then create new" | thin wrapper around `Channel<T>` exposing `TryGetWithTimeoutAsync` |
| `synchronized_map<K,V>`             | `ConcurrentDictionary` has no iterate-with-lock | **don't wrap** — use `ConcurrentDictionary` + snapshot where needed |
| `Cacheable` / `Serializable` family | `ISerializable` doesn't match PDX wire format | introduce `IDataSerializable` interface (Phase 2) |
| `PoolStats` (named counters + sampler) | `Meter` naming/sampling differs | thin wrapper that registers cppcache-named counters into a `Meter` |
| `CacheableString` / `CacheableBytes` | `string` / `byte[]` already exist    | **don't wrap** — handle DSCode tag in the codec only |
| `ServerLocation` (host+port+version) | nothing equivalent                   | **don't wrap** — define a record `ServerLocation(...)` directly |

#### Rule 4: when ambiguous → default to bucket 2

If a cppcache class doesn't clearly fit bucket 1 or 3, mirror it
(bucket 2) as a stub first. During wiring we'll discover whether it
collapses to BCL (move to bucket 1) or shrinks to a wrapper
(bucket 3). Same "mirror then prune" discipline as Options.

---

## Overall principles

1. **Async-first.** All I/O operations expose only an async API; no
   synchronous variants.
2. **Options pattern.** Configuration flows through `IOptions<T>`
   bound to `appsettings.json`.
3. **DI-first.** Registration via `services.AddGeodeClient(...)`; no
   static singletons.
4. **Zero external runtime dependencies.** Everything sits on the BCL;
   the only references are the `Microsoft.Extensions.*` abstraction
   packages.
5. **API-first / interface-first.** Declare interface shells first
   (`NotImplementedException` bodies), then fill in implementations;
   interfaces are translated from cppcache `clicache/`.
6. **Walking skeleton.** Each sub-phase delivers an end-to-end minimum;
   never finish a whole layer before any layer above it works.
7. **Living document.** This file evolves alongside development.

---

## Dependency policy

| What cppcache uses    | Our replacement                                                 |
| --------------------- | --------------------------------------------------------------- |
| Boost.Asio            | `System.Net.Sockets` + `System.IO.Pipelines` + `Channels`       |
| OpenSSL               | `System.Net.Security.SslStream`                                 |
| Xerces-C (cache.xml)  | **Cut entirely.** Use `Microsoft.Extensions.Configuration`.     |
| SQLite (overflow)     | Not implemented.                                                |
| Google Test / Benchmark | xUnit v3 / BenchmarkDotNet                                    |

---

## Configuration

cppcache uses two files: a `.ini` (`SystemProperties`) and `cache.xml`
(region / pool declarations parsed by Xerces). **We replace both with
the .NET `IOptions<T>` pattern** — `appsettings.json` + `IConfiguration`
binds straight to record / class options. **No `cache.xml`. No `.ini`.**

### Options policy

1. **Mirror, then prune.** When porting cppcache config, **copy every
   property first** (one C# property per cppcache key, defaults
   matching cppcache constants). Pruning happens once, late — likely
   end of Phase 1.5 or before the first NuGet release — when we audit
   which properties any code path actually reads. Do not pre-judge
   "this looks unused" while porting; the cppcache audit window stays
   open until the .NET pool design is settled.

2. **Document semantics on the property, not in side notes.** Every
   options property's XML doc must capture what was learned by reading
   cppcache: which file consumes it, what the value actually drives
   (e.g. `SO_SNDBUF`, expiry-task interval, per-endpoint cap), whether
   it's pool-level / connection-level / endpoint-level, and any
   platform-specific quirks (`#ifdef __linux` etc.). The doc is the
   audit trail — anyone reviewing the property six months later
   should not need to re-read cppcache to understand it.

3. **No invented schema ahead of implementation.** Concrete JSON
   shapes are decided phase-by-phase against cppcache
   `SystemProperties` semantics; do not write a target schema in this
   doc that the code hasn't reached yet.

---

## Public API sketch (DI-first)

```csharp
// Registration
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

// Usage
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, byte[]> _orders = cache.GetRegion<string, byte[]>("orders");
    public Task SaveAsync(string id, byte[] payload, CancellationToken ct)
        => _orders.PutAsync(id, payload, ct);
}
```

Main interfaces:

```csharp
public interface IGeodeCache
{
    IRegion<TKey, TValue> GetRegion<TKey, TValue>(string name);
    IQueryService QueryService { get; }
}

public interface IRegion<TKey, TValue>
{
    string Name { get; }
    Task PutAsync(TKey key, TValue value, CancellationToken ct = default);
    Task<TValue?> GetAsync(TKey key, CancellationToken ct = default);
    Task<bool> RemoveAsync(TKey key, CancellationToken ct = default);
    Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default);
    // ... bulk / Clear / Invalidate / convenience queries land in Phase 1.3 / 1.4
}

public interface IQueryService { IQuery<T> NewQuery<T>(string oql); }
public interface IQuery<T>      { Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default); }
```

**Important:** in MVP we do not support cache.xml or region creation.
A DBA pre-creates the region with gfsh
(`gfsh create region --name=test --type=REPLICATE`); the client only
acts as a proxy.

---

## Feature phases

### Phase 1 (MVP — a production-ready client)

- Connect
- Single-key CRUD (Put / Get / Remove / ContainsKey)
- Bulk operations (PutAll / GetAll / RemoveAll)
- Clear
- Invalidate
- Region convenience queries (ExistsValue / SelectValue)
- Built-in type serialisation (including collections: List, Dictionary,
  arrays, HashSet)
- OQL queries (`SELECT *` and `SELECT COUNT(*)`)
- Connection pool
- Locator discovery
- Server failover / automatic reconnect

### Phase 2 (custom objects + advanced query)

- Custom-object serialisation (PDX)
- Interop with the Java client
- OQL projection queries (`SELECT field1, field2`)
- PdxInstance (read fields without full deserialisation)
- Continuous Query (server-push subscriptions)
- Transactions (Begin / Commit / Rollback)

### Phase 3 (security + compute)

- Authentication (username/password, custom auth providers)
- TLS / mTLS
- Function execution (server-side)

### Phase 4 (performance + sharding)

- Delta propagation (ship only changed fields)
- Partition resolver (custom colocation)

### Not implemented

- **cache.xml** — replaced by `appsettings.json` + `IOptions<T>`.
- **Sub-regions** — Geode itself discourages them.
- **Synchronous APIs** — async only.
- **Cache listener / loader / writer** — niche use cases; easier to
  implement server-side in Java.
- **Region expiration / eviction** — managed by server-side
  configuration; the client stays out.

---

## Phase 1 sub-phase breakdown

Split into 5 sub-phases by dependency order. Each sub-phase is its own
walking skeleton.

### Phase 1.1 — Connection foundation + serialisation

Single socket, handshake, built-in type codec. The plumbing works,
nothing yet visible to the user.

- Frame codec (big-endian, TcrPart, TcrMessage)
- Handshake (against
  `cppcache/src/TcrConnection.cpp::sendHandshakeForServer`)
- A single `TcrConnection` with reader / writer loops
- Built-in DSFID codec (string, byte[], bool, int, long, short, byte,
  float, double, DateTime, null, List, Dictionary, arrays, HashSet)
- Ping / Reply verification

### Phase 1.2 — Single-key CRUD

The first demo-able milestone.

- Put(7) / Request(0) / Destroy(9) / ContainsKey(38) messages
- Exception(2) reply handling
- `IGeodeCache` / `IRegion<TKey,TValue>` public API
- DI registration (`AddGeodeClient`)
- Integration tests: put / get / remove / contains

### Phase 1.3 — Bulk + management operations

- PutAll(56) / GetAll70(100) / RemoveAll(109)
- Clear (region-wide entry clear)
- Invalidate
- Each gets its own message type; rounds out the basic region surface

### Phase 1.4 — Query

- OQL Query(34) message
- Result decoding: `SELECT *` returns `IReadOnlyList<TValue>`,
  `SELECT COUNT(*)` returns `long`
- Region convenience queries (ExistsValue / SelectValue)

### Phase 1.5 — Connection management

Promote the single socket to production-ready.

- Connection pool (min/max, idle eviction, health checks)
- Locator wire protocol (different from the server protocol)
- Multi-server failover, automatic reconnect
- Server endpoint health monitoring

---

## Wire protocol summary

### Frame layout (all big-endian)

```
+------------------+------------------+------------------+------------------+
| MessageType i32  | MessageLength i32| NumParts i32     | TransactionId i32|
+------------------+------------------+------------------+------------------+
| EarlyAck u8      |                                                        |
+------------------+--------------------------------------------------------+
| Part 1, Part 2, ... NumParts parts                                        |
+----------------------------------------------------------------------------+

Part:
+------------------+----------+--------+-------------+
| PartLength i32   | IsObject | Type   | Payload     |
|                  | u8       | u8     | (PartLen B) |
+------------------+----------+--------+-------------+
```

### Handshake (the easiest place to get burned)

The handshake does **not** use the standard frame format — it's an
ad-hoc byte sequence. Translate it byte-by-byte against
`cppcache/src/TcrConnection.cpp::sendHandshakeForServer`. **Do not
work from memory.**

### MessageType (MVP subset)

Pulled from `cppcache/src/TcrMessage.hpp`:

| Value | Name                | Sub-phase |
| ----- | ------------------- | --------- |
| 0     | Request (GET)       | 1.2       |
| 1     | Response (GET reply)| 1.2       |
| 2     | Exception           | 1.2       |
| 5     | Ping                | 1.1       |
| 6     | Reply               | 1.1       |
| 7     | Put                 | 1.2       |
| 9     | Destroy             | 1.2       |
| 18    | CloseConnection     | 1.1       |
| 34    | Query               | 1.4       |
| 38    | ContainsKey         | 1.2       |
| 56    | PutAll              | 1.3       |
| 99    | ServerToClientPing  | 1.1       |
| 100   | GetAll70            | 1.3       |
| 109   | RemoveAll           | 1.3       |

---

## Implementation principles (keep these in mind)

1. **Read cppcache before designing protocol.** `TcrMessage.cpp`,
   `TcrConnection.cpp`, `HandShake.cpp`, and `ThinClientPoolDM.cpp` are
   the spec.
2. **API-first.** Declare interface shells first
   (`NotImplementedException`), then fill in.
3. **Walking skeleton.** Each phase runs end-to-end before stacking
   the next layer.
4. **Frame codec must have unit tests** backed by Wireshark byte
   fixtures.
5. **Don't over-abstract.** Write concrete classes at the lower
   layers; only extract interfaces when DI wiring lands in Phase 1.2.
6. **Big-endian everywhere** (`BinaryPrimitives.WriteInt32BigEndian`).
   Geode is Java; the wire is network byte order.
7. **A single connection already supports concurrency** (pipelined
   requests keyed by transaction id). The pool is a
   throughput / fault-isolation optimisation, not a baseline
   requirement.

---

## Toolchain

- **.NET 10 SDK** (LTS, GA 2025-11)
- **xUnit v3** + FluentAssertions
- **Testcontainers** — integration tests boot `apachegeode/geode`
- **GitHub Actions** — CI on PR / push, release on tag
- **NuGet** — `MinVer` derives the version from git tags
- **Source Link** + `.snupkg`
- **Apache-2.0** licence (matches the upstream project)

---

## Dual-network sync (Tomi's setup)

The maintainer works across two networks:

- **Internet side** — primary development, GitHub, CI, NuGet publish.
- **Intranet side** (air-gapped) — internal CI/CD, internal GitLab /
  GitHub.
- Sync method — USB bare repo.
- Branches — `main` (features), `ci/offline` (CI/CD config,
  **intranet-only**).
- Rule — only reviewed / approved `main` crosses the USB boundary.

**No direct commits to `main`.** All changes go through PR + review.

---

## Bootstrapping the next task

Phase 1 starts with **Phase 1.1**. Suggested prompt:

```
Read CLAUDE.md. We're starting Phase 1.1.

API-first first:
1. Following the cppcache clicache/src/ headers, declare every Phase 1
   public interface (IGeodeCache, IRegion<TKey,TValue>, IQueryService,
   GeodeClientOptions, AddGeodeClient extension, related exceptions)
   under src/Geode.Client/. Method bodies are NotImplementedException;
   add full XML docs.
2. Wire up DI but leave internal bindings throwing
   (the API skeleton).
3. Make sure dotnet build and dotnet test pass (mark tests
   [Fact(Skip="Phase 1.1")] for now).

Once that lands, move into the real Phase 1.1 work:
Frame codec → Handshake → Ping.
```
