# ReName — 批量改名工具

Windows 用的批量改檔名小工具。選取或拖曳檔案進來，程式會自動辨識檔名裡的公司、文件名稱、頻率、日期、廠商、編號和備註，
再依照設定的格式改名。改名前可以預覽，改完會留下紀錄，可以一鍵還原。

## 下載

[release/BatchRename.exe](release/BatchRename.exe)：下載後雙擊就能用，不用安裝（需要 Windows 10/11 內建的 .NET Framework 4）。

第一次開啟若出現「Windows 已保護您的電腦」，按「其他資訊」→「仍要執行」。

## 自動更新

程式每次開啟會讀取 [release/version.json](release/version.json)。有新版時視窗上方會出現提示，按「立即更新」就會：

1. 下載新版 exe，並用 SHA-256 驗證檔案完整
2. 把舊版改名成 `.old`、新版放到原位置
3. 重新開啟程式（下次開啟時自動刪掉 `.old`）

設定、學到的規則存在 `%AppData%\BatchRename`，改名紀錄存在「文件\批量改名紀錄」，更新不會影響。

## 發布新版

```bash
# 1. 修改 src/ 裡的程式
# 2. 把 src/Updater.cs 的 AssemblyVersion / AssemblyFileVersion 改成新版本號
./publish.sh "這次更新的內容"
git add -A && git commit -m "批量改名工具 vX.Y.Z" && git push <Forgejo 遠端> main
```

更新來源固定是 Forgejo（https://forgejo.taiyopen.com/tsukiinuhibi/ReName）`main` 分支的 `release/version.json`，請保持儲存庫公開。

## 檔案

| 路徑 | 說明 |
|---|---|
| `src/Core.cs` | 檔名辨識、範例學習、改名與還原 |
| `src/MainForm.cs` | 視窗介面 |
| `src/Updater.cs` | 自動更新、版本號 |
| `build.sh` | 編譯（Mono `mcs`） |
| `publish.sh` | 編譯並產生 `release/version.json` |
