# 效能回合報告（0.3.0 dev）

目標：在不改任何行為的前提下，減少每幀／每發的 GC 垃圾（Mono 的 GC 會停住整個世界：host 停一下，所有 client 就跟著卡一下再跳）。
只動程式與 headless 測試；沒動美術資產、版本號、Thunderstore，沒 deploy、沒 push。

## 1. 量測方法（三個，都可重跑）

這回合禁止 deploy，所以 PerfLog／AutoTest（要把 DLL 裝進遊戲才會跑）**沒有跑**；三個量測都在 headless harness 裡，對 mod 本身的 DLL 做。

```zsh
dotnet build HtfFortress -c Release -p:Deploy=false
dotnet build tests/Headless -c Release
DOTNET_TieredCompilation=0 dotnet tests/Headless/bin/Release/net10.0/Headless.dll perf
```

| # | 量什麼 | 怎麼量 |
|---|---|---|
| M1 | 每幀程式碼的配置點（靜態） | 用 Cecil 讀 DLL 的 IL：從每個 MonoBehaviour 的 Update／LateUpdate／FixedUpdate／OnGUI／OnTriggerStay 出發，沿著呼叫往下 2 層（lambda、iterator 本體算同一層），數會配置記憶體或做 Unity 搜尋的指令（closure、delegate、LINQ、box、newarr、字串、GetComponent/Find…）。另外單獨數「在第一個分支之前」的配置＝**每次呼叫都一定會發生**的配置（例如方法頂端就建好的 closure，就算馬上 return 也已經配了）。 |
| M2 | 每次呼叫配置幾 byte（執行期） | 直接呼叫 mod 真正的方法 20 萬次，用 `GC.GetAllocatedBytesForCurrentThread()` 算平均。每個 case 都設定成「大多數幀走的那條路」又不碰到引擎（砲塔在換彈、沒有腰帶、沒有網路）。 |
| M3 | 每次呼叫花幾 ns（執行期） | 同一批呼叫，25 輪取最快一輪。`DOTNET_TieredCompilation=0` 讓 JIT 一次編到底，數字才穩（macOS 會把執行緒在快慢核心間搬，所以取最快）。 |

注意：
- M2／M3 跑在 .NET 10，不是遊戲的 Mono。closure／delegate／List 的配置兩邊一樣；LINQ 和 `List.AddRange` 在 Mono 配得**更多**（所以表上的 before 是低估）。ns 只能拿來比 before/after，不能當遊戲裡的時間。
- `Turrets.Fire` 在 headless 無法 JIT（它碰到 FishNet 的 `SyncVar`，.NET 10 載入失敗），只能用 M1 證明。
- 重現 baseline：`git checkout 12ab72c -- HtfFortress/*.cs`，重 build，跑同一個指令，再 `git checkout HEAD -- HtfFortress/*.cs`。

參考（不是這回合量的）：遊戲裡上一份 `LogOutput.log`（10/26 02:59）的 PerfLog，host 什麼都沒在打的時候 heap 也每 10 秒漲 2–40 MB、大約每 45 秒回收一次，回收那一幀 30–50 ms；大戰時最差幀 337–449 ms。

## 2. Before / After

### M2 + M3：熱路徑每次呼叫（`DOTNET_TieredCompilation=0`）

| 呼叫 | 多常跑 | before | after |
|---|---|---|---|
| `Turrets.Splash`（沒有魚） | host：每顆砲彈／迫擊砲 | 104 B, 92.5 ns | **0 B**, 6.7 ns |
| `Perks.TeamLegend` | host：每次砲塔命中 3–4 次 | 24 B, 7.0 ns | **0 B**, 5.0 ns |
| `FortressNet.RefreshWaist` | 每個 client 每幀 | 32 B, 17.2 ns | **0 B**, 4.0 ns |
| `Actives.Owned`（什麼都沒有） | 每個 client 每幀 1–2 次 | 464 B, 325.7 ns | **32 B**, 208.8 ns |
| `BossVfx.Sparks(color)` | 王的光環每秒約 10 次／隻、王的招式 | 264 B, 99.5 ns | **0 B**, 5.2 ns |
| `WeaponFx.Sparks("Cyan")` | 武器命中、光束每幀（每個 client） | 136 B, 30.9 ns | **0 B**, 7.2 ns |
| `Turrets.Fire`（換彈中） | host：每座砲塔每幀 | headless 跑不了 | 見 M1 |

