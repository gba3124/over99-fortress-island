# HTF Fortress：How to Fish 要塞防禦模組

給《How to Fish》加上「要塞防禦」模式：蓋牆守核心，一波波打越來越強的魚，跟朋友比誰撐得久。

BepInEx 5 模組，C#，目前在 Mac 的 Pixel Port 上開發測試。

## 現在能玩什麼

| 功能 | 狀態 |
|---|---|
| 波次刷怪（每波更多、血更厚） | ✅ |
| 全員倒下或核心被打爆就結束，保存最高紀錄 | ✅ |
| Fortnite 式建造：牆、地板、斜坡，格線對齊、可疊高 | ✅ |
| 建築有血量，被打會變紅、打爆消失 | ✅（待實測） |
| 要塞核心（有血量） | ✅（待實測） |
| 登陸魚：直衝核心，被牆擋住就拆牆 | ✅（待實測） |
| 上方 HUD：波數、核心血量、剩餘敵人 | ✅（待實測） |
| 英文／繁中／簡中，跟著遊戲語言 | ✅ |
| 要塞幣、準備期、砲塔、升級、新怪物、要塞島 | 規劃中 |

## 操作

| 按鍵 | 作用 |
|---|---|
| **F9** | 房主開始／停止要塞模式（在腳下放核心） |
| **B** | 進出建造模式 |
| **Q** | 切換 牆 → 地板 → 斜坡 |
| **按住左鍵** | 連續放置 |
| **R** | 反轉方向 |
| **X** | 拆掉正在看的建築 |
| **Esc** | 離開建造模式 |

所有數值都能在遊戲的 **Mod Settings**（需要 ModMenu 模組）裡調整，例如每波數量、血量成長、核心血量、登陸魚比例、語言。

> 登陸魚需要陸地，開始前請站在島上，不要站在船上或碼頭上。

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
│   ├── FortressPlugin.cs        進入點：設定、波次、刷怪、結束判定、最高紀錄
│   ├── BuildSystem.cs           建造系統：預覽、格線、放置／拆除、建築血量與同步
│   ├── Siege.cs                 核心、登陸魚 AI（Harmony 接管 RunningFish 移動）、上方 HUD
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
- **輸入**：遊戲用新版 Input System，要用 `Keyboard.current`，不能用 `Input.GetKey`

## Pixel Port（Mac）注意事項

- 只支援 D3D11（`-force-d3d11`），遊戲沒附 Vulkan shader
- BepInEx 靠 `version.dll` 和 prefix 的 `user.reg` 裡的 DLL override 載入。要玩原版，把 `version.dll` 改名成 `version.dll.off`
- 不要按 Pixel Port 的「Re-check」或「Fix & Play」，可能會還原設定
- 首頁的 Play 有時候沒反應，請到遊戲頁面按「Play via Steam」

## 路線圖

1. ✅ 刷怪、波數、結束判定、最高紀錄
2. ✅ 自由建造（牆、地板、斜坡）
3. 🔧 核心、建築血量、登陸魚（實測中）
4. 要塞幣、準備期／戰鬥期、建造花費
5. 砲塔：大砲、機槍、魚叉、迫擊砲、電網，可升級、可分支
6. 特殊彈、更多怪物（砲擊魚、坦克蟹、自爆河豚、飛魚群、魔王波）
7. 要塞島（CustomIslandsLoader），打完最後魔王才開放
