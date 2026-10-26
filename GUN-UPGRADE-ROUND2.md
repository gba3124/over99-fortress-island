# GUN-UPGRADE-ROUND2：槍械升級第二輪（模型與圖示）

> 依據：`GUN-MODEL-AUDIT.md`、`GUN-UPGRADE-ROUND1.md`
> 日期：2026-10-26
> 範圍：重做 OBJ 模型、重 render `Gear*.png`，並依新槍口修正 `WeaponItems.Spec` 的 3 個 `Muzzle`。沒有改版號、ID、enum；沒有 deploy／pack／commit；沒動 Thunderstore／release 檔。
> **沒有開遊戲實機驗證**，只跑了 headless 測試和 Blender 預覽圖。

## 測試結果

- 建置：`dotnet build HtfFortress -c Release -p:Deploy=false`：**0 Error(s)**
- `zsh tests/run.sh`：**124067 checks passed, 0 failed**
  - 改之前是 124050；多出來的 17 個是新加的預算檢查（每把要塞武器 ≤1500 tris、≤6 材質）。
  - Gatling 的「切出來的管 == `GatlingBarrels.obj`、沒有半截三角形」檢查照樣通過。

## 預覽圖（`preview/weapons/`）

| 檔案 | 內容 |
|---|---|
| `p0_before_after.png` | P0 七把：上面 7 列是改前、下面 7 列是改後；欄位依序是 side／3/4／front／top／fp（從持槍者肩後看） |
| `p1_before_after.png` | LobsterClaw、Absorber、WhirlCannon、Conch、StarThrower，改前與改後 |
| `icons_before_after.png` | 11 張 `Gear*.png`，上排改前、下排改後 |
| `*_before_after.txt` | 每列的尺寸、tris、材質數 |
| `before_obj/`、`before_icons/` | 改前的 OBJ／MTL／PNG（對照用；git 裡也有） |

## 改了哪些資產

尺寸是 Blender 軸（x 寬 × y 長 × z 高，公尺）。握把中心仍在原點，槍口方向仍是 -Y（匯出後是 +Z）。

