# 砲塔升級 Round 1：九座經典砲頭重做（＋火箭筒修正）

日期：2026-10-26。TODO-3D-MODELS §10（P0.5）。沒有升版號、沒有部署、沒有打包、沒有 commit。

## 方向
- Owen 覺得 V3 的胖卡通砲頭跟原本截圖差太多。這次回到 `img/build.png`、`preview/turrets_refined.png` 的經典風格：石／鐵／黃銅／木／繩／銅／玻璃／水晶。V3 的飽和青、紫、藍綠不再拿來當砲頭主體。
- Runtime 沒改：`Catalog.ClassicHeads = true`，照舊掛 `<T>Head.obj` + `TurretBase` + 分支裝飾／額外槍管。Gear／PieceType ID 和掛載程式都沒動。
- 慣例沿用：原點在砲塔樞軸、底部貼 z=0、正面朝 Blender −Y（遊戲 +Z）、單位公尺，尺寸和 V3 砲頭差不多（見下表）。
- 每個細節都對應它的機制，例如機槍有彈鏈進料、拋殼口、後座彈簧，大砲有液壓後座筒、配重、砲彈架，不是隨手貼的零件。

## 資產（`HtfFortress/Models/<T>Head.obj/.mtl`）

| 砲塔 | 寬×高×長 (m) | 三角面 | 材質 | 重點 |
|---|---|---|---|---|
| Gun | 1.01×0.66×2.01 | 2168 | 4：Iron Steel Brass Black | 低寬軛座、雙長槍管＋開孔冷卻套、彈藥箱→彎曲彈鏈→進彈托、右側拋殼口＋擋板＋掉落彈殼、後座導桿＋彈簧、兩側散熱片、雙握把＋拇指扳機、環形照門 |
| Cannon | 1.12×0.91×1.92 | 2106 | 5：Iron Wood Brass Black Copper | 圓形砲座、木質側板＋鐵箍、兩個鐵胎木輪＋輪軸、巨大單砲口（膛口加粗）、側孔制退器、砲閂＋手輪、後方三層配重、銅液壓後座筒＋管線、立式砲彈架 |
| Harpoon | 0.92×0.87×2.19 | 1608 | 5：Wood Iron Steel Rope Crystal | A 字木架、長矛滑軌（鐵槽＋木導軌＋夾具）、粗魚叉桿＋尾翼、大三叉頭＋倒鉤、絞盤（繩鼓＋齒輪＋曲柄＋棘爪）、繩索接到叉尾；B 路線冰：滑軌下冰霜罐＋叉座冰晶 |
| Mortar | 1.00×1.17×1.02 | 1420 | 5：Iron Brass Wood Black Copper | 粗短高仰角砲管＋球形砲尾、雙腳架＋撐桿、砲耳座、仰角扇形齒輪＋小齒輪＋手輪、方木彈藥箱（蓋半開、內有砲彈）、銅煙囪、散落彈殼 |
| Tesla | 0.84×1.72×0.84 | 2344 | 5：Iron Copper Brass Porcelain Glass | 瓷礙子疊、銅螺旋線圈、環形頂電極＋放電球、三根放電叉、黃銅電弧籠、後方變壓器（兩個繞組＋瓷套管＋引線） |
| Flame | 0.92×0.96×1.34 | 1884 | 5：Iron FuelRed Brass Black Ember | 兩支直立燃料桶（背包式剪影）＋綁帶＋扣條、頂部閥門手輪、黃銅供油管→歧管、兩個壓力錶、粗短噴嘴、防護籠、點火器＋引火 |
| Sniper | 0.78×0.98×2.88 | 1228 | 5：Iron Steel Wood Brass Glass | 高三腳架、木槍托＋托底板、拉柄、超長槍管＋箍、側孔制退器、大瞄準鏡（前後有玻璃、調整鈕）、液壓穩定筒、收折雙腳架 |
| Bubble | 1.03×1.17×1.03 | 1948 | 5：Iron Brass Copper Glass BubbleGlow | 圓形發生器＋肋條、銅壓力環、四支泡泡噴嘴（噴口有泡泡）、黃銅碟（build.png）、玻璃圓頂＋六根薄膜支架、頂部能量核心 |
| Mender | 1.29×1.74×1.16 | 1436 | 5：Iron Steel Brass MendGlow FuelRed | 鉚釘鐵筒＋綠色核心視窗、頂部核心籠、兩支多關節機械臂（液壓桿）＋焊槍（綠色電弧）／開口扳手、紅工具箱、修理招牌（鐵板＋綠十字） |

