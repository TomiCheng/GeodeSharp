# GeodeSharp — Implementation Progress

> 每個 phase 完工 / 開工時更新此檔。
> `CLAUDE.md` 是計畫（不變動），此檔是進度（會變動）。
> [PORTING.md](PORTING.md) 是 cppcache ↔ C# class 對應表（更細粒度的實作狀況）。
>
> **新會話 / 新 phase 銜接**：先讀本檔再決定要不要探索程式碼。

---

## Phase 0 — DI + entry interfaces ✅

- [x] 入口介面殼：`IGeodeCache` / `IRegion<TKey,TValue>` / `IQueryService` / `IQuery<T>` / `IGeodeCacheFactory`
- [x] `GeodeException`（BCL exceptions 用於 transport / 參數誤用；`GeodeException` 用於 Geode 協定失敗）
- [x] `GeodeClientOptions` + 子 options（cppcache 移植版，schema 尚待精簡）
- [x] `AddGeodeClient` 三個 overload（host config / 自帶 IConfiguration / Action delegate）× named & unnamed
- [x] `IGeodeCacheFactory` + `GeodeCacheFactory`（per-cache `AsyncServiceScope`、`Lazy<T>` 防競態、cascading async dispose）
- [x] `GeodeCache.EnsureInitializedAsync` 用 `Lazy<Task>(ExecutionAndPublication)`
- [x] 130 unit tests 通過、build 0 warning

**留待後續 phase 處理**（不算 Phase 0 漏項）：

- `IRegion` / `IQueryService` / `IQuery` 仍是空殼（無方法）— Phase 1.2 / 1.4 補上
- `GeodeClientOptions` 是 cppcache `SystemProperties` 全鏡像版（含 `LogOptions` / `StatisticsOptions` / `HeapOptions` / `CacheXmlOptions` / `ThreadPoolSize` / `EnableChunkHandlerThread` 等）— **這是刻意的**，依 CLAUDE.md「mirror then prune」政策，等 Phase 1.5 後期 / 釋出前才審視哪些保留
- 各 options 子類的 XML doc 需逐步補足 cppcache 來源（消費檔案 / 語意 / 平台限制），對齊 CLAUDE.md「Document semantics on the property」原則
- 缺 `AuthOptions` — Phase 3 安全工作再加

---

## Phase 1.1 — 建立單一伺服器連線（進行中）

**目標**：透過 `Cache` 公開 API（`EnsureInitializedAsync` / `CloseAsync`）端到端開一條 server connection、跑 handshake、能送 Ping、優雅關閉。**不**做 pool、**不**做多 endpoint、**不**做 failover。

### Foundation（已完成 — protocol layer）

- [x] `BigEndianBinaryReader` / `BigEndianBinaryWriter`（unit tested）
- [x] `TcrPart` / `TcrMessage` / `TcrPartBuilder` / `TcrMessageBuilder`（unit tested）
- [x] `ClientProxyMembershipIdBuilder`（unit tested）
- [x] `MessageType` enum
- [x] `TcrConnection` 框架 + handshake bytes
- [x] `PingIntegrationTests` 對 `apachegeode/geode` 真機通過

### 接到 Cache（剩餘工作）

- [ ] `TcrEndpoint.CreateNewConnectionAsync` 實作 — 開 socket、跑 handshake、回 `TcrConnection`
- [ ] `Cache.InitializeCoreAsync` 實作 path (b)：從 options 拿單一 host:port → 建 `TcrEndpoint` → 呼叫 `CreateNewConnectionAsync`
- [ ] `Cache.CloseAsync` 送 `CloseConnection(18)` 並釋放連線（`TcrEndpoint.DisposeAsync`）
- [ ] 確保 `EnsureInitializedAsync` 之後 `Cache` 上的 ping / 簡易往返能跑
- [ ] **（Phase 1.1 收尾）** Options 驗證：在 `AddGeodeClient` 接 `ValidateOnStart()` + `IValidateOptions<GeodeClientOptions>`，檢 `CacheXml.Pools` 必要欄位（Name 非空、Servers/Locators 至少一個、Host/Port 範圍）。讓 `InitializeCoreAsync` 內部可省驗證，假設輸入合法

**下一步入口**：[src/Geode.Client/Internal/TcrEndpoint.cs](src/Geode.Client/Internal/TcrEndpoint.cs) 的 `CreateNewConnectionAsync`。

### 後移到別的 phase

| 原 Phase 1.1 項目 | 移到 |
|---|---|
| Built-in DSFID 型別 codec（string / byte[] / 各 primitive / collection） | **Phase 1.2** — Put/Get 才實際需要序列化 |
| `PutGetIntegrationTests` / `GetDiagnosticTests` 五個 Skip | **Phase 1.2** — 是 Put/Get 的整合測試 |
| 多 endpoint / failover / pool | **Phase 1.5** |

---

## Phase 1.2 — Single-key CRUD（未啟動）

依 [CLAUDE.md](CLAUDE.md) Phase 1.2 計畫展開：

- [ ] `IRegion<TKey,TValue>` 介面方法殼：`PutAsync` / `GetAsync` / `RemoveAsync` / `ContainsKeyAsync`
- [ ] Built-in DSFID 型別 codec（string / byte[] / int / long / short / byte / bool / float / double / DateTime / null / List / Dictionary / array / HashSet）— 從原 Phase 1.1 移過來
- [ ] `Put(7)` / `Request(0)` / `Destroy(9)` / `ContainsKey(38)` 訊息建構
- [ ] `Response(1)` / `Exception(2)` 回覆解析
- [ ] `IGeodeCache.GetRegion<TKey,TValue>(name)` 公開 API
- [ ] 解開 `PutGetIntegrationTests` / `GetDiagnosticTests` 五個 Skip
- [ ] 整合測試：put / get / remove / contains

---

## Phase 1.3 — Bulk + management ops（未啟動）

- [ ] `PutAll(56)` / `GetAll70(100)` / `RemoveAll(109)`
- [ ] `Clear`
- [ ] `Invalidate`

---

## Phase 1.4 — OQL Query（未啟動）

- [ ] `IQueryService.NewQuery<T>(oql)` / `IQuery<T>.ExecuteAsync(ct)` 介面
- [ ] `Query(34)` 訊息與結果解碼（`SELECT *` → `IReadOnlyList<TValue>`、`SELECT COUNT(*)` → `long`）
- [ ] Region convenience：`ExistsValueAsync` / `SelectValueAsync`

---

## Phase 1.5 — Connection management（未啟動）

- [ ] Connection pool 設計（cppcache `ThinClientPoolDM` 為參考；先決定 `MaxConnections` 是 pool-wide 還是 per-endpoint）
- [ ] `PoolOptions` 審視：哪些 cppcache 欄位保留 / 改名 / 刪除（依 CLAUDE.md「mirror then prune」，此階段才處理）
- [ ] Locator 線路協定（與 server 不同）
- [ ] Multi-server failover、自動重連
- [ ] Server endpoint 健康監控

---

## Phase 2+ — Custom objects、安全、效能、分片

詳見 [CLAUDE.md](CLAUDE.md) Phase 2 / 3 / 4。
