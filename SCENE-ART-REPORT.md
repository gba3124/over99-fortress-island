# 場景／道具美術報告（scene-art 分支）

> 範圍：要塞島地形、三條路、舊城門、沉船、碼頭、路標、商店招牌；只動島嶼／場景模型、它們的擺放與碰撞、工具與預覽圖。
> 沒碰武器、砲塔、特效、版本號、Thunderstore。沒有 deploy／pack。

## 完成項目

### 1. 舊城門（東南路線）— 重做成真正的入口
- **拱圈**改成沿曲線 loft 的連續拱石環（`arch()`，一段一塊、深淺交替、頂上一塊凸出的拱心石），不再是 31 個階梯狀方塊。
- 拱從左柱越過頂端，到右柱前斷掉；右柱只剩柱頭，掉下來的拱石**躺在柱腳旁邊的沙裡（不在路上）**。
- 兩根柱子各用自己那一側的地面高度，柱腳往坡裡埋 0.6 m；拱起高取兩邊較高者，不再有一邊懸空。
- 每根柱子向外接一段**殘牆**（四段、逐段降低、最後變成半埋的碎石），把城門和兩側崖壁連起來。
- 門洞下鋪**石板門檻**（地面材質 `IslGateStone` ＋ 5×7 塊貼地石板），路線往海邊散落幾塊舊鋪石（這條路的辨識元素）。
- 預覽：`preview/scene_final/island_gate.png`、`island_gate_inside.png`。

### 2. 沉船（北路線）— 獨立模型＋獨立碰撞
- 舊沉船是開口 U 形殼＋肋骨，併在島的單一 MeshCollider 裡，而且算出來**正好壓在北路的中線上**（路在 r 22–28 處彎到 x≈-3）。
- 新的 `Models/Wreck.obj`（615 面）：船板船殼（內外兩層）、船舷側破洞露出斷肋、龍骨接船首柱、艉板、兩道鐵箍、斷桅＋帆桁＋破帆、繩索、船內橫座。材質 `WreckWood / WreckDark / WreckIron / WreckSail`。
- 移到路線東側 (3.6, 25.6)，朝島內側傾 30°，所以從島上看到的是船板那一面和破洞。
- 周圍：沙丘堆在低的一側、靠路側一道濕沙沖痕、半埋的船板（帶鐵件）、桶、箱、拖上沙灘的鐵錨＋鍊條。
- **碰撞**：`Models/WreckCollider.obj` 是一個封閉、略大的光滑船殼（102 面），在 `FortressIsland.Spawn` 裡掛成 **convex MeshCollider**；魚撞到會沿船身滑開，不會卡進肋骨之間。帆、桅、繩都不參與碰撞。
- 預覽：`island_wreck.png`、`island_wreck_sea.png`、`island_wreck_lane.png`。

### 3. 三條路＋村莊小徑 — 分層、邊緣不直切
- `ground_mat()` 改成：中央踩實的深色 `IslPathWet`（< 0.55 m）→ 主路 `IslPath` → 草地上的塵土過渡 `IslPathEdge`（寬度加 noise 抖動）；沙灘段邊緣改用濕沙色過渡。
- 村莊小徑同樣有邊緣層。
- 預覽：`island_lanes.png`、`island_top.png`。

### 4. 碼頭／木橋
- 木板下加一片深色**連續甲板底板**（縫隙看起來是陰影，碰撞連續、沒有洞）、兩條縱向樑。
- 樁柱加高穿過甲板（鐵箍＋柱帽），每個樁站有橫樑，樁與樁之間有斜撐（下到水下）。
- **兩側都有繩欄**，中間加短欄杆柱；橋頭加石造橋台、兩根繫纜柱和一圈繩子。
- 繩欄仍是碰撞體（擋人走出橋外）。
- 預覽：`island_dock.png`、`island_dock_head.png`。

### 5. 路標
- `vkit.signpost()` 重寫：四塊牌，**方向用實際建築位置算出**（`arm_yaw()`），牌面加深色邊、顏色帶，兩面都有圖示：
  軍需官＝打叉木箱、魚攤＝魚、鐵匠＝鐵砧、燈塔＝燈塔。
