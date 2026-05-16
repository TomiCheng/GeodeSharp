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

## Phase 1.2 — Single-key CRUD ✅（int32 KV walking-skeleton）

**目標**：`IRegion<int,int>` 的 4 個基本 op（Put / Get / Remove / ContainsKey）端到端通過真實 Apache Geode server。CRUD 完備之後就有第一個 demo-able milestone。

### Region lookup 路徑

- [x] `IRegionService.GetRegion(string)` / `GetRegion<TKey,TValue>(string)` interface 殼（lookup-only，找不到回 null，對齊 cppcache `CacheImpl::getRegion`）
- [x] `Cache.GetRegion(string)`（untyped）實作完成 — line-for-line 對齊 cppcache `CacheImpl::getRegion` (`CacheImpl.cpp:475-518`)：throwIfClosed / `_destroyPending` / 空字串 / `"/"` 驗證 / leading-slash strip / first-segment lookup ；sub-region 路徑（中間有 `/`）目前 NIE，留 sub-region phase
- [x] `Cache.GetRegion<TKey,TValue>(string)` typed overload — `region is null ? null : new RegionView<TKey,TValue>(region)`
- [x] `RegionView<TKey,TValue>` typed wrapper（[Services/RegionView.cs](src/Geode.Client/Services/RegionView.cs)）— compile-time-only typed view，每次 `GetRegion<K,V>` 都 new 一個；K/V 純編譯期保護，runtime 不追蹤；型別錯靠 unbox 自然噴 `InvalidCastException`
- [x] `IRegion` 加 `Name` / `FullPath` / 4 個 `object`-typed op；`IRegion<TKey,TValue>` 加 4 個 typed overload（無 `new` 修飾，純 overload）
- [x] `RegionInternal` / `LocalRegion` / `ThinClientRegion` 三層空殼建立（鏡像 cppcache `Region → RegionInternal → LocalRegion → ThinClientRegion`）
- [x] `Cache.InitializeCoreAsync` 從 `CacheXml.Regions` 預建 `ThinClientRegion` 寫入 `_regions`（含 refid 模板解析；commit `c830494`）

### Serialization

- [x] `Protocol/Serialization/IDataConverter` + 泛型版 + `SerializationRegistry`（per-cache Scoped；DSCode ↔ converter 雙向索引；`WriteObject` / `ReadObject` 中央 dispatch；對齊 cppcache `SerializationRegistry`）
- [x] `Int32DataConverter`（DSCode `CacheableInt32` = 57，4-byte BE）
- [x] `BooleanDataConverter`（DSCode `CacheableBoolean` = 53，1-byte）
- [x] `EventIdGenerator`（Scoped；`ThreadId=1` 常數 + 實例 seq；對齊 cppcache `EventIdTSS` instance scope，不能 static — 詳見「踩過的坑」）

### Wire 訊息 + region op 實作

- [x] `Put(7)` / `Request(0)` / `Destroy(9)` / `ContainsKey(38)` 全部走 `SerializationRegistry`（key / value / callbackArgument 一致路徑；no inline type guards）
  - [Protocol/TcrMessageBuilder.Put.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Put.cs)
  - [Protocol/TcrMessageBuilder.Get.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Get.cs)
  - [Protocol/TcrMessageBuilder.Destroy.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Destroy.cs) — `value=null, isUserNullValue=false` 分支（unconditional destroy）；conditional `remove(key, value)` 留以後
  - [Protocol/TcrMessageBuilder.ContainsKey.cs](src/Geode.Client/Protocol/TcrMessageBuilder.ContainsKey.cs)
- [x] `ThinClientRegion` 4 個 op 全部 end-to-end：
  - `ContainsKeyAsync` — Response part 0 → `bool`（commit `23f9f73`）
  - `PutAsync` — Reply OK / Exception
  - `GetAsync` — Response part 0 via `SerializationRegistry.ReadObject`（含 cppcache `readObjectPart` 對應的 lenObj/isObj 4 種情況：missing key → null）
  - `RemoveAsync` — Reply 最後一個 part 讀 entryNotFound i32（Phase 1.2 沒 versionTag 所以最後一個 part 一定是 entryNotFound；versionTag 落地時改順序解析）

### 踩過的坑（cppcache scope parity）

**Symptom**：`RegionCrudIntegrationTests` 第一次跑 3/5 過、2/5 fail — Put 看似成功（無 exception），但 Get 回 0、ContainsKey 回 false，像 server 把 Put 默默吃掉。

**Root cause**：`ClientProxyMembershipIdBuilder.s_uniqueTag` 我寫成 `static readonly`（process-wide singleton），但 cppcache `ClientProxyMembershipIDFactory::randString_` 是 **instance member**（每個 `CacheImpl` 一份）。同 process 內兩個 `Cache` 共用 clientId → 加上各自 `EventIdGenerator` 從 seq=1 開始 → server 的 `ClientHealthMonitor` 把 `(clientId, threadId=1, seq=1)` 第二次出現視為 duplicate event **靜默丟棄**。

**Fix**：
- [Protocol/ClientProxyMembershipIdBuilder.cs](src/Geode.Client/Protocol/ClientProxyMembershipIdBuilder.cs) — `s_uniqueTag` → `_uniqueTag` (instance field, ctor 生)
- [Internal/EventIdGenerator.cs](src/Geode.Client/Internal/EventIdGenerator.cs) — `_sequenceId` 維持 instance（uniqueTag per-cache 之後 clientId 跨 cache 不同 → seq 跨 cache 從 1 重來不會撞）

教訓寫進 [memory/cppcache-scope-parity.md](C:\Users\c_tom\.claude\projects\D--projects-tomi-GeodeSharp\memory\cppcache-scope-parity.md)：bucket-2 cppcache class 每個欄位的 `instance` / `static` / `thread_local` 都要鏡像，不要自作主張 optimize 成 static。

### 測試

- [x] Unit tests — 161/161 通過（含 `TcrMessageBuilderGetTests` / `PutTests` / `DestroyTests` 全部改成 int32 KV，外加 `ClientProxyMembershipIdBuilderTests` 加上 per-cache uniqueTag 驗證）
- [x] [RegionCrudIntegrationTests](tests/Geode.Client.IntegrationTests/RegionCrudIntegrationTests.cs) — 5 個 case（Put→Get、Get missing、ContainsKey 軌跡、Remove missing、Put 覆蓋）全綠對 `apachegeode/geode` 真機，含 3s `FreshConnectionSettleDelay` 防 cold-container race

### Deferred / 留待後續

- Built-in DSFID 型別 codec 擴充（string / byte[] / int64 / int16 / byte / float / double / DateTime / null / List / Dictionary / array / HashSet）— int32 + bool 已落地，其他 codec 等真的有 demo 需要時再補
- `PutGetIntegrationTests` / `GetDiagnosticTests` 五個 Skip — 是上 phase 用 byte[]/string 經由 raw `TcrConnection.SendRequestAsync` 的舊測試，等 String / Bytes codec 落地或乾脆刪掉（已被 RegionCrudIntegrationTests 涵蓋大半）
- `RegionView` 跟 `IRegion` op 殼的 unit test 還沒寫（行為已被整合測試蓋到，補 unit 是 nice-to-have）
- **`callbackArgument` overload**：cppcache `Region::put/get/destroy` 都收 `aCallbackArgument`（forward 給 server 端 CacheListener / CacheWriter / CacheLoader / PartitionResolver）。`TcrMessageBuilder.*` 已經接這個欄位（wire 對齊），但 `IRegion` / `IRegion<TKey,TValue>` 還沒暴露。等真的有需求或要對齊 cppcache public surface 時，加 overload：
  - `PutAsync(key, value, object? callbackArgument, CancellationToken)`
  - `GetAsync(key, object? callbackArgument, CancellationToken)`
  - `RemoveAsync(key, object? callbackArgument, CancellationToken)`
  - `ContainsKey` 不加（cppcache `containsKeyOnServer` 也沒收 callback）
  影響範圍：`IRegion` / `IRegion<TKey,TValue>` / `RegionInternal`（把 callback 版設 abstract、no-callback 版 forward 過去）/ `ThinClientRegion`（callback 改 canonical 實作）/ `RegionView`（typed + 顯式 IRegion 兩組 overload）。Builder 端不用動。
