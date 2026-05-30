# GeodeSharp — 專案說明

> 這份檔案是 Claude Code 的長期專案記憶。每個 session 開始時讀一次,
> 看一下 repo 裡 NIE / TODO 的分佈,挑一個還沒測試覆蓋的切片開紅燈。
>
> **這是一份持續更新的文件。** 學到新東西就寫回來。沒有 phase 概念 —
> skeleton 上的 NIE + TODO 就是進度地圖。

---

## 一句話目標

打造一個 **純 managed、零 runtime 相依、跨平台** 的 Apache Geode
client,target **.NET 10 (LTS)**,發佈到 NuGet。

上游參考:<https://github.com/apache/geode-native>
(我們**不**移植 C++/CLI `clicache/` — 限制太多而且只能跑在 Windows。)

專案命名(兩層,刻意分開):
- GitHub repo / 本機資料夾:`GeodeSharp` (https://github.com/TomiCheng/GeodeSharp)
- Solution:`geode-dotnet.sln`
- NuGet PackageId / Root namespace / AssemblyName:`Geode.Client`
- 原始碼資料夾:`src/Geode.Client/`;測試 `tests/Geode.Client.Tests/`
  跟 `tests/Geode.Client.IntegrationTests/`;範例
  `samples/Geode.Client.Sample/`

「GeodeSharp」是專案 / repo 名稱(對外的識別碼);assembly 層用
`Geode.Client`(符合業界慣例:Apache Geode 的 .NET client)。
程式碼、`using` 指令、`<PackageReference>` 一律用 `Geode.Client`;
GeodeSharp 保留給「談這個專案本身」的時候使用。

---

## 架構決策(已定案 — 不要再翻案)

### 路線

純 managed 實作,自己對 Geode wire protocol 編解碼。產出物是一個
100% C# 的 NuGet 套件,跨平台執行。

Geode wire protocol **沒有正式規格**(Apache 自己的 wiki 都承認)。
只能從 `cppcache/src/` 跟 Java `geode-core` 反推。

**對策 1:** 把 cppcache 當「可執行規格」來讀 — 不要從零自己設計。
**對策 2:** 走 TDD。從 public API 層開始寫測試,先紅後綠 — 測試先
描述「對外要看到什麼行為」,再往下挖實作。一次只逼出一個失敗測試
要的最小程式碼,避免從零自己設計時容易發生的 over-engineering。

### 移植 + 現代化

cppcache `clicache/` 已驗證所有介面形狀、命名、語意。我們做的是
「翻譯 + 現代化」,不是「從零設計」:

- **保留:** 型別 / method 名稱、核心概念(Region、Pool、QueryService)。
- **現代化:** sync → async、`gcnew` → record/class、cache.xml →
  `IOptions<T>`、static factory → DI。
- **可見性:** public 一律 C# `interface`(`IGeodeCache`、
  `IRegion<TKey,TValue>`),實作放 `internal sealed`;不出
  `abstract class`。預設 internal,逐 class 決定要不要升 public
  (C++ 沒 `internal`,所以 cppcache 放 `include/` ≠ 要 public)。

### 移植三原則

每碰到一個 cppcache class,先決定它落在哪一條,然後照規矩動。
判斷不出來的時候,預設走 **原則一**(鏡像)— 跟 config policy 的
「先鏡像再修剪」邏輯一樣。

實際的逐 class 對映表(cppcache 名稱 → C# 名稱、適用原則、可見性、
狀態)放在 [PORTING.md](PORTING.md)。每碰到新的 cppcache class
就加一筆。

#### 原則一:鏡像 C++ 架構(預設)

對齊 cppcache 的 class 名稱、檔案布局、繼承關係、method 名稱。
只把機制現代化(sync → async、static factory → DI 等)— 架構不動。
Domain 邏輯 / wire protocol 都走這條。

#### 原則二:BCL 能 100% 取代 → 直接用 BCL

cppcache 寫某些 class 是因為 C++ standard / boost 給了原語但沒給
抽象;.NET 內建就有。直接用 BCL 型別,不要移植 cppcache 那個
class。對映表記在 [PORTING.md](PORTING.md)。

#### 原則三:C++ 語法 C# 接不住 → 詳細看設計

C++ 有的東西(多重繼承、RAII、template、preprocessor macro、Pimpl、
operator overloading)C# 沒對應原語的時候,不能照搬。要把 cppcache
的「設計意圖」抓出來,再用 C# 慣用法重新表達:多重繼承 → 組合 /
interface、RAII → `IDisposable` / `using`、template → generic、Pimpl
→ internal sealed,等等。常見轉譯模式累積在 [PORTING.md](PORTING.md)。

---

## 通用原則

1. **Async-first。** 所有 I/O 都只開 async API,沒有 sync 版本。
2. **Options pattern。** 配置走 `IOptions<T>`,從 `appsettings.json`
   bind 過來。
3. **DI-first。** 註冊用 `services.AddGeodeClient(...)`;不要 static
   singleton。
4. **零外部 runtime 相依。** 全靠 BCL;唯一的 reference 是 `Microsoft.Extensions.*` 那組 abstraction 套件。
5. **Test-first。** 第一個失敗測試從 public API 角度寫;interface 殼
   是被測試逼出來的,不是先一次設計完。形狀參考 cppcache `clicache/`,
   但只長出當下測試需要的那塊。
6. **Walking skeleton。** 先讓最薄的端到端切片亮綠燈,再往兩端疊厚度;
   不要把整個下層做完才開始做上層。
