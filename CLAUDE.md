# Geode .NET Client — Project Context

> This file is Claude Code's long-term project memory. Read it once at the
> start of every session, confirm the current phase, then start work.

---

## One-line goal

Build a **pure-managed, zero-dependency, cross-platform** Apache Geode
client targeting **.NET 10 (LTS)** and ship it on NuGet.

Upstream reference: <https://github.com/apache/geode-native>
(We do **not** port the C++/CLI `clicache/` — too restricted, Windows-only.)

---

## Architectural decisions (settled — do not relitigate)

### Why not the alternatives

- **Route A (port C++/CLI to .NET 10)**: rejected. Microsoft has stated
  C++/CLI on .NET Core is supported for compatibility only, with no future
  investment, Windows-only, no AOT, no SDK-style projects.
- **Route B1 (keep native cppcache, add a P/Invoke wrapper)**: rejected.
  Forces us to maintain native binaries per RID, loses the "pure managed"
  benefit, and the C ABI shim is a project of its own.
- **Route B2 (pure managed, speak the wire protocol ourselves)**:
  ✅ **adopted**.

### B2's trade-offs and how we cope

Geode's wire protocol has **no normative spec** (Apache's own wiki admits
this). It has to be reverse-engineered from `cppcache/src/` and Java
`geode-core`.

Mitigation: **scope down hard to MVP.** Only Put / Get / Query / basic
CRUD. CQ / function execution / transactions / HA / delta are explicitly
out of MVP scope.

---

## Dependency policy

**Zero external runtime NuGet dependencies** (test tooling excepted).

| What `cppcache` uses    | Our replacement                                                 |
| ----------------------- | --------------------------------------------------------------- |
| Boost.Asio              | `System.Net.Sockets` + `System.IO.Pipelines` + `Channels`       |
| OpenSSL                 | `System.Net.Security.SslStream`                                 |
| Xerces-C (cache.xml)    | **Cut entirely.** Use `Microsoft.Extensions.Configuration`.     |
| SQLite (overflow)       | Out of MVP scope.                                               |
| Google Test / Benchmark | xUnit v3 / BenchmarkDotNet                                      |

Configuration follows .NET conventions: `appsettings.json` +
`IOptions<GeodeClientOptions>`. **No `cache.xml`. No `.ini`.**

---

## API surface (DI-first)

The user sees one extension method and two interfaces:

```csharp
// Registration
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

// Usage
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, Order> _orders = cache.GetRegion<string, Order>("orders");
    public Task<Order?> GetAsync(string id) => _orders.GetAsync(id);
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
    Task<IDictionary<TKey, TValue>> GetAllAsync(IEnumerable<TKey> keys, CancellationToken ct = default);
    Task PutAllAsync(IDictionary<TKey, TValue> entries, CancellationToken ct = default);
}

public interface IQueryService { IQuery<T> NewQuery<T>(string oql); }
public interface IQuery<T>      { Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default); }
```

Configuration schema:

```json
{
  "Geode": {
    "Locators": ["host1:10334", "host2:10334"],
    "Servers":  ["host1:40404"],
    "Pool": { "MinConnections": 1, "MaxConnections": 10, "ReadTimeout": "00:00:10" },
    "Tls":  { "Enabled": false },
    "Auth": { "Username": null, "Password": null }
  }
}
```

**Important**: in MVP we do **not** support cache.xml or region creation.
A DBA pre-creates regions with gfsh
(`gfsh create region --name=test --type=REPLICATE`); the client only acts
as a proxy.

---

## Protocol layering

```
┌──────────────────────────────────────────────┐
│ Operation layer: PutAsync, GetAsync, ...     │  C# public API
├──────────────────────────────────────────────┤
│ Message layer:   TcrMessage encode/decode    │  MessageType + Parts
├──────────────────────────────────────────────┤
│ Frame layer:     header + part bytes         │  pure byte I/O
├──────────────────────────────────────────────┤
│ Transport:       TcpClient + SslStream       │  BCL
└──────────────────────────────────────────────┘
```

### Frame layout (all big-endian / network byte order)

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

The handshake does **not** use the standard frame format — it's an ad-hoc
byte sequence. Translate it byte-for-byte from
`cppcache/src/TcrConnection.cpp::sendHandshakeForServer`. **Do not work
from memory.**

```
client → server:
  ConnectionType u8       (100 = client-to-server)
  ReplyOk         u8      (59)
  ProtocolVersion (major.minor.patch + ordinal)
  ClientProxyMembershipID (serialised: host / PID / UUID / durable id)
  Credentials             (optional Properties)

server → client:
  AcceptanceCode    u8    (38 = OK)
  ServerQueueStatus u8
  QueueSize         i32
  ServerMember      (membership ID)
  DeltaEnabled      u8
```

### MVP MessageType subset

Pulled from `cppcache/src/TcrMessage.hpp`:

