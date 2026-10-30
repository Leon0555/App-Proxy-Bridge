using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using AppProxyBridge.Core;
using Microsoft.Win32;

namespace AppProxyBridge.App;

public partial class MainWindow : Window, IDisposable
{
    readonly ConfigurationStore store;
    readonly Configuration config;
    readonly BridgeManager bridges = new();
    readonly Dictionary<Guid, Process> launched = [];
    readonly Dictionary<Guid, (long Revision, DateTimeOffset Time, bool Success)> probeResults = [];
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    ProxyProfile? selectedProfile;
    ApplicationEntry? selectedApp;
    bool loading = true, profileDirty, appDirty, busy, currentInputTested;
    // Optional presenters let isolated UI tests exercise the same decisions without modal dialogs.
    Func<string, string, MessageBoxResult>? confirmUnsaved = null;
    Action<string>? presentError = null;
    public string Theme => config.Settings.Theme;

    public MainWindow(string directory)
    {
        store = new ConfigurationStore(directory); config = store.Load();
        InitializeComponent();
        SourceInitialized += (_, _) => ((App)Application.Current).SetTheme(Theme);
        DashboardPage.NavigateRequested += NavigateFromDashboard;
        bridges.Error += message => Dispatcher.InvokeAsync(() => MessageText.Text = message);
        RefreshLists();
        ThemeChoice.SelectedIndex = Theme == "Light" ? 1 : Theme == "Dark" ? 2 : 0;
        SharedPort.Text = config.Settings.SharedPort.ToString();
        loading = false; ShowPage(DashboardPage); statusTimer.Tick += (_, _) => UpdateStatus(); statusTimer.Start();
    }