- Fresh-conn race（[memory](C:\Users\c_tom\.claude\projects\D--projects-tomi-GeodeSharp\memory\geode-fresh-conn-race.md)）— 用 `Task.Delay(3s)` 在測試端規避；正式 fix（pool warmup / readiness probe）留給 Phase 1.5

**下一步入口**：Phase 1.3.c — PutAll(56) + GetAll70(100)。Chunked-reply 基建已在 1.3.b 落地，1.3.c 主要是新 wire 訊息 + `GetAll` 端 keys-section / objects-section 真路徑（1.3.b 已寫的 decoder 第一次被「真實 hasObjects」打到）。

---

## Phase 1.3 — Bulk + management ops

### 1.3.0 — `IDataConverter` 內建型別擴充 ✅

Phase 1.2 只實作 `Int32` + `Boolean` 兩個 converter；bulk ops 端到端整合測試要更有代表性的 K/V 型別。先把 MVP scalar / string / bytes 一次補齊，後面 1.3.a–1.3.c 都吃這個前置。

**完工狀態**：
- 11 個 Tier A converter src + unit tests + integration tests 全綠（292 unit + 17 integration）
- `IDataConverter` API 改造完成（`DsCodes[]` / `GetDsCode(value)` / `Write(w, v, dsCode)` / `Read(r, dsCode)`），cppcache `Serializable::getDsCode()` 對齊
- `IRegion<TKey, TValue>` constraint `where TKey : IEquatable<TKey>`（編譯期擋集合 / `byte[]` / 無 IEquatable POCO）
- 順手修了 `BigEndianBinaryReader.ReadArrayLen` signed/unsigned bug（phase 1.1 留下來的潛在問題，length 128..252 被誤判負數）

**後續補強**（1.3.0 落地之後分別追加的工作）：

- **B 路 server-side type verification**（commit `2854ce4`）— Put/Get round-trip 無法證明 server 真的把 wire bytes 解成對的 Java 型別（encoder/decoder 同向出 bug 抓不到）。透過 `docker exec gfsh get` 讀 server 端 `Value Class` + `Value` 斷言，補上這個盲點。13 個 fact 涵蓋全部 Tier A converter（String 四個 DSCode variant 各一 fact）。`GeodeFixture` 加 `GfshAsync` helper + 容器 TZ=UTC（DateTime / java.util.Date 顯示穩定）。**意外發現**：gfsh 印 `java.util.Date` 用 raw ms-since-epoch（非 `Date.toString()`），精度直達 ms 強於原本計劃的秒級驗證。
  - **byte[] B 路 deferred** — gfsh 對 byte[] 印 `[B@<identityHash>`，沒值可驗。Phase 2 Java sidecar 補。
- **Tier B-1 primitive arrays 落地**（src + unit tests 已完成、整合 + B 路驗證待加）— 詳見下方 Tier B-1 段落。
- **文件結構整理**（commit `ab1d030`）— CLAUDE.md 把 Bucket 1 / Bucket 3 對應表移到 PORTING.md、Phase 1 sub-phase 細節 / MessageType 表 / Public API 介面 code block / Phase 1.1 bootstrap prompt 全部移除（reference data 各歸其位、過期模板砍掉），CLAUDE.md 從 456 → 406 行。

**架構決策（已拍板）：**

`IDataConverter` 介面改造（cppcache `Serializable::getDsCode()` + `Serializable::toData` 對齊）：

```csharp
interface IDataConverter
{
    byte[] DsCodes { get; }                              // decode 用，多 DSCode 對應同一 converter（String 4 個）
    Type ManagedType { get; }                            // encode lookup 用
    byte GetDsCode(object value);                                    // encode 時依 value 內容回實際 DSCode
    void Write(BigEndianBinaryWriter w, object value, byte dsCode);  // payload only；dsCode 由 registry 傳回避免 String 掃兩次
    object? Read(BigEndianBinaryReader r, byte dsCode);              // payload only；registry 已讀掉 DSCode byte、再傳回供 String 分支
}
```

`SerializationRegistry` 改動：
- `Register` 改成 loop `converter.DsCodes` 把每個都掛進 `_byDsCode`
- `WriteObject`：`var dsCode = converter.GetDsCode(value); writer.WriteByte(dsCode); converter.Write(writer, value, dsCode);`
- `ReadObject` 流程不變（registry 仍負責讀 DSCode byte + dict lookup）
- Read / Write 對稱：兩邊都 registry 處理 DSCode byte、converter 只處理 payload

**Tier A — Phase 1.3.0 範圍（9 個 converter + String 一 converter 多 DSCode）：**

| DSCode | cppcache | CLR | 備註 | 狀態 |
|---|---|---|---|---|
| 53 | `CacheableBoolean` | `bool` | | ✅ Phase 1.2 |
| 54 | `CacheableCharacter` | `char` | UTF-16 code unit, 2-byte BE | [ ] |
| 55 | `CacheableByte` | `byte` | 故意用 unsigned（.NET 慣例），wire bit pattern 與 Java signed byte 互通；Java 端 -1 ↔ 我們 255 | [ ] |
| 56 | `CacheableInt16` | `short` | | [ ] |
| 57 | `CacheableInt32` | `int` | | ✅ Phase 1.2 |
| 58 | `CacheableInt64` | `long` | | [ ] |
| 59 | `CacheableFloat` | `float` | IEEE-754 BE, NaN/±∞ wire 形狀與 Java 一致 | [ ] |
| 60 | `CacheableDouble` | `double` | IEEE-754 BE | [ ] |
| 61 | `CacheableDate` | `DateTime` | 8-byte ms-since-epoch UTC. Read 回 `Kind=Utc`（偏離 clicache 的 `Local`，修 round-trip footgun）；Write `Utc` 直用 / `Local` → `ToUniversalTime` / `Unspecified` **throw `ArgumentException`**（拒絕沉默假設 Local，clicache bug 修正）；精度 truncate to ms | [ ] |
| 46 | `CacheableBytes` | `byte[]` | VL-encoded length + raw bytes（1/3/5 byte prefix）；`null` 走 NullObj、`byte[0]` 走 DSCode 46 + length=0；**不可當 Key**（`Array` 不實作 `IEquatable<T>`、cppcache `CacheableArrayPrimitive` 不繼承 `CacheableKey`，編譯期被 `where TKey : IEquatable<TKey>` 擋掉）；**順手修了 `ReadArrayLen` signed/unsigned bug**（length 128..252 範圍原本被誤判為負數） | ✅ |
| 42 / 87 / 88 / 89 (+69 read-only) | `CacheableString` / `…ASCIIString` / `…ASCIIStringHuge` / `…StringHuge` (+`CacheableNullString`) | `string` | 一 converter 多 DSCode；ASCII vs modified UTF-8 × short(u16) vs huge(u32) — 但 huge UTF 路徑用 **UTF-16 BE** 不是 modified UTF-8 huge（對齊 cppcache `writeUtf16Huge`）；69 是 read-only null sentinel；`BigEndianBinaryReader.ReadJavaModifiedUtf8` 從 stub 補成實作 | ✅ |

**Tier B-1 — primitive arrays ✅（後續補強）**

8 個 converter src + 62 unit tests 落地（unit total 323 → 385）。Wire 形狀：`WriteArrayLen` 1/3/5 byte VL prefix + N × 元素位元（primitive raw bytes / `string[]` 每元素自己的 DSCode+payload）。整合測試 + B 路驗證仍待加。

