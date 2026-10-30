using System.Collections.Concurrent;

namespace AppProxyBridge.Core;

// Each listener owns an immutable upstream snapshot per accepted connection.
public sealed class BridgeManager : IDisposable
{
    readonly Dictionary<Guid, BridgeEngine> independent = [];
    readonly Dictionary<Guid, (Guid ProfileId, long Revision)> applied = [];
    readonly ConcurrentQueue<BridgeFailure> failures = new();
    public BridgeEngine Shared { get; } = new();
    public (Guid ProfileId, long Revision)? SharedApplied { get; private set; }
    public event Action<string>? Error;

    public BridgeManager()
    {
        Shared.Error += message => { RecordFailure(null, BridgeFailureKind.Connection); Error?.Invoke(message); };
        Shared.ListenerFailed += () => RecordFailure(null, BridgeFailureKind.Listener);
    }

    public void RecordFailure(Guid? appId, BridgeFailureKind kind)
    {
        string message = kind switch {
            BridgeFailureKind.Listener => "本地入口监听异常停止，请重新应用配置。",
            BridgeFailureKind.Probe => "上游测试未通过，请检查代理配置。",
            BridgeFailureKind.Start => "入口启动或应用失败，请检查端口与配置。",
            _ => "一次连接处理失败，请检查上游及目标网络。"
        };
        failures.Enqueue(new(appId, DateTimeOffset.Now, kind, message));
        while(failures.Count>100) failures.TryDequeue(out _);
    }
    public IReadOnlyList<BridgeFailure> RecentFailures(Guid? appId) => failures.ToArray().Where(f => f.ApplicationId == appId).Reverse().Take(5).ToArray();

    public void ApplyShared(ProxyProfile profile, string password, int port)
    {
        profile = profile.Normalize();
        if (Shared.Status.Running && Shared.Status.Port != port)
            throw new InvalidOperationException("共享端口已更改，请先停止桥接后重新启动。");
        if (Shared.Status.Running) Shared.Apply(profile, password);
        else {
            Shared.Apply(profile, password);
            try { Shared.Start(port); } catch { Shared.Stop(); throw; }
        }
        SharedApplied = (profile.Id, profile.Revision);
    }

    public int ApplyIndependent(ApplicationEntry app, ProxyProfile profile, string password)
    {
        profile = profile.Normalize();
        if (!independent.TryGetValue(app.Id, out var engine)) {
            engine = new BridgeEngine();
            engine.Error += message => { RecordFailure(app.Id, BridgeFailureKind.Connection); Error?.Invoke($"{app.Name}：{message}"); };
            engine.ListenerFailed += () => RecordFailure(app.Id, BridgeFailureKind.Listener);
            engine.Apply(profile, password);
            try { engine.Start(app.IndependentPort); } catch { engine.Dispose(); throw; }
            independent.Add(app.Id, engine);
        } else { engine.Apply(profile, password); if(!engine.Status.Running) engine.Start(app.IndependentPort); }
        applied[app.Id] = (profile.Id, profile.Revision);
        return engine.Status.Port;
    }

    public RuntimeStatus? IndependentStatus(Guid appId) => independent.TryGetValue(appId, out var engine) ? engine.Status : null;
    public (Guid ProfileId, long Revision)? IndependentApplied(Guid appId) => applied.TryGetValue(appId, out var value) ? value : null;
    public bool IsProfileApplied(Guid profileId) => (Shared.Status.Running && SharedApplied?.ProfileId == profileId) || applied.Any(pair => pair.Value.ProfileId == profileId && independent[pair.Key].Status.Running);
    public void StopIndependent(Guid appId) { if (independent.Remove(appId, out var engine)) engine.Dispose(); applied.Remove(appId); }
    public void StopAll()
    {
        Shared.Stop(); SharedApplied = null;
        foreach (var engine in independent.Values) engine.Dispose();
        independent.Clear(); applied.Clear();
    }
    public void Dispose() { StopAll(); Shared.Dispose(); }
}

