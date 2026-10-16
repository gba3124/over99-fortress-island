# HTF Fortress：How to Fish 要塞防禦模組

給《How to Fish》加上「要塞防禦」模式：蓋牆守核心，一波波打越來越強的魚，跟朋友比誰撐得久。

BepInEx 5 模組，C#，目前在 Mac 的 Pixel Port 上開發測試。

## 現在能玩什麼（0.3.0）

| 功能 | 說明 |
|---|---|
| 要塞核心 | 房主按 F9 時放在腳下，500 血，被打爆就輸 |
| 建造時間／戰鬥 | 開局 30 秒、每波之間 20 秒可以建造，房主按 N 提早開始 |
| 波次 | 每波怪更多、血更厚；一半是會上岸衝核心、撞牆拆牆的登陸魚，其餘在海裡 |
| 金幣（全隊共用） | 開局 100；殺怪 +3＋波數；清一波 +20＋5×波數 |
| Fortnite 式建造 | 牆、地板、斜坡、大砲、機槍塔，格線對齊、可疊高、按住連續蓋 |
| 升級 | 看著建築按 U：牆 木→石→鐵；砲塔 Lv3 傷害、射速、射程都提升 |
| 拆除 | 看著建築按 X，退一半花費 |
| 砲塔 | 自動瞄準最近的怪；大砲有範圍傷害，機槍射速快；所有人都看得到彈道 |
| 結束 | 全員倒下或核心被打爆；保存最高紀錄 |
| 模型 | 核心、牆、地板、斜坡、砲塔都是 Blender 做的低多邊形模型 |
| 多語系 | 英文／繁中／簡中，跟著遊戲語言 |
| 截圖 | 開始和每波自動截圖，F10 手動截，存在 `BepInEx/fortress-shots/` |

### 建築數值（在 `HtfFortress/Catalog.cs` 調）

| 建築 | 花費 | 耐久 Lv1/2/3 | 升級費用 Lv1→2 / 2→3 | 砲塔數值 Lv1 → Lv3 |
|---|---|---|---|---|
| 牆 | 10 | 150 / 300 / 500 | 15 / 30 | |
| 地板、斜坡 | 8 | 100 / 200 / 350 | 10 / 20 | |
| 大砲 | 80 | 200 / 300 / 400 | 60 / 120 | 傷害 25→60、每 2→1.4 秒、射程 18→24 公尺、爆炸半徑 3 公尺 |
| 機槍塔 | 50 | 200 / 300 / 400 | 40 / 80 | 傷害 4→9、每 0.25→0.15 秒、射程 14→18 公尺 |

登陸魚會停下來打擋路的牆和砲塔，地板和斜坡可以直接走上去。

## 操作

| 按鍵 | 作用 |
|---|---|
| **F9** | 房主開始／停止要塞模式 |
| **N** | 房主在建造時間立即開始下一波 |
| **B** | 進出建造模式 |
| **Q** | 切換 牆 → 地板 → 斜坡 → 大砲 → 機槍塔 |
| **按住左鍵** | 連續放置 |
| **R** | 反轉方向 |
| **U** | 升級正在看的建築 |
| **X** | 拆掉正在看的建築（退一半） |
| **F10** | 截圖 |
| **Esc** | 離開建造模式 |

所有數值都能在遊戲的 **Mod Settings**（需要 ModMenu）裡調：每波數量、血量成長、核心血量、登陸魚比例、金幣、砲塔傷害倍率、語言。

> 登陸魚需要陸地，開始前請站在島上，不要站在船上或碼頭上。

## 測試清單（一次測完）

1. 站在島上按 **F9**：腳下出現灰色石塔＋藍色水晶的核心，上方出現波數、核心血量、金幣、建造倒數
2. 按 **B**：下方出現建造提示；**Q** 換到機槍塔，在核心旁邊蓋一座，金幣減少
3. 在核心外圍蓋一圈牆，看著牆按 **U** 升成石牆（顏色變灰）
4. 房主按 **N** 開始：登陸魚從遠處衝向核心，撞到牆會停下來打（牆慢慢變紅）
5. 砲塔自動開火：機槍有黃色彈道，大砲有黑色砲彈和橘色爆炸
6. 按 **F10** 截幾張圖，結束後告訴我，我會看 `BepInEx/fortress-shots/` 的截圖和 log

## 安裝（玩家）

