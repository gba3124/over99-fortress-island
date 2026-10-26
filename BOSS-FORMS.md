# 王的第二形態（BOSS-FORMS）

> 2026-10-26。Owen：「還需要設計每隻王的二形態，還有需要讓剛變二形態的時候會變硬幾秒之類的，不然很多都沒看到王就掛了，然後如果王有 IK 可以各種情境有更自然的動作會更好。」
>
> 這份是**設計正本**：十隻王各自的第二形態、變身窗口規則、IK／程序化動作規格。
> 程式現況（同一天）：框架、變身無敵窗口、十種第二形態配色、程序化 IK 都已實作；十隻王的「新招」與各自的變身演出也已實作（見各節「實作」行）；「追加機制」欄位是下一輪要接的內容。

---

## 0. 這次解決的三個問題

1. **看不到第二形態**：原本血量過半就直接切 `Phase2`，沒有任何保護——高波數的一次爆發可以從 60% 打到 0，第二形態永遠不會出現。現在變身是一段**無敵窗口**（`BossBrain.Transform()`），而且「未變身前不能被一擊打死」。
2. **第二形態只是變快變多**：原本多數王只是「次數 ×2、傷害 ×1.2」；現在每隻都有自己的配色、宣告詞與追加機制。
3. **動作不自然**：魚類模型是骨架蒙皮（`SkinnedMeshRenderer.bones`），沒有現成 IK rig。新增 `BossIk`：頭部注視、鉗／手臂／觸手朝最近的玩家伸，第二形態時力道更強。

---

## 1. 變身窗口（所有王共用）

| 項目 | 規則 |
|---|---|
| 觸發 | HP ≤ 50%，且已經登場（`_live`）、不在硬直、不是分身 |
| 一擊保護 | 未變身前，任何一擊最多把 HP 打到 **1**，不會直接死（`Bosses.OnHurt` 夾住） |
| 長度 | 凍結 0.9 秒（`TransformSeconds`）＋ 該王自己的 `SecondForm()`（1.5–2.5 秒） |
| 期間 | **完全無敵**（`Transforming`；傷害回 0，記 `boss_hit_in_transition`）、不能被打斷、原招式取消 |
| 表現 | 全畫面重繪成第二形態配色（`BossFx "secondform"` → `BossLooks.Recolor`）、環形衝擊、四段金／紅爆閃、BossFightIntro 音效、該王的宣告詞 |
| 結束 | `Transforming = false` 才恢復可受傷；之後才輪到第二形態的招式組 |
| 觀測 | `AutoTest.Count("boss_phase2_<Kind>")` 與 `boss_hit_in_transition`（驗收用：被擋下的傷害次數應 > 0） |

**驗收**：把任一隻王一波打到過半，畫面上一定要看到換色＋宣告＋至少 1.5 秒的演出；期間打它傷害條不動；演出結束後才開始第二形態的招式。

---

## 2. 十隻王的第二形態

### 2.1 鐵鉗典獄長 Iron-Claw Warden（wave 10，龍蝦）
- **第二形態：熔鐵典獄長** — 配色換成鎔鐵（黑→暗紅→橙→白熱），鐵鉗發光。
- 追加機制：Guillotine 連剁兩下（已有）＋ 每一刀在地面留下 3 秒的燒灼帶；Shackles 一次鎖 3 座塔（已有）。
- 新招：**Anvil 鐵砧** — 雙鉗舉起後同時砸下，以自身為中心的環形衝擊（可打斷，前搖 1.4 秒）。
  - 實作：`Warden.Anvil()`，環形預警（內圈＝自身腳下）半徑 Reach+5.5，90/28/40（塔／玩家／燈塔，同 Stomp 量級）；`warden.heat`／`warden.anvil`；`warden_anvil`。
- IK：雙鉗在抬鉗階段朝最近的塔／玩家抬起，頭部持續注視目標。
- 宣告：`wardenPhase2`「爐火重燃！」；變身演出：後仰舉鉗發熱 → 砸地（1.8 秒）。
- 反制：站到燒灼帶的縫隙、斷招窗口在抬鉗時。

### 2.2 萬針刺皇 Thousand-Needle Urchin（wave 20，海膽）
- **第二形態：虛空星爆** — 深紫→霓虹粉白。
- 追加機制：滾動 Crush 滾過的地面留下 6 秒尖刺（`Caltrops`）、Volley 20 發（已有）、Bloom 4 圈（已有）。
- 新招：**Bristle Nova** — 原地繃緊後全向爆刺，外圈同時生成尖刺（前搖 1.2 秒，可斷）。
  - 實作：`Urchin.Nova()`，環形爆刺半徑 Radius+6（65/24/30）＋外圈 8 個 caltrop 點（35/12/15，留 6 秒）；`urchin.nova`；`urchin_nova`。
- IK：無肢體；本體旋轉與尖刺伸缩改為「繃緊→釋放」的彈性動畫（BossBody 擺幅加大）。
- 宣告：`urchinPhase2`「千針齊發！」；變身演出：原地越轉越快 → 全身爆刺（1.9 秒，無傷害）。