7. **Stub 優先 — NIE + TODO 大量灑。** Public surface 用
   `throw new NotImplementedException()` + `// TODO:` 撐起骨架。測試
   逼出哪個 stub 就只填那個,其他 stub 留著當地圖。每個 NIE 上面配
   一個 `// TODO:` comment 寫「該長成什麼 / 對應 cppcache 哪段」—
   skeleton 自己就是進度地圖,不需要另一份 phase 清單。
8. **Living document。** 這份檔隨開發進度一起演進。

---

## 細節

詳見 [NOTE.md](NOTE.md):「不實作」清單、wire protocol 參考、
Options 政策、與 cppcache 偏離的設計決策、待清理 dead-code、
未實作功能的設計初稿。
(進度本身看 repo 裡的 NIE / TODO,不維護在這。)

---

## 實作原則(時時放心上)

1. **設計 protocol 前先讀 cppcache。** `TcrMessage.cpp`、
   `TcrConnection.cpp`、`HandShake.cpp`、`ThinClientPoolDM.cpp`
   就是 spec。
2. **Test-first。** 從 public API 角度寫第一個失敗測試 → 寫剛好讓它
   過的程式 → refactor。Interface 殼是被測試逼出來的,不要先一次把
   interface 全宣告完。
3. **Walking skeleton。** 最薄的端到端切片先亮綠燈,再疊下一層。
4. **Frame codec 一定要有 unit test**,並用 Wireshark byte fixtur 當依據。
5. **不要 over-abstract。** 下層先寫具體 class;interface 只在 DI
   接線 / 多實作真的出現時才抽。
6. **到處都 big-endian**(`BinaryPrimitives.WriteInt32BigEndian`)。
   Geode 是 Java;wire 是網路 byte order。
7. **單一連線本來就支援併發**(透過 transaction id 對 pipelined
   request 配對)。Pool 是吞吐 / 故障隔離的最佳化,不是基本需求。
8. **每個 cppcache 的 log call 都要鏡像。Log 是我們在移植的可觀察
   行為的一部分 — 拿 .NET 客戶端的 wire-protocol bug 對著 cppcache
   trace 排查,需要同一組 breadcrumbs 在同一個順序出現。Logger 用
   `ILogger<T>` 透過 DI 注入;格式參數走 structured logging
   (`"Connecting to {Endpoint}"`,`endpointName`),不要用
   `string.Format`。cppcache 的訊息英文寫得彆扭的話,可以改寫,
   但 severity 跟關鍵資料欄位要保留。
9. **常數命名跟著 source。** 對應 cppcache `static const` 的 wire
   protocol 常數,完全保留 cppcache 的 `SCREAMING_SNAKE_CASE`
   (`FLAG_NULL_TAG`、`HAS_MEMBER_ID`、`LAST_CHUNK_MASK`);
   診斷時跟 grep 兩邊對得起來。C# 這邊自己發明的常數
   (`MetaTransactionId`、`ThreadId`)用標準 C# `PascalCase`。
   `.editorconfig` 不強制 — 兩種風格刻意共存,用「這個常數有沒有
   1:1 cppcache origin」來區分。
10. **不要自動跑 test。** 改完不要主動 `dotnet test`;等使用者要求
    才跑。但**在 commit 前**如果這一輪改動完全沒測過,要先提醒一句
    (「這次還沒跑 test,要先跑嗎?」),由使用者決定要不要先驗再
    commit。Build(`dotnet build`)還是該跑 — 那是「syntax 對不對」
    的快速回饋,不算 test。
11. **動工前先讀 C++,在 C# 程式裡留 step list。** 要實作某個 cppcache
    對應 method 之前,先把 cppcache 那段讀過、邏輯抓清楚,然後把切片
    計畫(Step A / B / C ...)寫成 comment 留在 C# 對應的 stub method
    內。下一個 session(或下一個人)看到這個 method 不用回 conversation
    翻紀錄,直接看 source 就知道整段該怎麼長出來。
12. **Commit 流程。** 需要使用者**明確說「commit」**才能 `git commit`。
    使用者說 commit 之後 → 直接 commit,**不要再丟 draft message 上來
    等二次確認**(訊息本身使用者不在意,他在意的是「程式碼到底要不要
    進 git history」這個決策,前一步就已經做完了)。流程:
    使用者說 commit → 跑 `git status` / `git diff --stat` 給看 file
    list(讓使用者最後一眼可以喊停)→ 直接 `git add` + `git commit`,
    訊息合理寫即可。`pause-before-commit.md` memory 是這條的更完整版。

---

## Toolchain

- **.NET 10 SDK**(LTS,GA 2025-11)
- **xUnit v3** + FluentAssertions
- **Testcontainers** — integration tests 開 `apachegeode/geode`
- **GitHub Actions** — PR / push 跑 CI,tag 出 release
- **NuGet** — `MinVer` 從 git tag 推版號
- **Source Link** + `.snupkg`
- **Apache-2.0** 授權(對齊上游)

---

## 下次開工怎麼起手

新 session:
1. 讀完這份檔。
2. `grep -rn "NotImplementedException\|TODO" src/` 看 stub 分佈,
   挑一個還沒測試覆蓋的 public API 切片。
3. 從寫第一個失敗測試開始(test-first → 紅 → 綠 → refactor)。
4. 需要設計脈絡時翻 [NOTE.md](NOTE.md) 的對應主題段落
   (wire protocol、Options 政策、跟 cppcache 偏離的決策、待 cleanup 等)。

沒有「目前 phase 是 X」的概念 — 紅燈在哪,工作就在哪。
