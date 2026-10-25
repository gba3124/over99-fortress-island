# GUN-MODEL-AUDIT — 槍械模型與顯示大盤點

> 專案：`htf-fortress`（How to Fish / BepInEx）  
> 版本：0.2.8 dev（依 repo 內文件與代碼）  
> 日期：2026-10-26  
> 範圍：全部 23 個 `GearCatalog.IsWeapon` 種類（17 把要塞武器 + 6 把遊戲原槍）  
> 性質：**唯讀審計**。本文件沒有改動任何 C# / OBJ / MTL / PNG / tool。

---

## 0. 先講結論

- 17 把要塞武器都有專屬 OBJ，沒有缺檔、沒有 cube placeholder；6 把遊戲槍使用原版 prefab。**「有沒有模型」不是問題，「模型怎麼被顯示」才是。**
- 多數輪廓是成功的：雷射槍、毒氣噴槍、泡泡槍、水母槍、撒網砲、河豚炸彈、海星發射器、龍蝦鉗、火箭筒、吸收槍、加特林都一眼可辨。真正弱的是 **Whirlpool（看不出漩渦）** 與 **Boomerang 拿在手上的樣子（像一條魚）**。
- 17 張 `Gear*.png` 全部存在，而且都是從同一批模型 render 的；**沒有「卡片放錯槍」**。但 icon、hotbar、掉落、手持是四套不同的尺度／材質／chirality，這才是最大問題。
- 最嚴重的不是單一模型，而是 **全體被同一個 0.22 m 上限壓成手槍大小**、**槍口不是由實際槍管決定**、**加特林只有 6 管但玩法是 10–40 管**、**沒有真正的握把／持槍姿勢系統**。

### 五個 P0（詳見 §3）

> 優先級定義：P0 = 每次看都錯、或影響全部槍；P1 = 某一種視圖／少數槍明顯不符；P2 = 收斂與細節。

1. **尺度坍縮**：`WeaponItems.InHand.Longest = 0.22f` 被當成全體上限；Rocket 0.620→0.220、Laser 0.494→0.220、Gatling 只有 0.330（0.22 ×1.5），Puffer 另被 `Across` 壓到 0.154。手槍、火箭筒、機槍在手上幾乎一樣大。
2. **加特林管數不符**：模型 6 管（`make_weapon_models4.py:34-46`），玩法是 Common 10 / Rare 20 / Epic 30 / Legendary 40 管（`Weapons.cs:65-69`），FX 最多只畫 12 條 tracers（`Weapons.cs:2345`）。
3. **槍口／FX 沒有跟槍管對齊**：本地用 mesh bounds 的 `center.x / center.y / max.z`（`Weapons.cs:1412-1419`）而非砲口；別人的畫面直接用 host 的 `o + d*0.6 - up*0.25`（`Weapons.cs:357`），等於從胸口前方發射。
4. **顯示來源分裂**：手持 0.22 m、掉落 0.60/0.75 m、hotbar 正規化到 pistol、2D icon 各自取景；稀有度 bands/crest 只出現在手持，drop/icon 沒有；icon 還是 Blender 原始 chirality，runtime 被 `Models.Parse` 做 X 鏡像。
5. **沒有握把／姿勢語言**：17 把全部套同一個 `InHand` 與同一組 pistol hand pose；Conch、Boomerang、PufferBomb、SharkWhistle 根本沒有 `grip()`；火箭筒、加特林、撒網砲、高壓水砲都是一手持。

---

## 1. 方法與可驗證範圍

- 對照 `Gear.cs`（`Gear` enum、`GearCatalog` 數值／冷卻）、`Weapons.cs`（實際玩法、`ModelName`、muzzle、FX、projectile）、`WeaponItems.cs`（手持 attach／縮放／icon）、`VaultUI.cs`／`GearCards.cs`／`Hud.cs`（卡片與圖示）、`Loot.cs`（掉落顯示）。
- 用 Python 解析 17 個 OBJ 的 `v` / `f` / `usemtl`：vertex 數、triangle 數、material slot、bbox、原始長度。數字見 §2。
- 檢查 `Icons/Gear*.png` 的 256×256 alpha bbox；檢查 `img/arsenal.png` 的意圖比例；檢查 `tools/make_weapon_models*.py`、`bkit.py`、`make_icons.py` 的生成慣例。
- **沒有啟動遊戲**，因此「第一人稱實際看起來如何、手有沒有穿模、朋友端 FX 是否真的從槍口出來」屬於未實機驗證；repo 內也沒有 `held_*` / `weapon_view` 截圖。凡是推論都會標「推論」。

---

## 2. 武器總表（23 個 `IsWeapon` 種類）

> 模型欄格式：`bbox X×Y×Z（m） → 手持最長邊`；`v/t/m` = OBJ vertices / triangles / material slots。  
> `✅` 專屬模型、`♻️` 重用、`⚠️` runtime 組裝；沒有 `❌` 缺檔。

