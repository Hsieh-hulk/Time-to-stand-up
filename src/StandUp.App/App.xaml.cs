using System;
using System.Threading;
using System.Windows;
using StandUp.Core;

namespace StandUp.App;

public partial class App : Application
{
    private const string AppTitle = "StandUp 站起來";

    /// <summary>單一執行個體用的具名 Mutex(同一個 Windows 登入工作階段內)。</summary>
    internal const string SingleInstanceMutexName = "StandUp.SingleInstance";

    private AppController? _controller;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(e.Args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(
                ex.Message + "\n\n用法:StandUp.exe [--work 分鐘] [--break 分鐘] [--idle 分鐘] [--config-dir 資料夾]",
                AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        // 開機自動啟動後,使用者又從開始功能表點一次時,不要跑出第二份(兩個系統匣、兩套休息畫面)。
        // 用 --config-dir 的測試執行不受限制,才能和已安裝的版本並存。
        if (options.ConfigDirectory is null && !TryAcquireSingleInstance())
        {
            MessageBox.Show(
                "StandUp 已經在執行中。\n請從工作列右下角系統匣的「站」圖示按右鍵開啟設定。",
                AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var store = new ConfigStore(options.ConfigDirectory ?? ConfigStore.DefaultDirectory);
        var loaded = store.Load();
        var config = loaded.Config;
        ThemeManager.Initialize(config.Theme);

        if (loaded.Status == ConfigLoadStatus.Corrupt)
        {
            MessageBox.Show(
                $"設定檔無法讀取,已改用預設值。\n原檔已保留為 {store.BackupPath}",
                AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (!config.FirstRunCompleted)
        {
            // 計時在這個畫面關閉之後才開始:AppController 還沒建立。
            var firstRun = new FirstRunWindow(config);
            if (firstRun.ShowDialog() != true || firstRun.Result is null)
            {
                // 按右上角 X 視為取消啟動,firstRunCompleted 維持 false。
                Shutdown();
                return;
            }

            config = firstRun.Result;
            if (!store.Save(config))
            {
                MessageBox.Show(
                    $"無法寫入設定檔({store.ConfigPath}),這次的選擇不會被記住。",
                    AppTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        _controller = new AppController(options.ResolveSettings(config), config, store, options);
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _singleInstance = mutex;
        return true;
    }
}