### 2.3 深淵提燈 Abyssal Lantern（wave 30，燈籠魚）
- **第二形態：永夜** — 深藍黑→電光青；「夜不再散去」（已實作）。
- 追加機制：Lure 同時丟兩顆誘餌（一真一假）、Devour 連吃兩次（已有）。
- 新招：**Blackout 熄燈** — 燈籠熄滅 2 秒，場上只剩燈塔光（純氛圍，不影響判定）。
  - 實作：`Angler.Blackout()`。黑暗本身只是畫面（燈籠、BossGlow 熄滅＋`BossFx.Blackout` 再壓暗，砲塔射程不變）；為了「每招都要地面預警」，熄燈的 2 秒就是紅色通道的預警，燈一亮牠就沿通道突進（90/28/40，不可斷）；`angler.blackout`；`angler_blackout`。
- IK：燈籠觸鬚朝最近玩家擺動（`BossIk` 的 tentacle 鏈）；頭部注視。
- 宣告：`anglerPhase2`（已存在）。

### 2.4 白銀天馬 Silver Steed（wave 40，海馬）
- **第二形態：黃金迴轉** — 銀白→鎏金。
- 追加機制：Charge 來回兩趟（已有）、幻影騎兵 5 路（已有）。
- 新招：**Golden Fissure** — 每次 Stomp 在地面留下放射狀金裂縫，踩到受傷（純視覺＋小傷害）。
  - 實作：第二形態的 `Steed.Stomp()` 多 6 道裂縫（細通道預警，與 Stomp 同一個可斷前搖），裂縫上留 6 秒 Hazard（只傷玩家 Scaled(2)／0.5 秒，不傷建築）；`steed.fissure`；`steed_fissure`。
- IK：頭部與前腳朝向衝鋒方向；四足步態在第二形態加速（BossBody 擺幅）。
- 宣告：`steedPhase2`（已存在）。

### 2.5 劇毒岩皇 Venom Rock Emperor（wave 50，石魚）
- **第二形態：毒潮** — 岩灰→毒綠白。
- 追加機制：Spires 5 線（已有）、Venom 8 球（已有）、毒池加大。
- 新招：**Geyser** — 鑽地後在原地留下 3 個毒泉，每 1.5 秒噴發一次（可斷）。
  - 實作：`Stonefish.Geyser()`，毒泉在上次鑽下去的洞（`_hole`），噴 3 輪、每輪 1.5 秒前搖各自可斷（每輪 3% 血），55/20/25；`stone.vent`／`stone.geyser`；`stone_geyser`。
- IK：頭部與毒刺朝目標；鑽地時整條身體下潛（BossBody）。
- 宣告：`stonePhase2`「毒潮湧現！」；變身演出：眼睛發光、地鳴、毒刺脈動 → 岩刺環（1.9 秒）。

### 2.6 天翔魚雷提督 Sky Torpedo Admiral（wave 60，飛魚）
- **第二形態：全彈發射** — 深藍→風暴白；升空高度更高（已實作）。
- 追加機制：Squadron 6 架（已有）。
- 新招：**Sonic Boom ×2** — 連續兩圈音爆，第二圈延遲 0.8 秒（逼玩家移動）。
  - 實作：`Skyfish.DoubleBoom()`，先內圓（半徑 6+Tier×0.1）再 0.8 秒後外環（到 2 倍半徑），兩段預警同時出現、填色時間差 0.8 秒；各 90/26/40；`sky.ring`；`skyfish_boom2`。變身演出改為 1.6 秒爬升翻滾。
- IK：無肢體；機身俯仰與側傾追蹤最近的玩家（BossIk 對 head 的 look-at 改為整身 banking）。
- 宣告：`skyfishPhase2`（已存在）。

### 2.7 深紅克拉肯 Crimson Kraken（wave 70，烏賊）
- **第二形態：血潮** — 深紅→黑血紅；墨汁更濃。
- 追加機制：Ink 遮蔽更久、Whirl 拉人更強。
- 新招：**Two-Arm Grab** — 一次抓兩個目標（塔或玩家），兩條觸手同時收束（可斷）。
  - 實作：`Kraken.TwoArm()`，最近的玩家＋最近的塔湊滿兩個，2 秒可斷前搖後收束：塔 150、圈內玩家 26 並往海拉（不像 Grab 會把塔拖走）；`kraken.squeeze`；`kraken_twoarm`。
- IK：8 條觸手全部朝最近玩家伸（`BossIk` tentacle 鏈，第二形態 weight 1.0）。
- 宣告：`krakenPhase2`「血潮來了！」；變身演出：海面泛紅、四條觸手朝海面揮下、本體浮起（1.9 秒，無傷害）。

### 2.8 碎裂像素王 Shattered Pixel King（wave 80，方塊魚）
- **第二形態：FATAL ERROR** — 綠→洋紅／青的故障色（已配）。
- 追加機制：分身 3 隻、Delete／Undo（已有）。
- 新招：**Glitch Tiles** — 場上冒出 5 塊故障格，塔的鎖定在 1.5 秒內亂跳（純干擾）。
  - 實作：`Voxel.Tiles()`，5 個方框預警 1.2 秒，格上的塔 `Bosses.Scramble` 1.5 秒（`Turrets.Fire` 改成每發隨機挑射程內一隻，狙擊塔也不挑最硬的），不造成傷害；`voxel.tile`；`voxel_tiles`／`voxel_scrambled`。