1. 遊戲先裝好 BepInEx 5.4.23.5
2. 把 `HtfFortress.dll` 放進 `How to Fish/BepInEx/plugins/HtfFortress/`
3. 建議一起裝 [ModMenu](https://thunderstore.io/c/how-to-fish/p/evansvl/ModMenu/)，才能在遊戲裡調設定
4. **一起玩的每個人都要裝同一版**（建築和 HUD 要每台電腦自己畫）

## 開發

### 資料夾

```
htf-fortress/
├── README.md
├── HtfFortress/                 模組原始碼
│   ├── HtfFortress.csproj       專案設定（參考遊戲的 DLL，編譯完自動複製進遊戲）
│   ├── FortressPlugin.cs        進入點：設定、波次、建造時間、刷怪、金幣獎勵、結束判定、截圖
│   ├── Catalog.cs               所有建築的花費、耐久、升級、砲塔數值（平衡都在這裡調）
│   ├── BuildSystem.cs           建造系統：預覽、格線、放置／升級／拆除、花費、建築血量與同步
│   ├── Siege.cs                 核心、金幣、登陸魚 AI（Harmony 接管 RunningFish 移動）、上方 HUD
│   ├── Turrets.cs               砲塔：房主瞄準並造成傷害，所有人畫彈道和轉砲塔
│   ├── Models.cs                載入 OBJ 模型、用遊戲自己的材質上色、升級外觀、受損變紅
│   ├── Models/                  Blender 模型（.obj/.mtl）＋原始檔 fortress_models.blend
│   ├── Hud.cs                   用遊戲字型畫螢幕文字（中文才不會變方框）
│   └── L10n.cs                  多語系字串表
└── decompiled/                  遊戲反編譯碼，只供查閱（已加入 .gitignore，不可公開）
```

### 編譯

```bash
cd ~/Desktop/Project/htf-fortress/HtfFortress
```

```bash
dotnet build -c Release
```

編譯完會自動複製到 Pixel Port 的遊戲資料夾。**遊戲要重開才會載入新版**。

需要 .NET SDK（Mac 原生版即可）。遊戲路徑寫在 `HtfFortress.csproj` 的 `GameDir`，換電腦或換安裝位置要改這裡。

### 看 log

- 模組 log：`How to Fish/BepInEx/LogOutput.log`
- 遊戲 log：`drive_c/users/<你>/AppData/LocalLow/Dazed Games/How to Fish/Player.log`

### 重新反編譯遊戲（遊戲更新後）

```bash
dotnet tool install -g ilspycmd
```

再對 `How to Fish_Data/Managed/Assembly-CSharp.dll` 跑 `ilspycmd -p -o decompiled/Assembly-CSharp`。

## 技術筆記

- **連線**：遊戲用 FishNet，房主說了算。模組用 FishNet 的「廣播訊息」同步，不另做連線物件：
  - 玩家送請求給房主，房主檢查後廣播給所有人，每台電腦各自生成建築
  - 中途加入的玩家，房主會補傳目前狀態
  - 模組編譯時沒有 FishNet 的程式碼產生器，所以訊息的序列化是手動註冊的（`GenericWriter/GenericReader.SetWrite/SetRead`）
- **刷怪**：照遊戲開發者指令的寫法：`GameInfo.GetSpawnable(名字)` → `Instantiate` → `Server.Instance.Spawn`
- **怪物血量**：要讀 `Creature._hp`（同步變數），`Creature.Hp` 要等生成後才會被填入
- **登陸魚**：遊戲的 `RunningFish`（`dripper`），用 Harmony 攔截 `UpdateMovement`，改成朝核心直衝、遇到牆就打
- **建築**放在跟地面同一個 Layer，玩家和怪都能站上去、走上斜坡
- **砲塔傷害**用遊戲的 `Creature.ServerChangeHp`（只能在房主呼叫），死亡效果由遊戲自己處理
- **材質**：遊戲是自訂 Shader Graph，Unity 預設材質不在遊戲裡（會變紫色），所以複製遊戲的 `Shader Graphs/DefaultShader` 材質再換顏色
- **玩家位置**要用 `Player.LocalPlayer.Transform`，`Player.LocalPlayer.transform` 永遠是原點
- **模型**：Blender 匯出 OBJ（Y 朝上、-Z 朝前），載入時翻轉 X 軸；砲塔的砲管朝 Blender 的 -Y
- **輸入**：遊戲用新版 Input System，要用 `Keyboard.current`，不能用 `Input.GetKey`

## Pixel Port（Mac）注意事項

- 只支援 D3D11（`-force-d3d11`），遊戲沒附 Vulkan shader
- BepInEx 靠 `version.dll` 和 prefix 的 `user.reg` 裡的 DLL override 載入。要玩原版，把 `version.dll` 改名成 `version.dll.off`
- 不要按 Pixel Port 的「Re-check」或「Fix & Play」，可能會還原設定
- 首頁的 Play 有時候沒反應，請到遊戲頁面按「Play via Steam」

## 路線圖

1. ✅ 刷怪、波數、結束判定、最高紀錄
2. ✅ 自由建造（牆、地板、斜坡）
3. ✅ 核心、建築血量、登陸魚
4. ✅ 金幣、建造時間、建造花費、升級、拆除退款
5. ✅ 砲塔：大砲、機槍
6. 更多砲塔（魚叉、迫擊砲、電網）、升級分支、特殊彈
7. 更多怪物（砲擊魚、坦克蟹、自爆河豚、飛魚群）、精英怪、魔王波
8. 要塞島（CustomIslandsLoader），打完最後魔王才開放
