# Phase 2 — 自訂物件、HA、訂閱、進階查詢

> Phase 2 的範圍:**讓 client 從「能 KV」進展到「能跟真實業務系統
> 接軌」**。Phase 1 走完 walking skeleton 之後,本階段先以
> **top-level NIE stub** 形式把功能面定型(端到端骨架接通),再回頭
> 細化單一功能。
>
> 主檔 [PROGRESS.md](PROGRESS.md);Phase 1 紀錄
> [PROGRESS1.md](PROGRESS1.md);類別對映表
> [PORTING.md](PORTING.md)。

---

## 範圍 (Scope)

Phase 2 拆四個子階段,**順序依 walking skeleton 原則**(top-down,
user-facing 功能先,robustness 後補):

- **2.1 PDX 自訂物件序列化** — 跨語言(.NET ↔ Java)欄位級序列化、
  `PdxInstance`(免反序列化讀欄位)。對齊 cppcache `PdxType` /
  `PdxTypeRegistry` / `PdxInstanceImpl`。
- **2.2 訂閱通道 / Continuous Query** — server-push 通知連線、
  `registerInterest`、CQ。v1 不耐 server 失敗(等價於 Phase 1.1
  single-server connection),斷線恢復推到 2.3。對齊 cppcache
  `TcrEndpoint::registerDM(clientNotification=true)`、
  `m_notifyConnection` / `m_notifyReceiver`。
- **2.3 HA / 冗餘** — primary / secondary server 角色、durable client、
  `RedundancyManager`、訂閱通道斷線 reconnect + 事件 replay。把 2.2
  v1 升級成 production-grade。
- **2.4 Transactions** — `Begin` / `Commit` / `Rollback`。對齊 cppcache
  `CacheTransactionManagerImpl`。

每個子階段都先做 walking skeleton entry point + NIE,再分批細做。

---

## Walking skeleton 待建項目

每個 entry point 必須:**(a) public surface 編譯得過、
(b) 進入點丟 `NotImplementedException` 並標 Phase 對應、
(c) cppcache 對應 class / method 在 xmldoc 引用**。

### Phase 2.1 — PDX 自訂物件序列化

- [ ] `IPdxSerializable` interface(`ToData(IPdxWriter)` /
      `FromData(IPdxReader)`)
- [ ] `IPdxWriter` / `IPdxReader` interface(用 GfErrType-free 風格,
      回傳 `void` / `T`)
- [ ] `PdxType` / `PdxField` 內部 metadata class
- [ ] `PdxTypeRegistry` service(typeId ↔ schema 映射)
- [ ] `IPdxInstance` public interface(`HasField` / `GetField` /
      `CreateWriter`)
- [ ] `SerializationRegistry.RegisterPdxType<T>()` 註冊入口
- [ ] `TcrMessageBuilder` 對 PDX 物件的 `WritePart` 路徑

### Phase 2.2 — 訂閱通道 / Continuous Query

> v1 不要求耐 server 失敗 — server 死掉訂閱也死掉,等價於 Phase 1.1
> 的 single-server connection。Reconnect / 事件 replay 屬於 Phase 2.3
> HA 範疇,留 NIE 點。

- [ ] `IRegion<TKey,TValue>.RegisterInterestAsync(...)` 入口
- [ ] `IRegion<TKey,TValue>.UnregisterInterestAsync(...)` 入口
- [ ] `IRegion<TKey,TValue>.SubscribeAsync(IRegionListener<TKey,TValue>)`
      入口(`IAsyncDisposable` 解訂)
- [ ] `IRegionListener<TKey,TValue>` interface(`OnCreated` /
      `OnUpdated` / `OnDestroyed` / `OnInvalidated`)
- [ ] `IQueryService.NewCqAsync(string oql, ICqListener<T>)` 入口
- [ ] `ICqListener<T>` interface
- [ ] `TcrEndpoint` 的 `notificationChannel` / `notifyReceiver` task
      (cppcache `m_notifyConnection` / `m_notifyReceiver`)
- [ ] `CreateNewConnectionAsync(isClientNotification: true)` 路徑
      (目前 NIE)

### Phase 2.3 — HA / 冗餘

> 把 Phase 2.2 v1 的「斷線就死」升級成「斷線 reconnect + 事件 replay」。

