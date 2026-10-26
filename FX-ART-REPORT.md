# FX 美術回合報告（0.3.0 dev，branch `gba3124/fx-art`）

目標：每一類特效在低多邊形風格下讀得清楚、風格一致；本地與遠端的槍口起點一致；粒子數與壽命有上限。
只動 Fx／WeaponFx／BossFx／BossVfx／AbsorberFx、`Weapons.cs` 的「effects (every client)」段落、測試與預覽；沒動砲塔／武器模型、場景、版本、Thunderstore。

## 已完成

### 1. 槍／投射物 FX
| 項目 | 之前 | 現在 | 檔案 |
|---|---|---|---|
| 火箭爆炸 | `RocketView` 自己畫 SmallExplosion＋16 火花＋音效，host 的 `Blast` 事件又在同點再畫一顆球：兩聲爆炸、兩個特效 | 火箭到點只閃一下光；爆炸只由 host 的 Blast 用真實半徑畫一次（`Fx.Explosion`） | `Weapons.cs RocketView` |
| 火箭尾焰 | 每 25 ms 灰塵球＋橙火花 | 火焰羽（`FlameFor` 橙）＋一條逐漸變大、上飄的煙（`RocketPuffEvery` 30 ms，數量按時間不按幀率）＋發射口白煙 | 同上 |
| 加特林曳光 | 已有 pooled 曳光條、槍口閃光 | 砲塔機槍／狙擊的曳光改用同一套發光材質（`BossVfx.Glow`），命中處加回彈火花，與加特林同語言 | `Fx.cs Tracer` |
| 魚叉塔 | 一根裸鐵條飛過去 | 鐵條後面從砲塔拉出繩索（hook 槍的繩索貼圖），到點濺水滴，繩索隨鐵條消失 | `Fx.cs Bolt` |
| 水砲 | 每 0.1 s 6 滴散在整條線上、0.35 s 壽命，半路就死：稀疏的點 | 10 滴全部從噴口出發，速度＝射程／壽命（`WaterJetLife`，0.25–0.7 s）真的飛到命中點；沿途水霧、命中泡沫＋水滴 | `Weapons.cs WaterView` |
| 雷射光束腳 | host 每 100 ms 送一次起點，轉身時光束腳掛在槍口後面空中 | `AnimateBeams` 每幀把腳移到槍口（本地 `MyMuzzle`、朋友 `HeldMuzzle`） | `Weapons.cs Beam/AnimateBeams` |
| 吸收槍起點 | `PlayAbsorber` 直接用 host 的 `From` | suck／shot／warden／angler／eye 五招先經 `Weapons.Origin(e)` 換成本畫面看到的槍口 | `Fx.cs Play`、`Weapons.cs Origin` |
| 吸收槍 sky 環 | 環停在槍口長到 5 m，包住射手自己的相機 | 從前方 0.6 m 出發沿氣流飛出去邊長大 | `AbsorberFx.cs` |
| 泡泡／海星／水母／漩渦／網 | 讀過一遍：投射物模型、尾跡與落點都已有層次且有上限，未改 | — |

### 2. 砲塔爆炸與王的特效
| 項目 | 之前 | 現在 |
|---|---|---|
| 砲塔 Blast | 一顆不透明橙色球，直徑隨等級 3.5 m → Lv20 約 7 m，貼臉時整個畫面橙色 | `Fx.Explosion`：閃光 ≤ `BlastCoreMax` 2.4 m、地面衝擊環＝真實半徑（Lv1 2 m、Lv20 4.1 m、火箭 3 m）、火花 8–32、煙 3–10 |
| 砲彈／迫擊砲落地 | 同上球（`Arc` 結尾） | 同 `Explosion`，半徑 1.6 m×等級，上限 3.5 m |
| 浪（Wave） | 6 m 水色球 | `Fx.Splash`：海面環＋泡沫＋水滴＋遊戲 splash（目前沒有人送 Wave，先留著） |
| 王／寶箱閃光 | 光暈直徑隨大小無限長（傳奇寶箱 7.8 m），站在旁邊整個螢幕變色；每次閃光都生一盞點光，Nova 一次打一群魚就幾十盞 | 光暈 ≤ `HaloMax` 4 m，且 ≤ `Room(相機距離)` 永遠不包住相機；點光同時最多 4 盞（`MaxLights`） |
| 獎盃光柱 | `Cyl` 網格從腳底 0..1，放在 ground+6 → 浮在 6–12 m | 立在獎盃上，8 m 高 |
| 死亡標記金柱 | 同樣的 bug，浮在 15–30 m | 立在屍體位置 |
| 王的地面預警 | 已清楚（紅色由起點填滿、亮前緣、王色外框加速閃），未改 | — |

