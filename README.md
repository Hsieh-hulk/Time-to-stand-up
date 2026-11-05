# StandUp 站起來

Windows 上的強制休息提醒工具。連續工作到設定的時間後,以全螢幕置頂畫面強制休息,倒數結束後按下「解鎖」才能繼續工作。

- 預設「工作 30 分鐘 / 休息 5 分鐘」,也可選 60/10 或自訂(工作 1–180 分鐘、休息 1–30 分鐘)。
- 開機自動啟動,常駐在工作列右下角系統匣(橘色「站」圖示)。
- 智慧暫停:電腦鎖定或睡眠時不計入工作時間(不偵測鍵盤滑鼠閒置,看影片、看直播不會被誤判為休息);離開的時間達到休息時長,就當作已經休息過,回來重新計時。
- 多螢幕會全部蓋住;支援深色 / 淺色主題。

## 安裝(一般使用者)

1. 下載 `Time-to-stand-up-Setup-<版本>.exe`。
2. 執行後一路按「下一步」即可。不需要系統管理員權限,也不需要另外安裝 .NET。
   - 「開機時自動啟動」預設已勾選。
3. 第一次啟動會出現歡迎畫面,選好提醒節奏後按「開始使用」。

之後要調整:在系統匣「站」圖示按右鍵 →「設定…」。

**Windows 顯示「Windows 已保護您的電腦」?** 這是因為安裝檔沒有付費的程式碼簽章,不代表有病毒。點「其他資訊」→「仍要執行」即可。

**解除安裝:** Windows 設定 → 應用程式 → 已安裝的應用程式 → StandUp 站起來 → 解除安裝。過程中會詢問是否一併刪除設定檔。

設定檔位置:`%AppData%\StandUp\config.json`。

> 本工具不是醫療建議;預設節奏參考的研究與限制,請見程式內「設定 → 研究依據與關於」。

## 開發

需要 .NET SDK 8 以上(已用 SDK 10 驗證)。

```bash
dotnet run --project src/StandUp.App
dotnet test tests/StandUp.Tests
```

測試時縮短時間(單位:分鐘,可為小數,不寫回設定檔),並改用暫存設定資料夾以免動到真正的設定:

```bash
dotnet run --project src/StandUp.App -- --config-dir C:\temp\standup-test --work 0.5 --break 0.3
```

打包安裝程式(需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

產出 `dist\Time-to-stand-up-Setup-<版本>.exe`。細節見 [docs/RELEASING.md](docs/RELEASING.md)。

### 專案結構

```
src/StandUp.Core/        與 UI 無關的邏輯(可單元測試)
  Breaks/                計時狀態機、沙漏模型、時鐘介面
  Config/                設定檔讀寫、命令列參數、輸入驗證
  Presence/              離開偵測(鎖定 / 睡眠)後的計時調整
  Content/               研究依據等顯示文字
src/StandUp.App/         WPF 應用程式
  App.xaml(.cs)          進入點:單一執行個體、讀設定、首次啟動
  AppController.cs       把狀態機接到 UI 與系統匣
  Views/                 視窗(設定、首次啟動、休息畫面、休息前提示)
  Controls/              自訂控制項(預設選項、研究說明、沙漏)
  Services/              開機自動啟動、系統事件監聽
  Interop/               Win32 API
  Theming/               主題切換與樣式(Themes/*.xaml)
  Assets/                字型、圖示
tests/StandUp.Tests/     xUnit 測試,資料夾對應 Core
installer/StandUp.iss    Inno Setup 安裝程式腳本
build/                   publish.ps1(打包)、make-icon.ps1(產生圖示)
docs/                    測試步驟、已知問題、待辦、發版流程
```

其他文件:[手動測試與已知問題](docs/TESTING.md) · [待辦清單](docs/ROADMAP.md)

## 授權

程式碼採 MIT 授權(見 [LICENSE](LICENSE))。內嵌字型 jf open 粉圓的授權見 `src/StandUp.App/Assets/Fonts/LICENSE.txt`。
