# Geode .NET Client

A pure managed **.NET 10** client for [Apache Geode](https://geode.apache.org/),
designed from scratch with dependency injection, modern async I/O, and zero
external NuGet dependencies.

> **Status**: Pre-alpha. Active development. Not yet production-ready.

## Why?

Apache Geode's official .NET client (`apache/geode-native` `clicache/`) is built
on **C++/CLI**, which Microsoft has officially placed in maintenance mode for
.NET 5+ — Windows-only, no AOT, no SDK-style projects, no future investment.

This project is a clean-room implementation that:

- Targets **.NET 10 LTS**
- Is **cross-platform** (Linux / macOS / Windows)
- Has **zero external runtime dependencies** (everything from BCL)
- Is **DI-first**: register with `services.AddGeodeClient(...)`, inject `IGeodeCache`
- Configures via `appsettings.json` + `IOptions<T>` — **no `cache.xml`**
- Is published as a regular NuGet package with Source Link

## Quick start

```bash
dotnet add package Geode.Client --prerelease
```

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));
var app = builder.Build();

// 任何服務注入 IGeodeCache
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, byte[]> _orders =
        cache.GetRegion<string, byte[]>("orders");

    public Task SaveAsync(string id, byte[] payload) =>
        _orders.PutAsync(id, payload);
}
```

```json
// appsettings.json
{
  "Geode": {
    "Locators": ["localhost:10334"],
    "Pool": { "MaxConnections": 10 }
  }
}
```

## Roadmap

See [`CLAUDE.md`](./CLAUDE.md) for the full 12-phase plan. Highlights:

- **MVP (Phase 1–5)**: Put / Get / Remove with primitive types + DI wiring
- **Production (Phase 6–10)**: Connection pool, locator discovery, TLS, auth, OQL
- **Interop (Phase 11)**: PDX serialization (compatibility with Java clients)
- **Advanced (Phase 12+)**: CQ, function execution, transactions, HA

## Development

```bash
# Build & test
dotnet build
dotnet test

# Run integration tests (auto-starts Geode in Docker via Testcontainers)
dotnet test tests/Geode.Client.IntegrationTests
```

To run a local Geode for manual testing:

```bash
docker compose up -d
# Geode locator on :10334, server on :40404
```

## License

Apache-2.0, matching the upstream `apache/geode-native` project.
