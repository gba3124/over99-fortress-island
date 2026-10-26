# GUN-UPGRADE-ROUND1：槍械升級第一輪

> 依據：`GUN-MODEL-AUDIT.md`（§0 五個 P0、§10 P0-A／P0-B、Top 10）
> 日期：2026-10-26
> 範圍：只動顯示與行為的程式碼。沒有改版號、沒有 deploy／pack／commit，沒動 Thunderstore／release metadata，也沒動 OBJ／PNG。
> **沒有開遊戲實機驗證**，只跑了 headless 測試。

## 測試結果

- 建置：`dotnet build HtfFortress -c Release -p:Deploy=false -v q -nologo` 得到 **0 Error(s)**
- `./tests/run.sh` 得到 **124050 checks passed, 0 failed**
  - 改之前的基準是 123912 passed / 0 failed，所以這一輪多了 138 個 check，全部通過。

## 改了哪些檔案

| 檔案 | 改了什麼 |
|---|---|
| `HtfFortress/WeaponItems.cs` | `Grip` 擴充成每把槍的 spec（`Display` 類別、`Longest`、`FromHand`、`Euler`、`Muzzle`）；新增 `Spec(g)`、`MuzzleOf`、`HeldScale`、`SlideForward`、`BackMax`、`StandUp`、`DropLength`、`GameGunModel`；`Place` 改吃 spec；拿掉 Gatling 的 ×1.5 特例和全體共用的 `Across` 上限 |
| `HtfFortress/Weapons.cs` | `Muzzle(who,o,d)` 和 `HeldMuzzle` 從實際槍口出發；`MyMuzzle` 改用 spec；`PlayFx` 畫朋友的射擊時，從「我這台機器上看到的他的槍口」出發；SharkWhistle 改成真的建立 `Shark` 實體；`SharkTick` 改成繞著水坑狩獵；新增 `SharkSpots`、`SharkStep`、`SharkSurface`、`SharkLeap`；client 端鯊魚的高度改看地面／水坑 |
| `HtfFortress/Absorber.cs` | 4 處 `Muzzle(o, d)` 改成 `Muzzle(who, o, d)` |
| `HtfFortress/Loot.cs` | `DropModel` 遇到遊戲原槍回傳 null；`MakeDropView` 對遊戲原槍改用原生 mesh；掉落尺寸改用 `DropLength`；落地對齊改用 world bounds（可處理轉過的模型） |
| `HtfFortress/Village.cs` | 遊戲原槍模型的程式碼搬進 `WeaponItems.GameGunModel`，Village 和掉落物共用 |
| `HtfFortress/Indicators.cs` | 鯊魚哨的瞄準提示改成新的落點（水坑＋`SharkSpots`），不再畫 `OnSea` 海圈 |
| `HtfFortress/AutoTest.cs` | `held_*` 截圖跑完後，改回該槍自己的 spec，不再改回 `InHand` |
| `tests/Headless/Tests.cs` | 新增 `GunSpecChecks` |

## 決策與完成項目

### 1. 每把槍自己的 GunSpec（P0 尺度坍縮）
- `WeaponItems.Spec(g)` 涵蓋 17 把要塞武器。6 把遊戲原槍還是用原版 rig，完全不動。
- 手持最長邊：
  - Pistol 類：0.26–0.32 m（Laser／Gas／Bubble／Hook／Jelly／Star／Lobster）
  - Long 類：0.36–0.42 m（Absorber／Whirl／Net／Water）
  - Heavy 類：Rocket 0.60 m、Gatling 0.55 m（原本是 0.22 和 0.33）
  - Thrown 類：Boomerang 0.40、Puffer 0.16
  - Blow 類：Conch 0.30、SharkWhistle 0.162
- **避免槍托撞到鏡頭**：模型往握把後方伸超過 `BackMax = 0.09 m`（等於舊 0.22 m 規則下最長的後伸量）的部分，改成整把往前推，不再整把縮小。
- **不是手槍的四把用不同持法**：
  - Boomerang：繞 Z 軸 roll 90°（`StandUp`），跟 hotbar `Icon()` 用同一個值
  - Puffer：yaw 30°，放在掌心較低的位置
  - Conch：pitch −15°，像號角一樣上翹
  - SharkWhistle：pitch −25°，舉向嘴邊
- **掉落**：武器掉在地上的大小是手持的 1.6 倍，限制在 0.45–0.95 m（Epic 以上再 ×1.25）。所以火箭筒掉在地上會比雷射槍大；非武器的裝備維持 0.6／0.75 m。
- **Hotbar**：還是用手槍的取景來正規化。格子大小固定，所以這是刻意保留的。Boomerang 的立起修正改用共用的 `StandUp`。