    void RefreshLists()
    {
        Guid? editingIndependentProfile = appDirty ? (IndependentProfile.SelectedItem as ProxyProfile)?.Id : selectedApp?.IndependentProfileId;
        bool old = loading; loading = true;
        AppList.ItemsSource = null; AppList.ItemsSource = selectedApp != null && !config.Applications.Any(a => a.Id == selectedApp.Id) ? config.Applications.Append(selectedApp).ToList() : config.Applications; AppList.SelectedItem = selectedApp;
        ProfileList.ItemsSource = null; ProfileList.ItemsSource = selectedProfile != null && !config.Profiles.Any(p => p.Id == selectedProfile.Id) ? config.Profiles.Append(selectedProfile).ToList() : config.Profiles; ProfileList.SelectedItem = selectedProfile;
        IndependentProfile.ItemsSource = null; IndependentProfile.ItemsSource = config.Profiles;
        IndependentProfile.SelectedItem = config.Profiles.FirstOrDefault(p => p.Id == editingIndependentProfile);
        loading = old;
    }
    void UpdateStatus()
    {
        var status = bridges.Shared.Status;
        BridgeState.Text = status.Running ? "共享桥接运行中" : status.Faulted ? "共享桥接监听异常" : "共享桥接未运行";
        BridgeDetail.Text = status.Running ? $"{status.ActiveName} · 127.0.0.1:{status.Port} · {status.Connections} 个入口连接" : status.Faulted ? "入口已停止，请重新应用代理配置。" : "应用一个已保存的配置以启动共享桥接";
        int independentRunning = config.Applications.Count(a => bridges.IndependentStatus(a.Id)?.Running == true);
        if (independentRunning > 0) BridgeDetail.Text += $" · 独立入口运行中 {independentRunning} 个";
        StopAllButton.IsEnabled = status.Running || independentRunning > 0;
        AppEmpty.Text = config.Applications.Count == 0 ? "添加一个软件，选择可执行文件及它支持的代理方式。" : "选择左侧软件查看详情，或添加软件。";
        ProfileEmpty.Text = config.Profiles.Count == 0 ? "新增代理配置，填写你的代理服务器信息。" : "选择左侧代理配置查看详情，或新增配置。";
        if (selectedProfile != null) {
            var active = bridges.SharedApplied;
            string state = active?.ProfileId == selectedProfile.Id ? active.Value.Revision == selectedProfile.Revision ? "共享代理正在使用" : "已保存，待应用" : "已保存";
            ProfileState.Text = profileDirty ? "未保存" : state;
        } else ProfileState.Text = "";
        AppDirtyLabel.Visibility = appDirty ? Visibility.Visible : Visibility.Collapsed;
        if (selectedApp != null) {
            var state = bridges.IndependentStatus(selectedApp.Id);
            var active = bridges.IndependentApplied(selectedApp.Id);
            var configured = config.Profiles.FirstOrDefault(p => p.Id == selectedApp.IndependentProfileId);
            ApplyIndependentButton.IsEnabled = Independent.IsChecked == true;
            StopIndependentButton.IsEnabled = state?.Running == true;
            IndependentState.Text = state?.Running == true ? $"独立入口运行中 · 127.0.0.1:{state.Port} · {state.ActiveName}" + (active?.ProfileId != configured?.Id || active?.Revision != configured?.Revision || !selectedApp.UseIndependentProxy ? " · 更改待应用" : "") : selectedApp.UseIndependentProxy ? "独立入口未运行" : "使用共享入口";
            if (launched.TryGetValue(selectedApp.Id, out var process)) {
                try { AppProcessState.Text = process.HasExited ? "本工具启动的进程已退出；软件可能仍有其他进程。" : "本工具启动的进程运行中；代理使用情况未知。"; }
                catch (InvalidOperationException) { AppProcessState.Text = "进程状态未知。"; }
            } else AppProcessState.Text = "尚未通过本工具启动；代理使用情况未知。";
        }
        if (DashboardPage.Visibility == Visibility.Visible) DashboardPage.Update(BuildDashboard());
    }
    void SetBusy(bool value) { busy = value; ContentArea.IsEnabled = !value; }
    void ShowError(Exception ex, string? nextStep = null) {
        string message = ex.Message + (nextStep == null ? "" : "\n\n" + nextStep);
        MessageText.Text = message;
        if (presentError != null) presentError(message);
        else MessageBox.Show(this, message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    MessageBoxResult AskUnsaved(string message, string title) => confirmUnsaved != null
        ? confirmUnsaved(message, title)
        : MessageBox.Show(this, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
    void ShowDashboard(object sender, RoutedEventArgs e) => ShowPage(DashboardPage);
    void ShowApps(object sender, RoutedEventArgs e) => ShowPage(ApplicationsPage);
    void ShowProfiles(object sender, RoutedEventArgs e) => ShowPage(ProfilesPage);
    void ShowSettings(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    void ShowPage(UIElement page)
    {
        DashboardPage.Visibility = page == DashboardPage ? Visibility.Visible : Visibility.Collapsed;
        ApplicationsPage.Visibility = page == ApplicationsPage ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = page == ProfilesPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in new[] { (DashboardNav, (UIElement)DashboardPage), (AppsNav, ApplicationsPage), (ProfilesNav, ProfilesPage), (SettingsNav, SettingsPage) })
            item.Item1.SetResourceReference(Control.BackgroundProperty, page == item.Item2 ? "SelectionBrush" : "SidebarBrush");
        if (page == DashboardPage) UpdateStatus();
    }

    void MarkProfileDirty(object sender, RoutedEventArgs e)
    {
        if (loading || selectedProfile == null) return;
        profileDirty = true; TestResult.Text = currentInputTested || probeResults.ContainsKey(selectedProfile.Id) ? "配置已修改，请重新测试。" : "配置已修改，尚未测试。"; UpdateStatus();
    }
    void MarkAppDirty(object sender, RoutedEventArgs e) { if (loading || selectedApp == null) return; appDirty = true; UpdateStatus(); }
    void AuthenticationChanged(object sender, RoutedEventArgs e) { if (AuthFields != null) AuthFields.Visibility = Authentication.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; MarkProfileDirty(sender, e); }
    void IndependentChanged(object sender, RoutedEventArgs e) { if (IndependentFields != null) IndependentFields.Visibility = Independent.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; MarkAppDirty(sender, e); }
    void LoadProfile(ProxyProfile? profile, string? passwordOverride = null)
    {
        loading = true; selectedProfile = profile; profileDirty = false; currentInputTested = false; TestResult.Text = "";
        ProfileEditor.Visibility = profile == null ? Visibility.Collapsed : Visibility.Visible; ProfileEmpty.Visibility = profile == null ? Visibility.Visible : Visibility.Collapsed;
        ProfileActions.Visibility = profile == null ? Visibility.Collapsed : Visibility.Visible;
        if (profile != null) {
            ProfileName.Text = profile.Name; Protocol.SelectedIndex = (int)profile.Protocol; Host.Text = profile.Host; Port.Text = profile.Port.ToString(); TestUrl.Text = profile.TestUrl;
            Authentication.IsChecked = profile.Authentication; AuthFields.Visibility = profile.Authentication ? Visibility.Visible : Visibility.Collapsed; Username.Text = profile.Username;
            try { Password.Password = passwordOverride ?? store.ReadPassword(profile); } catch (Exception ex) { Password.Password = ""; MessageText.Text = "凭据无法读取，请重新填写：" + ex.Message; }
        }
        loading = false; UpdateStatus();
    }
    void LoadApp(ApplicationEntry? app)
    {
        loading = true; selectedApp = app; appDirty = false;
        AppEditor.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible; AppEmpty.Visibility = app == null ? Visibility.Visible : Visibility.Collapsed;
        AppActions.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible;
        if (app != null) {
            AppName.Text = app.Name; AppPath.Text = app.Components.FirstOrDefault()?.Path ?? ""; AppDirectory.Text = app.WorkingDirectory;
            AppArguments.Text = string.Join(Environment.NewLine, app.Arguments); LaunchMode.SelectedIndex = (int)app.LaunchMethod;
            Independent.IsChecked = app.UseIndependentProxy; IndependentFields.Visibility = app.UseIndependentProxy ? Visibility.Visible : Visibility.Collapsed;
            IndependentProfile.SelectedItem = config.Profiles.FirstOrDefault(p => p.Id == app.IndependentProfileId);
        }
        loading = false; UpdateStatus();
    }
    bool ProtectProfile(bool forExit = false)
    {
        if (!profileDirty) return true;
        if (forExit) ShowPage(ProfilesPage);
        string continuation = forExit ? "继续退出" : "继续当前操作";
        var answer = AskUnsaved($"代理配置尚未保存。\n\n是：保存后{continuation}\n否：放弃未保存的修改后{continuation}\n取消：返回并继续编辑", "未保存的代理配置");
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) { LoadProfile(config.Profiles.FirstOrDefault(p => p.Id == selectedProfile?.Id)); RefreshLists(); return true; }
        return SaveProfileCore(forExit);
    }
    bool ProtectApp(bool forExit = false)
    {
        if (!appDirty) return true;
        if (forExit) ShowPage(ApplicationsPage);
        string continuation = forExit ? "继续退出" : "继续当前操作";
        var answer = AskUnsaved($"软件配置尚未保存。\n\n是：保存后{continuation}\n否：放弃未保存的修改后{continuation}\n取消：返回并继续编辑", "未保存的软件配置");
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) { LoadApp(config.Applications.FirstOrDefault(a => a.Id == selectedApp?.Id)); RefreshLists(); return true; }
        return SaveAppCore(forExit);
    }
    void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        var next = ProfileList.SelectedItem as ProxyProfile;
        if (next == selectedProfile) return;
        if (!ProtectProfile()) { loading = true; ProfileList.SelectedItem = selectedProfile; loading = false; return; }
        LoadProfile(next); RefreshLists();
    }
    void AppSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        var next = AppList.SelectedItem as ApplicationEntry;
        if (next == selectedApp) return;
        if (!ProtectApp()) { loading = true; AppList.SelectedItem = selectedApp; loading = false; return; }
        LoadApp(next); RefreshLists();
    }
    void AddProfile(object sender, RoutedEventArgs e)
    {
        if (!ProtectProfile()) return;
        var profile = new ProxyProfile(); LoadProfile(profile); RefreshLists(); profileDirty = true; UpdateStatus();
    }
    void CopyProfile(object sender, RoutedEventArgs e)
    {
        if (selectedProfile == null) return;
        try {
            if (!ProtectProfile() || selectedProfile == null) return;
            var source = ReadProfile(); string password = Password.Password;
            var copy = source with { Id = Guid.NewGuid(), Revision = 0, Name = source.Name + " 副本", CredentialReference = "" };
            LoadProfile(copy, password); RefreshLists(); profileDirty = true; UpdateStatus();
            MessageText.Text = "副本尚未保存。";
        } catch (Exception ex) { ShowError(ex); }
    }
    ProxyProfile ReadProfile()
    {
        if (selectedProfile == null || Protocol.SelectedIndex < 0 || !int.TryParse(Port.Text, out int port)) throw new ArgumentException("请检查代理协议和端口。");
        var profile = selectedProfile with { Name = ProfileName.Text, Protocol = (ProxyProtocol)Protocol.SelectedIndex, Host = Host.Text, Port = port, Authentication = Authentication.IsChecked == true, Username = Username.Text, TestUrl = TestUrl.Text };
        profile = profile.Normalize();
        if (profile.Authentication && (string.IsNullOrEmpty(Password.Password) || System.Text.Encoding.UTF8.GetByteCount(Password.Password) > 255)) throw new ArgumentException("请输入密码，长度不能超过 255 个 UTF-8 字节。");
        return profile;
    }
    bool SaveProfileCore(bool forExit = false)
    {
        try {
            var profile = ReadProfile() with { Revision = selectedProfile!.Revision + 1 };
            var prospective = config with { Profiles = config.Profiles.Any(p => p.Id == profile.Id) ? config.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToList() : config.Profiles.Append(profile).ToList() };
            var saved = store.SaveProfile(prospective, profile, Password.Password);
            config.Profiles = prospective.Profiles; selectedProfile = saved; profileDirty = false; RefreshLists(); LoadProfile(saved);
            MessageText.Text = "配置已保存；保存不等于应用。"; return true;
        } catch (Exception ex) { ShowError(ex, forExit ? "请修正代理配置后重试，或再次退出时选择“否”放弃未保存的修改。" : null); return false; }
    }
    void SaveProfile(object sender, RoutedEventArgs e) => SaveProfileCore();
    async Task<ProbeResult> Probe(ProxyProfile profile, string password)
    {
        TestResult.Text = "正在测试当前配置…";
        var result = await Upstream.Probe(new(profile, password, profile.Revision), CancellationToken.None);
        currentInputTested = true;
        TestResult.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + (result.Success ? "测试通过" : "测试未通过") + Environment.NewLine + string.Join(Environment.NewLine, result.Steps.Select(s => s.Name + "：" + s.Detail));
        if (!profileDirty && config.Profiles.Any(p => p.Id == profile.Id && p.Revision == profile.Revision)) probeResults[profile.Id] = (profile.Revision, DateTimeOffset.Now, result.Success);
        return result;
    }
    async void TestProfile(object sender, RoutedEventArgs e)
    {
        try { var profile = ReadProfile(); string password = Password.Password; SetBusy(true); var result = await Probe(profile, password); MessageText.Text = result.Success ? "测试通过，不代表目标软件全部业务可用。" : "测试失败，现有生效配置保持不变。"; }
        catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); UpdateStatus(); }
    }
    async void ApplySharedClick(object sender, RoutedEventArgs e)
    {
        if (selectedProfile == null || !ProtectProfile() || selectedProfile == null) return;
        try {
            var profile = selectedProfile; string password = store.ReadPassword(profile); SetBusy(true);
            if (!(await Probe(profile, password)).Success) { bridges.RecordFailure(null, BridgeFailureKind.Probe); MessageText.Text = "测试失败，当前共享代理保持不变。"; return; }
            bridges.ApplyShared(profile, password, config.Settings.SharedPort); config.Settings.SharedProfileId = profile.Id;
            try { store.Save(config); } catch (Exception ex) { MessageText.Text = "代理已应用，但默认配置未能保存：" + ex.Message; UpdateStatus(); return; }
            MessageText.Text = "共享代理已应用；新连接使用当前配置。";
        } catch (Exception ex) { bridges.RecordFailure(null, BridgeFailureKind.Start); ShowError(ex); } finally { SetBusy(false); UpdateStatus(); }
    }
    void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (selectedProfile == null) return;
        if (bridges.IsProfileApplied(selectedProfile.Id) || config.Applications.Any(a => a.IndependentProfileId == selectedProfile.Id)) { MessageBox.Show(this, "此配置正在使用或被软件引用，请先停止或更换配置。", "无法删除"); return; }
        if (MessageBox.Show(this, "删除此代理配置？", "删除配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { var id = selectedProfile.Id; var next = config with { Profiles = config.Profiles.Where(p => p.Id != id).ToList(), Settings = config.Settings with { SharedProfileId = config.Settings.SharedProfileId == id ? null : config.Settings.SharedProfileId } }; store.Save(next); config.Profiles = next.Profiles; config.Settings = next.Settings; selectedProfile = null; RefreshLists(); LoadProfile(null); try { store.DeletePassword(id); } catch (Exception cleanup) { MessageText.Text = "配置已删除，但旧凭据清理失败：" + cleanup.Message; return; } MessageText.Text = "配置已删除。"; }
        catch (Exception ex) { ShowError(ex); }
    }

    void AddApplication(object sender, RoutedEventArgs e)
    {
        if (!ProtectApp()) return;
        var app = new ApplicationEntry(); LoadApp(app); RefreshLists(); appDirty = true; UpdateStatus();
    }
    ApplicationEntry ReadApp()
    {
        if (selectedApp == null || string.IsNullOrWhiteSpace(AppName.Text) || LaunchMode.SelectedIndex < 0) throw new ArgumentException("请填写软件名称并选择启动设置。");
        string path = AppPath.Text.Trim(); if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请选择存在的 EXE 文件。");
        string directory = AppDirectory.Text.Trim(); if (directory.Length > 0 && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))) throw new ArgumentException("工作目录不存在或不是完整路径。");
        var profile = IndependentProfile.SelectedItem as ProxyProfile;
        if (Independent.IsChecked == true && profile == null) throw new ArgumentException("请选择独立代理配置。");
        return selectedApp with { Name = AppName.Text.Trim(), Components = [new() { Path = path }], WorkingDirectory = directory, Arguments = AppArguments.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList(), LaunchMethod = (LaunchMethod)LaunchMode.SelectedIndex, UseIndependentProxy = Independent.IsChecked == true, IndependentProfileId = Independent.IsChecked == true ? profile?.Id : null };
    }
    bool SaveAppCore(bool forExit = false)
    {
        try { var app = ReadApp(); var next = config with { Applications = config.Applications.Any(a => a.Id == app.Id) ? config.Applications.Select(a => a.Id == app.Id ? app : a).ToList() : config.Applications.Append(app).ToList() }; store.Save(next); config.Applications = next.Applications; selectedApp = app; appDirty = false; RefreshLists(); LoadApp(app); MessageText.Text = "软件配置已保存；端口或启动设置改变后，已有软件可能需要重启。"; return true; }
        catch (Exception ex) { ShowError(ex, forExit ? "请修正软件配置后重试，或再次退出时选择“否”放弃未保存的修改。" : null); return false; }
    }
    void SaveApplication(object sender, RoutedEventArgs e) => SaveAppCore();
    void BrowseApplication(object sender, RoutedEventArgs e) { var dialog = new OpenFileDialog { Filter = "可执行程序 (*.exe)|*.exe", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) AppPath.Text = dialog.FileName; }
    async Task<bool> ApplyIndependentCore()
    {
        if (selectedApp == null || !selectedApp.UseIndependentProxy || selectedApp.IndependentProfileId == null) throw new InvalidOperationException("请先为软件保存独立代理配置。");
        var profile = config.Profiles.FirstOrDefault(p => p.Id == selectedApp.IndependentProfileId) ?? throw new IOException("独立代理配置不存在。");
        string password;
        try { password = store.ReadPassword(profile); }
        catch { bridges.RecordFailure(selectedApp.Id, BridgeFailureKind.Start); throw; }
        var result = await Upstream.Probe(new(profile, password, profile.Revision), CancellationToken.None);
        probeResults[profile.Id] = (profile.Revision, DateTimeOffset.Now, result.Success);
        if (!result.Success) { bridges.RecordFailure(selectedApp.Id, BridgeFailureKind.Probe); throw new IOException("代理测试失败，当前入口保持不变：" + string.Join("；", result.Steps.Where(s => !s.Success).Select(s => s.Detail))); }
        bool created = bridges.IndependentStatus(selectedApp.Id) == null;
        int port;
        try { port = bridges.ApplyIndependent(selectedApp, profile, password); }
        catch { bridges.RecordFailure(selectedApp.Id, BridgeFailureKind.Start); throw; }
        if (selectedApp.IndependentPort != port) {
            int previous = selectedApp.IndependentPort; selectedApp.IndependentPort = port;
            try { store.Save(config); } catch { selectedApp.IndependentPort = previous; if (created) bridges.StopIndependent(selectedApp.Id); bridges.RecordFailure(selectedApp.Id, BridgeFailureKind.Start); throw; }
        }
        UpdateStatus(); return true;
    }
    async void ApplyIndependentClick(object sender, RoutedEventArgs e)
    {
        if (selectedApp == null || !ProtectApp() || selectedApp == null) return;
        try { SetBusy(true); await ApplyIndependentCore(); MessageText.Text = "独立代理已应用；新连接使用当前配置。"; }
        catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); UpdateStatus(); }
    }
    async void LaunchApplication(object sender, RoutedEventArgs e)
    {
        if (selectedApp == null || !ProtectApp() || selectedApp == null) return;
        try {
            SetBusy(true); int port;
            if (selectedApp.UseIndependentProxy) { await ApplyIndependentCore(); port = bridges.IndependentStatus(selectedApp.Id)!.Port; }
            else { if (!bridges.Shared.Status.Running) throw new InvalidOperationException("请先在代理配置页应用共享代理。"); port = bridges.Shared.Status.Port; }
            string path = selectedApp.Components.First().Path;
            var candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
            bool running;
            try { running = candidates.Any(p => { try { return string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase); } catch { return false; } }); }
            finally { foreach (var candidate in candidates) candidate.Dispose(); }
            if (running && MessageBox.Show(this, "该软件似乎已经运行。已有实例可能不会读取新的代理设置；本工具不会关闭它。仍要启动吗？", "软件已运行", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            var process = ApplicationLauncher.Start(selectedApp, port);
            if (launched.Remove(selectedApp.Id, out var prior)) prior.Dispose();
            launched[selectedApp.Id] = process; MessageText.Text = "已发送启动请求；是否使用代理取决于软件自身。";
        } catch (Exception ex) { ShowError(ex); } finally { SetBusy(false); UpdateStatus(); }
    }
    void StopIndependent(object sender, RoutedEventArgs e) { if (selectedApp == null) return; bridges.StopIndependent(selectedApp.Id); MessageText.Text = "独立入口已停止，使用此入口的连接会中断。"; UpdateStatus(); }
    void DeleteApplication(object sender, RoutedEventArgs e)
    {
        if (selectedApp == null) return;
        if (MessageBox.Show(this, "删除此软件配置并停止其独立入口？不会关闭目标软件。", "删除软件", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { var id = selectedApp.Id; var next = config with { Applications = config.Applications.Where(a => a.Id != id).ToList() }; store.Save(next); config.Applications = next.Applications; bridges.StopIndependent(id); selectedApp = null; RefreshLists(); LoadApp(null); }
        catch (Exception ex) { ShowError(ex); }
    }
    void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ThemeChoice.SelectedIndex < 0) return;
        string previous = config.Settings.Theme; config.Settings.Theme = ThemeChoice.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        try { store.Save(config); ((App)Application.Current).SetTheme(Theme); } catch (Exception ex) { config.Settings.Theme = previous; loading = true; ThemeChoice.SelectedIndex = previous == "Light" ? 1 : previous == "Dark" ? 2 : 0; loading = false; ShowError(ex); }
    }
    void SaveSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SharedPort.Text, out int port) || port is < 1 or > 65535) { ShowError(new ArgumentException("请输入 1–65535 之间的端口。")); return; }
        if (bridges.Shared.Status.Running && port != config.Settings.SharedPort) { ShowError(new InvalidOperationException("请先停止所有桥接，再修改共享端口。")); return; }
        int previous = config.Settings.SharedPort; config.Settings.SharedPort = port;
        try { store.Save(config); MessageText.Text = "共享端口已保存，使用旧端口的软件可能需要重新启动。"; } catch (Exception ex) { config.Settings.SharedPort = previous; ShowError(ex); }
    }
    public void StopAll() { if (busy) { MessageText.Text = "操作正在进行，请稍候。"; return; } bridges.StopAll(); MessageText.Text = "所有桥接已停止；对应连接会中断。"; UpdateStatus(); }
    void StopAllClick(object sender, RoutedEventArgs e) => StopAll();
    public bool HasPendingExitConfirmation => busy || profileDirty || appDirty;
    public bool PrepareExit() { if (busy) { MessageBox.Show(this, "操作正在进行，请稍候。"); return false; } return ProtectProfile(true) && ProtectApp(true); }
    void WindowClosing(object? sender, CancelEventArgs e) { if (!((App)Application.Current).Exiting) { e.Cancel = true; Hide(); } }
    public void ApplyTitleBarTheme(bool dark)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) { int enabled = dark ? 1 : 0; try { DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)); } catch (DllNotFoundException) { } }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public void Dispose() { statusTimer.Stop(); foreach (var process in launched.Values) process.Dispose(); launched.Clear(); bridges.Dispose(); }
}