| Value | Name               | Purpose                |
| ----- | ------------------ | ---------------------- |
| 0     | Request            | GET                    |
| 1     | Response           | GET reply              |
| 2     | Exception          | server error           |
| 5     | Ping               | health check           |
| 6     | Reply              | ack                    |
| 7     | Put                | PUT                    |
| 9     | Destroy            | REMOVE single key      |
| 18    | CloseConnection    | bye                    |
| 34    | Query              | OQL                    |
| 38    | ContainsKey        |                        |
| 56    | PutAll             |                        |
| 99    | ServerToClientPing | server-initiated ping  |
| 100   | GetAll70           |                        |

### Serialisation (MVP)

Only these DSFIDs (per `cppcache/include/geode/internal/DSCode.hpp`):

- String (DSFID 87)
- Integer / Long
- Boolean / Double
- Date
- byte[] / null

**PDX is not in MVP** (it lands in Phase 11).

---

## Roadmap (12 phases)

Every phase is a "walking skeleton" — it must run end-to-end before the
next one starts.

| Phase | Content                                       | Estimate | Done when                                  |
| ----- | --------------------------------------------- | -------- | ------------------------------------------ |
| 0     | Environment & skeleton (this zip)             | 0.5w     | solution builds, Docker server up          |
| 1     | Frame codec — pure encode/decode              | 0.5w     | byte-fixture round-trip tests pass         |
| 2     | **Slice 1: Ping** (with handshake)            | 1–2w     | server replies with `Reply (6)`            |
| 3     | **Slice 2: Put / Get**                        | 1w       | put `byte[]`, get back equal value         |
| 4     | Type expansion (Int / Long / Bool / Date)     | 1w       | integration test per type                  |
| 5     | API + DI wiring                               | 0.5w     | `IGeodeCache` injectable, demoable         |
| —     | **First NuGet release `0.1.0-alpha`**         |          | publishable                                |
| 6     | Connection pool                               | 1w       | high concurrency + auto-recover on restart |
| 7     | Locator discovery                             | 0.5w     | locator-only config connects               |
| 8     | TLS (`SslStream`)                             | 0.5w     | connects to TLS-enabled server             |
| 9     | Authentication                                | 0.5w     | username / password                        |
| 10    | Query / OQL                                   | 1w       | `SELECT * FROM /r WHERE x>10`              |
| 11    | PDX serialisation                             | 2w       | interoperable with the Java client         |
| 12+   | CQ / Function / TX / HA / Delta               | later    | advanced features, demand-driven           |

---

## Core principles

1. **Read `cppcache` before designing the protocol.** `TcrMessage.cpp`,
   `TcrConnection.cpp`, `HandShake.cpp`, `ThinClientPoolDM.cpp` are the
   spec.
2. **Walking skeleton.** Get every phase to run end-to-end before stacking
   the next layer.
3. **Frame codec must have unit tests** backed by byte fixtures from
   Wireshark or `cppcache` source.
4. **Don't over-abstract.** Write concrete classes at the lower layers;
   only extract interfaces in Phase 5 when DI lands.
5. **Big-endian everywhere** (`BinaryPrimitives.WriteInt32BigEndian`).
   Geode is Java; the wire is network byte order.

---

## Toolchain

- **.NET 10 SDK** (LTS, GA 2025-11)
- **xUnit v3** + FluentAssertions for assertions
- **Testcontainers** — integration tests boot `apachegeode/geode`
- **GitHub Actions** — `ci.yml` (currently **disabled** — see
  `CONTRIBUTING.md` §5) and `release.yml` (tag-driven)
- **NuGet** — `MinVer` derives the version from git tags
- **Source Link** + `.snupkg` so users can step into our source
- **Apache-2.0** licence (matches the upstream project)

---

## Dual-network sync (Tomi's setup)

The maintainer (`Tomi`) develops on two networks:

- **Internet side** — `origin` on GitHub, public CI, NuGet publish.
- **Intranet side** — air-gapped enterprise GitLab / GitHub, internal CI.

Sync is one-way: `main` on the internet → USB bare repo → intranet.

Branching model (full rules in `CONTRIBUTING.md`):

- `main` — protected, release-ready, the only branch that crosses the USB
  boundary
- `develop` — internet-side integration branch, day-to-day target for
  feature PRs (does **not** cross USB)
- `feat/*`, `fix/*`, `chore/*`, `docs/*`, ... — short-lived feature
  branches, deleted after merge
- `ci/offline` — intranet-only CI/CD configuration; **must never** be
  pushed to `origin`

**No direct commits to `main`.** All changes go through PR + review.
See `CONTRIBUTING.md` for the full workflow.

---

## Next step

Phase 0 was provided by the initial skeleton (solution, csproj, workflows,
docker-compose). **Start at Phase 1**: implement the frame codec.

Example kick-off prompt:

```
Read CLAUDE.md. We are starting Phase 1:
1) Add BigEndianBinaryReader / BigEndianBinaryWriter in
   src/Geode.Client/Protocol/.
2) Add TcrPart, TcrMessage records.
3) Add a frame round-trip unit test in
   tests/Geode.Client.Tests/Protocol/.
Follow the walking-skeleton principle — get the smallest path working
first.
```
