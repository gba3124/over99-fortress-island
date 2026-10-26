# WEAPON-ART-REPORT：武器美術收尾

> 分支：`gba3124/weapon-art`（Orca worktree）
> 日期：2026-10-26
> 依據：`GUN-MODEL-AUDIT.md`、`GUN-UPGRADE-ROUND1.md`、`GUN-UPGRADE-ROUND2.md`，以及 Owen 的回饋（鮪魚回力鏢要留；round 2 的火箭筒比以前更不真實）。
> 範圍：只動武器模型／圖示／`WeaponItems`／掉落顯示／武器預覽圖。沒動塔、場景、FX、版號、release／Thunderstore；沒有 deploy／pack。
> **沒有開遊戲實機驗證**：只有 headless 測試與 Blender 預覽圖。

## 測試結果

- `dotnet build HtfFortress -c Release -p:Deploy=false`：0 Error(s)
- `zsh tests/run.sh`：**124070 checks passed, 0 failed**（改之前 124067；多的 3 個是這輪新加的檢查）

## 完成項目

### 1. Rocket 重做（`HtfFortress/Models/Rocket.obj`，`tools/make_weapon_models3.py`）

從頭重建成「可信的、風格化低多邊形肩射火箭筒」，不再是短胖玩具：

| 構件 | 做法 |
|---|---|
| 發射管 | 橄欖綠、78 cm 長、直徑 9.2 cm（長徑比 8.5；round 2 是 6.3），管軸在 x 0、z 0.125 |
| 後方排氣口 | 喇叭形 loft，從 R 開到 R+0.036，後端蓋黑色（膛口暗部）；前面一圈黑色警示帶 |
| 握把／扳機 | 原有 `grip()`／`trigger()`，加一塊扳機座把握把接到管底 |
| 肩托 | 鐵支架＋黑色墊；管上肩膀位置多一圈黑色隔熱套 |
| 前握把 | 管下 26 cm 前方，向前傾 |
| 翻起式照門／準星 | 前：鉸鏈座＋片狀準星＋白點；後：鉸鏈座＋表尺片（兩道白刻度）＋覘孔環 |
| 側邊擊發盒 | 放在 **持槍者看得到的那一側**（Blender +X），黑色擊發線沿管往後 |
| 彈頭 | PG-7 式：管內尾段 → 白色肩環 → 比管粗的紅色鼓身（R 0.06）→ 尖錐 → 鐵引信帽；凸出管口 16.6 cm |
| 細節 | 三圈深色箍、白色鋼印條 |

- 尺寸 0.16 × 0.95 × 0.31 m（x 寬 × y 長 × z 高）；**1238 tris、6 材質**（預算 1500／6）。
- `RocketShell`（飛行中的火箭）同步改成同一顆彈頭＋橄欖綠推進段＋四片尾翼＋火焰（320 tris），所以飛出去的跟槍口上那顆是同一個造型。
- `WeaponItems.Spec(Rocket)`：`Longest` 0.60 → **0.66 m**，`Muzzle` → **(0, 0.125, 0.50)**（管口前端面、彈頭後方）。檢查：槍口在 bbox 內、在槍管前端 18 cm 內、x≈0 都通過。

預覽：`preview/weapons_final/rocket_before_after.png`（上到下：classic 88d3cb0／round 2 e6722c5／12a6148 復原版／本輪 final／final RocketShell；欄位 side、3/4、front、top、fp）。

### 2. 鮪魚 Boomerang 保留（`tools/make_weapon_models.py`、`Boomerang.obj`）

- 生成腳本從 round 2 的木 V **換回鮪魚生成器**（與 f7b618c／88d3cb0 的 OBJ 同源），所以之後重跑腳本不會再把魚蓋掉。
- 唯一的改動：魚身的「半寬」（立起來拿時的側面厚度）乘 **1.3**（`FAT`），身體從 9 cm 厚變 10.7 cm；V 形平面的輪廓（長度、彎角、鰭、尾）完全不變。預覽 `preview/weapons_final/boomerang_gatling_before_after.png` 前兩列可比對。
- 886 tris、5 材質，沒變。
- 這段（含重匯出）是另一個重複開啟的 weapon-art session 做的，它發現重複後停了；我核對過內容與本任務一致，保留。

