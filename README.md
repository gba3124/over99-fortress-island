# HTF Fortress：How to Fish 要塞島

打完最後魔王之後，海上會出現一座「要塞島」。開船過去，和朋友一起蓋防禦、守住燈塔，一波一波撐下去，看能走多遠。

BepInEx 5 模組，C#，在 Mac 的 Pixel Port 上開發與自動測試。完整設計見 [DESIGN.md](DESIGN.md)。

## 怎麼玩

1. **找到要塞島**：打完最後魔王後，雷達上會多一個橘色的點，它在你目前所在的島外海約 230 公尺。（測試期間可以在 Mod Settings 把 `Island / Unlock` 設成 `Always`）
2. **開始一局**：島上所有人站進燈塔前的**藍色光圈**，倒數 5 秒就開始
3. **建造時間**（開局 45 秒，之後每波之間 25 秒）：花金幣蓋牆和砲塔。房主按 **N** 可以提早開始下一波
4. **戰鬥**：敵人分三批從三個沙灘缺口登陸，直衝燈塔。牆擋住它們，砲塔負責輸出。**靠太近會被咬**，自爆河豚也會炸到人
5. **撿魚**：擊殺只給一點錢，大部分收入在魚屍上。走到死魚旁邊會自動收進漁網，滿了就拿到碼頭旁的**魚箱**賣掉換金幣
6. **倒下**：隊友可以把你扶起來（遊戲原本的機制）。全員倒下就輸，大家會在要塞碼頭重生，船也會停在旁邊
7. **結束**：燈塔被打爆或全員倒下。結算畫面會顯示撐過幾波、最高紀錄，以及每個人的擊殺、砲塔擊殺、捕魚收入、建造、倒下次數和 MVP（擊殺王、建築大師、漁夫王、不倒翁）。場地重置後站進光圈就能再挑戰

### 按鍵

| 按鍵 | 作用 |
|---|---|
| **G** | 進出建造模式（只能在要塞島、開局後使用） |
| **T** / **Shift+T** | 下一個／上一個建築 |
| **按住左鍵** | 連續放置 |
| **Y** | 反轉方向 |
| **U** | 升級正在看的建築；升到 Lv3 時 **U** 選分支 A，**Shift+U** 選分支 B |
| **J** | 修理正在看的建築 |
| **H** | 拆除（退一半金幣） |
| **U**（在魚箱旁） | 升級全隊的漁網 |
| **N** | 房主：建造時間提早開始下一波 |
| **F10** | 截圖（存到 `BepInEx/fortress-shots/`） |

遊戲本身已經佔用 B、Q、R、X、E、F、C、V、Z、1–9，所以模組改用 G、T、Y、U、J、H。建造模式中左右鍵不會揮動手上的道具。

### 建築

| 建築 | 花費 | 說明 | Lv3 分支 A / B |
|---|---|---|---|
| 牆 | 10 | 擋住登陸的魚，升級 木 → 石 → 鐵 | |
| 地板、斜坡 | 8 | 可以疊高、走上去 | |
| 機槍塔 | 50 | 射速快、單體 | 暴風（射速 ×2）／穿甲（對坦克、魔王 ×3） |
| 大砲 | 80 | 拋物線砲彈、範圍傷害 | 集束（3 顆）／重砲（傷害 ×2.5） |
| 魚叉塔 | 70 | 貫穿一整排、命中減速 | 多重（3 支）／冰凍（強力減速） |
| 迫擊砲 | 110 | 超遠射程、不能打近距離 | 燃燒（地面持續燒）／連發（3 發） |
| 電網塔 | 90 | 連鎖電擊，對水中敵人 ×2 | 擴散（連鎖更多）／超載（傷害 ×2） |
| 尖刺陷阱 | 25 | 踩到持續扣血、減速 | |

所有數值都在 [`HtfFortress/Catalog.cs`](HtfFortress/Catalog.cs) 和 [`HtfFortress/Enemies.cs`](HtfFortress/Enemies.cs)。

### 敵人

| 敵人 | 登場 | 行為 |
|---|---|---|
| 登陸魚 | 第 1 波 | 穿鞋的魚，衝向燈塔，擋路的牆和砲塔會被拆 |
| 衝鋒魚 | 第 3 波 | 小、快、血少 |
| 砲擊魚 | 第 5 波 | 浮在岸邊海面吐水彈打建築和人，登陸的魚全滅（或 75 秒）就潛回深海 |
| 自爆河豚 | 第 6 波 | 背著炸彈，貼到牆就爆炸 |
| 坦克蟹 | 第 8 波 | 背石殼，又慢又硬，拆牆傷害 ×2 |
| 精英 | 每 5 波 | 戴金冠，更大、更硬、獎勵 ×3 |
| 深海巨獸 | 每 10 波 | 巨型坦克，有專屬血條 |

