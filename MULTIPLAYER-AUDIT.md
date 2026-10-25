# 多人遊玩盤點（0.2.8 dev）

> 架構：**房主權威（host-authoritative）**。客戶端只送請求，主機改狀態後廣播。
> 結論：核心多人機制有在運作，但不是「所有東西都即時同步」；下面列出共享、個人、已知限制。

## 1. 共享（全隊一起變）

| 系統 | 誰改 | 同步方式 | 狀態 |
|---|---|---|---|
| 金錢 | host | SiegeState / 擊殺獎勵 | host 權威 |
| 魚鱗 | host | VaultState | host 權威 |
| 倉庫裝備 | host | VaultState | host 權威 |
| 塔裝備（10 格） | host | VaultState | 全隊生效 |
| 個人裝備 | host | VaultState（依 player key） | 只有自己生效 |
| 塔／牆／升級／搬移 | host | PieceState | 客戶端送 PieceUpgrade/Move |
| 小幫手 | host | HelperViews | 數量/速度/籃子共享 |
| 寶箱 | host | LootEvent | 一個人開，全隊拿 |
| 掉落物／魚鱗 | host | LootEvent | 走過去或 E |
| 王／波次／核心 | host | SiegeState | host 模擬 |
| 魂永久升級 | host | VaultState | **host 才能買**（共享存檔） |
| 結算／紀錄 | host | RoundResult | host 計算 |

## 2. 每位玩家分開

| 系統 | key | 說明 |
|---|---|---|
| 武器改造 | SteamID / objectId | 自己的 4 條改造；別人的不影響 |
| 個人裝備 | player key | 只影響自己 |
| 主動裝備（K） | player | 每人自己的冷卻 |
| 備用魚 | objectId | 每人腰間數量不同 |
| 魚網 | 已移除 | 小幫手共同撿 |
| 個人統計 | Stats | 擊殺/建造/釣魚/倒下 |

## 3. 這次修掉的多人問題

### 遲到玩家看不到別人的備用魚
- `NetState` 只同步自己的數量，`FoodPack` 只在「買/吃」時廣播。
- 遲到的玩家看不到已經站在場上的人腰間的魚。
- 已補：`OnClientLoadedStartScenes` 時把目前所有 `_backup` 的 `FoodPack` 補給新玩家。
- 房主自己也補了本地腰間模型更新。

## 4. 已知限制（不是 bug，但要知道）

1. **魂升級只有房主能買**
   - 共享存檔，避免兩個玩家同時花同一批魂。
   - 客戶端按鈕是灰的；要買請房主操作。

2. **客戶端 UI 是「本地先顯示、主機後校正」**
   - 改造、塔升級、倉庫升階、魚攤小幫手等會先動。
   - 若主機拒絕（魚鱗不足、別人先買走、庫存滿），下一個 VaultState 會改回正確值。
   - 不會因為本地預覽而真的多拿東西。

3. **一鍵全賣、買入、取物、存入仍等主機**
   - 金錢與物品實體在 host，沒有本地預覽。

4. **遲到玩家的其他狀態**
   - 寶箱、掉落物、魚鱗有補送；塔/波次/核心由 SiegeState 持續同步。
   - 小幫手用 HelperViews 補。

5. **多面板同時開**
   - UI 沒有統一 modal stack；`Hud.ModalOpen` 只擋一部分。
   - 建議不要同時開倉庫＋魂＋魚攤。

6. **版本/build**
   - 所有人必須同一個 build id；不同版本會被請離。

## 5. 建議的多人測試流程

1. 2 個玩家（host + 1 client）：
   - host 蓋塔、client 升級同一座塔 → 兩邊等級/數值一致。
   - client 改造武器 → 只影響 client。
   - host 換塔裝備 → 兩邊塔傷害都變。
   - 一人開寶箱 → 兩人都拿到。
   - host 買魂升級 → client 看到新數值。
   - 一人買備用魚 → 兩人看到腰間魚；新加入的第三人也要看到。
   - 一人按一鍵全賣 → 兩邊倉庫都清空。
2. 中途加入：
   - 第三人加入時，應看到既有寶箱、地上掉落、魚鱗、其他玩家腰間魚。
3. 延遲測試：
   - 客戶端按鈕後，本地先變；主機拒絕時要能回到正確值。

## 6. 程式上要再看的地方

- `Loot.Request` 的權限：SellAll 已開放所有人；其他 action 仍由 host 驗證。
- `FortressNet.AutoEatBackup` 只跑 host；客戶端只顯示。
- `Helpers.ServerTick` 只在 host；客戶端只畫 HelperViews。
- `Bosses` / `Turrets` 的傷害都在 host；客戶端預覽不會造成實際傷害。
- `VaultUI.PreviewAction` 只改 `ClientVault`，不是 host 的 `Vault`。