| DSCode | cppcache | CLR | 備註 |
|---|---|---|---|
| 26 | `BooleanArray` | `bool[]` | VL length + N×1 byte；decode tolerant 任何非 0 byte = true |
| 27 | `CharArray` | `char[]` | VL length + N×u16 BE（Java `char[]`，不是 UTF-8） |
| 47 | `CacheableInt16Array` | `short[]` | |
| 48 | `CacheableInt32Array` | `int[]` | VL 邊界（252 / 253 / 65536）unit test 集中寫在這檔，其他 array 共用 ReadArrayLen/WriteArrayLen 不重複 |
| 49 | `CacheableInt64Array` | `long[]` | |
| 50 | `CacheableFloatArray` | `float[]` | IEEE-754 BE，NaN / ±Infinity bit-pattern 保留 |
| 51 | `CacheableDoubleArray` | `double[]` | |
| 64 | `CacheableStringArray` | `string[]` | **唯一**收 `SerializationRegistry` ctor 注入；每元素重入 `WriteObject` 走完整 DSCode dispatch（per-element 42 / 87 / 88 / 89 / 41 都可能）；`null` 元素走 NullObj=41 由 registry 一層處理；`new this(this)` 安全（converter 只存 reference、Write/Read 才使用，那時 registry 已完整 populated） |

**Tier B-2 — 集合 ✅**（主要型別完成；Vector / LinkedHashSet deferred）

核心架構（ArrayList 落地時建立、後續 5 個 collection converter 共用）：

- **`TypedResultAdapter`**（Scoped DI；[TypedResultAdapter.cs](src/Geode.Client/Protocol/Serialization/TypedResultAdapter.cs)）— Java wire 不帶 container element type，所有 collection converter 的 `Read` 都回 canonical `<object?>`-element 容器；adapter 在 `RegionView` 邊界遞迴下降把 `object?` 重塑成宣告 `TValue`（`IList<int>` / `IList<IList<string>>` / `IDictionary<int, IList<string>>` / 等都通）。Two-pass cost MVP 可接受；profiling 顯示問題才把 hint 下推到 converter（API 不會破壞）。
- **`SerializationRegistry` open-generic write fallback**（[SerializationRegistry.cs](src/Geode.Client/Protocol/Serialization/SerializationRegistry.cs)）— `_byType[runtimeType]` miss 且 `runtimeType.IsGenericType` 時二次查 `GetGenericTypeDefinition()`；單字典雙探，不增加索引。Tier B-2 所有 converter `ManagedType` 都用 open generic（`typeof(List<>)` / `typeof(HashSet<>)` / `typeof(Dictionary<,>)` / `typeof(LinkedList<>)` / `typeof(Stack<>)`），一個 instance 通吃所有閉式具現。
- 涉檔（架構）：上述兩支 + [RegionView.cs](src/Geode.Client/Services/RegionView.cs)（adapter 注入）/ [Cache.cs](src/Geode.Client/Services/Cache.cs)（primary ctor 多收 adapter）/ [GeodeClientExtensions.cs](src/Geode.Client/GeodeClientExtensions.cs)（Scoped DI 註冊）。

Converter 清單：

| DSCode | cppcache | CLR | 狀態 | 備註 |
|---|---|---|---|---|
| 52 | `CacheableObjectArray` | `object[]` | ✅ commit `0671ae1` | 寫死 `"java.lang.Object"` Java class header + per-element re-entry |
| 65 | `CacheableArrayList` | `List<T>` / `IList<T>` 系列 | ✅ | 架構初登場（adapter + open-generic dispatch） |
| 10 | `CacheableLinkedList` | `LinkedList<T>` | ✅ | wire 與 ArrayList 完全一樣（cppcache 底層都 `std::vector`）；adapter 獨立 `LinkedList<>` branch（`LinkedList<T>` 不實作 `IList<T>`，不能與 `List<>` 共 branch） |
| 66 | `CacheableHashSet` | `HashSet<T>` / `ISet<T>` / `IReadOnlySet<T>` | ✅ | canonical decode 是 `HashSet<object?>`（Java HashSet 容許 null 元素，C++ 不容許但 wire 統一）；HashSet<T> 不實作非泛型 ICollection，write 端要先 collect 進 scratch list 拿 count |
| 67 | `CacheableHashMap` | `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` | ✅ | wire key/value **交錯**（不是 keys-then-values）；canonical decode 是 `Dictionary<object, object?>`；null key 在 read 端拒絕（Java HashMap 容許但 .NET Dictionary 不容；明訊息 > 沉默死） |
| 74 | `CacheableStack` | `Stack<T>` | ✅ | **write reverse** 對齊 clicache `Linq::Enumerable::Reverse(stack)`（.NET Stack iteration top→bottom，wire 要 bottom→top）；read plain push；adapter 端再反轉一次補償 `Stack<T>(IEnumerable<T>)` ctor 的 push-in-iteration-order 反向特性 |
| 71 | `CacheableVector` | — | [ ] | Java legacy thread-safe ArrayList；.NET 沒等價物（強行對 `List<T>` 會跟 ArrayList 撞 ManagedType）；等真有需求再做 |
| 73 | `CacheableLinkedHashSet` | — | [ ] | .NET 沒「保持插入順序的 Set」；要做需新型別（`Geode.Client.Collections.OrderedSet<T>` 之類），是 public API 決策不是技術問題；先跳過 |

**測試狀態**：464 unit + 18 collection integration 全綠。Tier B-2 直屬 unit 共 79（ListDataConverter 9 / HashSet 8 / Dictionary 8 / LinkedList 6 / Stack 7 / SerializationRegistry open-generic 5 / TypedResultAdapter 36），integration 11 round-trip + 4 B-route + 3 nested。

**記到 memory 的 gfsh quirks**（[gfsh-arraylist-format.md](C:\Users\c_tom\.claude\projects\D--projects-tomi-GeodeSharp\memory\gfsh-arraylist-format.md)）：

- 集合（ArrayList / LinkedList / HashSet / Stack）`Value :` 印 `[1,2,3]` **無空格**（不是 Java 標準 `[1, 2, 3]`）
- HashMap 印 **JSON-like** `{"42":"answer"}` — 雙引號連 Integer key 都加，不是 Java 標準 `{42=answer}`

**Tier C — 不做或 Phase 2+：**
`NullObj(41)` 已內聯；`CacheableNullString(69)` 走 41 即可；`PdxType/PDX/PDX_ENUM` Phase 2；`CacheableUserData*` Phase 2；`Properties(11)` Phase 3 auth；`JavaSerializable(44)`/`DataSerializable(45)`/`Class(43)`/`CacheableFileName(63)`/`CacheableTimeUnit(68)` 罕用，skip；`FixedID*(1–4)` 是 wire layer 內部碼，不放 `SerializationRegistry`。

---

### 1.3.a — Clear + Invalidate（非分片）✅

**完工狀態**：323 unit tests（先前 292 + 新增 31）+ 22 integration tests（先前 17 + 新增 5）全綠對 `apachegeode/geode` 真機。

- [x] `IRegion.ClearAsync(CancellationToken)` / `IRegion.InvalidateAsync(object, CancellationToken)` + typed `IRegion<TKey,TValue>.InvalidateAsync(TKey, CancellationToken)`（無 typed `ClearAsync` overload — 無 K/V 參數）
- [x] `RegionInternal` 加 2 個 abstract；`RegionView` typed forward + 顯式 `IRegion.InvalidateAsync` 實作
- [x] `ClearRegion(36)` — 2 parts（regionName / eventId）或 3 parts（含 callback）；對齊 cppcache `TcrMessageClearRegion` (`TcrMessage.cpp:1644-1682`)；reply `Reply(6)` / `ClearRegionDataError(37)` / `Exception(2)` / 其他 → throw；沒有 chunked
  - [Protocol/TcrMessageBuilder.ClearRegion.cs](src/Geode.Client/Protocol/TcrMessageBuilder.ClearRegion.cs)
  - `millisecondsResponseTimeout` part **不實作** — cppcache `ThinClientRegion::clear` (`ThinClientRegion.cpp:777`) 寫死傳 `-1`，正常路徑從不發
  - `localClearNoThrow` + `invokeCacheListenerForRegionEvent(AFTER_REGION_CLEAR)` 略過（Phase 2+ caching-enabled 才需要）
