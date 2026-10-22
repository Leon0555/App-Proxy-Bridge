using System.Text.Json.Serialization;

namespace AppProxyBridge.Core;

public enum ProxyProtocol { HTTP, HTTPS, SOCKS5 }
public sealed record ProxyProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "新配置";
    public ProxyProtocol Protocol { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 7890;
    public bool Authentication { get; init; }
    public string Username { get; init; } = "";
    public string CredentialReference { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 20;
    public string TestUrl { get; init; } = "https://example.com/";
    [JsonIgnore] public string Endpoint => $"{Protocol.ToString().ToLowerInvariant()}://{(Host.Contains(':') ? "[" + Host + "]" : Host)}:{Port}";
    public ProxyProfile Normalize()
    {
        var host = Host.Trim().Trim('[', ']');
        if (!Enum.IsDefined(Protocol) || Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Any(char.IsWhiteSpace))
            throw new ArgumentException("请输入有效的代理协议及主机名（不含 URL 路径）。");
        if (string.IsNullOrWhiteSpace(Name) || Port is < 1 or > 65535 || TimeoutSeconds is < 3 or > 120)
            throw new ArgumentException("请检查配置名称、端口（1–65535）和超时（3–120 秒）。");
        if (Authentication && (Username.Length == 0 || Username.Contains(':') || Username.Any(char.IsControl)))
            throw new ArgumentException("认证账号不能为空，也不能包含冒号或控制字符。");
        if (System.Text.Encoding.UTF8.GetByteCount(Username) > 255)
            throw new ArgumentException("账号不能超过 255 个 UTF-8 字节。");
        if (!Uri.TryCreate(TestUrl, UriKind.Absolute, out var target) || target.Scheme != "https" || target.UserInfo.Length != 0)
            throw new ArgumentException("测试地址必须为不含账号密码的 HTTPS URL。");
        return this with { Name = Name.Trim(), Host = host };
    }
}
public sealed record ProxySnapshot(ProxyProfile Profile, string Password, long Revision)
{
    public override string ToString() => $"ProxySnapshot {{ Id = {Profile.Id}, Revision = {Revision}, Password = [redacted] }}";
}
public sealed record AppComponent
{
    public string Path { get; init; } = "";
    public string PackageFamily { get; init; } = "";
    public string PackageRelativePath { get; init; } = "";
    public string ApplicationId { get; init; } = "";
}
public sealed record ApplicationEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "新软件";
    public bool Enabled { get; set; } = true;
    public List<AppComponent> Components { get; init; } = [];
}
public sealed record AppSettings
{
    public Guid? DefaultProfileId { get; set; }
    public string Theme { get; set; } = "System";
}
public sealed record Configuration
{
    public int Version { get; init; } = 2;
    public List<ProxyProfile> Profiles { get; set; } = [];
    public List<ApplicationEntry> Applications { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}
public sealed record ProbeStep(string Name, bool Success, string Detail);
public sealed record ProbeResult(bool Success, IReadOnlyList<ProbeStep> Steps);
public sealed record ConnectionEvent(DateTimeOffset Time, uint Pid, string Process, string Destination, string Action);
public sealed record Flow(ulong Id, uint Pid, uint ConfigId, string Process, string Host, int Port, bool Udp, bool IPv6, ulong Sent, ulong Received);
public sealed record EngineStatus(bool Running, string ActiveName, long Revision, int SelectedApps, string Detail, IReadOnlyList<Flow> Flows, IReadOnlyList<ConnectionEvent> Events, Guid? ProfileId=null, IReadOnlyList<string>? AppliedPaths=null);
public sealed record Activation(ProxyProfile Profile, string Password, List<string> Paths, int ApplicationCount)
{
    public override string ToString() => "Activation { Password = [redacted] }";
    public Activation Validate()
    {
        var p = Profile.Normalize();
        if (Paths.Count == 0 || Paths.Count > 256 || ApplicationCount<1 || ApplicationCount>Paths.Count || Paths.Any(x => !System.IO.Path.IsPathFullyQualified(x) || !File.Exists(x) || !x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || x.Any(char.IsControl) || x.IndexOfAny(['*','?']) >= 0))
            throw new ArgumentException("请选择至少一个有效软件（最多 256 个程序组件），失效路径请重新选择。");
        if (p.Authentication && (Password.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(Password) > 255))
            throw new ArgumentException("请输入密码，长度不能超过 255 个 UTF-8 字节。");
        return this with { Profile = p, Paths = Paths.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
    }
}