（`Splash` 的 104 B 是場上 0 隻魚的時候；有 N 隻活魚時 Mono 的 `Where().ToList()` 還要把陣列從 4 一路倍增到 ≥N，167 隻 ≈ 4 KB／發。after 是重複使用同一個 list，0 B。）

### M1：每幀 IL 配置點

| | before | after |
|---|---|---|
| 每次呼叫必定配置（第一個分支前），深度 ≤0 / ≤1 / ≤2 | 28 / 110 / 343 | 27 / 106 / 333 |
| 配置點總數，深度 ≤0 / ≤1 / ≤2 | 194 / 879 / 1999 | 188 / 842 / 1947 |
| 其中 closure ≤2 | 93 | 85 |
| 其中 LINQ ≤2 | 255 | 234 |

從「每次呼叫必定配置」名單上消失的：`Turrets.Fire`、`Turrets.Hit`、`Turrets.Others`、`Perks.TeamLegend`、`FortressNet.RefreshWaist`、`Enemies.Update`、`Actives.DrawHud`、`Actives.PickedIndex`、`BossVfx.Sparks`。新加入的 `Enemies.Prune`（每 5 秒一次）和 `Perks.HasLegend`（每幀每種裝備最多一次）是刻意搬過去的冷路徑。總數降得不多是正常的：M1 是靜態數，很多配置點本來就在很少走到的分支裡；這回合處理的是**真的每幀／每發都在跑**的那幾個。

## 3. 改了什麼（全部只改配置，順序、副作用、結果不變）

| 熱點 | 之前 | 現在 | 檔案 |
|---|---|---|---|
| 砲塔每幀檢查 | `Fire` 裡有一個抓 `s`（TurretStats）的 lambda，C# 把 closure 建在方法最上面：**每座砲塔每幀**（換彈中也是）配約 96 B（物件頭＋this＋64 B 的 TurretStats）。40 座、120 fps ≈ 450 KB/s | lambda 換成迴圈，closure 消失 | `Turrets.cs` |
| 多管砲塔 | `Others()` 的 LINQ closure 在入口就建（單管也建）、呼叫端再 `.ToList()` 一次 | 單管直接回傳共用的空 list；LINQ（排序要穩定，保留）搬進 `OthersInRange`，只有多管才跑 | `Turrets.cs` |
| 每次命中 | `Hit`（傳說火藥）和 `Ignite`（傳說燃燒）的 lambda 抓參數 `c`，closure **每次命中**都建 | 傳說分支搬到 `Burst`／`Spread`，只有真的觸發才建 | `Turrets.cs` |
| 砲彈／火焰／魚叉 | 每發 `alive.Where(Alive).ToList()` 複製整個活魚清單 | 一樣先複製（命中可能改到 `alive`），但用重複使用的 list（Stack，巢狀呼叫各拿一個） | `Turrets.cs` |
| 特效的 key | 每發 `p.Type.ToString()`、`"shots_" + p.Type`（AutoTest 沒開也照建） | 名字快取一次；AutoTest 字串只在 AutoTest 執行時建 | `Turrets.cs` |
| 傳說裝備查詢 | `TeamLegend` 的 lambda 抓 `g`，有快取答案也每次建 closure（每次命中 3–4 次） | 計算搬進 `HasLegend`，只在快取沒有時跑 | `Gear.cs` |
| 主動裝備 HUD | `Owned()` 每幀被 HUD（和顯示中的裝備列）叫 1–2 次，每種主動裝備每個稀有度一個 `Keys.Any` closure＋boxed enumerator；`DrawHud` 每幀重組 6–7 個字串＋regex | `Owned()` 改迴圈（結果與舊 LINQ 逐項相同，見測試）；`PickedIndex` 改迴圈；HUD 文字只在內容變了才重組（索引、數量、裝備、稀有度、剩餘次數、倒數秒數任一變動立即更新，另每 0.5 秒保底刷新一次給語言切換）；冷卻條仍然每幀 | `Actives.cs` |
| 敵人更新 | `Enemies.Update` 裡 5 秒一次的清理用了抓 `cm` 的 lambda，closure 卻是**每幀**建 | 清理搬進 `Prune(cm)` | `Enemies.cs` |
| 腰帶魚 | 每幀 `new List<int>(_pack.Keys)`＋每條腰帶一個 `FirstOrDefault` closure | 沒有腰帶直接 return；有的話用重複使用的 list＋迴圈找玩家 | `FortressNet.cs` |
| 王的大腦 | 每幀 `AddRange`（Mono 會先複製到暫存陣列）＋`Values.Any`（boxed enumerator） | 兩個都改迴圈 | `Bosses.cs` |
| 火花粒子 | `BossVfx.Sparks(color)` 每次都組 hex 字串＋closure 去查；`WeaponFx.Sparks(name)` 每次組字串＋closure | 先用顏色／名字本身查快取，查不到才走原本的路（拿到的是同一個粒子系統） | `BossVfx.cs`、`WeaponFx.cs` |
| 光束 | 每道光束每幀 new 兩個字串陣列 | 改成 static readonly | `Weapons.cs` |

