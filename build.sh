#!/usr/bin/env bash
# 編譯批量改名工具（需要 Mono：sudo apt-get install mono-devel）
set -euo pipefail
cd "$(dirname "$0")"
mcs -nologo -target:winexe -optimize+ -platform:anycpu -sdk:4.5 -win32icon:src/app.ico \
    -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Xml.dll \
    -out:release/BatchRename.exe src/*.cs
echo "已編譯：release/BatchRename.exe"
