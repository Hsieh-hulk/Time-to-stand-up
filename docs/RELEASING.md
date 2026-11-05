# 發版流程

1. 修改根目錄 `Directory.Build.props` 的 `<Version>`(例如 `1.0.1`)。exe 與安裝程式都讀這個值。
2. 執行:

   ```powershell
   powershell -ExecutionPolicy Bypass -File build\publish.ps1
   ```

   腳本會依序:跑單元測試 → `dotnet publish`(Release、win-x64、內含 .NET 的單一 exe,輸出到 `publish\`)→ 用 Inno Setup 編譯 `installer\StandUp.iss` → 產出 `dist\StandUp-Setup-<版本>.exe`。
   加 `-SkipTests` 可略過測試。
3. 在乾淨環境(或另一個 Windows 使用者帳號)照 [TESTING.md](TESTING.md) 的「安裝程式」段落驗證。
4. 把 `dist\StandUp-Setup-<版本>.exe` 上傳到發佈處(例如 GitHub Releases)。`publish\` 與 `dist\` 不進版控。

## 前置需求

- .NET SDK 8 以上。
- Inno Setup 6:`winget install JRSoftware.InnoSetup`。腳本會在 PATH、`%LocalAppData%\Programs\Inno Setup 6`、`Program Files (x86)\Inno Setup 6` 找 `ISCC.exe`。
- (選用)繁體中文安裝精靈:把 Inno Setup 的非官方翻譯檔 `ChineseTraditional.isl` 放到 `installer\Languages\`。檔案存在時,安裝精靈在繁中 Windows 上會顯示繁體中文;不存在則只有英文。

## 安裝程式行為(installer/StandUp.iss)

- 每位使用者各自安裝到 `%LocalAppData%\Programs\Time-to-stand-up`,不需要系統管理員權限(已安裝舊版的使用者升級時沿用原本的資料夾)。
- 附加工作「開機時自動啟動」預設勾選,寫入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `StandUp` 值。這和程式內「開機時自動啟動」核取方塊(`Services/AutoStartManager.cs`)是同一個值,值名稱改了兩邊要一起改。
- 安裝或升級前會結束執行中的 StandUp,以便覆蓋 exe;完成頁可勾選立即啟動。
- 解除安裝會移除 Run 值與程式檔案,並詢問是否刪除 `%AppData%\StandUp` 設定檔(預設「否」)。
- `AppId` GUID 固定不變,升級才會覆蓋舊版而不是並存。

## 程式碼簽章

目前沒有簽章,下載者會看到 SmartScreen「Windows 已保護您的電腦」警告。若要消除,需購買程式碼簽章憑證或使用 Azure Trusted Signing,再於 `publish.ps1` 對 `publish\StandUp.exe` 與產出的 Setup.exe 執行 `signtool sign`。