| Gear / 名稱 | 實際玩法（wave-1 數值） | 模型（檔案；v/t/m；bbox → 手持） | 手中 offset 來源 | Icon | 槍口／FX | UI／card | 判定 |
|---|---|---|---|---|---|---|---|
| `Laser` 雷射槍 | 30 m 光束；30–72/s，越照越痛 ×3；過熱 4.5–8 s；cd 0 | `LaserGun` ✅ 499/902/4；0.170×0.267×0.494 → 0.220 | `InHand` | `GearLaser.png` | bbox lens；`Beam` | PNG；HUD heat bar | ⚠️ P1 尺度／槍口 |
| `PoisonGas` 毒氣槍 | 24 m 拋毒雲，半徑 3 m／6 s；每層 4.5–15/s，最多 5 層；cd 2.2–1.5 | `GasGun` ✅ 447/806/7；0.142×0.365×0.543 → 0.220 | `InHand` | `GearPoisonGas.png` | bbox nozzle；`GasView` | PNG；一般卡片 | ✅ P2 輪廓好 |
| `Boomerang` 鮪魚回力鏢 | 18 m 往返；24–46 去回各一次；Common 1／Rare 2／Epic 3／Legendary 4 支；cd 0（約 1.25 s 回來） | `Boomerang` ✅♻️ 489/886/5；0.611×0.090×0.319 → 0.220 | `InHand` | `GearBoomerang.png` | bbox front；`BoomerangView` | PNG（HIGH 視角） | ⚠️ P1 手持像魚 |
| `SharkWhistle` 鯊魚哨 | 召喚 1–2 隻鯊魚到瞄準海面；每咬 60–105（spitter ×3）；cd 34–22 | `SharkWhistle` ✅ 449/826/7；0.067×0.161×0.162 → 0.162 | `InHand` | `GearSharkWhistle.png` | 無槍口；Water／Shark FX | PNG；一般卡片 | ⚠️ P2 無吹奏姿勢 |
| `BubbleGun` 泡泡槍 | 25 m 命中的魚進泡泡 2–3.5 s，+50% 傷害；cd 0.9–0.6 | `BubbleGun` ✅ 657/1238/7；0.190×0.343×0.481 → 0.220 | `InHand` | `GearBubbleGun.png` | bbox wand；`BubbleView` | PNG；一般卡片 | ✅ P2 輪廓好 |
| `Hook` 鉤爪槍 | 25 m 鉤魚 23–51 傷害＋拉回；可鉤地上的物品再甩出、可鉤牆把自己拉過去；cd 2.2–1.2 | `HookGun` ✅ 584/1100/7；0.127×0.302×0.567 → 0.220 | `InHand` | `GearHook.png` | bbox hook tip；`HookView` | PNG；HUD 鉤住物 | ⚠️ P1 槍口／icon 遮住捲線器 |
| `Whirlpool` 漩渦砲 | 24 m 拋漩渦，半徑 6 m，持續 3–5 s 拉魚；每秒 5 傷害；Legendary 結束爆炸 40；cd 9–5.5 | `WhirlCannon` ✅ 667/1290/6；0.264×0.309×0.525 → 0.220 | `InHand` | `GearWhirlpool.png` | bbox bell；`WhirlView` | PNG；一般卡片 | ❌ P1 看不出漩渦 |
| `Conch` 聲波海螺 | 10 m、60° 錐；推 4–6 m＋10 傷害；Legendary 定身 1.5 s；cd 4.5–2.6 | `Conch` ✅ 732/1420/6；0.293×0.262×0.407 → 0.220 | `InHand` | `GearConch.png` | bbox lip；`SonicView` | PNG；一般卡片 | ⚠️ P1 無握把 |
| `NetLauncher` 撒網砲 | 24 m 拋網，半徑 3.5 m（Legendary ×1.5）；網住 2.5–4.5 s＋8 傷害；cd 7–4 | `NetLauncher` ✅ 373/666/7；0.193×0.264×0.516 → 0.220 | `InHand` | `GearNetLauncher.png` | bbox net；`NetView` | PNG；一般卡片 | ⚠️ P1 尺度／單手 |
| `JellyGun` 水母槍 | 24 m 丟水母，6 s；半徑 3 m 每秒 12–32＋減速；cd 3.8–2.4 | `JellyGun` ✅ 315/574/6；0.104×0.327×0.415 → 0.220 | `InHand` | `GearJellyGun.png` | bbox trumpet；`JellyView` | PNG；一般卡片 | ✅ P2 輪廓好 |
| `WaterCannon` 高壓水砲 | 15 m 持續水柱，半徑 1.3 m；每秒推 6–10 m；水箱 3.5–5.5 s；cd 0 | `WaterCannon` ✅ 293/522/7；0.122×0.336×0.478 → 0.220 | `InHand` | `GearWaterCannon.png` | bbox nozzle；`WaterView` | PNG；HUD tank bar | ⚠️ P1 噴嘴太細／尺度 |
| `PufferBomb` 河豚炸彈 | 24 m 拋擲，1.2 s 引信；半徑 3.5 m，60–115 傷害＋毒；cd 3.8–2.6 | `PufferBomb` ✅♻️ 278/464/7；0.251×0.245×0.246 → 0.154 | `InHand` | `GearPufferBomb.png` | bbox nose；`PufferView` | PNG；一般卡片 | ⚠️ P1 被當槍拿 |
| `StarThrower` 海星飛鏢 | 25 m 扇形 3–5 隻，每隻 13–27，穿透 2 隻，會反彈；cd 0.7–0.42 | `StarThrower` ✅ 929/1570/5；0.085×0.255×0.267 → 0.220 | `InHand` | `GearStarThrower.png` | bbox spring；`StarView` | PNG；一般卡片 | ⚠️ P1 彈匣 4 隻／tris 超標 |
| `LobsterClaw` 龍蝦鉗 | 12 m 夾第一隻魚甩出，落地 90–180＋濺射 2.5 m；cd 1.9–1.1 | `LobsterClaw` ✅♻️ 180/300/4；0.150×0.262×0.440 → 0.220 | `InHand` | `GearLobsterClaw.png` | bbox jaw；`ClawView` | PNG；一般卡片 | ⚠️ P1 沒有線／投影 3.2× |
| `Rocket` 火箭筒 | 40 m 直線，半徑 3 m；40–73 傷害；Legendary 三發；cd 1.6–1.2 | `Rocket` ✅ 286/508/7；0.146×0.307×0.620 → 0.220 | `InHand` | `GearRocket.png` | bbox tube；`RocketView` | PNG；一般卡片 | ❌ P0 尺度＋姿勢 |
| `Pistol` 手槍 | 原版射擊／換彈；要塞魚傷害 ×1–2.6（再乘 wave／mod） | 原版 `pistol` prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 稀有度不可見 |
| `Shotgun` 霰彈槍 | 原版射擊／換彈；×1–2.6 | 原版 prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 |
| `Smg` 衝鋒槍 | 原版射擊／換彈；×1–2.6 | 原版 prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 |
| `Sniper` 狙擊槍 | 原版射擊／換彈；×1–2.6 | 原版 prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 |
| `Rifle` 突擊步槍 | 原版射擊／換彈；×1–2.6 | 原版 prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 |
| `Bow` 弓 | 原版射擊／換彈；×1–2.6 | 原版 prefab ♻️ | 原版 rig | runtime `ItemIcons` | 原版 FirePoint；原版 FX | ItemIcons；`gunNow` | ✅ P2 |
| `Absorber` 吸收槍 | 8 m／30° 吸取；複製物品或王招；18–40 基礎；cd 0.9–0.55 | `Absorber` ⚠️ 515/966/6；0.223×0.362×0.444 → 0.220；**runtime 加玻璃罩** | `InHand` | `GearAbsorber.png` | bbox intake；`AbsFx` | PNG；HUD 顯示持有物 | ⚠️ P1 icon 缺玻璃罩 |
| `Gatling` 加特林機槍 | 30 m；轉滿 12 波/s × 10–40 管；每發 11.5–24；cd 0 | `Gatling` ⚠️ 545/950/7（+ `GatlingBarrels` 252/440/4）；0.190×0.319×0.628 → 0.330；**runtime 切管＋旋轉** | `InHand ×1.5` | `GearGatling.png` | bbox cluster；`GatlingVolley` | PNG；HUD spin bar | ❌ P0 6 管 vs 10–40 |

