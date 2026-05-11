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

## Phase 1.1 — 建立單一伺服器連線 ✅

**目標**：透過 `Cache` 公開 API（`EnsureInitializedAsync` / `CloseAsync`）端到端開一條 server connection、跑 handshake、能送 Ping、優雅關閉。**不**做 pool、**不**做多 endpoint、**不**做 failover。

### Foundation（已完成 — protocol layer）

- [x] `BigEndianBinaryReader` / `BigEndianBinaryWriter`（unit tested）
- [x] `TcrPart` / `TcrMessage` / `TcrPartBuilder` / `TcrMessageBuilder`（unit tested）
- [x] `ClientProxyMembershipIdBuilder`（unit tested）
- [x] `MessageType` enum
- [x] `TcrConnection` 框架 + handshake bytes
- [x] `PingIntegrationTests` 對 `apachegeode/geode` 真機通過

### 接到 Cache

- [x] `TcrEndpoint.CreateNewConnectionAsync` 實作 — 開 socket、跑 handshake、回 `TcrConnection`
- [x] `Cache.InitializeCoreAsync` 從 options 拿單一 host:port → 建 `TcrEndpoint` → 呼叫 `CreateNewConnectionAsync`（commit `20a53fc`）
- [x] `Cache.CloseAsync` 送 `CloseConnection(18)` 並釋放連線
  - `TcrMessageBuilder.CloseConnection(bool keepAlive)` partial（1-byte payload，cppcache `TcrMessageCloseConnection` 對齊）
  - `TcrConnection.CloseAsync(keepAlive, ct)` — fire-and-forget 送 18 + 2s send budget + catch+LogInformation + `DisposeAsync`
  - `ThinClientPoolDM.DestroyAsync` Step 5a：drain `_opConnections` → 對每條 conn 呼叫 `CloseAsync(_keepAlive, ct)`
  - `_keepAlive` 欄位（cppcache `m_keepAlive` 鏡像；`DestroyAsync(bool keepAlive)` 寫入）；Phase 1.1 永遠 false
  - **TODO Phase 1.5**：`_endpoints` 釋放 TCCM ref（`ConnManager.RemoveRefToTcrEndpointAsync`），目前靠 cache scope dispose 連鎖收尾
- [x] `EnsureInitializedAsync` 之後 ping loop 端到端能跑
  - `ThinClientPoolDM.PingLoopAsync` + `PingServerLocalAsync`（commit `07820de`）
  - `TcrEndpoint.PingAsync(ThinClientPoolDM, ct)` 對齊 cppcache `pingServer`（含 `_msgSent` / `_pingSent` 短路）
  - `ThinClientBaseDM.SendSyncRequestAsync` / `SendRequestToEndpointAsync` 簽名收成 `TcrMessage` → `Task<TcrMessage>`（不再 by-ref reply + GfErrType code）
  - `ThinClientPoolDM.SendRequestToEndpointAsync` + `GetFromEPAsync` + `CreatePoolConnectionToAEndPointAsync` + `PutInQueueAsync` Phase 1.1 切片
  - 整合測試 `PingLoop_pings_endpoint_against_real_server`（commit `bc6b909`）— 配 `MinConnections=1` / `IdleTimeout=100ms` / `PingInterval=200ms`，驗 `PingTickCount>=3` && `PingSuccessCount>=2` && `PoolSize>=1`
- [x] **（Phase 1.1 收尾）** Options 驗證 + per-cache scope 架構整理
  - 新檔 [`Internal/GeodeClientOptionsValidator.cs`](src/Geode.Client/Internal/GeodeClientOptionsValidator.cs) — `IValidateOptions<GeodeClientOptions>`，accumulate failures：`Pools.Count >= 1` / `Pool.Name` 非空白 / `Locators+Servers >= 1` / `CacheXmlHostPort.Host` 非空 + `Port ∈ [1, 65535]` / `MinConnections >= 0` / `MaxConnections >= MinConnections`
  - 三個 `AddGeodeClient` overload 串 `.ValidateOnStart()`；`AddCore` 用 `TryAddEnumerable<IValidateOptions<>>` 註冊 validator（additive 語意 + 多 cluster 不重覆）
  - 新檔 [`Internal/CacheScopeContext.cs`](src/Geode.Client/Internal/CacheScopeContext.cs) — per-scope holder（`Name` + `Options` + 一次性 `Initialize`）。解掉 `IOptions<T>.Value` 永遠回 default name 的架構錯位（named-only 註冊下 `ClientProxyMembershipIdBuilder` / `TcrConnection` 之前都讀錯 options）
  - 所有 per-cache 「唯一一個」的服務改 Scoped：`Cache` / `TcrConnectionManager` / `PoolManager` / `ClientProxyMembershipIdBuilder` / `CacheScopeContext`。`GeodeCacheFactory.Build` 簡化成「建 scope → `Initialize(name, options)` → `GetRequiredService<Cache>()`」；`DisposeAsync` 只 dispose scope，cascade 連鎖 dispose 全部 scoped service
  - 「每 scope N 個動態實例」的型別（`ThinClientPoolDM` / `TcrEndpoint` / `TcrConnection`）保留 `ActivatorUtilities` — DI Scoped 是 exactly-one，不適用

**下一步入口**：Phase 1.2 — Single-key CRUD。

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