- [x] `Invalidate(83)` — 3 parts（regionName / key / eventId）或 4 parts（含 callback）；對齊 cppcache `TcrMessageInvalidate` (`TcrMessage.cpp:1896-1932`)；reply `Reply(6)` / `Exception(2)` / `InvalidateError(84)` / 其他 → throw；versionTag 先丟（同 `RemoveAsync`）
  - [Protocol/TcrMessageBuilder.Invalidate.cs](src/Geode.Client/Protocol/TcrMessageBuilder.Invalidate.cs)
  - 比 Destroy 少 `expectedOldValue` / `Operation` 兩個 NullObj part（Invalidate 沒有 conditional overload 共用 ctor）
- [x] `ThinClientRegion.ClearAsync` / `InvalidateAsync` 端到端 — 日誌對齊 cppcache `LOGFINE` / `LOGERROR` 嚴重度
- [x] Unit tests — `TcrMessageBuilderClearRegionTests`（15 cases）+ `TcrMessageBuilderInvalidateTests`（16 cases）
- [x] [RegionInvalidateClearIntegrationTests](tests/Geode.Client.IntegrationTests/RegionInvalidateClearIntegrationTests.cs) — 5 cases（Invalidate keeps key clears value / missing-key invalidate OK / Put after Invalidate restores / Clear removes-all keeps-region / Clear on empty region OK）

**不暴露**：`InvalidateRegion(55)` 是 server→client only，要 region-wide 就 `ClearAsync`

### 1.3.b — Chunked-reply 基建 + RemoveAll ✅

**完工狀態**：5/5 RemoveAll integration tests 通過對 `apachegeode/geode` 真機。Chunked-reply 解碼整條 wire 跑通（含 versioned region 的 `VersionTag.FromData` 路徑）。

#### Wire 請求 + 入口

- [x] `RemoveAll(109)` — 5+keys.Count parts（region / eventId / flags=0 / callback-or-NullObj / keyCount / N keys）；對齊 cppcache `TcrMessageRemoveAll` (`TcrMessage.cpp:2424-2468`)
  - [Protocol/TcrMessageBuilder.RemoveAll.cs](src/Geode.Client/Protocol/TcrMessageBuilder.RemoveAll.cs)
- [x] `EventIdGenerator.NextRange(int count)` — Interlocked.Add 一次保留 N 個連續 seq id（cppcache `writeEventIdPart(keys.size()-1)` 對應）
- [x] `IRegion.RemoveAllAsync(IReadOnlyCollection<object>, ct)` + `IRegion<TKey,TValue>.RemoveAllAsync(IReadOnlyCollection<TKey>, ct)` + `RegionView` typed forward（reference TKey 走 covariance、value TKey box 進 `object[]`）
- [x] `ThinClientRegion.RemoveAllAsync` body — build → `EventIdGenerator.NextRange(N)` → dispatch → REPLY/RESPONSE/EXCEPTION switch

#### DM / 連線層 chunked 路徑

- [x] `ThinClientBaseDM.SendSyncRequestAsync(TcrMessage, TcrChunkedResult, ...)` abstract overload
- [x] `ThinClientPoolDM.SendSyncRequestAsync(req, chunkedResult, ...)` — SelectEndpoint → AddEP → forward
- [x] `ThinClientPoolDM.SendRequestToEndpointAsync` chunked overload — borrow conn → 呼 `TcrConnection.SendRequestAsync(req, chunkedResult, ct)` → put-back / disconnect-on-error，整體跟非 chunked overload 形狀對齊
- [x] `TcrConnection.SendRequestAsync(req, TcrChunkedResult, ct)` overload — **inline chunked-reply 迴圈**（cppcache `readMessageChunked` 對應）：17-byte 首 frame header + 5-byte 後續 chunk header + last-chunk bit
- [x] `TcrConnection.Touch()` 空殼 + `PutInQueueAsync` 呼叫（Phase 1.5 `cleanStaleConnections` 用 `_lastAccessed` 真填）

**關鍵設計校正**：cppcache `m_pendingReplies` / 背景 reader 那層**我們不需要**。cppcache chunked 路徑是 **inline** 同步讀（`readMessageChunked` 在發送 thread 上接著跑），一條 connection 一次只服務一個 request。Audit 前期誤判要做 `_pendingReplies` 表跟背景 reader，看 cppcache 真碼後刪掉。

#### Chunked-result handler 階層

- [x] `TcrChunkedResult` abstract base（[Protocol/TcrChunkedResult.cs](src/Geode.Client/Protocol/TcrChunkedResult.cs)）— `HandleChunk(payload, isLastChunk)` + `Reset()`；cppcache 的 `finalize` / `binary_semaphore` / `m_ex` / `m_dsmemId` 槽位全部砍掉（Task/await + exception 自然冒泡 + Phase 4 才需要 dsmemId）
- [x] `ChunkedRemoveAllResponse` ([Services/ChunkedRemoveAllResponse.cs](src/Geode.Client/Services/ChunkedRemoveAllResponse.cs)) — `Reset` 對齊 cppcache 2 步（null+size guard → clear versionTags）；`HandleChunk` 5 步：
  - Step 1：wrap payload 進 `BigEndianBinaryReader`（via `ActivatorUtilities`）
  - Step 2：`TcrMessageHelper.ReadChunkPartHeader` 分類 chunk
  - Step 3a：`NullObject` → return（空 reply）
  - Step 3b：`Object` → `new VersionedCacheableObjectPartList` + `FromData` + `list?.AddAll`
  - Step 3c：`Bytes` → 讀 2 bytes（single-hop metadata，Phase 4 真用）
  - fallthrough：`Exception` / unknown → throw `GeodeException`
- [x] `TcrMessageHelper.ReadChunkPartHeader` — 9 步完整 impl（partLen + isObj → NullObject / Exception 早出；DSCode 分支 JavaSerializable / NullObj / FixedIDByte+compId / 不符 → throw）
- [x] `ChunkObjectType` enum（`NullObject` / `Object` / `Exception` / `Bytes`）

#### VersionedObjectPartList 解碼器（真實作）

- [x] `CacheableObjectPartList` base（cppcache 對齊；primary ctor 收 `RegionInternal region`；9 個 protected 欄位 mirror cppcache `m_*`）
- [x] `VersionedCacheableObjectPartList` — primary ctor `(IServiceProvider, SerializationRegistry, ILogger, RegionInternal)`；
  - 7 個 wire 欄位 + 4 個 FLAG_* 常數 + `VersionTags` accessor + `Size` 屬性（cppcache `size()` 對應）
  - `FromData` 7 步真實作（在 `lock(_responseLock)` 內）：flags byte parse / init Values / 空訊息 LogDebug / keys section（`_hasKeys` 真讀 keys → tempKeys/ResultKeys/localKeys） / objects section（`hasObjects` → `ReadObjectPart` 進 _byteArray+Values） / version tags section（`_hasTags` switch on 4 FLAG_*） / putLocal merge（Phase 4+ NIE）
  - `AddAll(other)` 真實作（cppcache `addAll` 3 步：merge keys / OR-in regionIsVersioned / merge versionTags）
  - `ReadObjectPart` 真實作（3 分支：exception=2 → wrap `GeodeException` 進 `Exceptions` / `_serializeValues=true` → raw bytes / 一般 → `serializationRegistry.ReadObject`）
- [x] `BigEndianBinaryReader.ReadUnsignedVL` 真實作（Java VL unsigned u64，1-9 bytes、9-byte cap throw `InvalidDataException`）
- [x] `BigEndianBinaryReader.AdvanceCursor(int)` 真實作 / `ReadString` 暫 NIE（exception part 才呼到）

#### VersionTag + DiskVersionTag

- [x] `VersionTag` — primary ctor `(IServiceProvider, ILogger, MemberListForVersionStamp?)`；7 個欄位（`_bits` / `_entryVersion` / `_regionVersionHighBytes` / `_regionVersionLowBytes` / `_internalMemId` / `_previousMemId` / `_timeStamp`）+ 5 個 `HAS_*`/`VERSION_TWO_BYTES`/`DUPLICATE_MEMBER_IDS` 常數 + 3 個 `BITS_*` 常數
  - `FromData` 8 步真實作（flags / bits / skip distributedSystemId / entryVersion 16-or-32 / regionVersionHighBytes optional / regionVersionLowBytes / timeStamp VL / virtual `ReadMembers` 派發）
  - `ReadMembers` 2 步真實作（`HAS_MEMBER_ID` → `ClientProxyMembershipID.ReadEssentialData` + `MemberListForVersionStamp.Add` → `_internalMemId`；`HAS_PREVIOUS_MEMBER_ID` 含 `DUPLICATE_MEMBER_IDS` 短路）
  - `ReplaceNullMemberId(memId)` 真實作（4 行 if-設值）