### 3. Gatling 10 管（`tools/make_weapon_models4.py`，`Gatling.obj`、`GatlingBarrels.obj`）

- 管束從 6 管改成 **10 管**（內 3、外 7），對上 `Weapons.GatlingBarrelBase = 10`（Common 10 管）；icon 和掉落物都數得出 10 管。
- 軸心、`RECEIVER_Y / HUB_Y / MUZZLE_Y / CLUSTER_R` 不變，所以 `WeaponItems.Gatling` 的切管規則不用改；「切出來的管 == GatlingBarrels.obj」的既有檢查照常通過。
- 為了留在 1500 tris 預算內，馬達／散熱鰭／發光環／護管環的段數降了、彈鏈 8 → 6 發、拿掉管束中間被遮住的軸桿：整把 1478 → **1370 tris**；管束 316 → 532。
- Rare/Epic/Legendary 的 20/30/40 管仍只用 bands／crest 表現（見「還沒做」）。

### 4. 顯示一致性

- **Icon 左右（chirality）**：`Models.Parse` 會把 x 取負，所以遊戲裡玩家看到的是 Blender +X 那一側；17 張武器 icon 和 `HelpLoot` 現在 **鏡像 render**（`make_icons.py` 的 `MIRROR`，物件 scale x = -1），鉤爪槍的繩輪、加特林的彈箱、撒網砲的氣瓶、高壓水砲的壓力錶都跟手持同側。
- **Absorber icon 有玻璃罩了**：`make_icons.py` 依 `Absorber.cs` 的 `DomeAt / DomeR` 加一顆半透明球（`IconAbsorberGlass`，只在 render 時建，不存進 .blend）。
- **掉落物有稀有度外觀**：`Loot.MakeDropView` 對 Rare 以上的武器改用自己的材質並套 `WeaponItems.RarityLook`（bands／發光染色／傳奇鍍金＋crest），跟手持一致；Common 和非武器仍共用材質。`RarityLook` 從 private 改 internal。
- 重 render 的圖示：`GearRocket / Gatling / Boomerang / Absorber / Hook / NetLauncher / WaterCannon / Laser / PoisonGas / BubbleGun / JellyGun / Whirlpool / Conch / SharkWhistle / PufferBomb / StarThrower / LobsterClaw`、`HelpLoot`。前後對照：`preview/weapons_final/icons_before_after.png`（每組左舊右新）。
- Boomerang icon 取景換回鮪魚版（立起 70°、從高處看），再鏡像。

### 5. 預算與規格（全部 17 把）

全部 ≤1500 tris、≤6 材質（測試強制）。本輪數字見 `preview/weapons_final/weapons_final_1.txt`、`weapons_final_2.txt`；最高是 Conch 1420、WhirlCannon 1388、Gatling 1370。

### 6. 新增測試（`tests/Headless/Tests.cs` → `GunSpecChecks`）

- `GatlingBarrels.obj` 槍口面上的暗色膛口數 == `Weapons.GatlingBarrelBase`（10）。
- `Loot.cs` 有 `WeaponItems.RarityLook(model, r)`（掉落物有稀有度外觀）。
- `tools/make_icons.py` 有 `name in MIRROR`（icon 跟遊戲同 chirality）。
- 既有的 Rocket 檢查（槍口位置、手持 ≥ 2.5 × 0.22、掉落比雷射大、材質／tris 預算）用新模型與新 spec 全部通過。

## 改了哪些檔案

