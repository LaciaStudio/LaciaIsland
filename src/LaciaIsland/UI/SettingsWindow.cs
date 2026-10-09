using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using LaciaIsland.Core;
using LaciaIsland.Model;
using LaciaIsland.Services;

namespace LaciaIsland.UI;

/// <summary>Settings window, built in code (no XAML) to keep the app a single small assembly.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private readonly ServiceHub _hub;
    private readonly ComboBox _monitor = new() { MinWidth = 190 };
    private bool _loading = true;

    private static readonly IBrush Card = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27));
    private static readonly IBrush Sub = new SolidColorBrush(Color.FromRgb(0x98, 0x98, 0x9F));

    public SettingsWindow(AppSettings settings, ServiceHub hub, Action quit)
    {
        _s = settings;
        _hub = hub;
        Title = "Lacia Island — настройки";
        Width = 480;
        Height = 720;
        MinWidth = 420;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18));
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LaciaIsland/Assets/app.ico"))); } catch { /* ignore */ }

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(20, 18, 20, 24) };

        root.Children.Add(new TextBlock { Text = "Lacia Island", FontSize = 26, FontWeight = FontWeight.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Dynamic Island и чёлка для Windows. Клик — раскрыть, правый клик — быстрая панель, колесо — громкость, Ctrl + колесо — длина, средняя кнопка — пауза.",
            TextWrapping = TextWrapping.Wrap, Foreground = Sub, FontSize = 13,
        });

        // ── look
        var modeBox = new ComboBox { ItemsSource = new[] { "Остров (Dynamic Island)", "Чёлка (Notch)" }, MinWidth = 190 };
        modeBox.SelectedIndex = _s.Mode == IslandMode.Island ? 0 : 1;
        modeBox.SelectionChanged += (_, _) => Set(() => _s.Mode = modeBox.SelectedIndex == 1 ? IslandMode.Notch : IslandMode.Island);

        var scaleLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right };
        var scale = new Slider { Minimum = 0.8, Maximum = 1.5, Value = _s.Scale, TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 150 };
        scaleLabel.Text = $"{_s.Scale * 100:0}%";
        scale.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            scaleLabel.Text = $"{scale.Value * 100:0}%";
            Set(() => _s.Scale = Math.Round(scale.Value, 2));
        };

        var lengthLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right };
        var length = new Slider
        {
            Minimum = AppSettings.MinLength, Maximum = AppSettings.MaxLength, Value = _s.Length,
            TickFrequency = 0.05, IsSnapToTickEnabled = true, Width = 150,
        };
        lengthLabel.Text = $"{_s.Length * 100:0}%";
        length.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            lengthLabel.Text = $"{length.Value * 100:0}%";
            Set(() => _s.Length = Math.Round(length.Value, 2));
        };
        // keep the slider in sync when the length is changed with Ctrl + wheel on the island
        _s.Changed += () =>
        {
            if (Math.Abs(length.Value - _s.Length) < 0.001) return;
            _loading = true;
            length.Value = _s.Length;
            lengthLabel.Text = $"{_s.Length * 100:0}%";
            _loading = false;
        };

        var gapLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right };
        var gap = new Slider { Minimum = 0, Maximum = 30, Value = _s.TopGap, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 150 };
        gapLabel.Text = $"{_s.TopGap:0} px";
        gap.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            gapLabel.Text = $"{gap.Value:0} px";
            Set(() => _s.TopGap = gap.Value);
        };

        _monitor.SelectionChanged += (_, _) => Set(() => _s.Monitor = _monitor.SelectedIndex <= 0 ? null : (_monitor.SelectedIndex - 1).ToString());

        root.Children.Add(Section("Вид",
            Row("Режим", null, modeBox),
            Row("Размер", null, Pair(scale, scaleLabel)),
            Row("Длина", "или Ctrl + колесо мыши над островом", Pair(length, lengthLabel)),
            Row("Отступ сверху", "только для режима «остров»", Pair(gap, gapLabel)),
            Row("Монитор", null, _monitor)));

        // ── behaviour
        var autostart = new ToggleSwitch { OnContent = "", OffContent = "", IsChecked = SafeAutostart() };
        autostart.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try { _hub.System.AutostartEnabled = autostart.IsChecked == true; }
            catch (Exception ex) { Log.Error("autostart", ex); }
        };
        root.Children.Add(Section("Поведение",
            Toggle("Раскрывать при наведении", "иначе — по клику", () => _s.ExpandOnHover, v => _s.ExpandOnHover = v),
            Toggle("Прятать, когда ничего не происходит", "чтобы показать, подведите мышь к верхнему краю экрана, где остров", () => _s.HideWhenIdle, v => _s.HideWhenIdle = v),
            Toggle("Прятать в полноэкранных приложениях", "игры, видео, презентации", () => _s.HideInFullscreen, v => _s.HideInFullscreen = v),
            Row("Запускать вместе с Windows", null, autostart)));

        // ── activities
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            var n = new NumericUpDown
            {
                Minimum = 1, Maximum = 240, Increment = 1, Value = _s.TimerPresets[i], Width = 104,
                FormatString = "0", ShowButtonSpinner = true,
            };
            n.ValueChanged += (_, _) => Set(() =>
            {
                var arr = (int[])_s.TimerPresets.Clone();
                arr[idx] = (int)(n.Value ?? arr[idx]);
                _s.TimerPresets = arr;
            });
            presets.Children.Add(n);
        }

        var ignored = new TextBox
        {
            Text = string.Join(", ", _s.IgnoredCallApps), Width = 210, Watermark = "через запятую",
        };
        ignored.LostFocus += (_, _) => Set(() => _s.IgnoredCallApps = (ignored.Text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        root.Children.Add(Section("Активности",
            Toggle("Музыка", "Spotify, Яндекс Музыка, браузер и всё, что видно в системном плеере", () => _s.ShowMusic, v => _s.ShowMusic = v),
            Toggle("Живой визуализатор", "полоски реагируют на настоящий звук", () => _s.RealVisualizer, v => _s.RealVisualizer = v),
            Toggle("Звонки", "когда приложение использует микрофон (Discord, Telegram, Zoom…)", () => _s.ShowCalls, v => _s.ShowCalls = v),
            Row("Не считать звонком", "программы, которые держат микрофон всегда", ignored),
            Toggle("Громкость", null, () => _s.ShowVolume, v => _s.ShowVolume = v),
            Toggle("Раскладка клавиатуры", null, () => _s.ShowLayout, v => _s.ShowLayout = v),
            Toggle("Зарядка", "только на ноутбуках", () => _s.ShowCharging, v => _s.ShowCharging = v),
            Toggle("Наушники и колонки", "при смене устройства вывода", () => _s.ShowDevices, v => _s.ShowDevices = v),
            Toggle("Звук таймера", null, () => _s.TimerSound, v => _s.TimerSound = v),
            Row("Быстрые таймеры, мин", null, presets)));

        // ── footer
        var logs = new Button { Content = "Папка с логами" };
        logs.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Log.Dir) { UseShellExecute = true }); } catch (Exception ex) { Log.Error("open logs", ex); }
        };
        var exit = new Button { Content = "Выйти из Lacia Island" };
        exit.Click += (_, _) => quit();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        footer.Children.Add(logs);
        footer.Children.Add(exit);
        root.Children.Add(footer);
        root.Children.Add(new TextBlock { Text = "Версия 1.0 · настройки сохраняются автоматически", Foreground = Sub, FontSize = 12 });

        Content = new ScrollViewer { Content = root };

        Opened += (_, _) =>
        {
            _loading = true;
            var items = new List<string> { "Основной" };
            // Same numbering the island uses when it picks a monitor.
            var all = ScreenOrder.Sort(Screens.All);
            for (var i = 0; i < all.Count; i++)
                items.Add($"Монитор {i + 1} — {all[i].Bounds.Width}×{all[i].Bounds.Height}" + (all[i].IsPrimary ? " (основной)" : ""));
            _monitor.ItemsSource = items;
            _monitor.SelectedIndex = int.TryParse(_s.Monitor, out var m) && m + 1 < items.Count ? m + 1 : 0;
            _loading = false;
        };
        _loading = false;
    }

    private bool SafeAutostart()
    {
        try { return _hub.System.AutostartEnabled; } catch { return false; }
    }

    private void Set(Action change)
    {
        if (_loading) return;
        change();
        _s.NotifyChanged();
    }

    private static Control Pair(Control a, Control b)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(a);
        p.Children.Add(b);
        return p;
    }

    private static Control Section(string title, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x37)), Margin = new Thickness(14, 0, 0, 0) });
            stack.Children.Add(rows[i]);
        }
        var outer = new StackPanel { Spacing = 6 };
        outer.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 12, Foreground = Sub, Margin = new Thickness(14, 4, 0, 0) });
        outer.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(14), Child = stack });
        return outer;
    }

    private static Control Row(string title, string? hint, Control control)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 10, 12, 10), MinHeight = 34 };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (hint is not null) texts.Children.Add(new TextBlock { Text = hint, FontSize = 12, Foreground = Sub, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(texts);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private Control Toggle(string title, string? hint, Func<bool> get, Action<bool> set)
    {
        var t = new ToggleSwitch { OnContent = "", OffContent = "", IsChecked = get() };
        t.IsCheckedChanged += (_, _) => Set(() => set(t.IsChecked == true));
        return Row(title, hint, t);
    }
}