海裡也會混一些原版的攻擊魚騷擾水邊的玩家。敵人數量會隨玩家人數增加（每多一人 +40%）。

## 安裝

1. 遊戲裝好 BepInEx 5.4.23.5
2. 把 `HtfFortress/` 資料夾（含 `HtfFortress.dll` 和 `Models/`）放進 `How to Fish/BepInEx/plugins/`
3. 建議一起裝 [ModMenu](https://thunderstore.io/c/how-to-fish/p/evansvl/ModMenu/)，可以在遊戲裡調設定
4. **一起玩的每個人都要裝同一版**

## 開發

```
htf-fortress/
├── README.md / DESIGN.md
├── HtfFortress/
│   ├── FortressPlugin.cs    進入點、設定、波次組成、刷怪、獎勵、結算
│   ├── FortressIsland.cs    要塞島：生成、碰撞、雷達點、解鎖、集合圈、結算畫面、碼頭重生
│   ├── Siege.cs             燈塔核心、金幣、敵人 AI（Harmony 接管 RunningFish）、上方 HUD
│   ├── Enemies.cs           敵人種類與數值、外觀同步（縮放、配件）、畫面邊緣敵人標記
│   ├── BuildSystem.cs       建造：格線、預覽、放置、升級分支、修理、拆除、建造列
│   ├── Turrets.cs           砲塔與陷阱的攻擊邏輯
│   ├── FortressNet.cs       漁網：撿屍體、魚箱賣出、漁網升級
│   ├── Catalog.cs           建築數值（平衡在這裡調）
│   ├── Fx.cs                特效、音效（沿用遊戲音效）、傷害數字、橫幅
│   ├── Models.cs            OBJ 載入、遊戲材質上色、升級外觀、受損變紅
│   ├── Hud.cs / L10n.cs     遊戲字型的 HUD、三語字串
│   ├── AutoTest.cs          自動測試（見下）
│   └── Models/              Blender 模型（.obj/.mtl）＋原始檔 fortress_models.blend
├── tools/
│   ├── autotest.sh          設定下一次啟動要跑的測試情境
│   └── launch.sh            直接用 Pixel Port 的 Wine 啟動遊戲
└── decompiled/              遊戲反編譯碼（.gitignore，只供查閱）
```

```bash
cd ~/Desktop/Project/htf-fortress/HtfFortress
```

```bash
dotnet build -c Release
```

編譯完會自動複製到遊戲資料夾，重開遊戲才會載入新版。

### 自動測試

```bash
cd ~/Desktop/Project/htf-fortress
```

```bash
tools/autotest.sh island && tools/launch.sh
```

情境：`probe`（環境資訊、音效清單）、`island`（完整流程）、`enemies`（敵人圖鑑＋各波）、`build`（所有砲塔與分支）、`balance`（AI 建造者真實打到失敗）、`coop`（不開無敵站在缺口被咬、撿魚、全滅、碼頭重生、結算表）。
測試會用一個獨立的 `FortressTest` 存檔，跑完自動關遊戲。報告在 `BepInEx/fortress-test-report.txt`，截圖在 `BepInEx/fortress-shots/`。

## 技術筆記

- **連線**：FishNet 廣播訊息（手動註冊序列化），房主決定一切，客戶端只畫畫面；中途加入的玩家會收到完整狀態
- **要塞島**：每台電腦依同樣的規則，在目前主島外海自己生成同一座島（不需要網路同步），地面是 MeshCollider（同地面 Layer）
- **敵人**：全部是遊戲的 `dripper`（RunningFish），Harmony 攔截 `UpdateMovement` 換成我們的 AI，血量用自訂的絕對值（遊戲原本 350 太高）
- **傷害**：`Creature.ServerChangeHp`（房主），死亡效果由遊戲處理
- **材質**：遊戲的 `Shader Graphs/DefaultShader` 從 `_Colors` 調色盤貼圖取色，所以每個材質用一張 2×2 純色貼圖
- **玩家位置**用 `Player.LocalPlayer.Transform`（`transform` 永遠是原點）
- **Wine/Pixel Port**：只能 D3D11；Steam 卡住時用 `tools/launch.sh` 直接啟動 exe；從 shell 跑 Wine 需要 `WINEMSYNC=1` 等環境變數
