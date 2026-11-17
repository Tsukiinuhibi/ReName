#!/usr/bin/env bash
# 發布新版：先把 src/Updater.cs 裡的 AssemblyVersion 改成新版本號，再執行
#   ./publish.sh "這次更新的內容"
# 會重新編譯、計算 SHA-256、更新 release/version.json。之後 commit 並 push 到 main 分支，
# 使用者的程式下次開啟就會看到更新提示。
set -euo pipefail
cd "$(dirname "$0")"
NOTES="${1:-}"
VERSION=$(grep -oP 'AssemblyVersion\("\K[0-9]+\.[0-9]+\.[0-9]+' src/Updater.cs)
./build.sh
SHA=$(sha256sum release/BatchRename.exe | cut -d' ' -f1)
python3 - "$VERSION" "$SHA" "$NOTES" <<'PY'
import json, sys
v, sha, notes = sys.argv[1:4]
data = {
    "version": v,
    "url": "https://forgejo.taiyopen.com/tsukiinuhibi/ReName/raw/branch/main/release/BatchRename.exe",
    "sha256": sha,
    "notes": notes,
}
open("release/version.json", "w", encoding="utf-8").write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
PY
cat release/version.json
