#!/bin/bash
# OVER 99 Fortress Island - 換房主：接收端（新主機，Mac / Pixel Port）
# 用法：雙擊這個檔，或在終端機執行 ./install-save-mac.command [HTF-transfer-xxxx.zip]
# 會把舊房主打包好的存檔 + 要塞倉庫裝進這台 Mac 的 Pixel Port 遊戲裡，
# 安裝前會先把現有檔案備份到桌面（HTF-hostmove-backup-...）。
set -euo pipefail

PP="${HTF_PP_ROOT:-$HOME/Library/Application Support/PixelPort/GameData/prefixes/free-steam}"
SAVES="$PP/drive_c/users/owen/AppData/LocalLow/Dazed Games/How to Fish/Saves"
GAME="$PP/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish"
VAULT="$GAME/BepInEx/fortress-vault"
BACKS="$GAME/BepInEx/fortress-backups"

say()  { printf '%s\n' "$*"; }
fail() { printf '\n[錯誤] %s\n' "$*"; exit 1; }

say "=== OVER 99 換房主：存檔/倉庫安裝（Mac） ==="
say ""

[ -d "$GAME" ] || fail "找不到遊戲資料夾：$GAME"

# 1) 找 zip：參數 → 桌面最新的 HTF-transfer-*.zip → 請使用者拖進來
ZIP="${1:-}"
if [ -z "$ZIP" ]; then
  ZIP=$(ls -t "$HOME/Desktop"/HTF-transfer-*.zip 2>/dev/null | head -1 || true)
fi
if [ -z "$ZIP" ] || [ ! -f "$ZIP" ]; then
  say "找不到 HTF-transfer-*.zip。"
  say "把朋友傳來的 zip 拖到這個視窗（或貼上路徑），按 Enter："
  IFS= read -r ZIP
  ZIP="${ZIP%\'}"; ZIP="${ZIP#\'}"
fi
[ -f "$ZIP" ] || fail "檔案不存在：$ZIP"
say "使用壓縮檔：$ZIP"

# 2) 遊戲必須關閉
if [ -z "${HTF_NO_PGCHECK:-}" ] && pgrep -f "How to Fish.exe" >/dev/null 2>&1; then
  say ""
  say "偵測到遊戲正在執行。請先完全關閉 How to Fish（含 Pixel Port 的視窗），"
  say "然後按 Enter 繼續……"
  IFS= read -r _
  pgrep -f "How to Fish.exe" >/dev/null 2>&1 && fail "遊戲還在跑，先關掉再執行這個檔。"
fi

# 3) 解開到暫存資料夾
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
unzip -q "$ZIP" -d "$TMP" || fail "解壓失敗：$ZIP"
[ -d "$TMP/Saves" ] || fail "這個 zip 裡沒有 Saves 資料夾（不是收集腳本產生的檔案？）"

# 4) 找目前的 SteamID 存檔資料夾（有 local.txt 的那個）
mkdir -p "$SAVES"
SID=""
for d in "$SAVES"/*; do
  [ -d "$d" ] || continue
  if [ -f "$d/local.txt" ]; then SID=$(basename "$d"); break; fi
done
if [ -z "$SID" ]; then
  SID=$(ls -1 "$SAVES" 2>/dev/null | head -1 || true)
fi
[ -n "$SID" ] || SID="76561198009466774"
DEST="$SAVES/$SID"
say "存檔資料夾：$DEST"

# 5) 先備份目前所有相關資料（桌面上，時間戳記）
STAMP=$(date +%Y%m%d-%H%M%S)
BKDIR="${HTF_BACKUP_DIR:-$HOME/Desktop}"
BK="$BKDIR/HTF-hostmove-backup-$STAMP"
mkdir -p "$BK"
[ -d "$SAVES" ] && cp -R "$SAVES" "$BK/Saves" 2>/dev/null || true
[ -d "$VAULT" ] && cp -R "$VAULT" "$BK/fortress-vault" 2>/dev/null || true
[ -d "$BACKS" ] && cp -R "$BACKS" "$BK/fortress-backups" 2>/dev/null || true
say "已備份原本的存檔與倉庫到：$BK"

# 6) 安裝：存檔（不覆蓋自己的 local.txt）、倉庫、備份
mkdir -p "$DEST" "$VAULT" "$BACKS"

installed_saves=0
while IFS= read -r -d '' f; do
  base=$(basename "$f")
  case "$base" in
    local.txt|local.txt.backup) continue ;;   # 每台電腦自己的玩家檔，不搬
  esac
  cp -f "$f" "$DEST/"
  installed_saves=$((installed_saves + 1))
done < <(find "$TMP/Saves" -type f \( -name '*.txt' -o -name '*.backup' \) -print0 2>/dev/null)

installed_vault=0
if [ -d "$TMP/fortress-vault" ]; then
  while IFS= read -r -d '' f; do
    cp -f "$f" "$VAULT/"
    installed_vault=$((installed_vault + 1))
  done < <(find "$TMP/fortress-vault" -maxdepth 1 -type f -print0 2>/dev/null)
fi

installed_backs=0
if [ -d "$TMP/fortress-backups" ]; then
  while IFS= read -r -d '' f; do
    cp -f "$f" "$BACKS/"
    installed_backs=$((installed_backs + 1))
  done < <(find "$TMP/fortress-backups" -type f -print0 2>/dev/null)
fi

# 7) 結果
say ""
say "安裝完成：存檔 $installed_saves 個、倉庫 $installed_vault 個、備份 $installed_backs 個。"
say ""
say "存檔："
ls -1t "$DEST" 2>/dev/null | head -20 | sed 's/^/  /'
say ""
say "倉庫（檔名 / 大小 / 魚鱗 / 裝備件數）："
for f in "$VAULT"/*.txt; do
  [ -f "$f" ] || continue
  sc=$(grep -m1 -E '^scales ' "$f" 2>/dev/null | awk '{print $2}' || true)
  own=$(grep -c '^own ' "$f" 2>/dev/null || true)
  printf '  %-28s %8s bytes  魚鱗 %-6s 裝備 %s 種\n' "$(basename "$f")" "$(wc -c < "$f" | tr -d ' ')" "${sc:-?}" "${own:-0}"
done

say ""
say "接下來："
say "  1. 開遊戲 → 載入剛剛裝進來的存檔 → 開房（Host）"
say "  2. 所有人要用同一版模組（目前 0.1.3-a64b5c），不然會跳版本警告"
say "  3. 有問題就從備份還原：$BK"
say ""
say "按 Enter 關閉這個視窗。"
if [ -z "${HTF_TEST:-}" ]; then IFS= read -r _ || true; fi