| 檔案 | 改動 |
|---|---|
| `tools/make_weapon_models3.py` | Rocket 整段重寫；`warhead_rings()` 共用彈頭輪廓；RocketShell 重做 |
| `tools/make_weapon_models.py` | Boomerang 換回鮪魚生成器＋`FAT = 1.3` |
| `tools/make_weapon_models4.py` | 10 管管束、段數／彈鏈瘦身 |
| `tools/make_icons.py` | `WEAPON_MODELS`／`MIRROR` 鏡像 render、Boomerang 取景、`IconAbsorberGlass` |
| `HtfFortress/WeaponItems.cs` | `Spec(Rocket)` 長度與槍口；`RarityLook` internal |
| `HtfFortress/Loot.cs` | 掉落物套 `RarityLook`（Rare+ 武器不共用材質） |
| `HtfFortress/Models/Rocket.obj`、`RocketShell.obj`、`Boomerang.obj`、`Gatling.obj`、`GatlingBarrels.obj`、`fortress_models.blend` | 重匯出 |
| `HtfFortress/Icons/Gear*.png`（17 張）、`HelpLoot.png` | 重 render |
| `tests/Headless/Tests.cs` | 三個新檢查 |
| `preview/weapons_final/*` | 預覽圖與尺寸／tris／材質表 |

## 重跑指令

```
B=/Applications/Blender.app/Contents/MacOS/Blender
BL=HtfFortress/Models/fortress_models.blend
ONLY=Rocket,RocketShell $B -b $BL --python tools/make_weapon_models3.py
ONLY=Boomerang $B -b $BL --python tools/make_weapon_models.py
ONLY=Gatling,GatlingBarrels $B -b $BL --python tools/make_weapon_models4.py
$B -b $BL --python tools/make_icons.py -- GearRocket GearGatling GearBoomerang \
  GearAbsorber GearHook GearNetLauncher GearWaterCannon GearLaser GearPoisonGas \
  GearBubbleGun GearJellyGun GearWhirlpool GearConch GearSharkWhistle \
  GearPufferBomb GearStarThrower GearLobsterClaw HelpLoot
$B -b --factory-startup --python tools/gun_preview.py -- \
  preview/weapons_final/weapons_final_1.png final=HtfFortress/Models -- \
  Rocket Gatling LaserGun GasGun BubbleGun HookGun JellyGun StarThrower LobsterClaw
zsh tests/run.sh
```

## 還沒做／請 Owen 實機看

1. **實機驗證**（這輪最需要）：AutoTest `weapons` 的 `held_*` 截圖，特別看 Rocket：模型 0.95 m 縮到 0.66 m 後，後端會超過 `BackMax`，整把會往前推約 0.13 m；肩托、隔熱套在第一人稱是否合理，彈頭有沒有擋到準心。不對的話調 `Spec(Rocket)` 的 `Longest` / `FromHand`。
2. **Gatling 20/30/40 管**：模型固定 10 管；更高稀有度仍只靠 bands／crest 與 tracer（上限 12 條）。要做「管數隨稀有度」得在 runtime 換管束模型或疊第二層管。
3. **Hotbar 沒有 bands／crest**：那是 child 物件，`Item.Mesh` 只有主 mesh；傳奇鍍金有進 hotbar，bands 沒有。要做得把 bands 合併進 mesh 並多一個 submesh／材質。
4. **非圓柱體的 bands**（Boomerang、Puffer、Conch、Whistle）仍是用 bbox 切的圓盤，手持和掉落都一樣；要漂亮得改成每把自己定義的位置。
5. **`Icons/_sheet.png`** 沒重產（用 `ONLY` 跑不會更新；整支重跑會重 render 全部圖示，超出本任務範圍）。
6. Rocket 的 Legendary 三發仍是一根管；卡片文字（Gatling「12 發/秒」、DPS、range）屬 UI／L10n，沒動。
7. 投射物尺度（Boomerang、Claw、Hook、Jelly 飛出去變大）維持 round 2 狀態；RocketShell 這輪改了造型但長度（0.62 m）沒變。
