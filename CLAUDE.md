# Geode .NET Client — Project Context

> 這份檔案是 Claude Code 的長期專案記憶。每次 session 啟動時讀過一次，
> 確認當前 phase 後再開始工作。

---

## 一句話目標

寫一個**純 managed、零外部相依、跨平台**的 Apache Geode client，
target **.NET 10 (LTS)**，發到 NuGet。

Repository 上游參考：<https://github.com/apache/geode-native>
（C++/CLI 的 `clicache/` **不**移植；它的限制太多，且只能 Windows。）

---

## 路線決策（已定，不要再翻案）

### 為什麼不選其他路線

- **A 路線（C++/CLI 移植到 .NET 10）**：放棄。MS 官方說 C++/CLI on .NET Core
  只為相容性而支援、不會投資、僅 Windows、不能 AOT、不能 SDK-style project。
- **B1 路線（保留 native cppcache + P/Invoke wrapper）**：放棄。要為每個 RID
  維護 native binary，喪失 .NET 純 managed 的好處；C ABI shim 也是工作量。
- **B2 路線（純 managed，自己講 wire protocol）**：✅ **採用**。

### B2 的代價與對策

Geode wire protocol **沒有官方規格文件**（Apache 自己 wiki 承認），
只能從 `cppcache/src/` 與 Java `geode-core` 兩邊反推。

對策：**功能範圍縮到 MVP**。只做 put/get/query/CRUD，
CQ / function / transaction / HA / delta 全部不在 MVP 範圍。

---

## 相依策略

**零外部 NuGet 相依**（除了 test 工具）。

| cppcache 用的 | 我們的對策 |
| --- | --- |
| Boost.Asio | `System.Net.Sockets` + `System.IO.Pipelines` + `Channels` |
| OpenSSL | `System.Net.Security.SslStream` |
| Xerces-C (cache.xml) | **直接砍掉**，改用 `Microsoft.Extensions.Configuration` |
| SQLite (overflow) | MVP 不做 |
| Google Test / Benchmark | xUnit v3 / BenchmarkDotNet |

設定走 .NET 慣例：`appsettings.json` + `IOptions<GeodeClientOptions>`。
**不支援 cache.xml、不支援 .ini**。

---

## API 表面（DI-first）

使用者只看到一個 extension method 跟兩個介面：

```csharp
// 註冊
builder.Services.AddGeodeClient(builder.Configuration.GetSection("Geode"));

// 使用
public class OrderService(IGeodeCache cache)
{
    private readonly IRegion<string, Order> _orders = cache.GetRegion<string, Order>("orders");
    public Task<Order?> GetAsync(string id) => _orders.GetAsync(id);
}
```

主要介面：

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

設定 schema：

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

**重要**：MVP 階段不需要支援 cache.xml / Region 建立。Region 由 DBA 用 gfsh
建好（`gfsh create region --name=test --type=REPLICATE`），client 只是 proxy。

---

## Protocol 三層架構

```
┌──────────────────────────────────────────────┐
│ Operation 層: PutAsync, GetAsync, ...        │  C# public API
├──────────────────────────────────────────────┤
│ Message 層:  TcrMessage 編解碼               │  MessageType + Parts
├──────────────────────────────────────────────┤
│ Frame 層:    header + part bytes             │  純 byte I/O
├──────────────────────────────────────────────┤
│ Transport:   TcpClient + SslStream           │  BCL
└──────────────────────────────────────────────┘
```

### Frame 結構（all big-endian / network byte order）

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

### Handshake（最容易踩雷的一段）

**不**走標準 frame 格式，是 ad-hoc bytes。請逐 byte 對著
`cppcache/src/TcrConnection.cpp::sendHandshakeForServer` 翻譯，**不要靠記憶**。

```
client → server:
  ConnectionType u8       (100 = client-to-server)
  ReplyOk         u8       (59)
  ProtocolVersion (major.minor.patch + ordinal)
  ClientProxyMembershipID (serialised: host/PID/UUID/durable id)
  Credentials             (optional Properties)

server → client:
  AcceptanceCode  u8       (38 = OK)
  ServerQueueStatus u8
  QueueSize       i32
  ServerMember    (membership ID)
  DeltaEnabled    u8
```

### MVP MessageType 子集

從 `cppcache/src/TcrMessage.hpp` 抓出：

