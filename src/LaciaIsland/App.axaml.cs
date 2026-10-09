using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.Services;
using LaciaIsland.UI;

namespace LaciaIsland;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static ServiceHub? Hub { get; private set; }
    public static IslandController? Controller { get; private set; }
    private IIslandHost? _host;
    private SettingsWindow? _settingsWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var args = desktop.Args ?? Array.Empty<string>();
            var demo = args.Contains("--demo") || !OperatingSystem.IsWindows();

            Settings = AppSettings.Load();
            Hub = demo ? DemoHub.Create() : PlatformFactory.CreateServices();
            Controller = new IslandController(Settings, Hub);
            _host = PlatformFactory.CreateHost(Controller, Program.UseAvaloniaWindow);
            Controller.SettingsRequested += ShowSettings;
            _host.Start();

            if (demo) new DemoDirector(Hub, Controller).Start();

            SetupTray();
            desktop.Exit += (_, _) =>
            {
                try { _host?.Dispose(); } catch { /* ignore */ }
                try { Hub?.Dispose(); } catch { /* ignore */ }
                Settings.Save();
            };
            Log.Info($"started (demo={demo}, mode={Settings.Mode})");
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray()
    {
        try
        {
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LaciaIsland/Assets/tray.ico"))),
                ToolTipText = "Lacia Island",
            };
            var menu = new NativeMenu();

            var settings = new NativeMenuItem("Настройки…");
            settings.Click += (_, _) => ShowSettings();
            menu.Items.Add(settings);

            var mode = new NativeMenuItem("Режим: остров ↔ чёлка");
            mode.Click += (_, _) => Controller?.ToggleMode();
            menu.Items.Add(mode);

            var timers = new NativeMenu();
            foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
            {
                var item = new NativeMenuItem($"{m} мин");
                var minutes = m;
                item.Click += (_, _) => Controller?.StartTimer(TimeSpan.FromMinutes(minutes));
                timers.Items.Add(item);
            }
            timers.Items.Add(new NativeMenuItemSeparator());
            var sw = new NativeMenuItem("Секундомер");
            sw.Click += (_, _) => Controller?.StartStopwatch();
            timers.Items.Add(sw);
            var stop = new NativeMenuItem("Остановить");
            stop.Click += (_, _) => Hub?.Timer.Cancel();
            timers.Items.Add(stop);
            menu.Items.Add(new NativeMenuItem("Таймер") { Menu = timers });

            menu.Items.Add(new NativeMenuItemSeparator());
            var quit = new NativeMenuItem("Выход");
            quit.Click += (_, _) => _desktop?.Shutdown();
            menu.Items.Add(quit);

            tray.Menu = menu;
            tray.Clicked += (_, _) => ShowSettings();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
        }
        catch (Exception ex) { Log.Error("tray", ex); }
    }

    private void ShowSettings()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Controller is null || Hub is null) return;
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(Settings, Hub, () => _desktop?.Shutdown());
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }
}