- 預覽：`island_signpost.png`。

### 6. 商店招牌
- 鐵匠：門口吊牌改成**鐵砧＋交叉雙錘**（原本是錘子交叉槍管）。
- 軍需官倉庫：門上招牌改成**黃銅盾牌＋鐵十字，兩側各一個綁帶木箱**。
- 魚攤原本就有大魚招牌，未動。
- 預覽：`island_smithy_front.png`、`island_vault_front.png`、`island_village.png`。

### 7. 卡住／碰撞清理
- `FortressIsland.NotSolid` 加入：網繩 `IslCord`、海星、貝殼、花（`IslFlowerA/B`、`VkPetalA/B/C`、`VkLeaf`）、燈籠玻璃（`VkLit`、`IslLampGlow`）—— 都是薄件，只畫不碰。碼頭繩欄的 `Rope` 維持實體。
- 潮池的岩架由直壁圓柱改成**斜坡圓錐**（`cone(size+1.0, size+0.35)`），魚是走上去而不是撞台階。
- 城門掉落石塊全部移出路面；沉船附近的船板埋進沙裡只露 2 cm、移到路外。
- 草叢／植物的擺放避開沉船（半徑 6 m）和城門門檻（4.2 m）。

### 8. 預算
| 模型 | 三角面 |
|---|---|
| FortressIsland.obj | 35,350（原 33,000；沉船已拆出） |
| Wreck.obj | 1,234 |
| WreckCollider.obj | 216（convex） |
| Smithy / Vault | 和原本同量級（只換招牌） |
- 島仍是一個網格、一個 MeshCollider；沉船多 4 個材質 submesh。

## 檔案
- `tools/make_island.py` — 路面分層、城門、沉船（獨立 Builder＋碰撞殼）、沙丘、碼頭、路標、預覽鏡頭（新增城門／沉船／碼頭／路標／商店特寫）。
- `tools/vkit.py` — `signpost()` 重寫、新增 `sign_icon()`。
- `tools/make_station_models.py` — 鐵匠招牌。
- `tools/make_vault_models.py` — 倉庫招牌。
- `HtfFortress/FortressIsland.cs` — `NotSolid` 擴充；`Spawn()` 掛 `Wreck` 模型＋ `WreckCollider` convex 碰撞。
- `HtfFortress/Models/` — `FortressIsland.obj/.mtl/.decor.txt`、新 `Wreck.obj/.mtl`、`WreckCollider.obj/.mtl`、`Smithy.obj`、`Vault.obj`、`fortress_models.blend`。
- `tests/Headless/Tests.cs` — 島面朝上檢查加入新地面材質 `IslPathWet / IslPathEdge / IslGateStone`。
- `preview/scene_final/*.png` — 17 張預覽。

## 測試
- `dotnet build`：0 errors（經 `tests/run.sh`）。
- `zsh tests/run.sh`：**124,067 checks passed, 0 failed**（含島地面朝上、各站點平地、碼頭到各站可走、泊位淨空）。
- 重建指令：
  ```
  /Applications/Blender.app/Contents/MacOS/Blender -b HtfFortress/Models/fortress_models.blend \
    --python tools/make_island.py
  /Applications/Blender.app/Contents/MacOS/Blender -b HtfFortress/Models/fortress_models.blend \
    --python tools/make_island.py -- preview preview/scene_final
  ONLY=Smithy ... --python tools/make_station_models.py ;  ONLY=Vault ... --python tools/make_vault_models.py
  ```

## 未做／待驗證
- **遊戲內驗證**沒做（依規則不開遊戲測）：沉船 convex 碰撞對魚的實際效果、20 波無卡魚的驗收、路標在夜晚的可讀性，要 Owen 實玩確認。
- 路面沒有腳印／泡沫線貼花（面數與一網格的限制）；分層靠 0.65–1 m 的地面網格著色，近看是色塊。
- 魚攤招牌、燈塔阿伯徽章、商店其他小道具（冰塊、價格牌已有；煙囪火星、門簷等）未加。
- 小道具 LOD（>90 m 不畫）未做：島仍是單一網格。
- 碼頭木板的釘子、木紋細節未加（面數考量）。