- [x] `DiskVersionTag` (`internal sealed : VersionTag`) — `ReadMembers` override NIE（persistent region 才碰到 DiskStoreId 解碼，Phase 4+）
- [x] `ClientProxyMembershipID` — primary ctor 收 `SerializationRegistry`（DI 注入）；`ReadEssentialData` 真實作（cppcache 7-field wire format：array length + hostAddr bytes + hostPort + skip flag + vmKind + uniqueTag/vmViewIdStr（loner 分支） + dsName）
- [x] `MemberListForVersionStamp` — `Add` 真實作（簡化版：monotonic id 不做 hashKey dedup，Phase 4 補）；`GetDsMember` 真實作（dict lookup + lock）
- [x] `DSFid` enum（25 個 entry，含 `VersionedObjectPartList = 7` / `DiskVersionTag = 2131` 等，跟 cppcache 1:1）

#### 命名 / 型別注入慣例

- [x] CLAUDE.md 第 9 條原則：**cppcache wire 鏡像常數用 `SCREAMING_SNAKE_CASE`**（`FLAG_NULL_TAG` / `HAS_MEMBER_ID`）；自製 C# 常數 PascalCase（`MetaTransactionId` / `ThreadId`）。`.editorconfig` 不強制
- [x] **Internal 類別注入「最具體必要型別」而非介面**：`ChunkedRemoveAllResponse` 收 `ThinClientRegion`、`VersionedCacheableObjectPartList` / `CacheableObjectPartList` 收 `RegionInternal`——避免 future downcast 風險
- [x] **`ActivatorUtilities.CreateInstance` 廣泛採用**：`ChunkedRemoveAllResponse` / `VersionedCacheableObjectPartList` / `VersionTag` / `DiskVersionTag` / `ClientProxyMembershipID` / `BigEndianBinaryReader` 都走 ActivatorUtilities，DI 依賴自動注入

#### 測試

- [x] [RegionRemoveAllIntegrationTests](tests/Geode.Client.IntegrationTests/RegionRemoveAllIntegrationTests.cs) — 5 cases（4-key batch / mixed present+missing / empty arg / null arg / single-key N=1 邊界）全綠對 `apachegeode/geode` 真機，15 秒
- [x] [TcrMessageBuilderRemoveAllTests](tests/Geode.Client.Tests/Protocol/TcrMessageBuilderRemoveAllTests.cs) — 3 unit tests（header+5+N 部數 / 全 part 對齊 cppcache wire bytes / 空 keys ArgumentException）；落地時順帶補在 1.3.c 階段

#### Deferred / 留待後續

- **NIE 仍存在但 RemoveAll 不踩**：`DiskVersionTag.ReadMembers`（persistent region，Phase 4+）/ `BigEndianBinaryReader.ReadString`（exception chunk，Phase 1.3.c GetAll 才可能）/ Step 7 `putLocal` merge（`AddToLocalCache`，Phase 4+ client-side caching）
- **欄位仍 placeholder**：`_endpointMemId` / `_msg`（pragma CS0649 包住）—— Phase 3 auth / Phase 4 single-hop 才寫入
- `MemberListForVersionStamp.Add` 的 hashKey dedup 跳過——需要 `ClientProxyMembershipID.HashKey`，Phase 4 補
- **架構決策已收進 memory 或 CLAUDE.md**：
  - constants naming convention（CLAUDE.md #9）
  - internal class 注入最具體型別（待 memory）

### 1.3.c — PutAll + GetAll70 ✅

**完工狀態**：6/6 PutAll + GetAll integration tests 全綠對 `apachegeode/geode` 真機；509 unit tests（含新增 9 個 = RemoveAll 3 / PutAll 3 / GetAll 3 wire-shape tests）。chunked-reply 在 1.3.b 已落地，1.3.c 主要是新 wire 訊息 + GetAll 端 `hasObjects=true` 真路徑首次觸發。

#### 公開 API

- [x] `IRegion.PutAllAsync(IReadOnlyDictionary<object, object>, CancellationToken)` + typed `IRegion<TKey,TValue>.PutAllAsync(IReadOnlyDictionary<TKey, TValue>, ct)`
- [x] `IRegion.GetAllAsync(IReadOnlyCollection<object>, ct) → Task<IReadOnlyDictionary<object, object?>>` + typed `IRegion<TKey,TValue>.GetAllAsync → Task<IReadOnlyDictionary<TKey, TValue?>>`
- [x] `RegionInternal` 加 2 個 abstract；`RegionView` typed forward + 顯式 `IRegion` 實作

#### Wire 訊息

- [x] `PutAll(56)` — 5+`map.Count`*2 parts（region / eventId / **skipCallbacks 佔位 int=0** / flags=0 / count / N×(key,value) 交錯）；對齊 cppcache `TcrMessagePutAll` (`TcrMessage.cpp:2354-2422`)；callback overload (`PutAllWithCallback=108`) 收 callback 但 throw `NotSupportedException` — Phase 1.3 不暴露
  - [Protocol/TcrMessageBuilder.PutAll.cs](src/Geode.Client/Protocol/TcrMessageBuilder.PutAll.cs)
- [x] `GetAll70(100)` — 3 parts（region / **inline CacheableObjectArray keys** / int(0) callback placeholder）；對齊 cppcache `TcrMessageGetAll` ctor + `InitializeGetallMsg` (`TcrMessage.cpp:2470-2523`)；keys section inline 寫 `[52][arrayLen][43][writeString "java.lang.Object"][N × WriteObject(key)]` —— **重點**：`writeString` 本身會加 DSCode prefix（cppcache `DataOutput::writeString` 行為一致）
  - [Protocol/TcrMessageBuilder.GetAll.cs](src/Geode.Client/Protocol/TcrMessageBuilder.GetAll.cs)

#### Region op 實作

- [x] `ThinClientRegion.PutAllAsync` 4-step：NextRange(N) / build / `ChunkedPutAllResponse` + dispatch / reply switch（Reply/Response/Exception/PutDataError/default）
- [x] `ThinClientRegion.GetAllAsync` 5-step：keys materialise → IReadOnlyList<object> / build / 計算 `addToLocalCache = true && (Attributes.CachingEnabled ?? false)`（對齊 cppcache `LocalRegion::getAll_internal` 寫死 true + `getAllNoThrow_remote` AND with caching-enabled）→ `ChunkedGetAllResponse` + dispatch / reply switch（Response/Exception/GetAllDataError/default）/ return `chunkedResult.Values`

#### Chunked-result handlers

- [x] `ChunkedPutAllResponse`（[Services/ChunkedPutAllResponse.cs](src/Geode.Client/Services/ChunkedPutAllResponse.cs)） — 結構與 `ChunkedRemoveAllResponse` 1:1，5 步 HandleChunk（NullObject / Object / Bytes / Exception）+ 2 步 Reset
- [x] `ChunkedGetAllResponse`（[Services/ChunkedGetAllResponse.cs](src/Geode.Client/Services/ChunkedGetAllResponse.cs)） — 比 PutAll/RemoveAll 多了：(1) 收 `keys: IReadOnlyList<object>` ctor 參數（chunk reply 用 `Keys[index + KeysOffset]` 反查 caller 送的 key）；(2) `addToLocalCache: bool` ctor 參數；(3) `_values` / `_exceptions` / `_resultKeys` / `_keysOffset` 累積器；(4) HandleChunk 把 shared accumulator 餵給 VCOPL.Initialize，後讀 `vcObjPart.ConsumedObjectCount` 推進 `_keysOffset`；(5) **沒有 NullObject / Bytes 分支** — cppcache GetAll 嚴格只接 Object/Exception；(6) `Values` accessor 揭露為 `IReadOnlyDictionary<object, object?>`

#### VersionedCacheableObjectPartList 變動