粗估（Mono，假設：host 40 座砲塔、120 fps、每秒 300 次命中、30 發砲彈、167 隻魚）：host 大戰時少約 **0.6 MB/s** 垃圾；每個 client 閒置時少約 0.05–0.1 MB/s，回合中（主動裝備 HUD 顯示時）再少約 0.1 MB/s。GC 應該會變得比較不頻繁，但實際差多少要等遊戲裡的 PerfLog 才知道（見第 5 節）。

## 4. 看過但沒改／量不到的

| 項目 | 狀態 | 原因 |
|---|---|---|
| `Enemies.LateUpdate`／`DrawPlates` 每幀每隻敵人 `GetComponent<Creature>()` ×2 | 沒改 | 是引擎原生呼叫，headless 量不到；300 隻魚時估計每幀數十 µs。可以用 id→(nob, creature) 快取，但要先在遊戲裡量。 |
| `L10n.T` 的 `Current()`（語言設定為 Auto 時每次 `Replace`＋`ToLowerInvariant`）、名牌每幀 `L10n.T("enemy" + kind)` | 沒改 | Auto 會讀 Unity Localization，headless 只會走例外路徑，量到的不是遊戲裡那條；很可能是剩下最大的每幀字串來源，建議下一輪在遊戲裡量。 |
| `BossIk` | 沒改 | 每幀沒有配置（瞄準每 0.1 秒一次）；成本是每根骨頭 2 次 Slerp 的原生運算，headless 量不到；場上通常只有一隻王。 |
| FortressNet 同步頻率 | 沒改 | 看過：`NetState`／`FoodPack` 只在改變時送，`ServerTick` 4 Hz 只做比對；`SiegeState` 最多 5 Hz 且只在改變時送；FX 每幀最多一包。沒有可砍的；頻寬沒量。 |
| BossVfx 燈光／粒子、WeaponFx 池 | 沒改 | 點光已限同時 4 盞、粒子每種一個系統（本來就是池）；`BossVfx.Wash` 每個衝擊環 new 一個 Material（原生配置）量不到，量級是每招一次。 |
| 閒置時 heap 每秒約 2 MB 的成長（舊 PerfLog） | 未歸因 | 這回合找到的閒置來源（`Actives.Owned`、腰帶）只占一小部分；其餘可能是遊戲本體，headless 分不出來。 |
| HUD 其他（`Siege`、`HotbarBadges`、`RecordBoard`、`WeaponRack`、`Markers`） | 沒改 | 已經是 10 Hz／5 Hz 或內容變了才重畫（之前的效能回合做過）。 |

## 5. 驗證

- `dotnet build HtfFortress -c Release -p:Deploy=false`：0 error、0 warning。
- `zsh tests/run.sh`：**124,535 passed / 0 failed**（之前 124,235；新增 300 個：`Actives.Owned` 新寫法和舊 LINQ 在 300 個隨機金庫上逐項相同。故意把新寫法弄壞一次，這個測試會抓到。）
- harness 的變動：新增 `tests/Headless/Perf.cs`（`perf` 指令）；`Program.cs` 只在 `perf` 時把 `UnityEngine.Object` 的 `==`／`!=`／`if (obj)` 換成參考比較（讓不需要引擎的替身物件能跑），`Time.unscaledTime`／`unscaledDeltaTime` 也接到 Shim（以前沒接，走到就當掉）；`Headless.csproj` 加參考 `UnityEngine.ParticleSystemModule`。

遊戲內確認（這回合沒做，需要 deploy，只在 Owen 要的時候、用 FortressTest 存檔）：開 VerboseLogs 打一個後期波次，比較 `BepInEx/LogOutput.log` 裡 `perf host:` 的 worst frame 和 KB 鋸齒的週期（回收間隔變長＝垃圾變少）。
