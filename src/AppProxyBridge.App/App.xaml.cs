using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AppProxyBridge.App;

public partial class App : Application
{
    Mutex? mutex;
    EventWaitHandle? wake;
    DispatcherTimer? timer;
    Forms.NotifyIcon? tray;
    public bool Exiting { get; private set; }
    MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var instance = "";
        int instanceIndex = Array.IndexOf(e.Args, "--instance-id");
        if (instanceIndex >= 0) {
            if (instanceIndex + 1 >= e.Args.Length || !Guid.TryParse(e.Args[instanceIndex + 1], out var id)) { Shutdown(1); return; }
            instance = "." + id.ToString("N");
        }
        mutex = new Mutex(true, "Local\\AppProxyBridge.Next.Instance" + instance, out bool first);
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AppProxyBridge.Next.Wake" + instance);
        if (!first) { wake.Set(); Shutdown(); return; }
        try {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppProxyBridge.Next");
            int index = Array.IndexOf(e.Args, "--data-dir");
            if (index >= 0 && index + 1 < e.Args.Length) dir = Path.GetFullPath(e.Args[index + 1]);
            window = new MainWindow(dir); MainWindow = window;
            SetTheme(window.Theme); window.Show();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => { if (wake.WaitOne(0)) ShowWindow(); };
            timer.Start();
            tray = new Forms.NotifyIcon { Text = "App Proxy Bridge", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开 App Proxy Bridge", null, (_, _) => Dispatcher.Invoke(ShowWindow));
            menu.Items.Add("停止所有代理", null, (_, _) => Dispatcher.Invoke(() => window.StopAll()));
            menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApp));
            tray.ContextMenuStrip = menu;
            SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
        } catch (Exception ex) { MessageBox.Show(ex.Message, "App Proxy Bridge 启动失败"); Shutdown(1); }
    }
    void OnSystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.InvokeAsync(() => { if (window != null) SetTheme(window.Theme); });
    void ShowWindow() { if (window == null) return; window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }
    public void ExitApp() { if (window != null && !window.PrepareExit()) return; Exiting = true; Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
        timer?.Stop(); tray?.Dispose(); window?.Dispose(); wake?.Dispose(); mutex?.Dispose(); base.OnExit(e);
    }
    public void SetTheme(string choice)
    {
        bool dark = choice == "Dark" || choice == "System" && (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) == 0;
        string[] colors = dark ? ["#191A1D", "#212225", "#28292D", "#F1F2F4", "#B5B9C0", "#44474D", "#85AAD8"] : ["#FFFFFF", "#F4F5F7", "#F8F9FA", "#202328", "#59616C", "#DDE0E4", "#4777B5"];
        string[] keys = ["BackgroundBrush", "SidebarBrush", "CardBrush", "TextBrush", "MutedBrush", "LineBrush", "AccentBrush"];
        for (int i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        void Set(string key, string color) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Set("SuccessBrush", dark ? "#62D98C" : "#17733A"); Set("WarningBrush", dark ? "#B5B9C0" : "#59616C");
        Set("PrimaryBrush", dark ? "#E6E8EC" : "#202328"); Set("PrimaryTextBrush", dark ? "#202328" : "#FFFFFF");
        Set("FocusBrush", dark ? "#AAB3C0" : "#77808C"); Set("PrimaryHoverBrush", dark ? "#CBD0D7" : "#3A3E45");
        Set("InputBrush", dark ? "#222428" : "#FFFFFF"); Set("SelectionBrush", dark ? "#3A3D43" : "#E7E9EC");
        if (SystemParameters.HighContrast) {
            foreach (string key in new[] { "BackgroundBrush", "SidebarBrush", "CardBrush", "InputBrush" }) Resources[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "TextBrush", "MutedBrush", "LineBrush", "FocusBrush", "SuccessBrush", "WarningBrush" }) Resources[key] = SystemColors.WindowTextBrush;
            Resources["AccentBrush"] = SystemColors.HighlightBrush; Resources["PrimaryBrush"] = SystemColors.HighlightBrush;
            Resources["PrimaryHoverBrush"] = SystemColors.HighlightBrush; Resources["PrimaryTextBrush"] = SystemColors.HighlightTextBrush; Resources["SelectionBrush"] = SystemColors.ControlBrush;
        }
        window?.ApplyTitleBarTheme(dark && !SystemParameters.HighContrast);
    }
}