| 武器（OBJ） | 改了什麼 | 尺寸 前 → 後 | tris 前 → 後 | 材質 前 → 後 | 手持最長 | Spec 槍口 |
|---|---|---|---|---|---|---|
| Rocket（`Rocket`） | 長發射管（R 0.064）、後方喇叭狀排氣口（黑色喉部）、前握把、肩墊、翻起式照門＋準星、側邊擊發盒、紅色彈頭插在砲口 | 0.15×0.62×0.31 → 0.20×0.81×0.33 | 508 → 1018 | 7 → 6 | 0.60 | **(0, 0.13, 0.41)**（原 0.115, 0.38） |
| Gatling（`Gatling`＋`GatlingBarrels`） | 更大的機匣、前擋板、帶散熱鰭片的馬達＋發光環、護管環（R 0.07，在旋轉區外）、前握把、提把＋準星、大彈藥箱（把手、銘牌）、供彈槽和 8 發彈鏈。6 管、軸心、`AXIS_Z/RECEIVER_Y/HUB_Y/MUZZLE_Y/CLUSTER_R` 不變；管口暗色改用 GatlingDark | 0.19×0.63×0.32 → 0.25×0.67×0.34 | 950 → 1478（管 440 → 316） | 7 → 6（管 4 → 3） | 0.55 | 不變 (0, 0.11, 0.443) |
| Hook（`HookGun`） | 木槍托、喇叭狀鉤座（z 0.29 的槍口）、三爪金色抓鉤坐在鉤座上、大繩輪＋搖把、繩子從輪拉到抓鉤 | 0.13×0.57×0.30 → 0.19×0.61×0.25 | 1100 → 1252 | 7 → 6 | 0.32 | 不變 (0, 0.09, 0.29) |
| WaterCannon（`WaterCannon`） | 寬扁的扇形高壓噴嘴（前端 12 cm 寬、發光縫）、更大的圓頂水箱、水箱到噴嘴的軟管、壓力錶、加壓把手、泵式前握把 | 0.12×0.48×0.34 → 0.13×0.54×0.36 | 522 → 1050 | 7 → 6 | 0.40 | 不變 (0, 0.09, 0.398) |
| NetLauncher（`NetLauncher`） | 寬網筒（R 0.092）、8 顆浮球、更大的網團、側邊壓縮空氣瓶（白瓶紅帶、閥、接到後膛的管）在持槍者看得到的那側、槍托底板 | 0.19×0.52×0.26 → 0.22×0.57×0.29 | 666 → 1276 | 7 → 6 | 0.42 | 不變 (0, 0.10, 0.313) |
| Boomerang（`Boomerang`） | 從「彎成 V 的鮪魚」改成**木製 V 形回力鏢**：翼面上有鮪魚藍的漆線、黃銅包尖、肘部 3 圈繩綁（12 cm 厚，側面看得出來）。沒有任何槍的零件 | 0.61×0.32×0.09 → 0.51×0.23×0.12 | 886 → 500 | 5 → 5 | 0.40 | 0（從手放出） |
| SharkWhistle（`SharkWhistle`） | 保留鯊魚頭。改成扁平哨嘴（黃銅唇環、尾端黑縫）、上方發聲窗、吊耳移到哨嘴下、紅色掛繩（含珠子）往下垂；沒有槍柄。pearl 併進 white | 0.07×0.16×0.16 → 0.07×0.16×0.16 | 826 → 852 | 7 → 6 | 0.162 | 不變 (0, 0.008, 0.078) |
| LobsterClaw（`LobsterClaw`） | 分節甲殼護腕、胖掌節、下方固定指（尖端上勾）＋上方張開的活動指（深紅，尖端下勾），中間有開口；內緣是圓瘤，不再是錐形牙；鉸鏈關節、殼上突點、加了扳機 | 0.15×0.44×0.26 → 0.12×0.46×0.26 | 300 → 630 | 4 → 4 | 0.32 | **(0, 0.11, 0.36)**（兩指尖之間；原 0.087） |
| Absorber（`Absorber`） | 形狀不變；粉紅／金改成石板灰機身、鋼環＋鋼箍，吸口內是發光紫色虛空，中間有一顆往外鼓的虛空核心（發光材質，稀有度會換它的顏色）。玻璃罩一樣由遊戲加 | 0.22×0.44×0.36 → 0.22×0.46×0.36 | 966 → 1046 | 6 → 5 | 0.36 | 不變 |
| Whirlpool（`WhirlCannon`） | 從螺紋喇叭改成**機械渦輪**：往前收窄的深藍機身＋黃銅環、後方散熱環＋發光核心、前方白色導管裡有 8 片扭轉葉片、黃銅整流錐、後面一圈發光「眼」。Conch 維持有機貝殼，兩者輪廓不同 | 0.26×0.53×0.31 → 0.18×0.49×0.27 | 1290 → 1388 | 6 → 5 | 0.40 | **(0, 0.10, 0.315)**（導管前緣；原 0.38） |
| StarThrower（`StarThrower`） | 彈匣裡的海星每支手臂只留 1 個點（飛出去的 `Starfish` 不變） | 不變 | **1570 → 1170** | 5 | 0.26 | 不變 |
| GasGun／BubbleGun／PufferBomb | 只合併 1 個材質（Gas：steel→iron；Bubble：pearl→white；Puffer：眼白→puffer_belly），幾何完全沒變 | 不變 | 不變 | 7 → 6 | — | 不變 |

**材質**：17 把要塞武器現在全部 ≤6 個材質、≤1500 tris（Gatling 1478 最高）。發光都是材質的 `Ke`，沒有貼圖。

**圖示**：重 render 了 `GearRocket／Gatling／Hook／WaterCannon／NetLauncher／Boomerang／SharkWhistle／LobsterClaw／Absorber／Whirlpool／StarThrower／PoisonGas／BubbleGun／PufferBomb` 和 `HelpLoot`（畫面裡有火箭筒）。Boomerang 的圖示改成從上方斜看（原本為了鮪魚轉 70°，換成木 V 後會變成一個「L」）。

## 管線變更