- [ ] `ThinClientPoolHADM` 從骨架升級成真實實作
- [ ] `RedundancyManager` 服務(主備角色、reconnect 時保留訂閱)
- [ ] `CachePoolOptions.SubscriptionRedundancy` 真實 consumer
      (目前 Phase 5 prune 清單上)
- [ ] `CachePoolOptions.SubscriptionAckInterval` 真實 consumer
- [ ] `CachePoolOptions.SubscriptionMessageTrackingTimeout` 真實
      consumer
- [ ] Durable client ID / durable timeout 路徑

### Phase 2.4 — Transactions

- [ ] `ICacheTransactionManager` interface(`Begin` / `Commit` /
      `Rollback` / `Suspend` / `Resume`)
- [ ] `TXState` 內部狀態 class(cppcache `TXState`)
- [ ] `CacheTransactionManagerImpl` 實作
- [ ] `IGeodeCache.CacheTransactionManager` 屬性入口

---

## 已知需要回填的 stat / metric

從 Phase 1.5 推到這的可觀測性項目:

- `subscriptionServers` IntGauge(`PoolStatistics` catalogue #2)—
  HA primary/secondary 數量
- `messagesBeingReceived` LongCounter(catalogue #21)— notification
  通道收到的 frame 數
- `processedDeltaMessages` LongCounter(catalogue #22)
- `deltaMessageFailures` LongCounter(catalogue #23)
- `processedDeltaMessagesTime` LongCounter(catalogue #24)
- `queryExecutions` IntCounter(catalogue #25)— Phase 1.4 路徑已
  存在,只是 stat 沒接
- `queryExecutionTime` LongCounter(catalogue #26)

---

## Locator follow-ons(從 Phase 1.5 推來)

cppcache `ThinClientLocatorHelper` 上四個 public method,我們只實作
了前兩個(`UpdateLocators` / `GetEndpointForNewFwdConn`);剩下兩個
落 Phase 2 / Phase 4。

- **`getEndpointForNewCallBackConn`** — 訂閱通道專用的 endpoint
  選擇(避開主 op 通道)。Phase 2 Continuous Query 直接相依。
- **`getAllServers`** — Phase 4 PR single-hop bucket-to-server
  resolution 用。Phase 2 不會碰。
- **`ClientReplacementRequest`** — Phase 4 failover swap(locator
  在某 endpoint 倒掉時挑替代 server)。Phase 2 不會碰。

---

## 待補的 PORTING.md class

Phase 2 開工前先確認 [PORTING.md](PORTING.md) 已收錄以下 cppcache
class(目前部分未登記):

- `PdxType` / `PdxTypeRegistry` / `PdxInstanceImpl` / `PdxFieldType`
- `IPdxSerializable` interface(cppcache `PdxSerializable`)
- `ThinClientRedundancyManager`
- `CqService` / `CqQueryImpl` / `CqListener`
- `RegisterInterestList` / `RegisterInterestListMessage`
- `TXState` / `CacheTransactionManagerImpl`
- `TcrChunkedResult` 變體(訂閱 chunk 處理)

---

## 設計考量

- **Pool 模式 only** — 沿用 Phase 1 的決定,不重啟非 pool 路徑;
  訂閱 / HA 走 `ThinClientPoolHADM`,不會做
  `TcrHADistributionManager`(cppcache 的非 pool HA 變體)。
- **TcrPoolEndPoint 階層遷移** — Phase 2 訂閱通道用到
  `TcrEndpoint.RegisterDMAsync(clientNotification: true)`,屆時 pool
  與 endpoint 的關係會被測試到。可能就是 pool-per-endpoint vs
  cache-wide-shared 設計選擇的最後決定時機(見 PROGRESS1.md 的
  「設計決策與已知偏離」)。
- **PDX 對 .NET reflection 的依賴** — `PdxTypeRegistry` 需要從
  user-defined class 提取欄位 metadata;考慮 source generator
  方案(`[PdxSerializable]` attribute → compile-time emit)避開
  reflection 性能成本。
- **訂閱事件的 async 形狀** — cppcache `CacheListener` 是 sync
  callback;.NET 慣例會做 `IAsyncEnumerable<RegionEvent>` 或
  `Channel<T>` 推送,讓 listener 可以 await。決策推到實作時。