- IK：無肢體；本體以「逐格跳動」位移（BossBody 加抖動）。
- 宣告：`voxelPhase2`（已存在）。

### 2.9 登龍錦鯉 Koi of the Dragon Gate（wave 90，錦鯉）
- **第二形態：化龍** — 白紅金→龍金（已實作，`BossLooks.Paint` 的 koi second）。
- 追加機制：Gate Leap 變 Dragon Dive（已有）、Jet 火焰、雷電鏈。
- 新招：**Dragon Breath** — 龍化後沿直線吐息 1.2 秒，地面留火（可斷）。
  - 實作：`Koi.DragonBreath()`，寬 3.4 的直線（長 12–26）、火線前緣 0.35 秒推到底，115/35/50，沿線每 2.6 m 留火 4 秒；`koi.flame`；`koi_line`。
- IK：頭部與兩條鬚朝目標；飛行時身體 S 形擺動（BossBody 擺幅 ×1.35）。
- 宣告：`koiPhase2`「登龍門！」（飛上去時），到頂化龍時接原本的 `koiDragon`。

### 2.10 終焉之眼 The Eye at the End（wave 100，巨眼）
- **第二形態：OVER 99** — 暗紅→白熱審判。
- 追加機制：紅天不再退（已類似）、光束加寬。
- 新招：**Second Pupil** — 第二顆瞳孔張開，兩道旋轉光束（前搖 2 秒，可斷）；Judgement 覆蓋全場一次。
  - 實作：`Eye.SecondPupil()`，眼球旁長出第二瞳孔，兩道光束反向掃 ±48° 交叉（與 Beam 同傷害）；`eye.pupil`／`eye.twin`；`eye_pupil`。Judgment 加一層全場（半徑 30）預警：燈塔圈照舊 200/45、圈外 60/20，同一個 8 秒可斷前搖。
- IK：瞳孔永遠追著主玩家（BossEye 自己處理），眼瞼在第二形態張更大。
- 宣告：`eyePhase2`（已存在字串）。

---

## 3. IK／程序化動作（`BossIk.cs`）

| 部位 | 做法 | 適用 |
|---|---|---|
| 頭／頸 | 朝最近玩家 look-at，平滑 0.35、每 0.1 秒更新一次目標 | 所有有 head／neck 骨的王 |
| 鉗／手臂 | 對骨鏈做「越靠身體轉越多」的彎曲（0.55 / (i+1)） | Warden 等有 claw/arm/hand 骨的王 |
| 觸手 | 同上，鏈長一次處理 | Kraken、Angler（燈籠觸鬚） |
| 第二形態 | `BossBody.Second` → IK 權重 0.55→1.0、擺幅 ×1.35、光暈更密 | 全部 |

- 只認名字像肢體的鏈（claw/arm/hand/tentacle/tent/jaw/pincer/whip）；腿由 `BossBody` 的步態擺動處理，尾巴與鰭不拉直（拉直會很奇怪）。
- 找嘸可用的骨頭就 `enabled = false`，外觀與改動前完全相同（安全退路）。
- 純視覺、不上網路：每台機器各自用看得到的玩家算目標，不影響判定與同步。

---

## 4. 下一輪（已列入 backlog）

- [x] 每隻王的宣告詞補齊 16 語系（`wardenPhase2`／`urchinPhase2`／`stonePhase2`／`krakenPhase2`／`koiPhase2`，加上十個新招的招式名）。
- [x] 「新招」欄位的實際招式實作（十隻都有，見 §2 各節「實作」行）。
- [ ] 變身時鏡頭輕微拉近／慢動作（可選；先確認不影響多人同步）。
- [x] 驗收腳本：AutoTest `bosses` 打到過半 → 變身中補一擊、記錄被擋次數與血量不變 → `CastNew` 讓新招下一個出手並記數。
- [ ] 「追加機制」欄位（燒灼帶、Crush 留刺、雙誘餌等）。

## 5. 對應程式

| 檔案 | 內容 |
|---|---|
| `Bosses.cs` | `OnHurt` 的一擊保護與變身無敵；`BossBrain.Transform()`；`Transforming` |
| `BossFx.cs` | `"secondform"` → `SecondFormFx()`（全畫面重繪＋爆閃） |
| `BossLooks.cs` | 十種第二形態配色（`Paint(k, second)`）、`Recolor()`、`BossBody.Second` |
| `BossIk.cs` | 頭部注視與肢體彎曲 |
| `BossBrains.cs` | 十個新招（`NewMove()`）與各王的 `SecondForm()` 演出 |
| `BossMoves.cs` | 新招的畫面（`warden.anvil` … `eye.twin`） |
| `Turrets.cs` | 故障格：被 `Bosses.Scramble` 的塔每發隨機選目標 |