> `InHand` = `Grip { Longest = 0.22f, FromHand = (-0.03, 0.03, 0.05) }`（`WeaponItems.cs:229`）；Gatling 另 ×1.5（`WeaponItems.cs:136`）；遊戲槍用原版 rig。  
> 取得：Rocket 開局贈送；Absorber / Gatling 只在鐵匠（Best ≥10 / ≥80）；其餘掉落或鐵匠。  
> 遊戲槍的 ID 與原版 prefab 對應在 `WeaponItems.cs:38-49`；17 把要塞武器的 `ModelName` 映射在 `Weapons.cs:130-143`；所有 `Gear*.png` 都是 256×256、透明背景、內容留約 14 px 邊。  
> 缺失：0；placeholder：0；重用：Boomerang／LobsterClaw／PufferBomb 也當場上投射物，6 把遊戲槍用原版 prefab。

---

## 3. 共用管線問題（依嚴重度）

### 3.1 P0：尺度坍縮，所有槍都變成手槍大小

`WeaponItems.Place` 的公式（`WeaponItems.cs:229-239`）：

```text
k = min(1, 0.22 / max(bbox), 0.15 / min(bbox.x, bbox.y))
Gatling 再 ×1.5
```

結果：

| 武器 | 原始最長邊 | 手持最長邊 | 說明 |
|---|---:|---:|---|
| Rocket | 0.620 m | **0.220 m** | 接近遊戲手槍的 0.22 m；原始模型還比 real M72 LAW（≈0.9 m）短 |
| Laser | 0.494 m | **0.220 m** | 從 SMG 級 ray gun 變成手槍 |
| Gatling | 0.628 m | **0.330 m** | ×1.5 後仍只有 33 cm，real M134 ≈0.8–1.0 m |
| NetLauncher | 0.516 m | **0.220 m** | 長槍變手槍 |
| Boomerang | 0.611 m（翼展） | **0.220 m** | 真實回力鏢 0.55–0.65 m |
| PufferBomb | 0.251 m | **0.154 m** | 只有 `Across = 0.15` 這條限制在作用 |
| SharkWhistle | 0.162 m | **0.162 m** | 唯一沒有被縮小的（k = 1） |

- 17 把裡有 15 把套用同一個 0.22 m 公式；其中 Gatling 最後再 ×1.5 → 0.33 m。只有 SharkWhistle（不縮）與 PufferBomb（被 `Across` 限制成 0.154 m）完全不受 0.22 m 限制。
- 這不是「每把槍剛好都 22 cm」，而是 **相對比例被抹平**。`img/arsenal.png` 的意圖比例明明有手槍、長槍、火箭筒、巨大加特林的分級；目前第一人稱只剩一個尺寸。
- 掉落的尺度又是另一套：`Loot.MakeDropView` 把**每一把**武器的最長邊縮放到 0.60 m（Epic+ 0.75 m）（`Loot.cs:1184-1193`）。  
  → 0.22 m 類的槍：手持 0.22 m、掉落 0.60–0.75 m（**2.7–3.4×**）。撿起來會瞬間縮小。Gatling／Puffer 的比例不同，但同樣是兩套尺度。
- 投射物也沒有跟手持模型對齊：

| 武器 | 手持 | 場上投射物 | 比例 |
|---|---:|---:|---:|
| Boomerang | 0.220 | 0.611（`Boomerang` 原模型） | **2.8×** |
| LobsterClaw | 0.220 | 0.440 ×1.6 = 0.704 | **3.2×** |
| Hook | 0.220 | `GrappleClaw` 0.437 ×1.5 = 0.656 | **3.0×** |
| Rocket | 0.220 | `RocketShell` 0.575 | **2.6×** |
| JellyGun | 0.220 | `Jellyfish` 0.870 ×1.3 = 1.131 | **5.1×** |
| PufferBomb | 0.154 | 0.251 ×1.3 = 0.326 | **2.1×** |
| StarThrower | 0.220 | `Starfish` 0.301 ×1.4 = 0.421 | 1.9×（彈匣內單顆約 0.09） |

  `Models.Attach` 在模型存在時會忽略 fallback size（`Models.cs:110-130`）；上表是真的模型尺寸，不是備援 cube。

**根因**：`Place` 用單一 `InHand.Longest` 當全武器上限；不是每把槍有自己的 held scale。註解也承認這是為了避免槍托撞鏡頭（`WeaponItems.cs:124-127`）。  
**正確方向**：per-weapon profile（class、held length、grip offset、shoulder/pose、muzzle）；長槍用 shouldered／two-handed pose 與相機安全距離，不是把整把槍縮小。

### 3.2 P0：槍口／FX 不是從槍管出來

- Host 的通用 muzzle（`Weapons.cs:357`）：`o + d*0.6 + Vector3.down*0.25`。  
  → 對**別人的畫面**，所有光束／tracer／火箭都從「相機前方 0.6 m、下方 0.25 m」開始，而不是從他們的槍口。
- 本地射手才替換成 `MyMuzzle`：取 mesh bounds 的 `center.x / center.y / max.z`（`Weapons.cs:1412-1419`）。  
  → 不是砲口軸線；只要模型左右／上下不對稱（彈箱、電池、瞄具、鉤子），FX 就會偏。
- 以目前模型換算（手持尺度下，實際偏移）：

| 武器 | 槍管軸 vs `MyMuzzle` | 備註 |
|---|---:|---|
| StarThrower | **2.8 cm 偏低** | bbox 中心 y=.0565，槍管 y=.09 |
| NetLauncher | **1.9 cm 偏低** | 網球讓 max.z 提前 |
| LobsterClaw | **1.8 cm 偏低** | 鉗子軸線在 y=.09 |
| Laser | **1.5 cm 偏低** | 鏡片在 y=.09 |
| Gatling | **1.5 cm 偏低＋1.9 cm 偏右** | 彈箱讓 bbox 中心偏 x；X 又被鏡像 |
| Rocket | **1.4 cm 偏低** | 管軸 y=.115 |
| Hook | 0.8 cm 偏低＋6.6 cm 偏前 | max.z 是鉤尖，不是槍口（原始 17 cm × 0.388） |
| Boomerang | 無槍口 | max.z 是魚身厚度邊緣 |

- 而且 **沒有 muzzle marker**：模型沒有空物件、沒有專用 material，`MyMuzzle` 只能猜 bbox。  
- `KickHeld` 的後座只動本地（`Weapons.cs:1404-1409`），朋友看不到後座；`fromMe` 的判定也只看 `e.From` 是否在 2 m 內（`Weapons.cs:1560-1566`）。

**影響**：自己的光束／火箭可能從槍身下方出來；朋友看到的是胸口火球。近距離（2–3 m）最明顯。

### 3.3 P0：握把與姿勢是 one-size-fits-all