- [x] 加 `Initialize(keys, keysOffset, values, exceptions?, resultKeys?, addToLocalCache)` 方法（鏡像 cppcache 10-arg ctor 的角色）；GetAll chunked handler 用這個把累積器注入到 per-chunk 實例
- [x] 加 `ConsumedObjectCount` accessor（`_byteArray.Count`） — cppcache 用 `uint32_t* m_keysOffset` 共享指標推進，我們改成 post-FromData 顯式 read-back
- [x] Step 7 (`putLocal` merge) NIE 加 gate：`if (hasObjects && AddToLocalCache)` —— Phase 1.3 MVP `AddToLocalCache` 因 `CachingEnabled=null/false` 被 AND 成 false，這個 NIE 永遠不踩到，Phase 4+ client-side caching 才實作

#### addToLocalCache 流轉（cppcache 完整鏡像）

```
ThinClientRegion.GetAllAsync
  ├── const addToLocalCacheRequested = true   ← cppcache LocalRegion::getAll_internal:585 寫死
  └── addToLocalCache = requested && (Attributes.CachingEnabled ?? false)
                                     ↑ cppcache getAllNoThrow_remote:1100 AND
       ↓
ChunkedGetAllResponse ctor (addToLocalCache: bool, stored as field)
       ↓
VCOPL.Initialize(..., addToLocalCache)
       ↓ stored on AddToLocalCache field
VCOPL.FromData Step 7 gate: if (hasObjects && AddToLocalCache) → Phase 4+ NIE
```

#### 踩過的坑

**(1) VersionTag ActivatorUtilities ctor 匹配失敗**

- Symptom：`A suitable constructor for type 'Geode.Client.Protocol.VersionTag' could not be located` —— 整合測試 GetAll 第一次跑就炸
- Root cause：`ActivatorUtilities.CreateInstance<VersionTag>(sp, memberListForVersionStamp!)` 傳 null，runtime ctor matcher 無法從 null 推型別
- 為何 1.3.b RemoveAll 沒踩到：REPLICATE region 預設 `concurrency-checks-enabled=false`，server reply 不 ship version tags，VCOPL step 6 整段不進；GetAll reply 觸發 _hasTags 進 step 6
- Fix：`MemberListForVersionStamp` 註冊成 Scoped DI（per-cache，鏡像 cppcache `CacheImpl::m_memberListForVersionStamp` instance scope）；`NewVersionTag` 簽名移掉 `MemberListForVersionStamp?` 參數，純走 DI 解析
- 涉檔：[GeodeClientExtensions.cs](src/Geode.Client/GeodeClientExtensions.cs)（DI 註冊）/ [VersionedCacheableObjectPartList.cs](src/Geode.Client/Protocol/VersionedCacheableObjectPartList.cs)（NewVersionTag 簽名）

**(2) `IRegion<TKey, TValue?>` 對 value-type TValue 的 null 語意 footgun**

- Symptom：`xUnit2002: Do not use Assert.Null() on value type 'int'`
- Root cause：`TValue?` 對 unconstrained T **只是編譯期 nullability annotation**，runtime 對 value type 不會 wrap 成 `Nullable<T>`；missing key 會 collapse 到 `default(int)=0`，無法區分 missing vs 真實存的 0
- Fix：`RegionView.GetAllAsync` 跳過 null wire values → typed dict 不含 missing keys → caller 用 `TryGetValue` / `ContainsKey` 偵測（.NET idiomatic）；non-typed 入口維持 cppcache parity（null 留在 dict）
- Phase 1.2 PutAsync / PutAll 都 ArgumentNullException-guard value → region 不可能存 null，wire 的 null **必定**是 cppcache miss-flag-3，跳過安全
- 涉檔：[RegionView.cs](src/Geode.Client/Services/RegionView.cs)（typed 邊界過濾 null）/ [IRegion.cs](src/Geode.Client/IRegion.cs)（XML doc 對齊新語意）

**(3) cppcache `DataOutput::writeString` 不是 `writeUTF`**