### 2. 槍口（P0 FX 起點）
- `Spec.Muzzle` 是 mesh 座標（已做 X 鏡像），數值是用 Python 從各 OBJ 前端截面量出來的槍管中心。Hook 刻意設在鉤座（z 0.29），不是鉤尖。
- 本地：`MyMuzzle` 改用 `WeaponItems.MuzzleOf(model, g)`，不再用 bbox 的 `center.x/y + max.z`。
- Host：`Muzzle(who, o, d)` 先找射手手上模型的槍口，而且要離 `o` 2.5 m 以內才採用。固定的 `o + d*0.6 − up*0.25` 只剩作為 fallback（沒有模型時才用），全 repo 只剩這一處。
- 遠端（朋友的畫面）：`PlayFx` 會用 `e.Who` 找到射手，改從本機看到的他的槍口出發（要離 host 送來的點 1.5 m 以內）。鉤爪連鎖、海星反彈這類「起點本來就不是槍口」的事件，因為超出距離門檻，會保留原本的起點。持續型的 FX（Hook）會跟著槍移動。
- Absorber 的 FX 走 `FxKind.Absorber`／BossFx，沒有經過 `PlayFx`，所以只吃到 host 端的修正。

### 3. SharkWhistle 死碼（P0-A）
- `Fire` 現在會對每個 `SharkSpots(at, r)` 執行 `_sharks.Add(new Shark{…})`：一般 1 隻，Legendary 2 隻。
- 保留 Owen 要的「落點開一灘水，鯊魚從下面跳上來」：一樣送跳躍 FX，跳完（`SharkLeap` 0.55 s）才開始咬。
- `SharkTick`：
  - 鯊魚以水坑為中心、`Sight`（14 m × 射程 mod）為半徑狩獵，沙地或海上的魚都咬，用 `SharkStep` 拴在範圍內。
  - 沒有獵物時回到水坑。
  - 高度取海面和地面的較高者（`SharkSurface`），所以沙地上的鯊魚會露出背鰭和牙。
  - 每 1.2 s 咬一口。
  - 活多久：`SharkLife[r]` 加上 trait 秒數，跟 `WeaponMods.TraitStep` 寫的「hunts for seconds」一致，也跟 `Sim.Dps` 原本的算法一致。
- **行為差異（請 Owen 確認）**：舊版是落點半徑 2.8 m 內 AoE 咬一次（Legendary 兩次）；新版是單體連咬 15–25 s。DPS 表本來就是照新的行為算的，所以 DpsChecks 照常通過。
- 新增 AutoTest 計數 `shark_bite`。

### 4. 遊戲原槍掉在地上看不到（P0-B）
- `Loot.DropModel` 遇到 `IsGameGun` 回傳 null。`MakeDropView` 會改用 `WeaponItems.GameGunModel`，也就是原版 `Item.Mesh` 加原版材質，跟軍械庫掛架用的是同一份程式碼，並關掉陰影。
- 萬一遊戲裡找不到那把槍，就退回成稀有度顏色的方塊，不會是空的。

### 5. Boomerang 手持
- 改用 `Spec(Boomerang).Euler = StandUp`（0,0,90），跟 `Icon()` 的修正是同一個值。

### 6. 測試（`GunSpecChecks`）
- 17 把都有 spec 和槍口，而且槍口在 OBJ bbox 內。槍類（Pistol／Long／Heavy）的槍口要在槍管前端、x≈0。
- `HeldScale` 會得到 spec 指定的長度；後伸不超過 `BackMax`；掉落 ≥ 手持，Epic 比 Rare 大。
- 尺寸排序：Pistol < Long < Heavy，而且都大於舊的 0.22 m。Rocket 和 Gatling 至少是 0.22 m 的 2.5 倍。
- Boomerang／Whistle／Conch／Puffer 不是 Pistol 類，而且有轉角；Boomerang 的轉角等於 `StandUp`。
- Source guard：
  - 不再有 `Muzzle(o, d)`，固定 fallback 只剩一處
  - 朋友的射擊有走 `HeldMuzzle(shooter`
  - Loot 有用 `GameGunModel`
  - Weapons 有 `_sharks.Add(sh)`
- 遊戲原槍：`DropModel == null` 且 `GameName` 有值；其他武器的掉落 OBJ 都存在。
- 鯊魚：
  - 隻數 1／1／1／2
  - `SharkStep` 會朝獵物前進，也會被拴在 14 m 內
  - 壽命足夠咬一次以上

## 還沒做（第二輪以後）

- **實機驗證**：held／ADS／drop／hotbar 截圖，以及兩台 client 的槍口對齊。上面的數值（長度、`FromHand`、四把非手槍的轉角、`BackMax`）都還沒實機看過，可以用 AutoTest `weapons` 的 `held_*` 截圖校正。
- **姿勢**：手仍然是遊戲的 pistol hand rig。長槍沒有雙手／肩托姿勢，只是往前推。
- **投射物尺度**跟手持一致（Boomerang／Claw／Hook／Jelly／Rocket 飛出去會變大）。
- **Gatling**：模型 6 管 vs 玩法 10–40 管；tracer 上限 12。
- **單一顯示來源**：icon 的 chirality、drop／hotbar 沒有 rarity bands、Absorber icon 沒有玻璃罩。
- **模型重做**：Rocket、LobsterClaw、Whirlpool vs Conch、四把無握把武器的握把；材質數 ≤4；StarThrower 的 tris 要 ≤1500。
- 卡片文字／DPS／range（L10n）。
- 鯊魚在沙地上沒有持續存在的水坑視覺，目前只有跳出和每次咬時的水花，加上 foam wake。