- 所有武器共用同一個 `Grip { Longest = 0.22, FromHand = (-0.03, 0.03, 0.05) }`（`WeaponItems.cs:225-241`）；沒有 per-weapon grip offset、pitch、roll、shoulder。
- 17 把之中只有 13 把呼叫 generator 的 `grip()`（`make_weapon_models.py:51-54`）；**沒有真正握把**的分別是：
  - `Conch`：整顆海螺，沒有握把、沒有扳機（`make_weapon_models.py:271-302`）。
  - `Boomerang`：origin 在 V 的中心，沒有握把（`make_weapon_models.py:114-177`）。
  - `SharkWhistle`：鯊魚頭＋吹嘴，沒有握把（`make_weapon_models.py:179-202`）。
  - `PufferBomb`：圓球＋引信，只有一塊「hand holds it」的方塊（`make_weapon_models2.py:153-170`）。
- `Conch`、`Boomerang`、`PufferBomb`、`SharkWhistle` 被當成 pistol 拿：手的位置是 pistol hand target，**不是**這些物件實際被握／被吹／被丟的位置。推論：手會穿進海螺、握在河豚底部、或托著回力鏢中心。
- `Rocket`、`Gatling`、`NetLauncher`、`WaterCannon` 都是一手持，沒有前握把／肩托／支持手；與它們的長度、重量、後座設定不符。
- 第一人稱與朋友端看到的是同一個 prefab、同一個 `hand.parent` 下的模型（`WeaponItems.cs:130-135`），所以**手持模型本身兩邊一致**；不一致的是 FX 起點（§3.2）與後座只動本地（`KickHeld`）。真正的問題是沒有 per-weapon pose，而不是同步。
- 遊戲槍直接使用原版 hand rig，這部分反而是正確的；問題只在要塞武器。

### 3.4 P0：五條顯示路徑，五套尺度／材質／chirality

| 顯示路徑 | 來源 | 尺度 | 材質／稀有度 | runtime 追加件 |
|---|---|---|---|---|
| 手持（自己／朋友） | `Weapons.ModelName` + `Place` | 最長 0.22 m（Gatling 0.33） | `RarityLook`：rare/epic bands、legendary crest、金化 | ✅ Absorber 玻璃罩、Gatling 旋轉管 |
| 地上掉落 | `Loot.DropModel` + `MakeDropView` | 最長 0.60／0.75 m | `share:true` 的原始材質；**沒有 bands/crest** | ❌ 沒有 |
| Hotbar | `Item.Mesh = model mesh` + `Icon()` | 以 pistol 最長邊正規化 | 只有主 mesh 材質；**bands/crest 是 child，不會進 slot** | ❌ 沒有 |
| 2D icon（vault／card／HUD） | `Icons/Gear*.png` | 各圖獨立取景、留約 14 px | Blender 原始材質；**沒有稀有度變化** | ❌ Absorber 沒有玻璃；Gatling 沒有切管 |
| 遊戲槍 icon | `ItemIcons.Of(GameId)` | runtime 截圖 160×160 | 原版 | — |

補充事實：

- **Chirality 不一致**：`Models.Parse` 把 X 取負（`Models.cs:250`），但 `make_icons.py` 直接 render Blender 模型（`make_icons.py:325,357-361`）。  
  → icon 是 Blender 原始左右；手持／掉落是 X 鏡像。Hook 的捲線器、Gatling 的彈箱、WaterCannon 的視窗、Absorber 的軟管在兩邊會不同側。  
  → `SIDE = (-1, -0.45, 0.35)` 是從 Blender -X 看；runtime 又做 X 鏡像，**不能假設 icon 與第一人稱看到同一面**。實際第一人稱看到哪一側還需截圖，但 chirality 不一致是幾何事實。
- **Hotbar 沒有 bands/crest**：`RarityLook` 用 `GameObject.CreatePrimitive` 加 child（`WeaponItems.cs:200-218`），但 `Icon()` 只把主 mesh 設給 `Item.Mesh`（`WeaponItems.cs:245-263`），`Colours()` 也只回主 renderer 的材質（`WeaponItems.cs:266-267`）。
- **Drop 沒有稀有度外觀**：`MakeDropView` 只加 rarity ring + gem（`Loot.cs:1196-1200`），沒有 `RarityLook`。
- **Absorber icon 缺玻璃罩**：PNG 從 `Absorber.obj` render；玻璃罩是 runtime 加的（`Absorber.cs:385-419`）。vault／card 永遠是空的吸收槍。
- **Gatling icon 是整把槍**：runtime 會把 barrels 切出來旋轉（`WeaponItems.cs:147-172`），icon 不會；不過外觀大致一致，除了管數。

### 3.5 P1：材質與多邊形預算

- 15/17 把超過 `CURRENT-DESIGN.md:306` 的「材質 1–4 個」：Gas 7、Whistle 7、Bubble 7、Hook 7、Net 7、Water 7、Puffer 7、Rocket 7、Gatling 7、Whirl 6、Conch 6、Jelly 6、Absorber 6、Boomerang 5、Star 5。只有 Laser 與 LobsterClaw 是 4。
- 17 把主模型合計 **8,238 vertices / 14,988 triangles**（不含 `GatlingBarrels` 252/440/4）；只有 `StarThrower` 1,570 tris 超過 `CURRENT-DESIGN.md:305` 的武器 1,500 預算（超 4.7%）。
- 所有 MTL 都有對應 `Kd`，沒有缺 material；但多數是 5–7 個 slot，會讓每個 rarity／每個持有者都多出材質與 draw call，也讓 `RarityLook` 的「只染發光材質」規則更難一致。
- 風格局部正確：低多邊形、flat、硬邊、無貼圖、`Ke` 發光；配色也大致跟 `img/arsenal.png` 一致。**不是美術方向錯，是資產規格沒收斂。**

### 3.6 P1：卡片數值與文字沒有完全對上玩法

| 問題 | 代碼／文字 | 實際 |
|---|---|---|
| Gatling 寫「每秒 12 發」 | `L10n.cs:830-832` | 12 波/s × 10–40 管；`DpsNow` 也是 `12 × barrels`（`Weapons.cs:1451`）。中文版甚至省略「10 管」 |
| Whirlpool 只寫拉魚 | `L10n.cs:882` | 每秒 5 傷害（`Weapons.cs:1183-1188`）；Legendary 結束爆炸 40（`Weapons.cs:1173-1178`） |
| Hook 只寫鉤魚 | `L10n.cs:879` | 可鉤地上的物品、甩出、鉤牆自我牽引（`Weapons.cs:501-527`） |
| NetLauncher 只寫纏住 | `L10n.cs:773` | 落地還有 8 傷害（`Weapons.cs:555-565`） |
| DPS 顯示不是真 DPS | `Weapons.cs:1453-1455` | 多數武器用「單發傷害 ÷ cd」；Boomerang cd=0 → 顯示 0；Star 的 3–5 發／穿透、Rocket 的 3 發／AoE 都沒算 |
| 卡片沒有 range | `GearCatalog.Effect`（`Gear.cs:163-171`） | `RangeOf` 有 10–40 m 的差距（`Weapons.cs:106-128`），卡片完全看不到 |
| 遊戲槍只寫 ×1–2.6 | `L10n.cs:802-822` | 實際是 `GunShare × wave`（`WeaponItems.cs:54-58`）；HUD 持有時才顯示真實單發 |