### 3. 共用材質與上限
- 砲塔能量類（曳光、狙擊曳光、電弧、爆炸核、火、治療）改用 `BossVfx.Glow`：每色一份、與武器／王共用，同一套 bloom 發光；物質類（砲彈、石彈、砲殼、吐出物、塵土、水）維持 lit 材質。
- 所有粒子仍走 `WeaponFx.System` 的 `Max`（300–800）；火箭尾跡改成按秒計數；水砲每事件 ≤ 12 滴；Flash 點光 ≤ 4；pooled cube／sphere 上限 200 不變。

### 4. 預覽（`preview/fx_final/`）
- `projectiles.png`／`.txt`：10 個投射物模型（火箭彈、毒氣罐、鉤爪、龍蝦鉗、海星、網球、張開的網、水母、河豚彈、回力鏢）五視角 Blender 渲染＋尺寸／三角形／材質數。
- 關鍵影格分鏡（before／after，依程式數值按 1 m 尺度畫）：`turret-blast.png`、`rocket.png`、`water-jet.png`、`laser-foot.png`、`harpoon-rope.png`、`flash-camera.png`、`trophy-pillar.png`、`absorber-sky-ring.png`、`boss-telegraphs.png`。
- 產生方式：`python3 tools/make_fx_sheets.py`；投射物表 `Blender -b --factory-startup --python tools/gun_preview.py -- preview/fx_final/projectiles.png models=HtfFortress/Models -- RocketShell GasCanister GrappleClaw LobsterClaw Starfish NetBall NetSpread Jellyfish PufferBomb Boomerang`。

## 改動檔案
`HtfFortress/Fx.cs`、`HtfFortress/Weapons.cs`（只有 effects 段）、`HtfFortress/BossVfx.cs`、`HtfFortress/BossFx.cs`、`HtfFortress/AbsorberFx.cs`、`tests/Headless/Tests.cs`（新增 `FxChecks`）、`tools/make_fx_sheets.py`、`preview/fx_final/*`。

## 測試
- `dotnet build HtfFortress -c Release -p:Deploy=false`：0 errors。
- `zsh tests/run.sh`：124078 checks passed, 0 failed。新增的 `FxChecks`：`BlastLook` 閃光封頂／環＝半徑／數量上限；`HaloSize`、`Room`；`WaterJetLife`、`RocketPuffEvery` 範圍；原始碼檢查（火箭不再自畫爆炸、光束腳跟槍、吸收槍走 `Origin`、砲塔 Blast 走 `Explosion`、魚叉有繩、曳光發光、死亡柱落地）。
- 沒有開遊戲實測（規則：除非 Owen 要求）。

## 未完成／待實機確認
1. **第一人稱實際觀感**：Explosion 的環、火箭煙、水砲噴流、雷射腳跟槍，都只用公式與分鏡驗過，要實機看一次（尤其火箭 30 ms 一組粒子在高射速 legendary 下的密度）。
2. **BossMoves.cs 各招**（1380 行）只透過共用的 Flash 封頂受益，沒有逐招審視大小與顏色。
3. **砲塔 Blast 的半徑**：host 對砲塔只送 PieceId 沒送 splash 半徑（`Turrets.cs` 不在本回合範圍），所以用 `BlastDefault` 2 m × 等級係數近似；要精準得改 `Turrets.cs` 把 `s.Splash` 塞進 `Arg`。
4. **毒氣雲**：單顆 splat 最大 2.3 m，貼臉仍會蓋畫面；未動（那是雲的本質，改了會變稀）。
5. **FxKind.Wave** 目前沒有任何發送點，`Splash` 先留作接口。
6. 同一 worktree 原有另一個 FX 代理（fx-art-eb）做了王側三個檔案與四張分鏡，其腳本被我同名覆蓋；我已把那四張表的產生邏輯用同一公式併進 `tools/make_fx_sheets.py`，圖可重現。
