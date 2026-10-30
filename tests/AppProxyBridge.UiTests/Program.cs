using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net.Sockets;
using System.Threading;
using AppProxyBridge.App;
using AppProxyBridge.Core;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "AppProxyBridgeNext-ui-" + Guid.NewGuid().ToString("N"));
        var artifacts = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "ui"));
        Directory.CreateDirectory(artifacts);
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow(root);
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
        T Field<T>(string name) where T : class => (T)(window.FindName(name) ?? throw new Exception("Missing control: " + name));
        void Call(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [window, new RoutedEventArgs()]);
        void Snapshot(string name, MainWindow? target = null)
        {
            var content = (FrameworkElement)(target ?? window).Content;
            content.InvalidateMeasure(); content.Measure(new Size(1000, 680)); content.Arrange(new Rect(0, 0, 1000, 680)); content.UpdateLayout();
            var image = new RenderTargetBitmap(1000, 680, 96, 96, PixelFormats.Pbgra32); image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(artifacts, name + ".png")); encoder.Save(file);
        }
        try {
            app.SetTheme("Light"); Snapshot("dashboard-empty-light");
            Check(Field<DashboardView>("DashboardPage").Visibility == Visibility.Visible, "看板为默认首页");
            var emptyView = Field<DashboardView>("DashboardPage");
            ((Canvas)emptyView.FindName("TopologyCanvas")).Children.OfType<Button>().First(b => (string?)b.Content == "前往软件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<Grid>("ApplicationsPage").Visibility == Visibility.Visible, "空看板可跳转添加软件");
            Call("ShowDashboard");
            ((Canvas)emptyView.FindName("TopologyCanvas")).Children.OfType<Button>().First(b => (string?)b.Content == "前往代理配置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<Grid>("ProfilesPage").Visibility == Visibility.Visible, "空看板可跳转添加配置");
            Call("ShowApps"); Snapshot("apps-empty-light");
            Call("AddProfile"); Field<TextBox>("ProfileName").Text = "示例代理";
            Check(Field<TextBlock>("TestResult").Text.Contains("尚未测试"), "未经测试的配置修改提示尚未测试");
            Field<TextBox>("Host").Text = "127.0.0.2"; Field<TextBox>("Host").Text = "127.0.0.1";
            Check(Field<TextBlock>("TestResult").Text.Contains("尚未测试"), "连续修改未经测试的配置不误提示重新测试");
            Check(!File.Exists(Path.Combine(root, "profiles.json")), "未保存的新配置不会写入磁盘");
            Call("SaveProfile");
            var config = new ConfigurationStore(root).Load();
            Check(config.Profiles.Count == 1 && config.Profiles[0].Name == "示例代理", "界面保存代理配置");
            Call("ShowProfiles"); Snapshot("profile-light");
            var profileListHeading = Field<TextBlock>("ProfileListHeading"); var profileDetailHeading = Field<TextBlock>("ProfileDetailHeading");
            Check(profileListHeading.FontSize == profileDetailHeading.FontSize && profileListHeading.FontWeight == profileDetailHeading.FontWeight && Math.Abs(profileListHeading.TranslatePoint(new Point(), (UIElement)window.Content).Y - profileDetailHeading.TranslatePoint(new Point(), (UIElement)window.Content).Y) < 0.1, "代理配置列表和详情标题的字号字重与上沿一致");
            Check(Math.Abs(Field<Button>("ProfileSaveButton").TranslatePoint(new Point(), (UIElement)window.Content).Y - Field<Button>("ProfileTestButton").TranslatePoint(new Point(), (UIElement)window.Content).Y) < 0.1, "最小内容区代理配置与连接操作同排对齐");
            var profileItem = (ListBoxItem)Field<ListBox>("ProfileList").ItemContainerGenerator.ContainerFromIndex(0);
            Check(System.Windows.Automation.AutomationProperties.GetName(profileItem) == "示例代理", "代理列表无障碍名称只暴露配置名称");
            app.SetTheme("Dark"); Snapshot("profile-dark");
            app.SetTheme("Light"); Field<CheckBox>("Authentication").IsChecked = true; Field<TextBox>("Username").Text = "test-user"; Field<PasswordBox>("Password").Password = "test-password";
            Call("SaveProfile");
            var restored = new MainWindow(root);
            try {
                ((ListBox)restored.FindName("ProfileList")).SelectedIndex = 0;
                typeof(MainWindow).GetMethod("ShowProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(restored, [restored, new RoutedEventArgs()]);
                Snapshot("profile-auth-light", restored);
                Check(((PasswordBox)restored.FindName("Password")).Password == "test-password", "认证配置与凭据重开后恢复");
            } finally { restored.Dispose(); }
            Call("ShowApps"); Call("AddApplication");
            Field<TextBox>("AppName").Text = "示例工具"; Field<TextBox>("AppPath").Text = Environment.ProcessPath!;
            Field<CheckBox>("Independent").IsChecked = true;
            Field<ComboBox>("IndependentProfile").SelectedIndex = 0;
            Call("SaveApplication");
            config = new ConfigurationStore(root).Load();
            Check(config.Applications.Count == 1 && config.Applications[0].UseIndependentProxy && config.Applications[0].IndependentProfileId == config.Profiles[0].Id, "界面保存独立代理绑定");
            app.SetTheme("Light"); Snapshot("app-independent-light");
            var appListHeading = Field<TextBlock>("AppListHeading"); var appDetailHeading = Field<TextBlock>("AppDetailHeading");
            Check(appListHeading.FontSize == appDetailHeading.FontSize && appListHeading.FontWeight == appDetailHeading.FontWeight && Math.Abs(appListHeading.TranslatePoint(new Point(), (UIElement)window.Content).Y - appDetailHeading.TranslatePoint(new Point(), (UIElement)window.Content).Y) < 0.1, "软件列表和详情标题的字号字重与上沿一致");
            Check(Math.Abs(Field<Button>("AppSaveButton").TranslatePoint(new Point(), (UIElement)window.Content).Y - Field<Button>("LaunchButton").TranslatePoint(new Point(), (UIElement)window.Content).Y) < 0.1 && Math.Abs(Field<Button>("LaunchButton").TranslatePoint(new Point(), (UIElement)window.Content).Y - Field<Button>("StopIndependentButton").TranslatePoint(new Point(), (UIElement)window.Content).Y) < 0.1, "最小内容区软件操作同排对齐");
            var appItem = (ListBoxItem)Field<ListBox>("AppList").ItemContainerGenerator.ContainerFromIndex(0);
            Check(System.Windows.Automation.AutomationProperties.GetName(appItem) == "示例工具", "软件列表无障碍名称只暴露软件名称");
            app.SetTheme("Dark"); Snapshot("app-independent-dark");
            app.SetTheme("Light"); Call("ShowSettings"); Snapshot("settings-light"); app.SetTheme("Dark"); Snapshot("settings-dark");
            var secondProfile = new ProxyProfile { Name = "办公代理", Revision = 1 };
            config.Profiles.Add(secondProfile); config.Settings.SharedProfileId = config.Profiles[0].Id;
            config.Applications.Add(new ApplicationEntry { Name = "聊天工具", Components = [new() { Path = Environment.ProcessPath! }] });
            config.Applications.Add(new ApplicationEntry { Name = "开发工具", UseIndependentProxy = true, IndependentProfileId = secondProfile.Id, Components = [new() { Path = Environment.ProcessPath! }] });
            new ConfigurationStore(root).Save(config);
            var graph = new MainWindow(root);
            try {
                var manager = (BridgeManager)typeof(MainWindow).GetField("bridges", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
                manager.ApplyShared(config.Profiles[0], "test-password", 0);
                manager.ApplyIndependent(config.Applications[0], config.Profiles[0], "test-password");
                manager.ApplyIndependent(config.Applications[2], secondProfile, "");
                using var pending = new TcpClient(); pending.Connect("127.0.0.1", manager.Shared.Status.Port);
                for (int i = 0; i < 100 && manager.Shared.Status.Connections == 0; i++) Thread.Sleep(20);
                manager.RecordFailure(null, BridgeFailureKind.Probe);
                typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null);
                var snapshot = (DashboardSnapshot)typeof(MainWindow).GetMethod("BuildDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null)!;
                Check(snapshot.Nodes.Count(n => n.Kind == DashboardKind.Application) == 3 && snapshot.Nodes.Count(n => n.Kind == DashboardKind.Entry) == 3 && snapshot.Nodes.Count(n => n.Kind == DashboardKind.Profile) == 2, "看板完整展示软件、共享及独立入口与代理配置");
                Check(snapshot.Links.Count(l => l.Active) == 3 && snapshot.Links.Count(l => !l.Active) == 3 && snapshot.Summary.Contains("入口连接 1"), "实际上游与软件配置关系分离，计数包含建立中连接");
                var view = (DashboardView)graph.FindName("DashboardPage");
                var canvas = (Canvas)view.FindName("TopologyCanvas");
                var entryButton = canvas.Children.OfType<Button>().First(b => (string)b.Tag == "entry:shared");
                typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null);
                Check(ReferenceEquals(entryButton, canvas.Children.OfType<Button>().First(b => (string)b.Tag == "entry:shared")), "仅状态刷新时保留节点，避免丢失焦点");
                entryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBlock)view.FindName("DetailText")).Text.Contains("上游测试未通过"), "入口详情展示本次运行的最近错误");
                app.SetTheme("Light"); Snapshot("dashboard-multiple-light", graph); app.SetTheme("Dark"); Snapshot("dashboard-multiple-dark", graph);
                var appButton = canvas.Children.OfType<Button>().First(b => (string)b.Tag == "app:" + config.Applications[1].Id.ToString("N"));
                appButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBlock)view.FindName("DetailText")).Text.Contains("无法确认"), "软件节点说明不能确认全部流量归属");
                ((Button)view.FindName("JumpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((Grid)graph.FindName("ApplicationsPage")).Visibility == Visibility.Visible && ((ListBox)graph.FindName("AppList")).SelectedItem is ApplicationEntry chosen && chosen.Id == config.Applications[1].Id, "看板节点跳转到对应软件");
                var graphConfig = (Configuration)typeof(MainWindow).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
                graphConfig.Profiles[0] = graphConfig.Profiles[0] with { Revision = graphConfig.Profiles[0].Revision + 1 };
                snapshot = (DashboardSnapshot)typeof(MainWindow).GetMethod("BuildDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null)!;
                Check(snapshot.Nodes.Single(n => n.Key == "entry:shared").Status.Contains("待应用") && snapshot.Links.Any(l => l.From == "entry:shared" && l.Active), "已保存新版本不冒充已生效版本");
                manager.Shared.Stop();
                typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null);
                Check(((Button)graph.FindName("StopAllButton")).IsEnabled && ((TextBlock)graph.FindName("BridgeDetail")).Text.Contains("独立入口运行中 2 个"), "仅独立入口运行时仍可停止全部并显示入口数量");
                manager.StopAll();
                typeof(MainWindow).GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null);
                Check(!((Button)graph.FindName("StopAllButton")).IsEnabled, "没有运行入口时停止全部不可用");
                snapshot = (DashboardSnapshot)typeof(MainWindow).GetMethod("BuildDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, null)!;
                Check(snapshot.Links.All(l => !l.Active) && snapshot.Nodes.Single(n => n.Key == "entry:shared").Detail.Contains("上游测试未通过"), "停止后不再显示生效连线且保留本次错误");
                for (int i = 0; i < 25; i++) graphConfig.Applications.Add(new ApplicationEntry { Name = "模拟软件 " + (i + 1) });
                typeof(MainWindow).GetMethod("ShowDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(graph, [graph, new RoutedEventArgs()]);
                Check(canvas.Height > 2000 && canvas.Children.OfType<Button>().Count(b => ((string?)b.Tag)?.StartsWith("app:") == true) == 28, "大量软件仍可在可滚动拓扑中完整展示");
                var sharedApp = canvas.Children.OfType<Button>().First(b => (string?)b.Tag == "app:" + config.Applications[1].Id.ToString("N"));
                sharedApp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(canvas.Children.OfType<System.Windows.Shapes.Path>().Any(p => p.Visibility == Visibility.Visible && p.StrokeDashArray?.Count > 0 && p.StrokeThickness > 2), "选择共享软件高亮其配置关系并保留虚线语义");
                app.SetTheme("Light"); Snapshot("dashboard-many-light", graph);
            } finally { graph.Dispose(); }
            Console.WriteLine($"RESULT: {passed} UI checks passed; screenshots: {artifacts}");
        } finally { window.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