卡片不是「全錯」：核心效果大多正確，rarity／cooldown／legend 文字也有；問題是**關鍵機制、AoE、多發、range 被省略，DPS 又用單發÷cd 假裝成實際輸出**。

---

## 4. 逐槍判定（不把 §3.1 的尺度問題重複算進每一把）

### P0

- **`Rocket` 火箭筒 — ❌**：模型本身是 0.620 m 的「fat olive launch tube」＋固定 warhead（`make_weapon_models3.py:45-61`），但手持被縮到 0.220 m，且用 pistol grip 單手拿。warhead 永遠插在管口；實際 projectile 是另一顆 `RocketShell`（0.575 m，`Weapons.cs:1968-1981`），發射後槍上那顆不會消失／不會 reload。Legendary 三發（`Weapons.cs:632-645`）也只有一根管子。**尺度、姿勢、裝填狀態、三發表現全部不符。**
- **`Gatling` 加特林 — ❌**：輪廓是合格的 minigun（receiver、carry handle、ammo box、belt、glow ring），但 **6 管**（`make_weapon_models4.py:34-46`）對上 10/20/30/40 的玩法（`Weapons.cs:65-69`）；tracers 上限 12（`Weapons.cs:2345`）。旋轉管有做出來（`Weapons.cs:2355-2367`），但管數與 rarity 的管數成長沒有視覺化（只有 bands/crest）。手持 0.330 m，仍遠小於設定。

### P1

- **`Boomerang` — ⚠️**：icon 的 V 形鮪魚很成功，但手持時是平放的一條魚（`Place` 只給 0.22 m），沒有「握住投擲端」的姿勢；2–4 支的 rarity 成長只存在於玩法，手上永遠一支。投擲物又是 0.611 m（2.8×），一丟就變大。
- **`Conch` — ⚠️**：輪廓強、貝殼／珊瑚／黃銅配色對，但**完全沒有握把／扳機**（`make_weapon_models.py:271-302`），卻套 pistol hand pose；吹嘴在 +Y（靠玩家臉），開口在前；以「槍」的持法會很怪。這是 17 把裡最需要補握把的。
- **`PufferBomb` — ⚠️**：河豚＋刺＋引信讀得很清楚，但它是**拋擲物**，卻用 `InHand` 手槍姿勢、按左鍵「射」出去；手上沒有拔 pin 的動作，投射物又從 0.154 m 變 0.326 m。
- **`NetLauncher` — ⚠️**：木托＋黃銅網筒＋紅浮球的方向正確；但 0.516 m 的長槍被縮成 0.220 m，單手拿，沒有 shoulder stock 的實際貼肩。網球永久塞在槍口，沒有「重新裝網」的視覺。
- **`WaterCannon` — ⚠️**：藍色壓力桶＋黃銅噴嘴＋水位窗的辨識度高；但噴嘴管徑只有 2.8 cm（`make_weapon_models2.py:135-151`），對比 1.3 m 半徑、6–10 m/s 的推力顯得過細；水箱／泵把也沒有連到噴嘴的 hose。手持也應是 two-handed。
- **`Hook` — ⚠️**：iron grapple pistol＋gold hook＋rope reel 的設定很好；但 hook 是**固定在槍口**的模型，發射時另外飛出 `GrappleClaw`（`Weapons.cs:2117-2123`），槍上那支鉤子不會離手，讀起來像「鉤子後面還有一支鉤子」。rope reel 在 icon 的背面（SIDE 相機）看不到。`max.z` 的 muzzle 是鉤尖，不是槍口。
- **`Absorber` — ⚠️**：粉色真空＋金環＋玻璃罩的設定很強，`DomeAt` 也跟模型對得上（`Absorber.cs:385-386`）；但 **icon 沒有玻璃罩**、card 永遠是空的；手持是單手，且 0.444 m 被縮成 0.220 m。吸收物的展示只在 runtime 的玻璃裡，vault 看不到「它現在吸了什麼」。
- **`StarThrower` — ⚠️**：海星彈匣＋navy／brass 的輪廓好；但它是「wrist launcher」設定，卻有 pistol grip＋扳機，且彈匣固定 4 顆，玩法是 3/3/4/5 顆（`StarCount`）。1,570 tris 超過 1,500 預算；投射海星 0.421 m，彈匣內單顆約 0.09 m，比例跳 4–5×。
- **`LobsterClaw` — ⚠️**：紅鉗＋白牙＋iron handle 的剪影很好，但沒有 rope／spool；玩法是「鉗子射出去再拉回來」（`Weapons.cs:2015-2040`），槍上卻沒有任何線索。投射鉗 0.704 m，是手持的 3.2×。
- **`Whirlpool` — ❌（輪廓）**：`WhirlCannon` 是 navy 底＋白螺旋條紋＋flared bell＋背後 crystal core（`make_weapon_models.py:253-269`）；它讀起來像「條紋喇叭／手砲」，沒有漩渦、沒有水、沒有旋轉環。玩法是 6 m 半徑的 vortex，模型上沒有對應的視覺元素。

### P2

- **`Laser` — ⚠️**：白色 ray gun＋teal＋crystal lens＋battery＋fins，辨識度高，比例也像 ray gun；但 `CURRENT-DESIGN` 要求的「散熱管」不明顯，過熱只有 HUD 條。muzzle 比 lens 低 1.5 cm。
- **`PoisonGas` — ✅**：紫色 pump＋綠色玻璃毒罐＋黃銅噴嘴，完全符合「毒氣噴槍」；7 mats 與 0.6 cm muzzle 偏移是待收斂項。
- **`BubbleGun` — ✅**：粉紅玩具＋橘色泡泡環＋肥皂瓶，讀得出「泡泡槍」；7 mats 與 0.481→0.22 的縮放是系統性問題。
- **`JellyGun` — ✅**：珊瑚粉＋玻璃水母罩＋trumpet nozzle，設定正確；投擲水母 1.13 m 與手持 0.22 m 的比例是 §3.1 的投射物問題。
- **`SharkWhistle` — ⚠️**：鯊魚頭＋牙＋吹嘴＋紅繩，辨識度高；但用 pistol 姿勢「發射」，沒有吹奏／舉到嘴邊的動作。尺寸 0.162 m 反而是 17 把裡最正確的。
- **6 把遊戲槍 — ✅（模型）／⚠️（rarity）**：直接用原版 prefab、原版射擊、原版 icon（`WeaponItems.cs:100-112`；`Hud.cs:70-72`）。模型與顯示**沒有被 mod 破壞**；但 rare/epic/legendary 只反映在名字、卡片 ring 與 `RarityGem`，槍身本身沒有任何稀有度視覺。這跟要塞武器的 bands/crest 是兩套語言。