- 一開始我以為 cppcache `writeString("java.lang.Object")` 就是 `writeUTF`（u16 length + bytes，無 DSCode prefix），寫單元測試期望這個 wire 形狀，跑起來 5/6 pass、GetAll layout test 1 失敗
- 實際：cppcache `DataOutput::writeString`（[DataOutput.hpp:264-305](D:\github\geode-native\cppcache\include\geode\DataOutput.hpp#L264)）**會加 DSCode prefix**（ASCII → `CacheableASCIIString=87`，含非 ASCII → `CacheableString=51`，huge 變體類推）。GetAll keys section 完整 wire：`[52][arrayLen][43][87][u16 length][bytes][N × key]`
- 我們 `BigEndianBinaryWriter.WriteString` 跟 cppcache 一致；單元測試期望值改對即可，src 不用改

#### 測試

- [x] Unit tests — `TcrMessageBuilderPutAllTests`（3 個：header / per-part wire 對齊 / empty map）+ `TcrMessageBuilderGetAllTests`（3 個：header / per-part wire 對齊 incl. `CacheableASCIIString` prefix in class header / empty keys）+ `TcrMessageBuilderRemoveAllTests`（3 個，順手補了 1.3.b 漏的）；總計 509 unit tests
- [x] [RegionPutAllIntegrationTests](tests/Geode.Client.IntegrationTests/RegionPutAllIntegrationTests.cs) — 3 cases（4-key batch 寫入＋ Get 驗值 / 覆寫既存 key / 空 map ArgumentException）
- [x] [RegionGetAllIntegrationTests](tests/Geode.Client.IntegrationTests/RegionGetAllIntegrationTests.cs) — 3 cases（4-key 全 present / mixed present+missing missing-keys 從 typed dict 省略 / 空 keys ArgumentException）

#### Deferred / 留待後續

- `PutAllWithCallback(108)` / `GetAllWithCallback(107)` callback overload — builder 收 callback 參數但 throw `NotSupportedException`；要落地時改 msg type 一行 + IRegion 加 overload
- 多 keys 跨 chunk 邊界的 `_keysOffset` 推進路徑沒被測過（單 chunk happy path 已測） — 拆 chunk 邊界靠 server framing；要刻意觸發要 ship 大量 keys
- `_exceptions` / `_resultKeys` 累積器宣告但未曝光於 public surface（Phase 3+ 例外路徑 / Phase 4+ single-hop）

**下一步入口**：Phase 1.4 — OQL Query。Phase 1.3 子階段（1.3.0 / 1.3.a / 1.3.b / 1.3.c）全部完工，Phase 1 MVP 還剩 OQL 查詢（1.4）跟連線管理（1.5）。

### Phase 1.3 共用決策

- bulk ops 進用 `IReadOnlyDictionary` / `IReadOnlyCollection`、出用新 `Dictionary` / `IReadOnlyDictionary`（.NET 慣例 + 不洩漏內部 mutable state）
- versionTag 全部忽略（讀完丟），同 Phase 1.2 `RemoveAsync`；Phase 4 client-side cache / delta 才回填
- **Key 型別約束**：`IRegion<TKey, TValue>` 加 `where TKey : IEquatable<TKey>`（cppcache `CacheableKey` 強制 `operator==` + `hashcode()` 的 .NET 等效）
  - 編譯期擋住 `byte[]`（`Array` 不實作 `IEquatable<T>`）、`List<>` / `Dictionary<>` / `HashSet<>` 等集合、未實作 `IEquatable<T>` 的 user POCO
  - PDX user class（Phase 2）必須實作 `IEquatable<T>`，強迫使用者面對 Java server 端 `equals` / `hashCode` 語意問題
  - **沒對應 converter 的型別只能 runtime 擋**：`IRegion<MyType, ...>` 編譯過、但 `SerializationRegistry.WriteObject` 找不到 `_byType[typeof(MyType)]` 時 throw `NotSupportedException`（既有行為，不用動）
  - 非泛型 `IRegion` 不加約束（untyped `GetRegion` 回它，cast 到泛型版時編譯期擋）

---

## DI surface 重塑 — `IGeodeCacheFactory` + `GeodeClientExtensions`（未啟動）

**性質**：Phase 0 既有設計的回頭重塑，不算新 phase。範圍 `src/Geode.Client/IGeodeCacheFactory.cs` + `src/Geode.Client/Services/GeodeCacheFactory.cs` + `src/Geode.Client/GeodeClientExtensions.cs` + 全部 options class（加 `DeepClone`）+ 對應測試。

### 背景

Phase 0 的設計：`AddGeodeClient` 三個 overload（unnamed + optional `name`）；`IGeodeCacheFactory.Get(name)` 一個方法走 lazy build；DI 容器同時暴露 `IGeodeCache` (unnamed alias) 跟 `[FromKeyedServices(name)] IGeodeCache` (keyed)。對 Phase 0 來說可以動，但有幾個累積的問題：

- `Get(name)` lazy build 行為跟「找不到丟例外」直覺衝突
- DI keyed singleton 一旦資源 dispose（例如未來加 `RemoveAsync`）就持著 stale instance
- 沒有 cacheName / configName 的解耦概念，多 cluster 共用 config 或 runtime 覆蓋 config 都做不到
- `IGeodeCacheFactory` 只有 `Get`，沒有列舉 / 移除 / 顯式建構入口

### 討論流程的關鍵分歧點

1. **`Get` 找不到怎麼辦** — `null` / `bool` / `KeyNotFoundException` 三選。最終：`Get` 丟 `KeyNotFoundException`、`TryGet` 回 bool。對齊 `IServiceProvider.GetRequiredService` / `GetService`。
2. **Cache 是否該由 factory 統一管理** — 一度收斂到「完全只走 factory，砍掉 `IGeodeCache` 直接注入」。後來考慮到 95% 使用者只有一個 cluster + EF Core 的雙注入 pattern，改成兩層：簡易層直接注入 `IGeodeCache`、進階層走 `IGeodeCacheFactory`。
3. **Manual Create 還是 auto Create** — 選 manual。`AddGeodeClient` 只負責註冊 config 與 `IGeodeCache` 注入點；`factory.Create()` 必須由使用者啟動時呼叫。`IGeodeCache` 注入若先於 `Create` 觸發 → `KeyNotFoundException`，fail fast 不 silent magic。production / 測試行為一致。
4. **cacheName / configName 解耦** — 加進 `Create` 簽章。同一份 config 可給多個 cache 用（讀寫分流、tenant 隔離）。`Get` / `RemoveAsync` 只認 cacheName。
5. **`Action<sp, opts>` 的 cascade 語意** — `Create` 的 `action` 是「lookup configName → DeepClone → action 在 clone 上改 → validator 重跑 → 用 clone 建 cache」。原 config 不污染。
6. **DeepClone 方案** — 否決 `ICloneable`（MS 反對）跟 JSON round-trip（怕未來 options 加非 JSON 屬性）。選方案 B：每個 options class 自己加 `DeepClone()` 方法，不走 interface。
7. **`AddGeodeClient` / `AddGeodeFactory` 分層** — 兩個 method 各 3 overload。`AddGeodeClient` 永遠 unnamed、會註冊 `IGeodeCache` 直接注入；`AddGeodeFactory` name 在最後（有 default `""`），只往 factory 加 entry、不註冊 `IGeodeCache` alias。
8. **驗證邏輯搬進 `GeodeClientOptions` 本身** — 在 options class 加一個 `Validate(string? name = null)` 方法，回 `ValidateOptionsResult`。原 `GeodeClientOptionsValidator` 縮成一行轉發 `opts.Validate(name)`。好處：(a) `factory.Create(action)` 在 DeepClone + action 後直接 `clone.Validate(configName)` 一行檢查，不用從 sp 撈 `IValidateOptions<T>`；(b) options 自己負責自己合法性，cohesion 高；(c) 測試可繞過 DI 直接驗。子 options class 同樣加 `Validate()`，root 跑時遞迴呼叫子物件。

### 最終定稿

```csharp
public static class GeodeClientExtensions
{
    public static IServiceCollection AddGeodeClient(this IServiceCollection services);
    public static IServiceCollection AddGeodeClient(this IServiceCollection services, IConfiguration cfg);
    public static IServiceCollection AddGeodeClient(this IServiceCollection services, Action<GeodeClientOptions> configure);

    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, string name = "");
    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, IConfiguration cfg, string name = "");
    public static IServiceCollection AddGeodeFactory(this IServiceCollection services, Action<GeodeClientOptions> configure, string name = "");
}

public interface IGeodeCacheFactory
{
    IGeodeCache Get(string cacheName = "");                                   // KeyNotFoundException if missing
    bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache);
    IGeodeCache Create(                                                       // InvalidOperationException if cacheName exists
        string cacheName = "",
        string configName = "",
        Action<IServiceProvider, GeodeClientOptions>? action = null);
    IReadOnlyCollection<string> CacheNames { get; }
    ValueTask<bool> RemoveAsync(string cacheName);
}
```

行為契約：

- 95% 使用者：`AddGeodeClient(cfg)` → 啟動時 `factory.Create()` → 各處 `public class S(IGeodeCache cache)`
- 5% 使用者：`AddGeodeFactory(cfg, "legacy")` → `factory.Create("legacy", "legacy")` → `factory.Get("legacy")`
- DI keyed `[FromKeyedServices]` 注入完全不支援（避免 `RemoveAsync` stale instance 雷區）

### 撤回的決定（討論過但決定不做）

- ❌ Validator 收緊 `CacheXml == null` ── 保留 nullable（手動建立路徑落地後再回頭審）
- ❌ `Register` / `Unregister` runtime options（透過 `IOptionsMonitorCache<T>.TryAdd`）── 不需要,`Create(action)` 已涵蓋
- ❌ `RegisteredNames` / `IsRegistered` 查詢介面 ── 「能不能查 config 組態」放棄
- ❌ `GeodeClientRegistry` sidecar ── 不需要
- ❌ `ICloneable` ── MS 反對的設計（type erasure + deep/shallow 語意不明）
- ❌ `IDeepCloneable<T>` interface ── 過度抽象,簡化成方案 B
- ❌ `[FromKeyedServices]` keyed 注入 ── 全部走 factory（簡化 + 避免 stale instance 雷）
- ❌ `AddGeodeClient` 自動 Create（hosted service）── manual,保持 production / 測試行為一致
- ❌ `GetOrCreate(name, action)` 三合一 ── silent-ignore on second call 雷區
- ❌ `IGeodeCache?` Get（nullable 回傳）── 改丟例外,不要強迫 caller 處理 null

### 實施順序

1. 列 `CacheXml*` 巢狀類別,補完 options class 完整名單
2. 每個 options class 加 `DeepClone()` + `Validate(name)` 兩個方法
3. options unit tests（每個 class round-trip + mutation isolation + Validate 正反向）
4. `GeodeClientOptionsValidator` 縮成轉發 `opts.Validate(name)` 的 thin wrapper(保留 DI 註冊以維持 `ValidateOnStart` pipeline)
5. 重塑 `IGeodeCacheFactory` interface（5 個成員）
6. 重塑 `GeodeCacheFactory` 實作（含 Get/Dispose race 修 — 用 `DisposeEntryAsync` helper 跟 `RemoveAsync` 共用；`Create(action)` 在 DeepClone + action 後呼叫 `clone.Validate(configName)`）
7. `GeodeClientExtensions` 改 6 個 overload + 拿掉 keyed/unnamed `IGeodeCache` 註冊以外的東西 + 重寫 XML doc
8. 既有測試呼叫點更新（grep `[FromKeyedServices]` + `IGeodeCacheFactory.Get` 影響範圍）
9. 補新測試：Create 重複丟、Create+action mutation isolation、Create+action validator fail、Get/TryGet 找不到、RemoveAsync 後再 Create 同名、CacheNames snapshot 行為
10. build + test 全綠後 commit

每步做完停下來給 review，按 memory 規則。

---

## Phase 1.4 — OQL Query ✅

### 已完成

- [x] `IQueryService.NewQuery<T>(oql)` / `IQuery<T>` 介面 + DI wiring
- [x] `RemoteQueryService` + `RemoteQuery<T>` 殼 + `ExecuteCoreAsync` B1-B11
      完整實作（closed guard / logs / TcrMessage build / DM send / server-exception
      handling / result projection）
- [x] `TcrMessageBuilder.Query(34)` / `QueryWithParameters(80)` wire 編碼器
- [x] `ChunkedQueryResponse<T>` **完整解碼** — C1-C12 主流程、R1-R3
      `ReadObjectPartList`、S1-S4 `SkipClass`、K1-K2 `Reset`、helper
      `ReadStructRow` / `ReadExceptionAndThrow`。三條 wire shape 全處理：
      scalar COUNT (C3b)、CacheableObjectArray (C11a)、CacheableObjectPartList (C11b)
- [x] **`QueryStruct` 公開型別**（拉前自 Phase 2）— 不叫 `Struct` 因為跟 C#
      keyword 衝突。實作 `IReadOnlyList<object?>` + by-name indexer +
      `FieldNames` / `GetFieldIndex` / `GetFieldName`
- [x] **StructSet 兌現** — 採 Option C（collector 內每 K 個值組好
      `QueryStruct` 直接 push，跳過 cppcache 的「攤平 → 外層 reshape」
      中介），B10 簡化為單行 return
- [x] **NewQuery type guard** — `T` 須是 `SerializationRegistry` 註冊型
      或 `QueryStruct`，擋掉 bucket 2 (PDX 自訂型) / bucket 4 (ORM mapping)
- [x] **`BigEndianBinaryReader.ReadArrayLength`** — Java 變長 array
      length 解碼（cppcache `DataInput::readArrayLength` 對等）
- [x] **`TcrPartBuilder.ModifiedUtf8`** + `RegionName` 內部改委派 —
      OQL / region path 編碼從 ASCII 換 Modified UTF-8 body，跟 Java
      server `CacheServerHelper.fromUTF` 對齊；純 ASCII 場景 byte 不變
- [x] **`QueryExtensions`**（`ExecuteSingleAsync` /
      `ExecuteFirstOrDefaultAsync` / `WithParameters` /
      `WithResponseTimeout`）—  caller-side fluent / scalar 包裝
- [x] **單元測試**（39 個）：`QueryStructTests` (16) +
      `QueryExtensionsTests` (18) + `TcrMessageBuilderQueryTests` (17)
      + `TcrMessageBuilderQueryWithParametersTests` (22)
- [x] **整合測試**（14 個，全 PASS）：`QueryIntegrationTests` (7) 覆蓋
      `SELECT *` ResultSet、`SELECT COUNT(*)` scalar、
      `QueryWithParameters(80)` + bind values、`ExecuteSingleAsync` 組
      合 extension、type 不符 → `InvalidCastException`；
      `RegionQueryConvenienceIntegrationTests` (7) 覆蓋 region
      convenience（見下方）
- [x] **Region convenience：`ExistsValueAsync` / `SelectValueAsync`** —
      `IRegion.ExistsValueAsync` / `IRegion.SelectValueAsync` + 泛型
      overlay `IRegion<TKey,TValue>.SelectValueAsync` (typed,
      `new Task<TValue?>`)。實作走 `ThinClientRegion.QueryAsync` 私有
      helper (mirror cppcache `Region::query` 共用體)；OQL 字串組裝邏輯：
      caller 給 full query (`^\s*(?:select|import)\b` 偵測) → verbatim；
      否則 prepend `select distinct * from <FullPath> this where `（`this`
      alias 在 FROM 子句宣告，跟 cppcache `ThinClientRegion.cpp:536-540`
      一致）。`RegionView<TKey,TValue>` 加 3 個 forwarder（`ExistsValueAsync`
      / typed `SelectValueAsync<TValue>` 走 adapter / explicit
      `IRegion.SelectValueAsync` 跳 adapter）。
- [x] **`RemoteQueryService.NewQuery<object>` 白名單** — type guard 加
      `typeof(T) != typeof(object)` 例外，承認 cppcache
      `shared_ptr<Serializable>` (≈ `object?`) 的基底路徑。
      `TypedResultAdapter.Convert<object>` 早已是 identity（`IsInstanceOfType`
      永真），所以這條開放零成本。Region convenience 內部就吃這條路徑。
- [x] **`ProxyRemoteQueryService` 殼**（Phase 3 預先) — mirror cppcache
      `ProxyRemoteQueryService` (sibling of `RemoteQueryService` under
      `IQueryService`)，`NewQuery<T>` NIE，Phase 3 multi-user 才填。