- `tools/bkit.py`：加了 `ONLY=Name,Name` 環境變數，只重建／匯出列出的物件。**原因**：舊腳本裡還留著後來被別的腳本取代的模型（例如 `make_weapon_models3.py` 的 `MenderHead` 會蓋掉 turrets v3 版本）。第一次整支重跑時就踩到，已經從 git 還原。
- `tools/gun_preview.py`（新）：把 OBJ 匯回 Blender，用 icon 的著色從 5 個角度 render，排成一張對照表，並輸出尺寸／tris／材質。
- `tools/make_icons.py`：Boomerang 的取景改了（見上）。
- `WeaponItems.Spec`：Rocket、LobsterClaw、Whirlpool 的 `Muzzle` 改成新模型量到的值，並更新註解。`Longest`／`FromHand`／`Euler` 沒動。
- `tests/Headless/Tests.cs`：`GunSpecChecks` 加上每把武器 ≤1500 tris、≤6 材質的檢查。

## Blender 指令

```
B=/Applications/Blender.app/Contents/MacOS/Blender
BL=HtfFortress/Models/fortress_models.blend
ONLY=HookGun,Boomerang,SharkWhistle,WhirlCannon,GasGun,BubbleGun \
  $B -b $BL --python tools/make_weapon_models.py
ONLY=NetLauncher,WaterCannon,LobsterClaw,StarThrower,PufferBomb \
  $B -b $BL --python tools/make_weapon_models2.py
ONLY=Rocket,Absorber $B -b $BL --python tools/make_weapon_models3.py
ONLY=Gatling,GatlingBarrels $B -b $BL --python tools/make_weapon_models4.py
$B -b $BL --python tools/make_icons.py -- GearRocket GearGatling GearHook \
  GearWaterCannon GearNetLauncher GearBoomerang GearSharkWhistle \
  GearLobsterClaw GearAbsorber GearWhirlpool GearStarThrower \
  GearPoisonGas GearBubbleGun GearPufferBomb HelpLoot
$B -b --factory-startup --python tools/gun_preview.py -- \
  preview/weapons/p0_before_after.png before=preview/weapons/before_obj \
  after=HtfFortress/Models -- Rocket Gatling HookGun WaterCannon \
  NetLauncher Boomerang SharkWhistle
zsh tests/run.sh
```

## 請 Owen 確認

1. **Boomerang 不再是鮪魚**：照這輪的規格改成木製 V，只用藍漆線保留「鮪魚」的感覺。名稱還是「鮪魚回力鏢」；如果要保留魚的造型，可以退回 `preview/weapons/before_obj/Boomerang.*`，再另外加厚。
2. **Absorber 換色**：從粉紅改成暗色機身＋紫色虛空。稀有度上色只會作用在發光的虛空材質上。
3. **Whirlpool** 從「螺紋喇叭」改成「渦輪」。

## 還沒做（第三輪以後）

- **實機驗證**：held／ADS／drop／hotbar 截圖（AutoTest `weapons` 的 `held_*`）。特別要看三件事：Rocket 變長後往前推（`SlideForward` 約 0.11 m）手的位置對不對；Boomerang 立起來（`StandUp`）後繩綁的位置；SharkWhistle 的掛繩會不會擋到鏡頭。
- **投射物**：LobsterClaw 與 Boomerang 的模型也是飛出去的投射物，會自動換成新造型。`ClawView` 的繩子接點（`claw.forward * 0.25`）還沒對新的掌節實測。
- **`Icons/_sheet.png`** 沒有重產：用 `ONLY` 參數跑不會更新總表；要更新就跑一次不帶參數的 `make_icons.py`（會重 render 全部圖示）。
- Conch 沒動（已經是有機貝殼）；四把非手槍的持法姿勢、雙手／肩托姿勢、Gatling 6 管對 10–40 管的文字，維持第一輪的狀態。
- Icon 的左右（chirality）還是 Blender 原始方向，跟遊戲裡的 X 鏡像相反（第一輪就列出的問題）。NetLauncher 的氣瓶、Hook 的繩輪在 icon 上看起來在另一側。