---

## 5. 共用風格指南（給之後的模型／顯示）

### 5.1 槍械語言

| 類別 | 範例 | 握法 | 手持目標長度 | 必要構件 |
|---|---|---|---:|---|
| Pistol／ray gun | Laser、Gas、Bubble、Hook、Absorber | 單手 pistol grip | 0.22–0.35 m | 握把、扳機護弓、明確槍口 |
| 長槍／工具 | NetLauncher、WaterCannon、Whirlpool | 雙手／前握把 | 0.40–0.60 m | 主握把＋前握把、stock 或 shoulder rest |
| 重武器 | Rocket、Gatling | 右手主握＋左手前握／肩托 | 0.70–0.90 m | 前握把、肩托、後座空間、清楚的 muzzle plane |
| 拋擲物 | Boomerang、PufferBomb | 單手握持點／投擲握把 | 0.20–0.55 m | 手握區、pin／投擲缺口、沒有假槍管 |
| 吹奏／特殊 | Conch、SharkWhistle | 手握 shell／whistle body | 0.16–0.40 m | 吹嘴、手握帶、開口方向 |
| 遊戲槍 | Pistol…Bow | 原版 | 原版 | **不要改模型**；只加一致的 rarity 提示 |

- **原點**：手持武器以「握把中心」為原點（現有 generator 已是這樣，`make_weapon_models.py:51-54`）；`CURRENT-DESIGN.md:302` 的「底部中心」是塔／場景規格，**不適用於手持槍**。這條要寫回文件，否則下一個模型 LLM 會做錯。
- **軸向**：`+Z` 前、`+Y` 上、1 unit = 1 m；槍管軸線固定在 `x=0`，高度 `y≈0.09–0.11`（依類別），且 **所有槍口都要有一個明確的 `Muzzle` 位置**（見 §7）。
- **槍托**：長槍的 stock 最遠不超過握把後方 0.28 m；超過就需要 shoulder pose 或把槍往前移，不能靠全域縮小解決。
- **材質**：2–4 個 slot；金屬／木頭／布料用 `Kd`；只有能量、毒液、水、玻璃用 `Ke`；不要用透明貼圖。共用色票（`CURRENT-DESIGN.md:321-327`）優先，特殊色才新增 `Wpn*`。
- **稀有度**：每把槍只留 **一個 accent slot**；rare 一圈 band、epic 兩圈、legendary 金化＋crest。bands 要貼在 receiver／stock 的固定局部座標，不要用整個 bbox 的 `min.z + 0.32*size.z` 亂套（`WeaponItems.cs:194-206` 目前對 Boomerang、Puffer、Conch 這類非圓柱體會切錯）。
- **比例分級**：`img/arsenal.png` 已經給了意圖：手槍小、雷射中、火箭筒與加特林大。任何新模型先跟這張圖比對，不要讓 17 把都落在同一個 0.22 m。

### 5.2 單一顯示來源

建議資料流（未來實作，不是本任務）：

```text
WeaponProfile (per Gear)
  ├─ Model name / MTL
  ├─ HeldLength, GripFromHand, HeldEuler, Pose (pistol/two-hand/shoulder/throw/blow)
  ├─ MuzzleLocal (x,y,z), MuzzleRadius
  ├─ Class (pistol/rifle/launcher/heavy/thrown/tool)
  └─ IconView (yaw/pitch, rarity variants)

同一個 runtime mesh + profile
  ├─ 手持：Place(profile)
  ├─ 掉落：DropScale = HeldLength × 1.0（或固定 0.45 m，但每把一致）
  ├─ Hotbar：用同一個 mesh + profile 的 inventory framing
  └─ Card/Icon：用同一個 runtime mesh render（或至少同 chirality、同材質）
```

規則：

1. **一把槍只有一個 mesh、一個材質集合、一個 muzzle**；icon、drop、hotbar 都是它的「視圖」，不是另外一套資產。
2. **Icon 必須跟 runtime 同 chirality**：若 runtime 做 X 鏡像，icon 也要鏡像（相機改到 +X，或 render 時 scale X = -1），不能像現在 `make_icons.py` 直接 render Blender。
3. **Rarity 只改 accent**：手持、drop、hotbar、icon 都吃同一個 rarity profile；不要只在手持加 bands。
4. **Runtime 追加件要進 icon**：Absorber 玻璃罩、Gatling 切管、Puffer 引信、Conch 手握帶，至少要在 icon 的「代表狀態」出現，不能永遠是空殼。
5. **Game gun 保持原版**，但 rarity 要在同一個 UI 語言裡（ring／gem／name），不要硬套要塞武器的 bands。

---

## 6. 逐槍重設計 brief（給 3D LLM／Blender builder）

> 格式：**輪廓／主形；槍管／槍口；握把／槍托／可動；配色；目標尺寸（手持）；rarity／顯示**。  
> 目標尺寸是「設定感」，不是硬性碰撞值；若與第一人稱鏡頭衝突，優先調整持槍姿勢與前後位置，不要再次全域縮小。

### P0

- **Rocket Launcher**：粗長 olive 發射管，後方喇叭口＋黑膛，前方 **可看見的 loaded rocket**（warhead 不要永遠黏在管口）；前握把＋肩托，two-handed shouldered；warhead 離管／reload／後方 blast flap 可動；olive／olive_dark／red warhead／brass；0.85–1.0 m；Legendary 三管或三聯裝，icon 要看到長度與後方開口。
- **Gatling Gun**：squat receiver＋**10 管 hex cluster**（Common 10、Rare 20、Epic 30、Legendary 40 以管數或分層視覺表現）＋olive ammo box＋brass belt＋carry handle＋橙色 glow ring；muzzle plane 要齊平，cluster 以真實軸旋轉；雙手主握＋前支撐；gunmetal／dark／olive／orange；0.70–0.85 m；rarity 增加管數／環數，icon 要數得出管數。

### P1