| 值 | 名稱 | 用途 |
|---|---|---|
| 0 | Request | GET |
| 1 | Response | GET reply |
| 2 | Exception | server 錯誤 |
| 5 | Ping | 健康檢查 |
| 6 | Reply | ack |
| 7 | Put | PUT |
| 9 | Destroy | REMOVE 單 key |
| 18 | CloseConnection | bye |
| 34 | Query | OQL |
| 38 | ContainsKey | |
| 56 | PutAll | |
| 99 | ServerToClientPing | server 主動 ping |
| 100 | GetAll70 | |

### 序列化（MVP）

只做以下 DSFID（對應 `cppcache/include/geode/internal/DSCode.hpp`）：

- String (DSFID 87)
- Integer / Long
- Boolean / Double
- Date
- byte[] / null

**PDX 不在 MVP**（Phase 11 才做）。

---

## 開發順序（12 phases）

每個 phase 都是「walking skeleton」，end-to-end 跑通才往下。

| Phase | 內容 | 工時估 | 完成條件 |
|---|---|---|---|
| 0 | 環境與骨架（這份 zip）| 0.5w | solution build / Docker server up |
| 1 | Frame codec 純編解碼 | 0.5w | 對 byte fixture 來回測試通過 |
| 2 | **Slice 1: Ping 通**（含 handshake）| 1–2w | server 回 Reply(6) |
| 3 | **Slice 2: Put/Get 通** | 1w | put `byte[]` 再 get 回來相等 |
| 4 | 型別擴充（Int/Long/Bool/Date）| 1w | 各型別整合測試 |
| 5 | API + DI 包裝 | 0.5w | `IGeodeCache` 可注入、可 demo |
| — | **第一版 NuGet `0.1.0-alpha`** | | 可發佈 |
| 6 | Connection Pool | 1w | 高併發 + server restart 自動恢復 |
| 7 | Locator discovery | 0.5w | 只給 locator 也能連 |
| 8 | TLS (`SslStream`) | 0.5w | 對 SSL server 能連 |
| 9 | Authentication | 0.5w | username/password |
| 10 | Query / OQL | 1w | `SELECT * FROM /r WHERE x>10` |
| 11 | PDX 序列化 | 2w | 跟 Java client 互通 |
| 12+ | CQ / Function / TX / HA / Delta | 之後 | 進階功能，視需求 |

---

## 重要原則

1. **先讀 cppcache，不要憑空設計 protocol**。`TcrMessage.cpp`、`TcrConnection.cpp`、
   `HandShake.cpp`、`ThinClientPoolDM.cpp` 是規格。
2. **Walking skeleton**：每個 phase 跑通端到端，不要做完整層才往上。
3. **Frame codec 一定寫單元測試**，用 Wireshark 抓的 byte fixture 對照。
4. **不要過度抽象**。底層程式碼先寫具體 class，到 Phase 5 要做 DI 才 extract interface。
5. **Big-endian**（`BinaryPrimitives.WriteInt32BigEndian`）。Geode 是 Java，全網路位元序。

---

## 工具鏈

- **.NET 10 SDK** (LTS, 2025-11 GA)
- **xUnit v3** + FluentAssertions（assertion 風格）
- **Testcontainers**：整合測試自動起 `apachegeode/geode` container
- **GitHub Actions**：CI on PR/push、release on tag
- **NuGet**：`MinVer` 從 git tag 取版本號
- **Source Link** + `.snupkg`：使用者能 step into 原始碼
- **Apache-2.0** 授權（與上游一致）

---

## 內外網同步（Tomi 環境特化）

開發者 Tomi 使用 dual-network 工作流：

- 外網（internet）：主開發、GitHub、CI、發 NuGet
- 內網（air-gapped）：CI/CD 測試，內網 GitLab/GitHub
- 同步方式：USB bare repo
- 分支：`main`（feature）、`ci/offline`（CI/CD config，**只活在內網**）
- 規則：只有 reviewed/approved 的 `main` 才透過 USB 帶進內網

**不要**在 main 直接 commit。所有變更走 PR + review。

---

## 下一步

Phase 0 已經由本骨架提供（solution、csproj、workflow、docker-compose）。
**從 Phase 1 開始**：實作 Frame codec。

啟動指令範例：

```
讀 CLAUDE.md。我們從 Phase 1 開始：
1) 在 src/Geode.Client/Protocol/ 建 BigEndianBinaryReader/Writer
2) 建 TcrPart, TcrMessage record
3) 在 tests/Geode.Client.Tests/Protocol/ 寫 frame round-trip 單元測試
照 walking skeleton 原則做，先把最小路徑跑通。
```