- 九座都在 2500 三角面以內，最多 5 個材質（Gun 只用 4 個）。
- Mender 的招牌板放在 BuildSystem `MendBadge` 的位置後面（head 座標 (0,1.45,−0.1)）。所以 runtime 那個發光十字是貼在招牌上，不會懸空。
- **限制**：遊戲的 OBJ 材質只讀 Kd/Ke，沒有透明度。泡泡圓頂是淡藍發光的「玻璃色」，實際不透明。

## 火箭筒（兩個槍械回報）
- **Boomerang**：Lead 已經還原成鮪魚造型，這輪沒碰。
- **Rocket**（`tools/make_weapon_models3.py` 的 Rocket 段，用 `ONLY=Rocket` 輸出）：長橄欖綠發射管（0.9 m）、後端排氣喇叭口、槍管箍＋白色標線、手槍握把＋扳機、前握把、肩托（托架＋墊）、可折疊前準星與後照門（翻起的照門片＋孔）、側邊擊發盒，紅色彈頭插在砲口（內頸＋胖彈體＋尖頭＋引信探針）。1178 三角面，6 個材質。
- `WeaponItems.Spec(Rocket).Muzzle` 改成 (0, 0.12, 0.58)：開火點在彈頭上（測試要求開火點離模型最前端 0.18 m 內）。手持長度維持 0.60 m。
- 對照圖：`preview/turrets_classic/rocket_before_after.png`。

## 前後對照
- `preview/turrets_classic_before_after.png`：九座都有，各三個角度（側／3/4／正面）。左邊是 V3，右邊是這次的經典版，都放在 TurretBase 上、用同一個取景。
- `preview/turrets_classic_silhouettes.png`：純黑剪影、128 px（約等於 1080p 畫面上 20 m 遠）、側面＋3/4。九座輪廓都不同：Gun 低寬雙長管、Cannon 輪子＋粗砲口、Harpoon 長滑軌＋叉頭、Mortar 斜仰短粗管、Tesla 高籠塔、Flame 雙直立桶、Sniper 超長單管＋三腳架、Bubble 圓頂、Mender 機械臂＋招牌。
- 替換前的檔案（OBJ/MTL/圖示）存在 `preview/turrets_classic/before/`。
- 圖示：重新產生了 `HtfFortress/Icons/Piece{Gun,Cannon,Harpoon,Mortar,Tesla,Flame,Sniper,Bubble,Mender}.png` 和 `GearRocket.png`。

## 指令
```
B=/Applications/Blender.app/Contents/MacOS/Blender
$B -b HtfFortress/Models/fortress_models.blend --python tools/make_turrets_classic.py        # ONLY=GunHead,... 可篩選
ONLY=Rocket $B -b HtfFortress/Models/fortress_models.blend --python tools/make_weapon_models3.py
$B -b HtfFortress/Models/fortress_models.blend --python tools/make_icons.py -- GearRocket PieceGun PieceCannon \
  PieceHarpoon PieceMortar PieceTesla PieceFlame PieceSniper PieceBubble PieceMender
python3 tools/turret_sheets.py     # 兩張預覽圖（內部呼叫 tools/turret_preview.py）
```

## 測試
- `dotnet build HtfFortress -c Release -p:Deploy=false -v q -nologo`：0 error（126 個 warning，都是原本就有的）。
- `zsh tests/run.sh`：**124067 passed, 0 failed**。中間有一次失敗：火箭開火點離彈頭尖端太遠。把 Muzzle 移到彈頭上就修好了。

## 還沒做／下一輪
- 這輪只做單件 `<T>Head`。§10.3 的分件（Base／Chassis／Module／Barrel／Glow）、36 個分支模組，還有 `<T>Barrel`（Lv5/10/15 長出的額外槍管）都還是舊的。Barrel 的樣式可能跟新砲頭不搭，下一輪要對齊。
- 分支裝飾（BranchA/B/A1/A2/B1/B2）的掛點 `BuildSystem.BranchMount` 是照舊砲頭定的，沒有重新驗證。需要出一張 Lv3/Lv6 分支圖逐座檢查有沒有浮空或穿模。
- Harpoon 的冰晶細節是固定在模型上的，不會只在 B 路線出現。要做到只在 B 路線顯示，得在 runtime 依分支切換。
- 泡泡圓頂沒有真的透明（材質管線不支援 alpha）。
- 沒有遊戲內測試（照規則只跑 headless）。實際遊戲光線下的顏色還要 Owen 看過才算數。