- **Conch**：保留巨大螺旋貝＋粉紅開口＋黃銅吹嘴；在貝腰加 **rope／ leather 手握帶**與一個小扳機式閥門（讓它也能當「槍」用）；開口朝 +Z，吹嘴在後；手握帶／貝唇可微微動；cream／coral／brass；0.38–0.45 m；rarity 在唇緣加 band，icon 要看到握帶。
- **Tuna Boomerang**：保留鮪魚 V 形；在 V 的 elbow 加 **投擲握把／凹槽**，讓手有明確位置；沒有槍管，muzzle 就是投擲起點；可動：鰭／尾在投擲時張開；fish blue／white／yellow；0.55–0.60 m 翼展；rarity 增加 1–4 支（手上或腰間），icon 顯示支數。
- **Pufferfish Bomb**：保留圓河豚＋刺＋大眼＋pin；在底部加 **手握帶／掌心凹槽**，不要 pistol grip；pin／ring 可拔，刺可豎起；sand／cream／brown spine／red mouth；0.18–0.22 m；Legendary 第二個 fuse ring，icon 要看到 pin。
- **Net Launcher**：短粗黃銅網筒＋木 stock＋shoulder rest＋前握把；網球在口部但可看見「可更換」的卡扣；muzzle 是網筒口；可動：net 離膛、reload ring；wood／brass／rope／red float；0.55–0.65 m；Legendary 更大的網袋，icon 要看到網與浮球。
- **Water Cannon**：藍色壓力桶＋黃銅粗噴嘴＋水位窗＋泵把＋hose；two-handed；muzzle 在噴嘴中心，孔徑至少 4–5 cm；可動：pump handle、water window、噴嘴環；tank blue／brass／cyan／yellow；0.50–0.60 m；Legendary 壓力環，icon 要看到水箱。
- **Grapple Gun**：保留 iron body＋gold hook＋rope reel；把 hook 做成 **可離膛** 的獨立件（槍上留 hook seat，不要永遠一支鉤子）；reel 會轉、rope 會出；muzzle 在 hook seat 中心；iron／steel／gold／rope；0.45–0.50 m；Legendary 第二支 hook，icon 要看到 reel 與線。
- **Absorber**：保留粉色真空＋gold intake＋玻璃罩；加 **前握把／雙手把**；muzzle 是 intake 中心；可動：iris／玻璃罩內物品旋轉；pink／gold／black gullet；0.48–0.55 m；rarity 加 hoop／crest，icon 一定要有玻璃罩（可空或放代表物）。
- **Starfish Thrower**：保留 navy rail＋brass spring＋海星彈匣；改成 **前臂 brace／wrist 型**，不要 pistol grip；muzzle 在 spring head 中心；可動：彈匣推進、spring 彈出；navy／brass／orange star；0.32–0.38 m；rarity 3/3/4/5 顆，icon 要數得出。
- **Lobster Claw**：保留紅鉗＋白牙＋iron handle；在 handle 加 **rope spool／line guide**，讓「射出去再拉回」有視覺依據；muzzle 在鉗的 hinge 前方；可動：jaw 開合、spool 轉；lobster red／dark／white／iron；0.48–0.55 m；Legendary 三鉗或三線，icon 要看到線。
- **Whirlpool Cannon**：保留 navy／白螺旋／flared bell；在 bell 內加 **旋轉水環／crystal core**，讓漩渦方向可讀；muzzle 在 bell 中心；可動：rings 反向旋轉、core 發光；navy／white／crystal／brass；0.42–0.50 m；Legendary 爆炸環，icon 要看到 vortex 紋。

### P2

- **Laser Gun**：保留白＋teal ray gun；加 **散熱鰭／vent glow** 與鏡片後的 heat sink；muzzle = lens 中心；可動：vent 開合、lens 亮度隨 heat；white／teal／crystal；0.32–0.38 m；rarity 換 lens 色，icon 要看到透鏡。
- **Gas Gun**：保留紫 pump＋綠毒罐＋黃銅 nozzle；把 7 mats 收到 4（purple／brass／toxic／iron）；muzzle 在 nozzle 中心；可動：pump handle、gauge；0.42–0.50 m；rarity 換 glow，icon 要看到毒罐。
- **Bubble Gun**：保留粉紅＋橘環＋肥皂瓶；加一個可看見的 soap tank／tube；muzzle = 泡泡環中心；可動：環轉、泡泡脹大；pink／orange／soap／white；0.36–0.42 m；rarity 換泡泡色，icon 要看到環。
- **Jellyfish Gun**：保留珊瑚粉＋玻璃罩＋trumpet nozzle；罩內水母要有觸手；muzzle 在 trumpet 中心；可動：水母漂浮、罩內發光；coral／jelly pink／brass；0.42–0.48 m；rarity 換 jelly 色，icon 要看到水母。
- **Shark Whistle**：保留鯊魚頭＋牙＋吹嘴＋紅繩；加手指孔／吹奏握位；muzzle 不是槍口，是吹嘴氣流方向；可動：下顎開合、繩擺；grey／white／pearl／red；0.16–0.18 m；rarity 換繩色，icon 要看到吹嘴。

> 6 把遊戲槍：**不要重做**。只加一個一致的 rarity 提示（槍身小 gem／band，或 UI ring），並保持原版 hand rig／icon。

---

## 7. 實作與驗證計畫（未來工作，不在本任務執行）

### 7.1 先建 profile，不再用單一 `InHand`

- 在 `WeaponItems.cs` 建 `WeaponProfile` table（或新 `WeaponVisuals.cs`）：`Model`、`HeldLength`、`GripFromHand`、`HeldEuler`、`Pose`、`MuzzleLocal`、`IconView`、`Class`。
- `Place()` 改成 `Place(profile)`；移除 / 降級 `InHand` 全域常數。Gatling 的 1.5× 特例消失，改成 profile 的 `HeldLength`。
- `Loot.MakeDropView` 用 `HeldLength` 的比例（例如 drop = held×1.5），不要每把都 0.60/0.75。
- 每把槍的 projectile（`BoomerangView`、`ClawView`、`HookView`、`StarView`、`JellyView`、`PufferView`、`RocketView`）用 profile 的 projectile scale，讓手持與投射物比例一致。

### 7.2 Muzzle 與遠端 FX

- 在模型上加 `Muzzle` 的 **資料**（profile 的 local position）或 runtime child transform；`MyMuzzle` 直接用它，不再用 `bounds center/max.z`。
- 遠端 FX：`Weapons.PlayFx` 不再用 host 的 `Muzzle(o,d)`；host 在 `FxEvent.From` 帶射手 id／muzzle，或 client 用 `WeaponItems.ModelOf` + profile 自己算。至少要讓朋友看到的光束從他們的槍口開始。
- `Muzzle` 要包含真正的 bore center（x/y），不是 bbox center；Gatling 用 cluster 軸心，Rocket 用管口中心。

### 7.3 握把／姿勢

- 先做 4 個 pose preset：`PistolOneHand`、`TwoHand`、`Shouldered`、`Throw`／`Blow`。
- 對沒有 `grip()` 的 Conch／Boomerang／Puffer／Whistle，先加可見的握把／握帶（幾何）再加 pose；不要讓 pistol hand IK 硬套。
- 長槍（Rocket／Gatling／Net／Water）加前握把與 shoulder rest；若沒有 animation rig，至少把模型前移、調 pitch/roll，讓 stock 不進鏡頭。**不要用 0.22 m 全域縮小當解法。**

### 7.4 Icon／Hotbar／Drop 單一化

