using System.Security.Authentication;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using AppProxyBridge.Core;

if (args.Length == 2 && args[0] == "--launch-fixture")
{
    File.WriteAllText(args[1], Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? "");
    return;
}

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
async Task ExpectFailure(Func<Task> action, string name)
{
    try { await action(); }
    catch (Exception ex) when (ex is IOException or AuthenticationException or ArgumentException or InvalidOperationException or SocketException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}

foreach (var protocol in new[] { ProxyProtocol.HTTP, ProxyProtocol.SOCKS5 })
foreach (bool auth in new[] { false, true })
{
    using var server = new Mock(protocol, auth, "ok:");
    var profile = new ProxyProfile { Protocol = protocol, Host = server.Host, Port = server.Port, Authentication = auth, Username = auth ? "user" : "" };
    var snapshot = new ProxySnapshot(profile, auth ? "pass" : "", 1);
    using var stream = await Upstream.Connect(snapshot, "example.com", 443, CancellationToken.None);
    await stream.WriteAsync(Encoding.ASCII.GetBytes("hello"));
    var response = new byte[8];
    await stream.ReadExactlyAsync(response);
    Check(Encoding.ASCII.GetString(response) == "ok:hello", $"{protocol} {(auth ? "认证" : "无认证")} TCP 握手与往返");
    if (auth) await ExpectFailure(async () => { using var _ = await Upstream.Connect(snapshot with { Password = "wrong" }, "example.com", 443, CancellationToken.None); }, $"{protocol} 错误认证拒绝");
}

using (var server = new Mock(ProxyProtocol.HTTPS, false, "ignored"))
{
    var profile = new ProxyProfile { Protocol = ProxyProtocol.HTTPS, Host = server.Host, Port = server.Port };
    await ExpectFailure(async () => { using var _ = await Upstream.Connect(new(profile, "", 1), "example.com", 443, CancellationToken.None); }, "HTTPS 上游不可信证书拒绝");
}

var root = Path.Combine(Path.GetTempPath(), "AppProxyBridgeNext-test-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new ConfigurationStore(root);
    var profile = new ProxyProfile { Name = "测试", Authentication = true, Username = "user", Revision = 1 };
    var config = new Configuration { Profiles = [profile] };
    await ExpectFailure(() => Task.Run(() => store.SaveProfile(new Configuration(), profile, "only-test-password")), "拒绝保存不在配置列表中的条目");
    await ExpectFailure(() => Task.Run(() => store.SaveProfile(config, profile, "")), "认证配置拒绝空密码");
    var saved = store.SaveProfile(config, profile, "only-test-password");
    var loaded = store.Load().Profiles.Single();
    Check(store.ReadPassword(loaded) == "only-test-password", "当前用户 DPAPI 凭据往返");
    Check(!File.ReadAllText(Path.Combine(root, "profiles.json")).Contains("only-test-password"), "JSON 不含明文密码");
    Check(File.ReadAllBytes(Path.Combine(root, saved.CredentialReference)).AsSpan().IndexOf(Encoding.UTF8.GetBytes("only-test-password")) < 0, "凭据文件不含明文密码");
    Directory.CreateDirectory(Path.Combine(root, "profiles.json.tmp"));
    try { store.SaveProfile(new Configuration { Profiles = [profile] }, profile with { Revision = 2 }, "new-password"); throw new Exception("FAIL: 写入失败未报告"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(true, "保存失败有错误反馈"); }
    Check(store.ReadPassword(store.Load().Profiles.Single()) == "only-test-password", "保存失败保留旧配置与凭据");
    await ExpectFailure(() => Task.Run(() => store.ReadPassword(loaded with { CredentialReference = "../outside.secret" })), "拒绝越界凭据引用");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

Check(ConfigurationStore.ForCurrentUser().DirectoryPath.EndsWith("AppProxyBridge.Next", StringComparison.Ordinal), "默认存储独立于旧项目");
await ExpectFailure(() => Task.Run(() => new ProxyProfile { Host = "https://example.com/path" }.Normalize()), "拒绝带路径的代理地址");
Check(!new ProxySnapshot(new(), "sensitive", 1).ToString().Contains("sensitive"), "快照字符串不暴露密码");

using (var upstreamA = new Mock(ProxyProtocol.HTTP, false, "A:"))
using (var upstreamB = new Mock(ProxyProtocol.HTTP, false, "B:"))
using (var bridges = new BridgeManager())
{
    var a = new ProxyProfile { Name = "A", Host = upstreamA.Host, Port = upstreamA.Port, Revision = 1 };
    var b = new ProxyProfile { Name = "B", Host = upstreamB.Host, Port = upstreamB.Port, Revision = 1 };
    var app = new ApplicationEntry { Name = "fixture", UseIndependentProxy = true };
    bridges.ApplyShared(a, "", 0);
    int sharedPort = bridges.Shared.Status.Port;
    int independentPort = bridges.ApplyIndependent(app, b, "");
    async Task<TcpClient> Tunnel(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        await Wire.Write(stream, "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n", default);
        if (Wire.Status(await Wire.Header(stream, default)) != 200) throw new IOException("隧道建立失败");
        return client;
    }
    async Task<string> Echo(TcpClient client)
    {
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("x"));
        byte[] reply = new byte[3]; await stream.ReadExactlyAsync(reply);
        return Encoding.ASCII.GetString(reply);
    }
    using var old = await Tunnel(sharedPort);
    using var isolated = await Tunnel(independentPort);
    Check(await Echo(old) == "A:x" && await Echo(isolated) == "B:x", "共享和独立入口使用不同上游");
    Check(bridges.IsProfileApplied(a.Id) && bridges.IsProfileApplied(b.Id), "共享和独立入口的生效配置均受保护");
    bridges.ApplyShared(b, "", sharedPort);
    using var fresh = await Tunnel(sharedPort);
    Check(await Echo(old) == "A:x" && await Echo(fresh) == "B:x", "共享上游切换保留旧连接，新连接使用新上游");
    Check(bridges.IndependentApplied(app.Id)?.ProfileId == b.Id && await Echo(isolated) == "B:x", "切换共享入口不改变独立入口");
    await ExpectFailure(() => Task.Run(() => bridges.ApplyShared(a, "", independentPort)), "运行时拒绝静默修改共享端口");
    Check(bridges.SharedApplied?.ProfileId == b.Id && await Echo(fresh) == "B:x", "共享端口冲突保留当前配置和连接");
    bridges.StopIndependent(app.Id);
    Check(bridges.IndependentStatus(app.Id) is null && bridges.Shared.Status.Running, "停止独立入口不停止共享入口");
    bridges.StopAll();
    Check(!bridges.Shared.Status.Running, "停止所有入口释放监听");
}

var fixture = new ApplicationEntry { Components = [new() { Path = Environment.ProcessPath! }], LaunchMethod = LaunchMethod.Chromium, Arguments = ["hello world"] };
var start = ApplicationLauncher.CreateStartInfo(fixture, 8123);
Check(start.Environment["HTTPS_PROXY"] == "http://127.0.0.1:8123" && start.ArgumentList.Contains("--proxy-server=http://127.0.0.1:8123") && start.ArgumentList.Contains("hello world"), "启动参数和环境变量指向本地端口");
await ExpectFailure(() => Task.Run(() => ApplicationLauncher.CreateStartInfo(fixture with { Arguments = ["--no-proxy-server"] }, 8123)), "拒绝 Chromium 参数覆盖代理");
var output = Path.Combine(Path.GetTempPath(), "AppProxyBridgeNext-launch-" + Guid.NewGuid().ToString("N"));
try {
    var launchFixture = fixture with { LaunchMethod = LaunchMethod.Environment, Arguments = ["--launch-fixture", output] };
    var startFixture = ApplicationLauncher.CreateStartInfo(launchFixture, 8123); startFixture.CreateNoWindow = true;
    using var process = Process.Start(startFixture) ?? throw new IOException("模拟程序启动失败");
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    Check(process.ExitCode == 0 && File.ReadAllText(output) == "http://127.0.0.1:8123", "实际子进程继承本地代理环境变量");
} finally { if (File.Exists(output)) File.Delete(output); }

using (var rejecting = new Mock(ProxyProtocol.HTTP, true, "ignored"))
using (var bridge = new BridgeEngine())
{
    var profile = new ProxyProfile { Host = rejecting.Host, Port = rejecting.Port, Authentication = true, Username = "user" };
    bridge.Apply(profile, "wrong"); bridge.Start(0);
    using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", bridge.Status.Port);
    await Wire.Write(client.GetStream(), "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n", default);
    Check(Wire.Status(await Wire.Header(client.GetStream(), default)) == 502, "上游认证失败返回错误而非桥接直连");
}
using (var rejecting = new Mock(ProxyProtocol.HTTP, true, "ignored"))
using (var bridges = new BridgeManager())
{
    var profile = new ProxyProfile { Host = rejecting.Host, Port = rejecting.Port, Authentication = true, Username = "user" };
    bridges.ApplyShared(profile, "secret-wrong-password", 0);
    using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", bridges.Shared.Status.Port);
    await Wire.Write(client.GetStream(), "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n", default);
    await Wire.Header(client.GetStream(), default);
    for (int i = 0; i < 100 && bridges.RecentFailures(null).Count == 0; i++) await Task.Delay(10);
    var error = bridges.RecentFailures(null).Single();
    Check(error.Kind == BridgeFailureKind.Connection && !error.Message.Contains("secret") && !error.Message.Contains("example.com"), "连接错误记录不包含密码或目标地址");
}
using (var occupied = new TcpListener(System.Net.IPAddress.Loopback, 0))
using (var bridges = new BridgeManager())
{
    occupied.Server.ExclusiveAddressUse = true; occupied.Start();
    int port = ((System.Net.IPEndPoint)occupied.LocalEndpoint).Port;
    var app = new ApplicationEntry { Name = "occupied", IndependentPort = port };
    await ExpectFailure(() => Task.Run(() => bridges.ApplyIndependent(app, new ProxyProfile(), "")), "独立端口占用时报告失败");
    Check(bridges.IndependentStatus(app.Id) == null, "独立端口占用不留下虚假的运行状态");
}
using (var bridges = new BridgeManager())
{
    var profile = new ProxyProfile(); bridges.ApplyShared(profile, "", 0);
    var listener = (TcpListener)typeof(BridgeEngine).GetField("listener", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridges.Shared)!;
    listener.Stop();
    for (int i = 0; i < 100 && (!bridges.Shared.Status.Faulted || bridges.RecentFailures(null).Count == 0); i++) await Task.Delay(20);
    Check(bridges.Shared.Status.Faulted && !bridges.Shared.Status.Running, "监听意外停止后不再显示运行中");
    Check(bridges.RecentFailures(null).Any(f => f.Kind == BridgeFailureKind.Listener), "监听异常记录到共享入口");
    bridges.ApplyShared(profile, "", 0);
    Check(bridges.Shared.Status.Running && !bridges.Shared.Status.Faulted, "监听异常后可以重新启动");
    var appId = Guid.NewGuid();
    for (int i = 0; i < 7; i++) bridges.RecordFailure(appId, BridgeFailureKind.Probe);
    Check(bridges.RecentFailures(appId).Count == 5 && bridges.RecentFailures(null).All(f => f.ApplicationId == null), "最近错误按入口隔离并限制返回数量");
    bridges.StopAll();
    Check(bridges.RecentFailures(appId).Count == 5, "停止入口后仍保留本次运行的最近错误");
}
using (var bridges = new BridgeManager())
{
    var app = new ApplicationEntry { Name = "独立测试" }; var profile = new ProxyProfile();
    app.IndependentPort = bridges.ApplyIndependent(app, profile, "");
    var engines = (Dictionary<Guid, BridgeEngine>)typeof(BridgeManager).GetField("independent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridges)!;
    var listener = (TcpListener)typeof(BridgeEngine).GetField("listener", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engines[app.Id])!;
    listener.Stop();
    for (int i = 0; i < 100 && (bridges.IndependentStatus(app.Id)?.Faulted != true || bridges.RecentFailures(app.Id).Count == 0); i++) await Task.Delay(20);
    Check(bridges.IndependentStatus(app.Id)?.Faulted == true && !bridges.IsProfileApplied(profile.Id), "独立监听异常后不再显示生效");
    Check(bridges.ApplyIndependent(app, profile, "") == app.IndependentPort && bridges.IndependentStatus(app.Id)?.Running == true, "独立入口异常后可在原端口恢复");
}
using (var freshManager = new BridgeManager()) Check(freshManager.RecentFailures(null).Count == 0, "新进程状态不继承此前错误记录");
Console.WriteLine($"RESULT: {passed} checks passed");