### 待做

- [ ] 多欄 projection / StructSet 整合測試 — 需要 server 端 PDX 結構化
      資料（gfsh JSON put 或 Java 預載），暫時 deferred

### 整合測試實戰抓到的兩個 bug

**Bug 1：`TcrMessageHelper.ReadChunkPartHeader` 簽號 byte 錯解**
（`Protocol/TcrMessageHelper.cs:156-167`）。`compId = reader.ReadByte()`
回無號 byte，對負值 `DSFid` 解錯（`CollectionTypeImpl = -59` 的 wire
byte 是 `0xC5`，無號讀回 197 跟 -59 比對失敗）。修法：
`compId = (sbyte)reader.ReadByte()` 簽號解讀。之前 GetAll / RemoveAll
chunked decoder 都用正 DSFid（`VersionedObjectPartList = 7` 等），
此 bug 一直 latent；query 是第一個碰到負 DSFid。

**Bug 2：`ChunkedQueryResponse` C6 / C7 / R3a 對短字串 DSCode 太嚴**
（`Services/ChunkedQueryResponse.cs`）。原本只接
`DSCode.CacheableString(42)`，server 對純 ASCII 類別名 / 欄位名實際送
`DSCode.CacheableASCIIString(87)`。抽出 `ReadShortString` helper 同時
接受兩種 form — Modified UTF-8 解碼對 ASCII subset byte-identical，
共用 reader。cppcache `DataInput::readString` 本來就 dispatch 四種 form，
我們之前未實作的 huge / ASCII 分支現在 Phase 1.4 至少 ASCII 已覆蓋。

### 取捨備忘

**T 型別不符的 cast 失敗**（例 `IQuery<int>("SELECT name...")`）目前讓
`InvalidCastException` 自然冒出，跟 `IRegion<TKey,TValue>.GetAsync` 同源
（memory note：deferred to PDX phase 才會再回頭整合 `TypedResultAdapter`
+ ORM mapping）。

**OQL `this` 的真相**（前述「在 WHERE 不 work」描述不準）— `this`
**會 work**，但前提是 FROM 子句要明確宣告它作 region iteration alias：
`SELECT * FROM /region this WHERE this = ...`。我們之前的整合測試寫
`SELECT * FROM /test WHERE this = ...`（缺 `this` alias 宣告）所以炸；
cppcache `ThinClientRegion::query` (`cppcache/src/ThinClientRegion.cpp:536-540`)
也是這麼 prepend 的，region convenience 方法 `QueryAsync` helper 跟它
對齊。既有 `QueryIntegrationTests` 改用 alias `t` 是 caller 風格選擇，
不是被迫。

**拉前 projection 理由**：B10 ResultSet / StructSet 分支跟
`ChunkedQueryResponse.HandleChunk` 是同一條解碼路徑 — fieldNames 解碼跟
row values 解碼在 cppcache 同一個 `readObjectPartList`。若 StructSet 留
Phase 2，會出現「結構在但不解 fieldNames / 不 reshape」的
silent-corruption 半成品（caller 寫 `SELECT id, total` 拿到攤平 list，
無錯誤、無警告）。同期完成才不留漏洞。

**`NewQuery<object>` 白名單的設計含義** — 開放 `IQuery<object>` 為公開
API 等於正式承認「我接 wire 解出來的原樣，自己處理 row shape」這條
路徑（≈ cppcache `shared_ptr<Serializable>` 基底）。release 後不能撤；
但這條本來就是 cppcache 唯一的 row 型別契約，`<T>` 才是 .NET 端加的
type-safety 糖衣，補上 `<object>` 才完整。

**下一步入口**：Phase 1.5 — Connection management。Phase 1 MVP 只剩
連線池 / locator / failover / 健康監控。

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