- 最乾淨：用 runtime mesh + `ItemIcons` 的方式為 17 把武器生成 icon（同 chirality、同材質、可帶 rarity accent），不再維護 `Icons/Gear*.png`。若要保留 Blender bake，至少：
  - X 鏡像（相機改 +X 或 object scale X=-1）；
  - 把 Absorber 玻璃罩、Gatling 管數、Puffer pin 等 runtime 件加進 bake；
  - 每個 rarity render 一張，或 icon 上疊 rarity overlay。
- Hotbar：把 `RarityBand`/`RarityCrest` 也納入 `Item.Mesh` 的替代展示，或接受 hotbar 只顯示主體但在 slot 外框顯示 rarity；不要讓「手持有 bands、hotbar 沒有」變成常態。
- Drop：套用 `RarityLook` 或至少用同一個 rarity accent；目前只有 ring + gem，槍身仍是 common 色。

### 7.5 OBJ／MTL 規範（更新 `CURRENT-DESIGN.md`）

- 手持武器：origin = grip center；+Z 前；+Y 上；1 unit = 1 m；`Muzzle` 由 profile 定義。
- 材質 2–4 個；triangles < 1,500（StarThrower 目前 1,570）；不要貼圖；flat shading；`Ke` 只用於能量／液體。
- 檔名不變；大型重做用新檔（例如 `RocketV2.obj`）並在 profile 切換，保留 rollback。
- `CURRENT-DESIGN.md:302` 的「底部中心」要標明「場景／塔限定」；武器用 grip origin。

### 7.6 驗證

- **Asset 檢查腳本**（Python / headless）：對 `GearCatalog.WeaponKinds` 逐一 assert 模型存在、MTL 完整、tri < 1,500、mats ≤ 4、bbox 合理、icon 存在、profile 有 muzzle；輸出表格 diff。
- **第一人稱截圖**：每把槍 `held`、`ADS`、`drop`、`hotbar`、`icon` 各一張；檢查槍托不進鏡頭、手在握把上、muzzle 與光束起點重合。
- **兩客戶端**：一人開槍、一人截圖；檢查朋友端 FX 從槍口而非胸口出來，且投射物大小與手持一致。
- **數值／卡片**：用 headless `Sim` 算出的 DPS 對照 `DpsNow` 與卡片文字；Gatling、Star、Rocket、Boomerang 優先。
- **既有 auto-test**：`AutoTest.WeaponsScenario` 已有 `weapon_*`、`held_*`、`weapon_view`、`weapon_ads` 的掛點（`AutoTest.cs:978-1080`）；把新的 profile／muzzle 檢查加進去，並把截圖輸出到 repo 的 `docs/screenshots/weapons/`（未來）。
- **不要**只靠 icon 判斷模型：icon 是 2D bake，會隱藏 chirality、runtime 件、尺度。

---

## 8. Top 10 修復排序

| # | 修復 | 為何先做 | 影響 | 工作量 |
|---:|---|---|---|---|
| 1 | 建 `WeaponProfile`（per-gun held length／grip／pose／muzzle） | 所有槍現在都被 0.22 m 壓扁 | 全部 17 把 | M |
| 2 | Gatling 10+ 管與 rarity 管數 | 玩法與模型直接矛盾 | 1 把，但非常顯眼 | M |
| 3 | Muzzle transform＋遠端 FX | 現在從胸口／bbox 中心發射 | 全部槍、多人 | S–M |
| 4 | 單一顯示來源（held／drop／hotbar／icon） | 四套尺度／材質／chirality | 全部槍、UI | M |
| 5 | Rocket 重做＋shoulder pose | 招牌開局武器卻像小手槍 | 1 把，高可見度 | M |
| 6 | 投射物尺度跟手持一致 | Boomerang／Claw／Hook／Jelly／Rocket 會瞬間變大 | 7 把 | S |
| 7 | Conch／Boomerang／Puffer／Whistle 的真握把／姿勢 | 四把根本沒有槍握把 | 4 把 | M |
| 8 | 材質 4 個上限＋Star tris 降到 1,500 | 規格與效能 | 15 把 | S |
| 9 | 卡片文字／DPS／range 修正 | Gatling 12/s、Whirl 傷害、Hook 物品、DPS 假象 | UI 全部 | S |
| 10 | Asset／截圖驗證自動化 | 防止下一次又 drift | 長期 | S–M |

---

## 9. TODO checklist

- [ ] 確認這份 audit 的 P0 排序與否決項（先不改 code）。
- [ ] 定義 `WeaponProfile` schema 與 §5.1 的 class／尺度表。
- [ ] 把 `CURRENT-DESIGN.md` 的武器 origin／materials／poly 規格改成與手持模型一致（grip origin、2–4 mats、<1,500 tris）。
- [ ] 建 `MuzzleLocal` 表；`MyMuzzle` 與遠端 FX 都改用它。
- [ ] 重做 Rocket（可離管 warhead／reload／shoulder／legendary 三管）。
- [ ] 重做 Gatling 管數（10 起，rarity 增加）與 tracer 上限。
- [ ] 四把無握把武器加握把／握帶＋pose。
- [ ] 長槍（Rocket／Gatling／Net／Water）加 two-hand／shoulder pose 與相機安全距離。
- [ ] 統一 held／drop／hotbar／icon 的尺度、材質、chirality、rarity。
- [ ] Absorber icon 加玻璃罩；Gatling icon 顯示正確管數。
- [ ] 修正 `L10n` 的 Gatling（12 波 × 管數）、Whirl（傷害）、Hook（物品／fling）、Net（8 傷害）。
- [ ] 修 `DpsNow`：Boomerang 不再 0；Star／Rocket 計多發／AoE；卡片加 range。
- [ ] 材質收斂到 4；StarThrower tris 降到 1,500 以下。
- [ ] 建 asset validation + 兩客戶端截圖流程。
- [ ] 更新 `TODO-3D-MODELS.md`：新增「手持武器」章節（目前只有幫手／王／塔）。
- [ ] 重跑 audit，確認 P0 清零後再談 P1/P2。

---

### 附：本文件沒有解決／不能從 repo 驗證的事

- 第一人稱與朋友端的實際畫面（repo 無 `held_*`／`weapon_view` 截圖；auto-test 輸出在遊戲 prefix 外）。
- 遊戲 pistol prefab 的 hand bone 尺寸、`item.rotation` 是否精確等於槍管軸；這決定 `Place` 的旋轉是否正確。
- 6 把遊戲槍的原始 mesh 尺寸／tri／材質（原版 asset 不在 repo）。
- `ItemIcons` 在目前 build 對 6 把遊戲槍是否都能成功截圖；code path 有 null fallback。
- 材質 5–7 個在實機的實際 draw call／效能影響（只有資產事實，沒有 profiling）。
- 玩家手部 IK 是否真的穿進 Conch／Puffer（推論，需實機截圖）。
