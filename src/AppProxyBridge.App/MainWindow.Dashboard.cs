using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AppProxyBridge.Core;

namespace AppProxyBridge.App;

public partial class MainWindow
{
    static string AppKey(Guid id) => "app:" + id.ToString("N");
    static string ProfileKey(Guid id) => "profile:" + id.ToString("N");
    static string EntryKey(Guid? id) => id == null ? "entry:shared" : "entry:" + id.Value.ToString("N");

    DashboardSnapshot BuildDashboard()
    {
        var nodes = new List<DashboardNode>(); var links = new List<DashboardLink>();
        var statuses = new List<RuntimeStatus>(); var activeProfiles = new Dictionary<Guid, int>();
        string ProcessState(Guid id)
        {
            if (!launched.TryGetValue(id, out var process)) return "未由本工具启动 / 状态未知";
            try { return process.HasExited ? "启动进程已退出" : "启动进程运行中"; }
            catch (InvalidOperationException) { return "进程状态未知"; }
        }
        foreach (var app in config.Applications) {
            string state = ProcessState(app.Id);
            string mode = app.UseIndependentProxy ? "独立入口" : "共享入口";
            string shortState = state == "启动进程运行中" ? "启动进程运行中" : state == "启动进程已退出" ? "启动进程已退出" : "进程状态未知";
            nodes.Add(new(AppKey(app.Id), DashboardKind.Application, app.Id, app.Name, (app.UseIndependentProxy ? "独立" : "共享") + " · " + shortState,
                $"已保存：{mode}\n{state}；无法确认该软件的全部流量是否使用代理。\n软件到入口的虚线表示配置关系。"));
            links.Add(new(AppKey(app.Id), EntryKey(app.UseIndependentProxy ? app.Id : null), false));
        }
        void Entry(Guid? id, string name, RuntimeStatus? state, (Guid ProfileId, long Revision)? active, Guid? desired, bool staleMode = false)
        {
            bool running = state?.Running == true;
            if (state != null) statuses.Add(state);
            var saved = desired == null ? null : config.Profiles.FirstOrDefault(p => p.Id == desired.Value);
            var actual = running && active != null ? config.Profiles.FirstOrDefault(p => p.Id == active.Value.ProfileId) : null;
            bool pending = running && (staleMode || desired != active?.ProfileId || (saved != null && saved.Revision != active?.Revision));
            string entryState = running ? $"运行中 · {state!.Connections} 个入口连接" : state?.Faulted == true ? "监听异常" : "已停止";
            if (pending) entryState += " · 待应用";
            var errors = bridges.RecentFailures(id);
            string detail = running
                ? $"运行中 · 本地端口 {state!.Port} · 当前入口连接 {state.Connections}（含建立中）\n生效时的上游名称：{state.ActiveName ?? "未知"} · 旧版本连接：{state.OldConnections}"
                : (state?.Faulted == true ? "监听异常停止；请重新应用配置。" : "入口未运行。") + (saved == null ? "" : $"\n已保存选择：{saved.Name}（未生效）");
            if (pending) detail += "\n已保存的选择或版本与生效状态不同；新设置待应用。";
            detail += errors.Count == 0 ? "\n本次运行暂无错误记录。" : "\n最近错误：\n" + string.Join("\n", errors.Take(3).Select(e => e.Time.ToString("HH:mm:ss") + " · " + e.Message));
            nodes.Add(new(EntryKey(id), DashboardKind.Entry, id, name, entryState, detail, actual?.Id ?? saved?.Id));
            if (actual != null) {
                links.Add(new(EntryKey(id), ProfileKey(actual.Id), true));
                activeProfiles[actual.Id] = activeProfiles.GetValueOrDefault(actual.Id) + 1;
            }
            if (saved != null && (actual == null || saved.Id != actual.Id)) links.Add(new(EntryKey(id), ProfileKey(saved.Id), false));
        }
        Entry(null, "共享入口", bridges.Shared.Status, bridges.SharedApplied, config.Settings.SharedProfileId);
        foreach (var app in config.Applications) {
            var state = bridges.IndependentStatus(app.Id);
            if (app.UseIndependentProxy || state != null)
                Entry(app.Id, app.Name + " · 独立入口", state, bridges.IndependentApplied(app.Id), app.IndependentProfileId, !app.UseIndependentProxy);
        }
        foreach (var profile in config.Profiles) {
            int active = activeProfiles.GetValueOrDefault(profile.Id);
            string use = active > 0 ? $"{profile.Protocol} · {active} 个入口使用中" : $"{profile.Protocol} · 未被使用";
            string test = probeResults.TryGetValue(profile.Id, out var result)
                ? result.Revision == profile.Revision ? $"本次测试：{(result.Success ? "通过" : "未通过")} · {result.Time:HH:mm:ss}" : "配置已更改，请重新测试。"
                : "本次运行暂无测试记录。";
            nodes.Add(new(ProfileKey(profile.Id), DashboardKind.Profile, profile.Id, profile.Name, use,
                $"{use}\n{test}\n测试通过不代表目标软件的全部业务可用。"));
        }
        int activeEntries = statuses.Count(s => s.Running);
        int connections = statuses.Where(s => s.Running).Sum(s => s.Connections);
        return new(nodes, links, $"软件 {config.Applications.Count}   ·   配置 {config.Profiles.Count}   ·   运行入口 {activeEntries}   ·   入口连接 {connections}");
    }

    void NavigateFromDashboard(DashboardNode node)
    {
        if (node.Kind == DashboardKind.Application) {
            if (node.Id == null) { ShowPage(ApplicationsPage); return; }
            var app = config.Applications.FirstOrDefault(a => a.Id == node.Id);
            if (app == null) return;
            if (selectedApp?.Id != app.Id) { AppList.SelectedItem = app; if (selectedApp?.Id != app.Id) return; }
            ShowPage(ApplicationsPage); return;
        }
        Guid? id = node.Kind == DashboardKind.Profile ? node.Id : node.TargetId;
        var profile = config.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile != null && selectedProfile?.Id != profile.Id) { ProfileList.SelectedItem = profile; if (selectedProfile?.Id != profile.Id) return; }
        ShowPage(ProfilesPage);
    }
}

